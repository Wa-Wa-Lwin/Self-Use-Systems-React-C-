using System.Globalization;
using VideoCompression.Api.Ffmpeg;
using VideoCompression.Api.Storage;

namespace VideoCompression.Api.Compression;

public enum EstimateMethod
{
    /// <summary>The whole (short) video was test-encoded.</summary>
    Exact,
    /// <summary>Short clips from across the video were test-encoded and extrapolated.</summary>
    Sampled,
    /// <summary>Target-size mode: the size is set by the bitrate, no measurement needed.</summary>
    Target,
}

public sealed record SizeEstimate(long Bytes, long LowBytes, long HighBytes, double SavingsPercent, EstimateMethod Method)
{
    public static SizeEstimate Create(double bytes, long originalBytes, double lowFactor, double highFactor, EstimateMethod method)
    {
        var size = (long)Math.Max(1, bytes);
        var savings = originalBytes > 0 ? Math.Round((1 - size / (double)originalBytes) * 100, 1) : 0;
        return new(size, (long)(size * lowFactor), (long)(size * highFactor), savings, method);
    }
}

/// <summary>Measured size of the video stream alone for one set of video settings (audio is added arithmetically).</summary>
public sealed record VideoMeasurement(double VideoBytes, bool Exact);

/// <summary>Packet statistics gathered from the test encodes.</summary>
internal sealed class PacketStats
{
    public long Packets { get; private set; }
    public long TotalBytes { get; private set; }
    public long KeyCount { get; private set; }
    public long KeyBytes { get; private set; }
    public long OtherCount { get; private set; }
    public long OtherBytes { get; private set; }

    public void Add(long size, bool keyframe)
    {
        Packets++;
        TotalBytes += size;
        if (keyframe)
        {
            KeyCount++;
            KeyBytes += size;
        }
        else
        {
            OtherCount++;
            OtherBytes += size;
        }
    }
}

internal readonly record struct Segment(double Start, double Length);

/// <summary>
/// Predicts the compressed size by really encoding a few short clips with the exact settings
/// (content matters far more than any formula), then extrapolating to the full duration.
/// </summary>
public sealed class SizeEstimator(FfmpegTools tools, ILogger<SizeEstimator> logger)
{
    /// <summary>Total seconds of video test-encoded per estimate.</summary>
    private const double SampleBudgetSeconds = 8;
    private const double ContainerOverhead = 1.004;
    private const long ContainerFixedBytes = 16_000;

    // Test encodes are CPU-heavy; run one at a time so they don't starve each other.
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<SizeEstimate> EstimateAsync(VideoRecord video, EncodePlan plan, CancellationToken cancellationToken)
    {
        var media = video.Media;
        var audioBytes = plan.AudioBitrateBps * media.DurationSeconds / 8;

        if (plan.Mode == RateMode.TargetSize)
        {
            var expected = (plan.VideoBitrateBps * media.DurationSeconds / 8 + audioBytes) * ContainerOverhead + ContainerFixedBytes;
            var spread = plan.TwoPass ? 0.04 : 0.10;
            return SizeEstimate.Create(expected, media.SizeBytes, 1 - spread, 1 + spread, EstimateMethod.Target);
        }

        var measurement = await MeasureVideoAsync(video, plan, cancellationToken);
        var total = (measurement.VideoBytes + audioBytes) * ContainerOverhead + ContainerFixedBytes;
        return measurement.Exact
            ? SizeEstimate.Create(total, media.SizeBytes, 0.97, 1.03, EstimateMethod.Exact)
            : SizeEstimate.Create(total, media.SizeBytes, 0.85, 1.15, EstimateMethod.Sampled);
    }

