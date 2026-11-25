using VideoCompression.Api.Analysis;
using VideoCompression.Api.Compression;
using VideoCompression.Api.Ffmpeg;
using VideoCompression.Api.Media;
using VideoCompression.Api.Storage;

namespace VideoCompression.Tests;

internal static class TestData
{
    public static FfmpegStatus Ffmpeg(params string[] encoders) => new(
        true, "ffmpeg", "ffprobe", "7.1",
        new HashSet<string>(encoders.Length > 0 ? encoders : ["libx264", "libx265", "libsvtav1", "aac"]),
        null, DateTimeOffset.UtcNow);

    public static MediaInfo Media(
        int width = 1920,
        int height = 1080,
        double fps = 30,
        double duration = 100,
        long videoBps = 8_000_000,
        string codec = "h264",
        int bitDepth = 8,
        bool hdr = false,
        params AudioStreamInfo[] audio)
    {
        var tracks = audio.Length > 0 ? audio : [Aac()];
        var total = videoBps + tracks.Sum(a => a.BitrateBps ?? 0);
        return new MediaInfo("MP4", duration, (long)(total * duration / 8), total,
            new VideoStreamInfo(0, codec, CodecFacts.Label(codec), width, height, fps, videoBps, false,
                bitDepth > 8 ? "yuv420p10le" : "yuv420p", bitDepth, hdr, 0),
            tracks);
    }

    public static AudioStreamInfo Aac(int index = 1, long bitrate = 128_000, int channels = 2) =>
        new(index, "aac", "AAC", bitrate, channels, 48_000, false, "eng");

    public static AudioStreamInfo Pcm(int index = 1, int channels = 6) =>
        new(index, "pcm_s24le", "PCM (uncompressed)", 48_000L * 24 * channels, channels, 48_000, true, null);

    public static EncodePlan Plan(MediaInfo media, CompressionSettings settings)
    {
        var result = EncodePlanner.Create(media, settings, Ffmpeg());
        Assert.True(result.Ok, result.Error);
        return result.Plan;
    }

    /// <summary>A video record whose presets already have the given savings (in display order light, balanced, strong, maximum).</summary>
    public static VideoRecord RecordWithSavings(bool analysisDone, params double?[] savings)
    {
        var media = Media();
        var definitions = VideoAnalyzer.PresetDefinitions(Ffmpeg());
        var presets = definitions.Select((definition, i) =>
        {
            var state = new PresetState(definition, Plan(media, definition.Settings), null);
            if (i < savings.Length && savings[i] is { } s)
            {
                state.Estimate = SizeEstimate.Create(media.SizeBytes * (1 - s / 100), media.SizeBytes, 0.9, 1.1, EstimateMethod.Sampled);
                state.Status = PresetStatus.Done;
            }
            return state;
        }).ToList();

        return new VideoRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            FileName = "test.mp4",
            Folder = Path.GetTempPath(),
            SourcePath = "test.mp4",
            Media = media,
            Insights = [],
            Presets = presets,
            MinTargetSizeMb = 1,
            AnalysisDone = analysisDone,
        };
    }
}
