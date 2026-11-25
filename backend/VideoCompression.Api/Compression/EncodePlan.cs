using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using VideoCompression.Api.Ffmpeg;
using VideoCompression.Api.Media;

namespace VideoCompression.Api.Compression;

public enum AudioAction { Copy, Encode }

/// <param name="Channels">Downmix to this many channels, or null to keep the layout.</param>
/// <param name="SampleRate">Resample to this rate, or null to keep it.</param>
public sealed record AudioTrackPlan(int StreamIndex, AudioAction Action, long BitrateBps, int? Channels, int? SampleRate);

/// <summary>A fully resolved encode: dimensions, rate control and the FFmpeg arguments that produce it.</summary>
public sealed class EncodePlan
{
    public required CodecSpec Codec { get; init; }
    public required RateMode Mode { get; init; }
    public required EncodeSpeed Speed { get; init; }
    public int Crf { get; init; }
    /// <summary>Average video bitrate for target-size mode.</summary>
    public long VideoBitrateBps { get; init; }
    public required int VideoStreamIndex { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required bool Scaled { get; init; }
    public required double Fps { get; init; }
    public required bool FpsLimited { get; init; }
    public required string PixelFormat { get; init; }
    public required IReadOnlyList<AudioTrackPlan> AudioTracks { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    /// <summary>Target-size mode: "excellent" | "good" | "fair" | "poor", from bits per pixel.</summary>
    public string? QualityHint { get; init; }

    public bool TwoPass => Mode == RateMode.TargetSize && Codec.Id != VideoCodec.Av1;
    public long AudioBitrateBps => AudioTracks.Sum(t => t.BitrateBps);

    /// <summary>Identifies the video encode, so measurements can be cached and shared between identical settings.</summary>
    public string VideoKey => string.Join(' ', VideoArguments());

    public IReadOnlyList<string> MapArguments()
    {
        var args = new List<string> { "-map", $"0:{VideoStreamIndex}" };
        foreach (var track in AudioTracks) args.AddRange(["-map", $"0:{track.StreamIndex}"]);
        return args;
    }

    /// <param name="pass">0 = single pass, 1 or 2 = pass of a two-pass encode.</param>
    public IReadOnlyList<string> VideoArguments(int pass = 0)
    {
        var args = new List<string>();
        var filters = new List<string>();
        if (FpsLimited) filters.Add($"fps={Units.Arg(Fps)}");
        if (Scaled) filters.Add($"scale={Width}:{Height}:flags=lanczos");
        if (filters.Count > 0) args.AddRange(["-vf", string.Join(',', filters)]);

        var bitrate = $"{Math.Max(1, VideoBitrateBps / 1000)}k";
        switch (Codec.Id)
        {
            case VideoCodec.H264:
                args.AddRange(["-c:v", "libx264", "-preset", Speed switch
                {
                    EncodeSpeed.Fast => "veryfast",
                    EncodeSpeed.Slow => "slow",
                    _ => "medium",
                }]);
                if (Mode == RateMode.Quality) args.AddRange(["-crf", Int(Crf)]);
                else
                {
                    args.AddRange(["-b:v", bitrate]);
                    if (pass > 0) args.AddRange(["-pass", Int(pass)]); // log files go to the job's working folder
                }
                break;

            case VideoCodec.H265:
                var x265 = new List<string> { "log-level=error" };
                args.AddRange(["-c:v", "libx265", "-preset", Speed switch
                {
                    EncodeSpeed.Fast => "veryfast",
                    EncodeSpeed.Slow => "medium",
                    _ => "fast",
                }]);
                if (Mode == RateMode.Quality) args.AddRange(["-crf", Int(Crf)]);
                else
                {
                    args.AddRange(["-b:v", bitrate]);
                    if (pass > 0) x265.Add($"pass={pass}"); // stats file (x265_2pass.log) goes to the working folder
                }
                args.AddRange(["-x265-params", string.Join(':', x265), "-tag:v", "hvc1"]); // hvc1 = playable on Apple devices
                break;

            case VideoCodec.Av1:
                args.AddRange(["-c:v", "libsvtav1", "-preset", Speed switch
                {
                    EncodeSpeed.Fast => "10",
                    EncodeSpeed.Slow => "6",
                    _ => "8",
                }, "-g", Int(Codec.GopFrames)]);
                args.AddRange(Mode == RateMode.Quality ? ["-crf", Int(Crf)] : ["-b:v", bitrate]);
                break;
        }

        args.AddRange(["-pix_fmt", PixelFormat]);
        return args;
    }

    public IReadOnlyList<string> AudioArguments()
    {
        if (AudioTracks.Count == 0) return ["-an"];

        var args = new List<string>();
        for (var i = 0; i < AudioTracks.Count; i++)
        {
            var track = AudioTracks[i];
            if (track.Action == AudioAction.Copy)
            {
                args.AddRange([$"-c:a:{i}", "copy"]);
                continue;
            }
            args.AddRange([$"-c:a:{i}", "aac", $"-b:a:{i}", $"{track.BitrateBps / 1000}k"]);
            if (track.Channels is { } channels) args.AddRange([$"-ac:a:{i}", Int(channels)]);
            if (track.SampleRate is { } rate) args.AddRange([$"-ar:a:{i}", Int(rate)]);
        }
        return args;
    }

    public string RateDescription => Mode == RateMode.Quality
        ? $"Quality {Crf}"
        : $"{(TwoPass ? "2-pass" : "1-pass")} {Units.Bitrate(VideoBitrateBps)}";

    public string AudioDescription
    {
        get
        {
            if (AudioTracks.Count == 0) return "No audio";
            if (AudioTracks.All(t => t.Action == AudioAction.Copy)) return "Original audio";
            var encoded = AudioTracks.First(t => t.Action == AudioAction.Encode);
            var text = $"AAC {encoded.BitrateBps / 1000} kbps";
            return AudioTracks.Count > 1 ? $"{text} ×{AudioTracks.Count}" : text;
        }
    }

    public string Summary => $"{Codec.Label} · {Width}×{Height} · {Units.Fps(Fps)} fps · {RateDescription} · {AudioDescription}";

    private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);
}

public sealed record PlanResult(EncodePlan? Plan, string? Error, double? MinTargetSizeMb)
{
    [MemberNotNullWhen(true, nameof(Plan))]
    public bool Ok => Plan is not null;
}

/// <summary>Validates <see cref="CompressionSettings"/> against a specific video and the available encoders.</summary>
public static class EncodePlanner
{
    /// <summary>Common short-side resolutions, largest first.</summary>
    public static readonly int[] StandardResolutions = [2160, 1440, 1080, 720, 540, 480, 360, 240];

