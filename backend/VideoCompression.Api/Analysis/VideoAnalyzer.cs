using VideoCompression.Api.Compression;
using VideoCompression.Api.Ffmpeg;
using VideoCompression.Api.Media;
using VideoCompression.Api.Storage;

namespace VideoCompression.Api.Analysis;

/// <summary>Works out whether a video can be compressed, by how much, and with which options.</summary>
public sealed class VideoAnalyzer(SizeEstimator estimator, ILogger<VideoAnalyzer> logger)
{
    /// <summary>Display order, from gentlest to strongest.</summary>
    private static readonly string[] DisplayOrder = ["light", "balanced", "strong", "maximum"];

    /// <summary>Measure the recommended preset first (it drives the verdict), the expensive full-resolution one last.</summary>
    private static readonly string[] MeasureOrder = ["balanced", "strong", "maximum", "light"];

    public VideoRecord CreateRecord(string id, string fileName, string folder, string sourcePath, MediaInfo media, FfmpegStatus ffmpeg)
    {
        var presets = PresetDefinitions(ffmpeg)
            .Select(definition =>
            {
                var plan = EncodePlanner.Create(media, definition.Settings, ffmpeg);
                return new PresetState(definition, plan.Plan, plan.Error);
            })
            .ToList();

        return new VideoRecord
        {
            Id = id,
            FileName = fileName,
            Folder = folder,
            SourcePath = sourcePath,
            Media = media,
            Insights = BuildInsights(media),
            Presets = presets,
            MinTargetSizeMb = EncodePlanner.MinimumTargetMb(media.DurationSeconds, 0, VideoCodec.H264),
        };
    }

