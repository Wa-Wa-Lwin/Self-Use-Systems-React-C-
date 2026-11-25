using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace VideoCompression.Api.Ffmpeg;

public sealed record FfmpegStatus(
    bool Available,
    string? FfmpegPath,
    string? FfprobePath,
    string? Version,
    IReadOnlySet<string> Encoders,
    string? Message,
    DateTimeOffset CheckedAt)
{
    public bool HasEncoder(string name) => Encoders.Contains(name);
}

public sealed class FfmpegUnavailableException(string message) : Exception(message);

/// <summary>
/// Locates ffmpeg/ffprobe and reports which encoders the build supports.
/// While FFmpeg is missing, detection is retried (at most every few seconds), so dropping
/// it into tools/ffmpeg works without restarting the server.
/// </summary>
public sealed partial class FfmpegTools(IOptions<AppOptions> options, IHostEnvironment environment, ILogger<FfmpegTools> logger)
{
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(5);
    private readonly SemaphoreSlim _lock = new(1, 1);
    private FfmpegStatus? _status;

    public async Task<FfmpegStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        if (_status is { Available: true } ready) return ready;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_status is { } current && (current.Available || DateTimeOffset.UtcNow - current.CheckedAt < RetryInterval))
                return current;

            _status = await DetectAsync(cancellationToken);
            return _status;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<FfmpegStatus> RequireAsync(CancellationToken cancellationToken = default)
    {
        var status = await GetStatusAsync(cancellationToken);
        return status.Available ? status : throw new FfmpegUnavailableException(status.Message ?? "FFmpeg is not available.");
    }

    private async Task<FfmpegStatus> DetectAsync(CancellationToken cancellationToken)
    {
        var none = new HashSet<string>();
        var ffmpeg = FindExecutable("ffmpeg");
        var ffprobe = FindExecutable("ffprobe");

        if (ffmpeg is null || ffprobe is null)
        {
            var missing = ffmpeg is null && ffprobe is null ? "ffmpeg and ffprobe" : ffmpeg is null ? "ffmpeg" : "ffprobe";
            return new FfmpegStatus(false, ffmpeg, ffprobe, null, none,
                $"Could not find {missing}. Run scripts/get-ffmpeg.ps1, install FFmpeg on the PATH, or set VideoCompression:FfmpegFolder in appsettings.json.",
                DateTimeOffset.UtcNow);
        }

        try
        {
            var version = await ProcessRunner.RunAsync(ffmpeg, ["-hide_banner", "-version"], cancellationToken, captureStdout: true);
            var encoders = await ProcessRunner.RunAsync(ffmpeg, ["-hide_banner", "-encoders"], cancellationToken, captureStdout: true);
            if (version.ExitCode != 0 || encoders.ExitCode != 0)
                throw new InvalidOperationException(FfmpegErrors.Summarize(version.ExitCode != 0 ? version : encoders));

            var match = VersionPattern().Match(version.Stdout);
            return new FfmpegStatus(true, ffmpeg, ffprobe, match.Success ? match.Groups[1].Value : "unknown",
                ParseEncoders(encoders.Stdout), null, DateTimeOffset.UtcNow);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "FFmpeg was found at {Path} but could not be started", ffmpeg);
            return new FfmpegStatus(false, ffmpeg, ffprobe, null, none, $"FFmpeg was found at {ffmpeg} but could not be started: {ex.Message}", DateTimeOffset.UtcNow);
        }
    }

    internal static HashSet<string> ParseEncoders(string output)
    {
        // Lines look like: " V....D libx264              libx264 H.264 / AVC / MPEG-4 AVC ..."
        var encoders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in output.Split('\n'))
        {
            var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && parts[0].Length == 6 && parts[0][0] is 'V' or 'A' or 'S')
                encoders.Add(parts[1]);
        }
        return encoders;
    }

    private string? FindExecutable(string name)
    {
        var fileName = OperatingSystem.IsWindows() ? name + ".exe" : name;
        foreach (var folder in CandidateFolders())
        {
            try
            {
                var candidate = Path.Combine(folder, fileName);
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException)
            {
                // Malformed PATH entry; skip it.
            }
        }
        return null;
    }

    private IEnumerable<string> CandidateFolders()
    {
        var configured = options.Value.FfmpegFolder;
        if (!string.IsNullOrWhiteSpace(configured))
            yield return Path.GetFullPath(configured, environment.ContentRootPath);

        // <repo>/tools/ffmpeg, searched from the content root upwards (populated by scripts/get-ffmpeg.ps1).
        var directory = new DirectoryInfo(environment.ContentRootPath);
        for (var depth = 0; directory is not null && depth < 4; depth++, directory = directory.Parent)
        {
            var tools = Path.Combine(directory.FullName, "tools", "ffmpeg");
            if (!Directory.Exists(tools)) continue;
            yield return tools;
            foreach (var bin in BinFolders(tools)) yield return bin; // an extracted release zip keeps its own bin/ folder
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var entry in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            yield return entry.Trim('"');

        if (OperatingSystem.IsWindows())
        {
            // winget adds this folder to PATH, but only for shells started after the install.
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Links");
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ffmpeg", "bin");
            yield return @"C:\ffmpeg\bin";
        }
        else
        {
            yield return "/opt/homebrew/bin";
            yield return "/usr/local/bin";
            yield return "/usr/bin";
        }
    }

    private static List<string> BinFolders(string root)
    {
        try
        {
            return Directory.EnumerateDirectories(root, "bin", SearchOption.AllDirectories).Take(4).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    [GeneratedRegex(@"ffmpeg version (\S+)")]
    private static partial Regex VersionPattern();
}