    private const long MinVideoBps = 40_000;

    /// <summary>Share of the target size given to the streams; the rest absorbs container overhead and rate-control error.</summary>
    public static double TargetSafety(VideoCodec codec) => codec == VideoCodec.Av1 ? 0.92 : 0.96;

    public static double MinimumTargetMb(double durationSeconds, long audioBps, VideoCodec codec)
    {
        var bytes = (MinVideoBps + audioBps) * durationSeconds / 8 / TargetSafety(codec);
        return Math.Ceiling(bytes / 100_000) / 10; // round up to 0.1 MB
    }

    public static PlanResult Create(MediaInfo media, CompressionSettings settings, FfmpegStatus ffmpeg)
    {
        var codec = Codecs.Get(settings.Codec);
        if (!ffmpeg.HasEncoder(codec.Encoder))
            return new(null, $"{codec.Label} isn't available: this FFmpeg build has no {codec.Encoder} encoder.", null);

        var video = media.Video;
        var duration = media.DurationSeconds;
        var warnings = new List<string>();

        var sourceFps = video.Fps > 0 ? video.Fps : 30;
        var fpsLimited = settings.MaxFps is > 0 && sourceFps > settings.MaxFps.Value + 0.01;
        var fps = fpsLimited ? settings.MaxFps!.Value : sourceFps;

        var audio = PlanAudio(media, settings.AudioBitrateKbps);
        var audioBps = audio.Sum(a => a.BitrateBps);

        var crf = Math.Clamp(settings.Crf, codec.CrfMin, codec.CrfMax);
        var maxShortSide = settings.MaxResolution is > 0 ? settings.MaxResolution : null;
        long videoBps = 0;
        double? minTargetMb = null;

        if (settings.Mode == RateMode.TargetSize)
        {
            minTargetMb = MinimumTargetMb(duration, audioBps, codec.Id);
            if (settings.TargetSizeMb is not { } targetMb || !double.IsFinite(targetMb) || targetMb <= 0)
                return new(null, "Enter a target size.", minTargetMb);

            videoBps = (long)(targetMb * 1_000_000 * 8 * TargetSafety(codec.Id) / duration) - audioBps;
            if (videoBps < MinVideoBps)
            {
                var hint = audioBps > 0 ? " or remove the audio" : string.Empty;
                return new(null,
                    $"{Units.Megabytes(targetMb)} is too small for this {Units.Duration(duration)} video. Choose at least {Units.Megabytes(minTargetMb.Value)}{hint}.",
                    minTargetMb);
            }

            if (settings.AutoResolution) maxShortSide = PickResolution(video, fps, videoBps, codec);
            if (targetMb * 1_000_000 >= media.SizeBytes)
                warnings.Add("The target isn't smaller than the original file, so there is nothing to gain.");
        }

        var (width, height, scaled) = OutputSize(video.Width, video.Height, maxShortSide);

        string? qualityHint = null;
        if (settings.Mode == RateMode.TargetSize)
        {
            var bpp = Bpp(videoBps, width, height, fps);
            qualityHint = bpp >= codec.GoodBpp * 1.5 ? "excellent" : bpp >= codec.GoodBpp ? "good" : bpp >= codec.PoorBpp ? "fair" : "poor";
            if (qualityHint == "poor")
            {
                var better = StandardResolutions.FirstOrDefault(r =>
                    r < Math.Min(width, height) && Bpp(videoBps, OutputSize(video.Width, video.Height, r), fps) >= codec.GoodBpp);
                warnings.Add(better > 0
                    ? $"The bitrate is very low for {width}×{height}, so expect blocky video. A lower resolution such as {better}p would look cleaner."
                    : "The bitrate is very low for this video, so expect visible quality loss.");
            }
        }

        if (video.IsHdr && codec.Id == VideoCodec.H264)
            warnings.Add("HDR video will be converted to 8-bit H.264 without tone mapping, so colors may look washed out. Choose H.265 or AV1 to keep HDR.");

        // 10-bit sources stay 10-bit with HEVC/AV1 (keeps HDR, and compresses gradients better).
        var pixelFormat = video.BitDepth >= 10 && codec.Id != VideoCodec.H264 ? "yuv420p10le" : "yuv420p";

        return new(new EncodePlan
        {
            Codec = codec,
            Mode = settings.Mode,
            Speed = settings.Speed,
            Crf = crf,
            VideoBitrateBps = videoBps,
            VideoStreamIndex = video.Index,
            Width = width,
            Height = height,
            Scaled = scaled,
            Fps = fps,
            FpsLimited = fpsLimited,
            PixelFormat = pixelFormat,
            AudioTracks = audio,
            Warnings = warnings,
            QualityHint = qualityHint,
        }, null, minTargetMb);
    }

