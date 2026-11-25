namespace VideoCompression.Api.Media;

/// <summary>What ffprobe told us about an uploaded file.</summary>
public sealed record MediaInfo(
    string Container,
    double DurationSeconds,
    long SizeBytes,
    long BitrateBps,
    VideoStreamInfo Video,
    IReadOnlyList<AudioStreamInfo> Audio);

/// <param name="Width">Display width (rotation already applied).</param>
/// <param name="Height">Display height (rotation already applied).</param>
/// <param name="BitrateEstimated">True when the container didn't state the video bitrate and it was derived from the total.</param>
public sealed record VideoStreamInfo(
    int Index,
    string Codec,
    string CodecLabel,
    int Width,
    int Height,
    double Fps,
    long BitrateBps,
    bool BitrateEstimated,
    string? PixelFormat,
    int BitDepth,
    bool IsHdr,
    int Rotation);

public sealed record AudioStreamInfo(
    int Index,
    string Codec,
    string CodecLabel,
    long? BitrateBps,
    int Channels,
    int SampleRate,
    bool Lossless,
    string? Language);

public enum CodecEfficiency
{
    /// <summary>Editing / lossless / intra-only formats: many times larger than needed for viewing.</summary>
    Intermediate,
    /// <summary>Pre-H.264 delivery codecs (MPEG-2, MPEG-4 ASP, WMV...).</summary>
    Legacy,
    /// <summary>H.264.</summary>
    Standard,
    /// <summary>HEVC, VP9, AV1, VVC.</summary>
    Efficient,
}

public static class CodecFacts
{
    private static readonly Dictionary<string, string> Labels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["h264"] = "H.264 (AVC)",
        ["hevc"] = "H.265 (HEVC)",
        ["av1"] = "AV1",
        ["vp9"] = "VP9",
        ["vp8"] = "VP8",
        ["vvc"] = "H.266 (VVC)",
        ["mpeg4"] = "MPEG-4 Part 2",
        ["mpeg2video"] = "MPEG-2",
        ["mpeg1video"] = "MPEG-1",
        ["msmpeg4v3"] = "MS MPEG-4 v3",
        ["msmpeg4v2"] = "MS MPEG-4 v2",
        ["wmv1"] = "Windows Media Video 7",
        ["wmv2"] = "Windows Media Video 8",
        ["wmv3"] = "Windows Media Video 9",
        ["vc1"] = "VC-1",
        ["flv1"] = "Sorenson Spark (FLV)",
        ["h263"] = "H.263",
        ["theora"] = "Theora",
        ["prores"] = "Apple ProRes",
        ["dnxhd"] = "Avid DNxHD / DNxHR",
        ["cfhd"] = "GoPro CineForm",
        ["mjpeg"] = "Motion JPEG",
        ["rawvideo"] = "Uncompressed video",
        ["ffv1"] = "FFV1 (lossless)",
        ["huffyuv"] = "HuffYUV (lossless)",
        ["utvideo"] = "Ut Video (lossless)",
        ["qtrle"] = "QuickTime Animation",
        ["png"] = "PNG frames",
        ["dvvideo"] = "DV",
        ["aac"] = "AAC",
        ["mp3"] = "MP3",
        ["mp2"] = "MP2",
        ["opus"] = "Opus",
        ["vorbis"] = "Vorbis",
        ["ac3"] = "Dolby Digital (AC-3)",
        ["eac3"] = "Dolby Digital Plus",
        ["truehd"] = "Dolby TrueHD",
        ["dts"] = "DTS",
        ["flac"] = "FLAC (lossless)",
        ["alac"] = "Apple Lossless",
        ["wmav2"] = "Windows Media Audio",
        ["amr_nb"] = "AMR",
        ["amr_wb"] = "AMR-WB",
    };

    public static string Label(string codec)
    {
        if (Labels.TryGetValue(codec, out var label)) return label;
        if (codec.StartsWith("pcm_", StringComparison.OrdinalIgnoreCase)) return "PCM (uncompressed)";
        return codec.ToUpperInvariant();
    }

    public static CodecEfficiency VideoEfficiency(string codec) => codec.ToLowerInvariant() switch
    {
        "hevc" or "av1" or "vp9" or "vvc" => CodecEfficiency.Efficient,
        "h264" => CodecEfficiency.Standard,
        "prores" or "dnxhd" or "cfhd" or "mjpeg" or "rawvideo" or "ffv1" or "huffyuv" or "utvideo"
            or "qtrle" or "png" or "dvvideo" or "v210" or "r210" or "jpeg2000" => CodecEfficiency.Intermediate,
        _ => CodecEfficiency.Legacy,
    };

    public static bool IsLosslessAudio(string codec) =>
        codec.StartsWith("pcm_", StringComparison.OrdinalIgnoreCase)
        || codec.ToLowerInvariant() is "flac" or "alac" or "truehd" or "mlp" or "wavpack" or "ape" or "tta";
}
