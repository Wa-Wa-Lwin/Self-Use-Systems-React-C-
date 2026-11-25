using VideoCompression.Api.Analysis;
using VideoCompression.Api.Compression;
using VideoCompression.Api.Ffmpeg;
using static VideoCompression.Tests.TestData;

namespace VideoCompression.Tests;

public class SizeEstimatorTests
{
    [Fact]
    public void Short_videos_are_encoded_whole()
    {
        var segment = Assert.Single(SizeEstimator.PlanSegments(6));
        Assert.Equal((0.0, 6.0), (segment.Start, segment.Length));
    }

    [Theory]
    [InlineData(60, 3)]
    [InlineData(180, 4)]
    [InlineData(3600, 5)]
    public void Longer_videos_are_sampled_across_their_length(double duration, int expected)
    {
        var segments = SizeEstimator.PlanSegments(duration);
        Assert.Equal(expected, segments.Count);
        Assert.All(segments, s =>
        {
            Assert.True(s.Start >= 0 && s.Start + s.Length <= duration + 0.001);
            Assert.True(s.Length >= 2);
        });
        Assert.True(segments[0].Start < duration * 0.25 && segments[^1].Start > duration * 0.6, "samples should span the video");
    }

    [Fact]
    public void Parses_ffprobe_packet_csv()
    {
        var stats = new PacketStats();
        SizeEstimator.ParsePackets("15234,K__\r\n812,___\n900,__\n\nbogus\n", stats);

        Assert.Equal(3, stats.Packets);
        Assert.Equal(1, stats.KeyCount);
        Assert.Equal(15234, stats.KeyBytes);
        Assert.Equal(1712, stats.OtherBytes);
    }

    [Fact]
    public void Extrapolation_does_not_overcount_the_forced_keyframes()
    {
        // Static content (e.g. a screen recording): 3 samples, each one 100 KB keyframe + 59 tiny 1 KB frames.
        var stats = new PacketStats();
        for (var segment = 0; segment < 3; segment++)
        {
            stats.Add(100_000, keyframe: true);
            for (var i = 0; i < 59; i++) stats.Add(1_000, keyframe: false);
        }

        const double totalFrames = 600 * 30; // 10 minutes at 30 fps
        var estimate = SizeEstimator.Extrapolate(stats, segmentCount: 3, totalFrames, gopFrames: 250);
        var naive = stats.TotalBytes / (double)stats.Packets * totalFrames;

        // 72 GOPs → 72 keyframes × 100 KB + 17,928 frames × 1 KB.
        Assert.Equal(72 * 100_000 + (totalFrames - 72) * 1_000, estimate, 3);
        Assert.True(estimate < naive * 0.6, $"estimate {estimate} should be far below the naive {naive}");
    }

    [Fact]
    public void Extrapolation_counts_scene_cut_keyframes()
    {
        var stats = new PacketStats();
        for (var segment = 0; segment < 3; segment++)
        {
            stats.Add(50_000, keyframe: true);              // forced
            for (var i = 0; i < 29; i++) stats.Add(5_000, keyframe: false);
            stats.Add(50_000, keyframe: true);              // a natural scene cut
            for (var i = 0; i < 29; i++) stats.Add(5_000, keyframe: false);
        }

        var withCuts = SizeEstimator.Extrapolate(stats, 3, 18_000, 250);
        var keyframesPerFrame = 3.0 / stats.Packets;
        var expectedKeys = Math.Ceiling(18_000 / 250.0) + keyframesPerFrame * 18_000;
        Assert.Equal(expectedKeys * 50_000 + (18_000 - expectedKeys) * 5_000, withCuts, 3);
    }

    [Fact]
    public void Parses_the_encoder_list()
    {
        const string output = """
            Encoders:
             V..... = Video
             A..... = Audio
             ------
             V....D libx264              libx264 H.264 / AVC / MPEG-4 AVC / MPEG-4 part 10 (codec h264)
             V....D libx265              libx265 H.265 / HEVC (codec hevc)
             V....D libsvtav1            SVT-AV1(Scalable Video Technology for AV1) encoder (codec av1)
             A....D aac                  AAC (Advanced Audio Coding)
            """;
        var encoders = FfmpegTools.ParseEncoders(output);
        Assert.Contains("libx264", encoders);
        Assert.Contains("libx265", encoders);
        Assert.Contains("libsvtav1", encoders);
        Assert.Contains("aac", encoders);
        Assert.DoesNotContain("Encoders:", encoders);
    }
}

public class VerdictTests
{
    // Savings are given in display order: light, balanced (recommended), strong, maximum.

    [Fact]
    public void Waits_for_the_recommended_preset()
    {
        Assert.Null(VideoAnalyzer.ComputeVerdict(RecordWithSavings(false, 30, null, 60)));
    }

    [Theory]
    [InlineData(70, VerdictLevel.High)]
    [InlineData(30, VerdictLevel.Good)]
    [InlineData(10, VerdictLevel.Low)]
    public void Grades_by_the_recommended_savings(double balanced, VerdictLevel level)
    {
        var verdict = VideoAnalyzer.ComputeVerdict(RecordWithSavings(true, 5, balanced, balanced + 5, balanced + 10));
        Assert.NotNull(verdict);
        Assert.Equal(level, verdict.Level);
        Assert.Equal("balanced", verdict.SuggestedPresetId);
        Assert.Equal(balanced, verdict.RecommendedSavingsPercent, 0);
    }

    [Fact]
    public void Already_efficient_video_suggests_lowering_resolution()
    {
        var verdict = VideoAnalyzer.ComputeVerdict(RecordWithSavings(true, -40, 2, 35, 60));
        Assert.NotNull(verdict);
        Assert.Equal(VerdictLevel.Minimal, verdict.Level);
        Assert.Equal("strong", verdict.SuggestedPresetId); // gentlest option that still saves ≥ 10%
        Assert.Contains("Maximum", verdict.Summary);
    }

    [Fact]
    public void Says_so_when_nothing_helps()
    {
        var verdict = VideoAnalyzer.ComputeVerdict(RecordWithSavings(true, -50, -5, 2, 4));
        Assert.NotNull(verdict);
        Assert.Equal(VerdictLevel.None, verdict.Level);
    }

    [Fact]
    public void Insights_flag_high_bitrate_4k_and_lossless_audio()
    {
        var media = Media(width: 3840, height: 2160, fps: 60, videoBps: 120_000_000, codec: "prores", audio: [Pcm()]);
        var titles = VideoAnalyzer.BuildInsights(media).Select(i => i.Title).ToList();

        Assert.Contains("Editing or lossless format", titles);
        Assert.Contains("Very high bitrate", titles);
        Assert.Contains("High resolution", titles);
        Assert.Contains("High frame rate", titles);
        Assert.Contains("Uncompressed audio", titles);
    }

    [Theory]
    [InlineData(1920, 1080, 30, 4.5e6, 5.5e6)]
    [InlineData(1280, 720, 30, 2.3e6, 3.1e6)]
    [InlineData(3840, 2160, 30, 12e6, 16e6)]
    public void Typical_bitrates_are_realistic(int width, int height, double fps, double low, double high)
    {
        var typical = VideoAnalyzer.TypicalBitrate(width, height, fps);
        Assert.InRange(typical, low, high);
    }
}
