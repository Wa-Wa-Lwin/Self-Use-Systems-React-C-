using VideoCompression.Api.Compression;
using static VideoCompression.Tests.TestData;

namespace VideoCompression.Tests;

public class EncodePlannerTests
{
    [Theory]
    [InlineData(1920, 1080, 720, 1280, 720, true)]
    [InlineData(1080, 1920, 720, 720, 1280, true)]   // portrait: the cap applies to the short side
    [InlineData(1440, 1080, 720, 960, 720, true)]
    [InlineData(1280, 720, 1080, 1280, 720, false)]  // never upscale
    [InlineData(1921, 1081, null, 1920, 1080, true)] // odd sizes are trimmed to even for 4:2:0
    [InlineData(3840, 2160, 1080, 1920, 1080, true)]
    public void Output_size_keeps_aspect_ratio_and_even_dimensions(int w, int h, int? cap, int ew, int eh, bool scaled)
    {
        Assert.Equal((ew, eh, scaled), EncodePlanner.OutputSize(w, h, cap));
    }

    [Fact]
    public void Builds_hevc_quality_arguments_with_scaling()
    {
        var plan = Plan(Media(), new CompressionSettings { Codec = VideoCodec.H265, Crf = 26, MaxResolution = 720 });
        var args = plan.VideoArguments();

        Assert.Equal(["-vf", "scale=1280:720:flags=lanczos"], args.Take(2));
        AssertSequence(args, "-c:v", "libx265");
        AssertSequence(args, "-preset", "fast");
        AssertSequence(args, "-crf", "26");
        AssertSequence(args, "-x265-params", "log-level=error");
        AssertSequence(args, "-tag:v", "hvc1");
        AssertSequence(args, "-pix_fmt", "yuv420p");
        Assert.False(plan.TwoPass);
        Assert.Equal("H.265 · 1280×720 · 30 fps · Quality 26 · Original audio", plan.Summary);
    }

    [Fact]
    public void Target_size_uses_two_pass_with_the_right_bitrate()
    {
        // 25 MB over 100 s with 128 kbps copied audio: 25e6 * 8 * 0.96 / 100 - 128000 = 1,792,000 bps.
        var plan = Plan(Media(duration: 100), new CompressionSettings
        {
            Codec = VideoCodec.H264, Mode = RateMode.TargetSize, TargetSizeMb = 25, AudioBitrateKbps = 128,
        });

        Assert.True(plan.TwoPass);
        Assert.Equal(1_792_000, plan.VideoBitrateBps);
        var pass1 = plan.VideoArguments(pass: 1);
        AssertSequence(pass1, "-b:v", "1792k");
        AssertSequence(pass1, "-pass", "1");
        AssertSequence(plan.VideoArguments(pass: 2), "-pass", "2");
        Assert.DoesNotContain("-crf", pass1);
    }

    [Fact]
    public void Hevc_two_pass_goes_through_x265_params()
    {
        var plan = Plan(Media(), new CompressionSettings { Codec = VideoCodec.H265, Mode = RateMode.TargetSize, TargetSizeMb = 50 });
        AssertSequence(plan.VideoArguments(pass: 1), "-x265-params", "log-level=error:pass=1");
        AssertSequence(plan.VideoArguments(pass: 2), "-x265-params", "log-level=error:pass=2");
    }

    [Fact]
    public void Rejects_a_target_that_is_too_small()
    {
        var result = EncodePlanner.Create(Media(duration: 600), new CompressionSettings
        {
            Codec = VideoCodec.H264, Mode = RateMode.TargetSize, TargetSizeMb = 1, AudioBitrateKbps = 128,
        }, Ffmpeg());

        Assert.False(result.Ok);
        Assert.Contains("too small", result.Error);
        Assert.NotNull(result.MinTargetSizeMb);
        Assert.True(result.MinTargetSizeMb > 1);
    }

    [Fact]
    public void Automatic_resolution_drops_to_where_the_bitrate_looks_decent()
    {
        // 3 minutes of 1080p30 in 25 MB with H.264 leaves ~0.94 Mbps: too thin for 1080p or 720p.
        var plan = Plan(Media(duration: 180), new CompressionSettings
        {
            Codec = VideoCodec.H264, Mode = RateMode.TargetSize, TargetSizeMb = 25, AutoResolution = true, AudioBitrateKbps = 128,
        });

        Assert.True(plan.Height < 720, $"expected below 720p, got {plan.Width}x{plan.Height}");
        Assert.True(plan.Scaled);
        Assert.NotNull(plan.QualityHint);
    }