    private async Task<VideoMeasurement> MeasureVideoAsync(VideoRecord video, EncodePlan plan, CancellationToken cancellationToken)
    {
        var key = plan.VideoKey;
        if (video.Measurements.TryGetValue(key, out var cached)) return cached;

        var ffmpeg = await tools.RequireAsync(cancellationToken);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (video.Measurements.TryGetValue(key, out cached)) return cached;
            var measurement = await MeasureCoreAsync(ffmpeg, video, plan, cancellationToken);
            video.Measurements[key] = measurement;
            return measurement;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<VideoMeasurement> MeasureCoreAsync(FfmpegStatus ffmpeg, VideoRecord video, EncodePlan plan, CancellationToken cancellationToken)
    {
        var duration = video.Media.DurationSeconds;
        var segments = PlanSegments(duration);
        var whole = segments.Count == 1 && segments[0].Start == 0 && segments[0].Length >= duration;

        var folder = Path.Combine(video.Folder, "samples");
        Directory.CreateDirectory(folder);

        var stats = new PacketStats();
        long fileBytes = 0;
        double sampledSeconds = 0;

        foreach (var segment in segments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var output = Path.Combine(folder, $"{Guid.NewGuid():N}.mp4");
            try
            {
                var args = new List<string> { "-hide_banner", "-nostdin", "-y", "-loglevel", "error", "-nostats" };
                if (!whole) args.AddRange(["-ss", Units.Arg(segment.Start), "-t", Units.Arg(segment.Length)]);
                args.AddRange(["-i", video.SourcePath, "-map", $"0:{plan.VideoStreamIndex}"]);
                args.AddRange(plan.VideoArguments());
                args.AddRange(["-an", "-f", "mp4", output]);

                var run = await ProcessRunner.RunAsync(ffmpeg.FfmpegPath!, args, cancellationToken);
                if (run.ExitCode != 0 || !File.Exists(output))
                    throw new InvalidOperationException("FFmpeg couldn't encode a test sample: " + FfmpegErrors.Summarize(run));

                fileBytes += new FileInfo(output).Length;
                sampledSeconds += whole ? duration : Math.Min(segment.Length, duration - segment.Start);
                await CollectPacketsAsync(ffmpeg, output, stats, cancellationToken);
            }
            finally
            {
                TryDelete(output);
            }
        }

        double videoBytes;
        if (whole)
            videoBytes = stats.TotalBytes > 0 ? stats.TotalBytes : fileBytes;
        else if (stats.KeyCount > 0 && stats.Packets > segments.Count)
            videoBytes = Extrapolate(stats, segments.Count, duration * plan.Fps, plan.Codec.GopFrames);
        else
            videoBytes = fileBytes / Math.Max(0.1, sampledSeconds) * duration;

        logger.LogDebug("Measured {Settings}: {Bytes} video bytes from {Segments} segment(s)", plan.Summary, (long)videoBytes, segments.Count);
        return new VideoMeasurement(videoBytes, whole);
    }

    /// <summary>
    /// Every test clip starts with a forced keyframe, which is several times larger than a normal frame.
    /// Scaling the clip size up directly would overestimate (badly, for static content like screen
    /// recordings), so model the full encode as keyframes × average keyframe + other frames × average frame.
    /// </summary>
    internal static double Extrapolate(PacketStats stats, int segmentCount, double totalFrames, int gopFrames)
    {
        var averageKey = stats.KeyBytes / (double)stats.KeyCount;
        var averageOther = stats.OtherCount > 0 ? stats.OtherBytes / (double)stats.OtherCount : averageKey;
        var sceneCutsPerFrame = Math.Max(0, stats.KeyCount - segmentCount) / (double)stats.Packets;
        var keyframes = Math.Min(totalFrames, Math.Ceiling(totalFrames / gopFrames) + sceneCutsPerFrame * totalFrames);
        return keyframes * averageKey + (totalFrames - keyframes) * averageOther;
    }

    /// <summary>Short videos are encoded whole; longer ones are sampled at evenly spaced points.</summary>
    internal static List<Segment> PlanSegments(double duration)
    {
        if (duration <= SampleBudgetSeconds * 1.25) return [new Segment(0, duration)];

        var count = Math.Clamp((int)Math.Round(duration / 45), 3, 5);
        var length = Math.Max(2.0, SampleBudgetSeconds / count);
        var segments = new List<Segment>(count);
        for (var i = 0; i < count; i++)
        {
            var center = duration * (i + 0.5) / count;
            var start = Math.Clamp(center - length / 2, 0, Math.Max(0, duration - length));
            segments.Add(new Segment(Math.Round(start, 3), length));
        }
        return segments;
    }

    private static async Task CollectPacketsAsync(FfmpegStatus ffmpeg, string file, PacketStats stats, CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync(ffmpeg.FfprobePath!,
            ["-v", "error", "-select_streams", "v:0", "-show_entries", "packet=size,flags", "-of", "csv=p=0", file],
            cancellationToken, captureStdout: true);
        if (result.ExitCode == 0) ParsePackets(result.Stdout, stats);
    }

    /// <summary>Parses ffprobe CSV lines such as "15234,K__" (keyframe) or "812,___".</summary>
    internal static void ParsePackets(string csv, PacketStats stats)
    {
        foreach (var line in csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            long? size = null;
            var keyframe = false;
            foreach (var field in line.Split(','))
            {
                if (long.TryParse(field, NumberStyles.None, CultureInfo.InvariantCulture, out var number)) size ??= number;
                else if (field.Contains('K')) keyframe = true;
            }
            if (size is { } bytes) stats.Add(bytes, keyframe);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Swept up later by the storage cleanup.
        }
    }
}