    /// <summary>Output size for a short-side cap, keeping the aspect ratio and even dimensions (required by 4:2:0).</summary>
    public static (int Width, int Height, bool Scaled) OutputSize(int sourceWidth, int sourceHeight, int? maxShortSide)
    {
        var shortSide = Math.Min(sourceWidth, sourceHeight);
        if (maxShortSide is { } cap && shortSide > cap)
        {
            var factor = (double)cap / shortSide;
            return (RoundEven(sourceWidth * factor), RoundEven(sourceHeight * factor), true);
        }

        var width = Math.Max(2, sourceWidth & ~1);
        var height = Math.Max(2, sourceHeight & ~1);
        return (width, height, width != sourceWidth || height != sourceHeight);
    }

    /// <summary>Picks the largest resolution at which the bitrate still looks decent (target-size "Auto" resolution).</summary>
    private static int? PickResolution(VideoStreamInfo video, double fps, long videoBps, CodecSpec codec)
    {
        var shortSide = Math.Min(video.Width, video.Height);
        var candidates = StandardResolutions.Where(r => r < shortSide).Prepend(shortSide).ToList();
        foreach (var resolution in candidates)
        {
            if (Bpp(videoBps, OutputSize(video.Width, video.Height, resolution), fps) >= codec.GoodBpp * 0.75)
                return resolution == shortSide ? null : resolution;
        }
        var smallest = candidates[^1];
        return smallest == shortSide ? null : smallest;
    }

    private static List<AudioTrackPlan> PlanAudio(MediaInfo media, int requestedKbps)
    {
        var tracks = new List<AudioTrackPlan>();
        if (requestedKbps <= 0) return tracks; // audio removed

        var requested = Math.Clamp(requestedKbps, 32, 320) * 1000L;
        foreach (var audio in media.Audio)
        {
            var source = audio.BitrateBps;

            // Re-encoding lossy audio that is already small only loses quality, so copy it as-is.
            if (audio.Codec is "aac" or "mp3" && source is > 0 && source <= requested + 16_000)
            {
                tracks.Add(new AudioTrackPlan(audio.Index, AudioAction.Copy, source.Value, null, null));
                continue;
            }

            // Never encode lossy audio at a higher bitrate than it already has.
            var bitrate = requested;
            if (!audio.Lossless && source is > 0) bitrate = Math.Min(bitrate, Math.Max(32_000, source.Value / 8000 * 8000));

            int? channels = audio.Channels > 2 && (bitrate < 256_000 || audio.Channels > 6) ? 2 : null;
            int? sampleRate = audio.SampleRate > 48_000 ? 48_000 : null;
            tracks.Add(new AudioTrackPlan(audio.Index, AudioAction.Encode, bitrate, channels, sampleRate));
        }
        return tracks;
    }

    private static double Bpp(long videoBps, (int Width, int Height, bool) size, double fps) => Bpp(videoBps, size.Width, size.Height, fps);

    private static double Bpp(long videoBps, int width, int height, double fps) => videoBps / (width * (double)height * fps);

    private static int RoundEven(double value) => Math.Max(2, (int)Math.Round(value / 2, MidpointRounding.AwayFromZero) * 2);
}