    [Fact]
    public void Copies_small_aac_and_reencodes_everything_else()
    {
        var media = Media(audio: [Aac(index: 1, bitrate: 128_000), Pcm(index: 2, channels: 6)]);

        var keep = Plan(media, new CompressionSettings { AudioBitrateKbps = 128 });
        Assert.Equal(AudioAction.Copy, keep.AudioTracks[0].Action);
        Assert.Equal(AudioAction.Encode, keep.AudioTracks[1].Action);
        Assert.Equal(2, keep.AudioTracks[1].Channels); // 5.1 PCM is downmixed for a 128 kbps AAC track
        var audioArgs = keep.AudioArguments();
        AssertSequence(audioArgs, "-c:a:0", "copy");
        AssertSequence(audioArgs, "-c:a:1", "aac");
        AssertSequence(audioArgs, "-b:a:1", "128k");
        AssertSequence(audioArgs, "-ac:a:1", "2");
        Assert.Equal(["-map", "0:0", "-map", "0:1", "-map", "0:2"], keep.MapArguments());

        var shrink = Plan(media, new CompressionSettings { AudioBitrateKbps = 64 });
        Assert.Equal(AudioAction.Encode, shrink.AudioTracks[0].Action);
        Assert.Equal(64_000, shrink.AudioTracks[0].BitrateBps);

        var mute = Plan(media, new CompressionSettings { AudioBitrateKbps = 0 });
        Assert.Empty(mute.AudioTracks);
        Assert.Equal(["-an"], mute.AudioArguments());
        Assert.Equal(["-map", "0:0"], mute.MapArguments());
    }

    [Fact]
    public void Never_raises_lossy_audio_bitrate()
    {
        var media = Media(audio: [new(1, "opus", "Opus", 70_000, 2, 48_000, false, null)]);
        var plan = Plan(media, new CompressionSettings { AudioBitrateKbps = 160 });
        Assert.Equal(64_000, plan.AudioTracks[0].BitrateBps); // rounded down to 8 kbps steps, never above the source
    }

    [Fact]
    public void Caps_frame_rate_before_scaling()
    {
        var plan = Plan(Media(fps: 59.94), new CompressionSettings { MaxFps = 30, MaxResolution = 480 });
        Assert.Equal(["-vf", "fps=30,scale=854:480:flags=lanczos"], plan.VideoArguments().Take(2));
        Assert.Equal(30, plan.Fps);
    }

    [Fact]
    public void Keeps_ten_bit_for_hevc_and_warns_about_hdr_with_h264()
    {
        var media = Media(width: 3840, height: 2160, bitDepth: 10, hdr: true, codec: "hevc");

        var hevc = Plan(media, new CompressionSettings { Codec = VideoCodec.H265 });
        Assert.Equal("yuv420p10le", hevc.PixelFormat);
        Assert.Empty(hevc.Warnings);

        var h264 = Plan(media, new CompressionSettings { Codec = VideoCodec.H264, Crf = 23 });
        Assert.Equal("yuv420p", h264.PixelFormat);
        Assert.Contains(h264.Warnings, w => w.Contains("HDR"));
    }

    [Fact]
    public void Av1_uses_svt_and_reports_missing_encoders()
    {
        var plan = Plan(Media(), new CompressionSettings { Codec = VideoCodec.Av1, Crf = 35, Speed = EncodeSpeed.Fast });
        AssertSequence(plan.VideoArguments(), "-c:v", "libsvtav1");
        AssertSequence(plan.VideoArguments(), "-preset", "10");

        var missing = EncodePlanner.Create(Media(), new CompressionSettings { Codec = VideoCodec.Av1 }, Ffmpeg("libx264"));
        Assert.False(missing.Ok);
        Assert.Contains("libsvtav1", missing.Error);
    }

    [Fact]
    public void Clamps_crf_to_the_codec_range()
    {
        Assert.Equal(16, Plan(Media(), new CompressionSettings { Codec = VideoCodec.H264, Crf = 2 }).Crf);
        Assert.Equal(40, Plan(Media(), new CompressionSettings { Codec = VideoCodec.H265, Crf = 99 }).Crf);
    }

    private static void AssertSequence(IReadOnlyList<string> args, string flag, string value)
    {
        var index = args.ToList().IndexOf(flag);
        Assert.True(index >= 0, $"missing {flag} in: {string.Join(' ', args)}");
        Assert.True(index + 1 < args.Count && args[index + 1] == value, $"expected {flag} {value} in: {string.Join(' ', args)}");
    }
}
