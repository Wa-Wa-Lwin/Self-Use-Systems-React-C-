namespace VideoCompression.Api.Compression;

public enum VideoCodec { H264, H265, Av1 }

public enum RateMode
{
    /// <summary>Constant quality (CRF): the encoder spends whatever bits the content needs.</summary>
    Quality,
    /// <summary>Hit a requested file size (two-pass average bitrate).</summary>
    TargetSize,
}

public enum EncodeSpeed { Fast, Balanced, Slow }

/// <summary>What the user asked for. <see cref="EncodePlanner"/> validates it and turns it into an <see cref="EncodePlan"/>.</summary>
public sealed record CompressionSettings
{
    public VideoCodec Codec { get; init; } = VideoCodec.H265;
    public RateMode Mode { get; init; } = RateMode.Quality;

    /// <summary>Constant-quality value (CRF). Lower = better quality and a bigger file. Range depends on the codec.</summary>
    public int Crf { get; init; } = 28;

    /// <summary>Target size in MB (1 MB = 1,000,000 bytes). Used when <see cref="Mode"/> is TargetSize.</summary>
    public double? TargetSizeMb { get; init; }

    /// <summary>Target-size mode only: lower the resolution automatically when the bitrate would be too thin.</summary>
    public bool AutoResolution { get; init; }

    /// <summary>Limit for the shorter side of the frame (720 = 720p). Null keeps the original resolution.</summary>
    public int? MaxResolution { get; init; }

    /// <summary>Frame-rate cap. Null keeps the original frame rate.</summary>
    public double? MaxFps { get; init; }

    /// <summary>AAC bitrate per audio track in kbps. 0 removes the audio.</summary>
    public int AudioBitrateKbps { get; init; } = 128;

    public EncodeSpeed Speed { get; init; } = EncodeSpeed.Balanced;
}

/// <param name="GoodBpp">Bits per pixel per frame at which this codec looks good (used for target-size quality hints).</param>
/// <param name="PoorBpp">Bits per pixel per frame below which artifacts become obvious.</param>
public sealed record CodecSpec(
    VideoCodec Id,
    string Label,
    string Encoder,
    int CrfMin,
    int CrfMax,
    int CrfDefault,
    int GopFrames,
    double GoodBpp,
    double PoorBpp,
    string Compatibility,
    string Description);

public static class Codecs
{
    public static readonly CodecSpec H264 = new(
        VideoCodec.H264, "H.264", "libx264", 16, 36, 23, 250, 0.070, 0.030,
        "Plays everywhere",
        "The most compatible format: every phone, browser, TV, editor and messaging app plays it.");

    public static readonly CodecSpec H265 = new(
        VideoCodec.H265, "H.265", "libx265", 18, 40, 28, 250, 0.045, 0.020,
        "Most modern devices",
        "Around 40% smaller than H.264 at the same quality. Plays on recent phones, Macs, Windows PCs and smart TVs.");

    public static readonly CodecSpec Av1 = new(
        VideoCodec.Av1, "AV1", "libsvtav1", 20, 55, 35, 240, 0.035, 0.016,
        "Newer devices & browsers",
        "The smallest files. Slower to encode; plays in modern browsers and recent devices.");

    public static IReadOnlyList<CodecSpec> All { get; } = [H264, H265, Av1];

    public static CodecSpec Get(VideoCodec id) => id switch
    {
        VideoCodec.H264 => H264,
        VideoCodec.H265 => H265,
        _ => Av1,
    };
}