    /// <summary>Test-encodes every preset (in the background) and keeps the verdict up to date as results arrive.</summary>
    public async Task AnalyzeAsync(VideoRecord video, CancellationToken cancellationToken)
    {
        foreach (var id in MeasureOrder)
        {
            var preset = video.Presets.FirstOrDefault(p => p.Definition.Id == id);
            if (preset?.Plan is null) continue;

            lock (video.Sync) preset.Status = PresetStatus.Measuring;
            try
            {
                var estimate = await estimator.EstimateAsync(video, preset.Plan, cancellationToken);
                lock (video.Sync)
                {
                    preset.Estimate = estimate;
                    preset.Status = PresetStatus.Done;
                    video.Verdict = ComputeVerdict(video);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not measure preset {Preset} for {Video}", id, video.Id);
                lock (video.Sync)
                {
                    preset.Status = PresetStatus.Failed;
                    preset.Error = ex.Message;
                }
            }
        }

        lock (video.Sync)
        {
            video.AnalysisDone = true;
            video.Verdict = ComputeVerdict(video);
        }
    }

    internal static IReadOnlyList<PresetDefinition> PresetDefinitions(FfmpegStatus ffmpeg)
    {
        var hevc = ffmpeg.HasEncoder(Codecs.H265.Encoder);

        static CompressionSettings Quality(VideoCodec codec, int crf, int? maxResolution, int audioKbps, double? maxFps = null) => new()
        {
            Codec = codec,
            Mode = RateMode.Quality,
            Crf = crf,
            MaxResolution = maxResolution,
            MaxFps = maxFps,
            AudioBitrateKbps = audioKbps,
            Speed = EncodeSpeed.Balanced,
        };

        return
        [
            new("light", "Light", "Near-original quality",
                "Gentle compression in H.264, the format every device and app can play.",
                false, Quality(VideoCodec.H264, 22, null, 160)),
            new("balanced", "Balanced", "Best size-to-quality ratio",
                hevc
                    ? "Modern H.265 encoding, up to 1080p. Looks nearly identical at a fraction of the size."
                    : "Efficient H.264 encoding, up to 1080p. Looks nearly identical at a fraction of the size.",
                true, hevc ? Quality(VideoCodec.H265, 26, 1080, 128) : Quality(VideoCodec.H264, 24, 1080, 128)),
            new("strong", "Strong", "Great for sharing",
                "720p with stronger compression. Ideal for messaging apps and email.",
                false, hevc ? Quality(VideoCodec.H265, 28, 720, 96) : Quality(VideoCodec.H264, 26, 720, 96)),
            new("maximum", "Maximum", "Smallest possible file",
                "480p at up to 30 fps with aggressive compression. Quality drops noticeably.",
                false, hevc ? Quality(VideoCodec.H265, 31, 480, 64, 30) : Quality(VideoCodec.H264, 28, 480, 64, 30)),
        ];
    }

    /// <summary>Must be called while holding <see cref="VideoRecord.Sync"/>.</summary>
    internal static Verdict? ComputeVerdict(VideoRecord video)
    {
        var measured = video.Presets.Where(p => p.Estimate is not null).ToList();
        var recommended = measured.FirstOrDefault(p => p.Definition.Recommended);

        if (recommended is null)
        {
            if (!video.AnalysisDone) return null; // wait for the recommended preset
            if (measured.Count == 0)
                return new Verdict(VerdictLevel.Unknown, "Couldn't measure this video",
                    "The test encodes failed, so the result can't be predicted. You can still try compressing it.", 0, 0, null);
            recommended = measured.MaxBy(p => p.Estimate!.SavingsPercent)!;
        }

        var best = measured.MaxBy(p => p.Estimate!.SavingsPercent)!;
        var savings = recommended.Estimate!.SavingsPercent;
        var bestSavings = best.Estimate!.SavingsPercent;
        var original = Units.Bytes(video.Media.SizeBytes);
        var estimated = Units.Bytes(recommended.Estimate.Bytes);
        var name = recommended.Definition.Name;
        var bestName = best.Definition.Name;

        // Suggest the gentlest option that still makes a real difference.
        var suggested = savings >= 5
            ? recommended.Definition.Id
            : measured
                  .Where(p => p.Estimate!.SavingsPercent >= 10)
                  .OrderBy(p => Array.IndexOf(DisplayOrder, p.Definition.Id))
                  .Select(p => p.Definition.Id)
                  .FirstOrDefault()
              ?? (bestSavings > 0 ? best.Definition.Id : null);

        return savings switch
        {
            >= 45 => new Verdict(VerdictLevel.High, "Highly compressible",
                $"{name} should shrink it from {original} to about {estimated} ({Units.Percent(savings)} smaller) with little visible quality loss.",
                savings, bestSavings, suggested),
            >= 20 => new Verdict(VerdictLevel.Good, "Good compression potential",
                $"{name} should save about {Units.Percent(savings)} ({original} → {estimated}) while keeping the picture sharp.",
                savings, bestSavings, suggested),
            >= 5 => new Verdict(VerdictLevel.Low, "Limited compression potential",
                $"This video is already fairly efficient. {name} saves about {Units.Percent(savings)}"
                + (bestSavings >= savings + 10 ? $"; {bestName} reaches {Units.Percent(bestSavings)} by lowering the resolution." : "."),
                savings, bestSavings, suggested),
            _ when bestSavings >= 10 => new Verdict(VerdictLevel.Minimal, "Already well compressed",
                $"Re-encoding at the same quality won't make it meaningfully smaller. Lowering the resolution with {bestName} can still save about {Units.Percent(bestSavings)}.",
                savings, bestSavings, suggested),
            _ => new Verdict(VerdictLevel.None, "Can't be made meaningfully smaller",
                "This file is already about as small as it gets without a big drop in quality. Use Target size if you need to force a smaller file.",
                savings, bestSavings, suggested),
        };
    }

    internal static IReadOnlyList<Insight> BuildInsights(MediaInfo media)
    {
        var insights = new List<Insight>();
        var video = media.Video;
        var resolution = Units.Resolution(video.Width, video.Height);

        switch (CodecFacts.VideoEfficiency(video.Codec))
        {
            case CodecEfficiency.Intermediate:
                insights.Add(new(InsightTone.Positive, "Editing or lossless format",
                    $"{video.CodecLabel} is a production format that is many times larger than needed for viewing. Expect a dramatic reduction."));
                break;
            case CodecEfficiency.Legacy:
                insights.Add(new(InsightTone.Positive, "Outdated codec",
                    $"{video.CodecLabel} is an older format. Modern codecs need far less data for the same picture."));
                break;
            case CodecEfficiency.Standard:
                insights.Add(new(InsightTone.Positive, "H.264 video",
                    "Re-encoding with H.265 or AV1 typically saves 30–50% at the same visual quality."));
                break;
            default:
                insights.Add(new(InsightTone.Neutral, "Already a modern codec",
                    $"{video.CodecLabel} is efficient, so savings have to come from bitrate or resolution."));
                break;
        }

        var typical = TypicalBitrate(video.Width, video.Height, video.Fps);
        var ratio = video.BitrateBps / typical;
        var bitrate = Units.Bitrate(video.BitrateBps);
        if (ratio >= 2)
            insights.Add(new(InsightTone.Positive, "Very high bitrate",
                $"{bitrate} is about {ratio:0.#}× what a typical {resolution} video needs."));
        else if (ratio >= 1.2)
            insights.Add(new(InsightTone.Positive, "Above-average bitrate",
                $"{bitrate} is higher than the ~{Units.Bitrate(typical)} that is typical for {resolution}."));
        else if (ratio < 0.6)
            insights.Add(new(InsightTone.Warning, "Bitrate is already low",
                $"At {bitrate} this video is already lean for {resolution}. Big savings will need a lower resolution."));
        else
            insights.Add(new(InsightTone.Neutral, "Typical bitrate", $"{bitrate} is in the normal range for {resolution}."));

        var shortSide = Math.Min(video.Width, video.Height);
        if (shortSide > 1080)
        {
            var (w, h, _) = EncodePlanner.OutputSize(video.Width, video.Height, 1080);
            var removed = (1 - w * (double)h / (video.Width * (double)video.Height)) * 100;
            insights.Add(new(InsightTone.Positive, "High resolution",
                $"{resolution} ({video.Width}×{video.Height}). Downscaling to 1080p removes {Units.Percent(removed)} of the pixels."));
        }

        if (video.Fps > 32)
            insights.Add(new(InsightTone.Neutral, "High frame rate",
                $"{Units.Fps(video.Fps)} fps. Capping at 30 fps (Maximum or Custom) typically saves another 20–35%."));

        var lossless = media.Audio.FirstOrDefault(a => a.Lossless);
        var heavyLossy = media.Audio.FirstOrDefault(a => !a.Lossless && a.BitrateBps > 256_000);
        if (lossless is not null)
            insights.Add(new(InsightTone.Positive, "Uncompressed audio",
                $"{lossless.CodecLabel} audio{(lossless.BitrateBps is { } bps ? $" uses {Units.Bitrate(bps)}" : string.Empty)}. AAC at 128 kbps sounds virtually the same at a fraction of the size."));
        else if (heavyLossy is not null)
            insights.Add(new(InsightTone.Positive, "High-bitrate audio",
                $"{heavyLossy.CodecLabel} at {Units.Bitrate(heavyLossy.BitrateBps!.Value)} can be reduced without audible loss."));

        var audioBps = media.Audio.Sum(a => a.BitrateBps ?? 0);
        if (media.BitrateBps > 0 && audioBps / (double)media.BitrateBps > 0.25)
            insights.Add(new(InsightTone.Neutral, "Audio is a big part of the file",
                $"Audio makes up about {Units.Percent(audioBps * 100.0 / media.BitrateBps)} of this file's data."));

        if (media.Audio.Count == 0)
            insights.Add(new(InsightTone.Neutral, "No audio track", "Only the video needs compressing."));
        else if (media.Audio.Count > 1)
            insights.Add(new(InsightTone.Neutral, "Multiple audio tracks", $"{media.Audio.Count} audio tracks. All of them are kept."));

        if (video.IsHdr)
            insights.Add(new(InsightTone.Warning, "HDR video",
                "H.265 and AV1 keep the 10-bit HDR picture. H.264 converts it to standard range, so colors may look washed out."));

        if (media.SizeBytes < 3_000_000)
            insights.Add(new(InsightTone.Neutral, "Small file", "This file is already small, so the absolute savings will be modest."));

        return insights;
    }

    /// <summary>
    /// Bitrate of a good-quality, efficiently encoded H.264 video at this resolution and frame rate
    /// (≈5 Mbps for 1080p30, ≈2.7 Mbps for 720p30, ≈14 Mbps for 4K30).
    /// </summary>
    internal static double TypicalBitrate(int width, int height, double fps)
    {
        const double fullHdPixels = 1920 * 1080;
        var pixels = Math.Max(1, width * (double)height);
        var bitsPerPixel = 0.08 * Math.Pow(fullHdPixels / pixels, 0.25); // small frames need more bits per pixel
        var frameRate = fps > 0 ? fps : 30;
        return pixels * bitsPerPixel * 30 * Math.Pow(frameRate / 30, 0.75);
    }
}
