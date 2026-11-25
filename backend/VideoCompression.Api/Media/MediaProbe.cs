using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using VideoCompression.Api.Ffmpeg;

namespace VideoCompression.Api.Media;

/// <summary>Reads stream information with ffprobe.</summary>
public sealed partial class MediaProbe(FfmpegTools tools, ILogger<MediaProbe> logger)
{
    /// <returns>The media info, or null when the file isn't a readable video.</returns>
    public async Task<MediaInfo?> ProbeAsync(string path, long fileSize, CancellationToken cancellationToken)
    {
        var ff = await tools.RequireAsync(cancellationToken);
        var result = await ProcessRunner.RunAsync(ff.FfprobePath!,
            ["-v", "error", "-print_format", "json", "-show_format", "-show_streams", path],
            cancellationToken, captureStdout: true);

        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Stdout))
        {
            logger.LogInformation("ffprobe could not read {Path}: {Error}", path, FfmpegErrors.Summarize(result));
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(result.Stdout);
            return Parse(document.RootElement, Path.GetExtension(path), fileSize);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "ffprobe returned invalid JSON for {Path}", path);
            return null;
        }
    }

    /// <summary>Turns ffprobe's JSON (-show_format -show_streams) into <see cref="MediaInfo"/>.</summary>
    internal static MediaInfo? Parse(JsonElement root, string extension, long fileSize)
    {
        var format = Obj(root, "format");
        var streams = root.ValueKind == JsonValueKind.Object
                      && root.TryGetProperty("streams", out var list)
                      && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().ToList()
            : [];

        // Skip cover art: it shows up as a one-frame "video" stream in music/podcast files.
        var video = streams.FirstOrDefault(s => Str(s, "codec_type") == "video" && Num(Obj(s, "disposition"), "attached_pic") != 1);
        if (video.ValueKind != JsonValueKind.Object) return null;

        var duration = Num(format, "duration") ?? Num(video, "duration") ?? TagDuration(video) ?? 0;
        if (duration <= 0.05) return null;

        var size = Long(format, "size") ?? fileSize;
        var totalBitrate = Long(format, "bit_rate") ?? (long)(size * 8 / duration);

        var audio = streams
            .Where(s => Str(s, "codec_type") == "audio")
            .Select(s =>
            {
                var codec = Str(s, "codec_name") ?? "unknown";
                return new AudioStreamInfo(
                    Index: (int)(Num(s, "index") ?? 0),
                    Codec: codec,
                    CodecLabel: CodecFacts.Label(codec),
                    BitrateBps: Long(s, "bit_rate") ?? TagBitrate(s),
                    Channels: (int)(Num(s, "channels") ?? 2),
                    SampleRate: (int)(Num(s, "sample_rate") ?? 48000),
                    Lossless: CodecFacts.IsLosslessAudio(codec),
                    Language: Str(Obj(s, "tags"), "language"));
            })
            .ToList();

        var videoCodec = Str(video, "codec_name") ?? "unknown";
        var width = (int)(Num(video, "width") ?? 0);
        var height = (int)(Num(video, "height") ?? 0);
        if (width <= 0 || height <= 0) return null;

        var rotation = Rotation(video);
        if (Math.Abs(rotation) % 180 == 90) (width, height) = (height, width);

        var fps = Rational(Str(video, "avg_frame_rate"));
        if (fps is <= 0 or > 480) fps = Rational(Str(video, "r_frame_rate"));
        if (fps is <= 0 or > 480) fps = 30;

        var videoBitrate = Long(video, "bit_rate") ?? TagBitrate(video);
        var bitrateEstimated = videoBitrate is null;
        if (videoBitrate is null)
        {
            // Matroska/WebM often omit per-stream bitrates: derive it from the total minus the audio.
            var audioBitrate = audio.Sum(a => a.BitrateBps ?? 128_000);
            videoBitrate = Math.Max(totalBitrate / 10, totalBitrate - audioBitrate);
        }

        var pixelFormat = Str(video, "pix_fmt");
        var transfer = Str(video, "color_transfer");

        return new MediaInfo(
            Container: ContainerLabel(extension, Str(format, "format_name")),
            DurationSeconds: duration,
            SizeBytes: size,
            BitrateBps: totalBitrate,
            Video: new VideoStreamInfo(
                Index: (int)(Num(video, "index") ?? 0),
                Codec: videoCodec,
                CodecLabel: CodecFacts.Label(videoCodec),
                Width: width,
                Height: height,
                Fps: Math.Round(fps, 3),
                BitrateBps: videoBitrate.Value,
                BitrateEstimated: bitrateEstimated,
                PixelFormat: pixelFormat,
                BitDepth: BitDepth(video, pixelFormat),
                IsHdr: transfer is "smpte2084" or "arib-std-b67",
                Rotation: rotation),
            Audio: audio);
    }

    private static string ContainerLabel(string extension, string? formatName)
    {
        var ext = extension.TrimStart('.').ToUpperInvariant();
        if (ext.Length > 0) return ext == "WEBM" ? "WebM" : ext;
        return formatName?.Split(',')[0].ToUpperInvariant() ?? "Unknown";
    }

    private static int Rotation(JsonElement video)
    {
        if (video.TryGetProperty("side_data_list", out var sideData) && sideData.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in sideData.EnumerateArray())
            {
                if (Num(item, "rotation") is { } rotation) return (int)Math.Round(rotation);
            }
        }
        return (int)(Num(Obj(video, "tags"), "rotate") ?? 0);
    }

    private static int BitDepth(JsonElement video, string? pixelFormat)
    {
        if (Num(video, "bits_per_raw_sample") is { } bits and >= 8 and <= 16) return (int)bits;
        if (pixelFormat is not null)
        {
            var match = BitDepthPattern().Match(pixelFormat);
            if (match.Success) return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        }
        return 8;
    }

    private static long? TagBitrate(JsonElement stream)
    {
        var tags = Obj(stream, "tags");
        return Long(tags, "BPS") ?? Long(tags, "BPS-eng");
    }

    private static double? TagDuration(JsonElement stream)
    {
        var tags = Obj(stream, "tags");
        var text = Str(tags, "DURATION") ?? Str(tags, "DURATION-eng");
        if (text is null) return null;
        // "00:01:23.456000000" (nanoseconds) – TimeSpan only accepts 7 fractional digits.
        var dot = text.IndexOf('.');
        if (dot >= 0 && text.Length > dot + 8) text = text[..(dot + 8)];
        return TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var value) ? value.TotalSeconds : null;
    }

    private static double Rational(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        var parts = value.Split('/');
        if (parts.Length == 2
            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var denominator))
            return denominator > 0 ? numerator / denominator : 0;
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : 0;
    }

    private static JsonElement Obj(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : default;

    private static string? Str(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
    }

    private static double? Num(JsonElement element, string name) =>
        double.TryParse(Str(element, name), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number)
            ? number
            : null;

    private static long? Long(JsonElement element, string name) => Num(element, name) is { } number and > 0 ? (long)number : null;

    [GeneratedRegex(@"p(\d{2})(?:le|be)?$")]
    private static partial Regex BitDepthPattern();
}
