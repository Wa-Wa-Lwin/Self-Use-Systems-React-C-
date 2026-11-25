using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using VideoCompression.Api.Analysis;
using VideoCompression.Api.Compression;
using VideoCompression.Api.Media;

namespace VideoCompression.Api.Storage;

/// <summary>An uploaded video, its analysis results and where its files live.</summary>
public sealed class VideoRecord
{
    public required string Id { get; init; }
    /// <summary>Original file name, for display and the download name only (never used in paths).</summary>
    public required string FileName { get; init; }
    public required string Folder { get; init; }
    public required string SourcePath { get; init; }
    public required MediaInfo Media { get; init; }
    public required IReadOnlyList<Insight> Insights { get; init; }
    public required IReadOnlyList<PresetState> Presets { get; init; }
    public required double MinTargetSizeMb { get; init; }
    public DateTimeOffset UploadedAt { get; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastAccess { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>Guards the mutable analysis state (presets, verdict) shared between the worker and requests.</summary>
    public object Sync { get; } = new();
    public Verdict? Verdict { get; set; }
    public bool AnalysisDone { get; set; }

    public ConcurrentDictionary<string, VideoMeasurement> Measurements { get; } = new();

    /// <summary>Canceled when the video is deleted, stopping any FFmpeg work that uses it.</summary>
    public CancellationTokenSource Lifetime { get; } = new();

    public void Touch() => LastAccess = DateTimeOffset.UtcNow;
}

/// <summary>In-memory registry of uploads plus their folders on disk.</summary>
public sealed partial class VideoStore
{
    private readonly ConcurrentDictionary<string, VideoRecord> _videos = new();
    private readonly ConcurrentDictionary<string, byte> _reserved = new();
    private readonly ILogger<VideoStore> _logger;

    public VideoStore(IOptions<AppOptions> options, ILogger<VideoStore> logger)
    {
        _logger = logger;
        var configured = options.Value.StorageFolder;
        Root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Path.GetTempPath(), "VideoCompression")
            : Path.GetFullPath(configured);
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public static bool IsValidId(string? id) => id is not null && IdPattern().IsMatch(id);

    /// <summary>Creates a folder for a new upload. It is protected from cleanup until added or released.</summary>
    public (string Id, string Folder) Reserve()
    {
        var id = Guid.NewGuid().ToString("N");
        _reserved[id] = 0;
        var folder = Path.Combine(Root, id);
        Directory.CreateDirectory(folder);
        return (id, folder);
    }

    public void Add(VideoRecord video)
    {
        _videos[video.Id] = video;
        _reserved.TryRemove(video.Id, out _);
    }

    /// <summary>Gives up a reservation whose upload failed and deletes its folder.</summary>
    public void Release(string id, string folder)
    {
        TryDeleteFolder(folder);
        _reserved.TryRemove(id, out _);
    }

    /// <summary>Looks up a video and marks it as recently used.</summary>
    public VideoRecord? Get(string id)
    {
        if (!_videos.TryGetValue(id, out var video)) return null;
        video.Touch();
        return video;
    }

    /// <summary>Looks up a video without counting it as activity.</summary>
    public VideoRecord? Peek(string id) => _videos.TryGetValue(id, out var video) ? video : null;

    public IReadOnlyList<VideoRecord> All => _videos.Values.ToList();

    public bool Remove(string id)
    {
        if (!_videos.TryRemove(id, out var video)) return false;
        video.Lifetime.Cancel();
        TryDeleteFolder(video.Folder); // may fail while FFmpeg is still exiting; cleanup retries
        return true;
    }

    /// <summary>Folders that look like ours but belong to no live upload (previous runs, failed deletes).</summary>
    public IReadOnlyList<string> OrphanFolders() => Directory.EnumerateDirectories(Root)
        .Where(folder =>
        {
            var name = Path.GetFileName(folder);
            return IsValidId(name) && !_videos.ContainsKey(name) && !_reserved.ContainsKey(name);
        })
        .ToList();

    public bool TryDeleteFolder(string folder)
    {
        try
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Could not delete {Folder} yet", folder);
            return false;
        }
    }

    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex IdPattern();
}
