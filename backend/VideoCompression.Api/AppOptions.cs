namespace VideoCompression.Api;

/// <summary>Settings bound from the "VideoCompression" section of appsettings.json.</summary>
public sealed class AppOptions
{
    public const string SectionName = "VideoCompression";

    /// <summary>
    /// Folder that contains ffmpeg and ffprobe. Leave empty to auto-detect:
    /// &lt;repo&gt;/tools/ffmpeg first (see scripts/get-ffmpeg.ps1), then the PATH.
    /// </summary>
    public string? FfmpegFolder { get; set; }

    /// <summary>Where uploads and compressed files are kept. Empty = &lt;system temp&gt;/VideoCompression.</summary>
    public string? StorageFolder { get; set; }

    public long MaxUploadMegabytes { get; set; } = 4096;

    /// <summary>Uploads and results are deleted after this many minutes without activity.</summary>
    public int FileLifetimeMinutes { get; set; } = 120;

    /// <summary>How many compression jobs may run at the same time.</summary>
    public int MaxConcurrentJobs { get; set; } = 1;

    public long MaxUploadBytes => MaxUploadMegabytes * 1024L * 1024L;
}
