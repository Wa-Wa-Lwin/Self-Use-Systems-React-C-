using System.Collections.Concurrent;
using System.Globalization;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using VideoCompression.Api.Ffmpeg;
using VideoCompression.Api.Storage;

namespace VideoCompression.Api.Compression;

public enum JobStatus { Queued, Running, Completed, Failed, Canceled }

public sealed record JobSnapshot(
    JobStatus Status,
    double Progress,
    string Stage,
    double? Speed,
    double? EtaSeconds,
    double ElapsedSeconds,
    long? OutputBytes,
    string? Error);

public sealed class CompressionJob
{
    private readonly object _sync = new();
    private JobStatus _status = JobStatus.Queued;
    private double _progress;
    private string _stage = "Waiting in queue";
    private double? _speed;
    private long? _outputBytes;
    private string? _error;
    private DateTimeOffset? _startedAt;
    private DateTimeOffset? _finishedAt;

    public required string Id { get; init; }
    public required string VideoId { get; init; }
    public required EncodePlan Plan { get; init; }
    public required string SourcePath { get; init; }
    public required string OutputPath { get; init; }
    /// <summary>Per-job working folder: two-pass log files land here, so parallel jobs never collide.</summary>
    public required string WorkFolder { get; init; }
    public required string DownloadName { get; init; }
    public required long OriginalBytes { get; init; }
    public required double DurationSeconds { get; init; }
    public CancellationTokenSource Cancellation { get; } = new();

    public bool IsFinished
    {
        get
        {
            lock (_sync) return _status is JobStatus.Completed or JobStatus.Failed or JobStatus.Canceled;
        }
    }

    public JobSnapshot Snapshot()
    {
        lock (_sync)
        {
            var now = DateTimeOffset.UtcNow;
            var elapsed = _startedAt is { } started ? ((_finishedAt ?? now) - started).TotalSeconds : 0;
            double? eta = _status == JobStatus.Running && _progress > 0.02
                ? Math.Max(0, elapsed * (1 - _progress) / _progress)
                : null;
            return new JobSnapshot(_status, _progress, _stage, _speed, eta, elapsed, _outputBytes, _error);
        }
    }

    /// <returns>False if the job was canceled before it started.</returns>
    public bool TryStart()
    {
        lock (_sync)
        {
            if (_status != JobStatus.Queued) return false;
            _status = JobStatus.Running;
            _startedAt = DateTimeOffset.UtcNow;
            _stage = "Starting";
            return true;
        }
    }

    public void SetStage(string stage)
    {
        lock (_sync) _stage = stage;
    }

    public void ReportProgress(double fraction, double? speed)
    {
        lock (_sync)
        {
            if (fraction > _progress) _progress = Math.Min(fraction, 0.999);
            if (speed is > 0) _speed = speed;
        }
    }

    public void Complete(long outputBytes) => Finish(JobStatus.Completed, "Done", outputBytes, null);

    public void Fail(string error) => Finish(JobStatus.Failed, "Failed", null, error);

    public void MarkCanceled() => Finish(JobStatus.Canceled, "Canceled", null, null);

    public void RequestCancel()
    {
        try
        {
            Cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        lock (_sync)
        {
            if (_status == JobStatus.Queued) FinishLocked(JobStatus.Canceled, "Canceled", null, null);
        }
    }

    private void Finish(JobStatus status, string stage, long? outputBytes, string? error)
    {
        lock (_sync) FinishLocked(status, stage, outputBytes, error);
    }

    private void FinishLocked(JobStatus status, string stage, long? outputBytes, string? error)
    {
        _status = status;
        _stage = stage;
        _outputBytes = outputBytes;
        _error = error;
        _finishedAt = DateTimeOffset.UtcNow;
        if (status == JobStatus.Completed) _progress = 1;
    }
}

public sealed class CompressionQueue
{
    private readonly Channel<CompressionJob> _channel = Channel.CreateUnbounded<CompressionJob>();
    private readonly ConcurrentDictionary<string, CompressionJob> _jobs = new();

    public ChannelReader<CompressionJob> Reader => _channel.Reader;

    public void Enqueue(CompressionJob job)
    {
        _jobs[job.Id] = job;
        _channel.Writer.TryWrite(job);
    }

    public CompressionJob? Get(string id) => _jobs.TryGetValue(id, out var job) ? job : null;

    public bool HasActiveJobs(string videoId) => _jobs.Values.Any(j => j.VideoId == videoId && !j.IsFinished);

    public void RemoveForVideo(string videoId)
    {
        foreach (var job in _jobs.Values.Where(j => j.VideoId == videoId).ToList())
        {
            job.RequestCancel();
            _jobs.TryRemove(job.Id, out _);
        }
    }
}

/// <summary>Runs queued compression jobs with FFmpeg and reports live progress.</summary>
public sealed class CompressionWorker(
    CompressionQueue queue,
    VideoStore store,
    FfmpegTools tools,
    IOptions<AppOptions> options,
    ILogger<CompressionWorker> logger) : BackgroundService
{
    private static readonly string NullDevice = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var workers = Math.Clamp(options.Value.MaxConcurrentJobs, 1, 8);
        return Task.WhenAll(Enumerable.Range(0, workers).Select(_ => ConsumeAsync(stoppingToken)));
    }

    private async Task ConsumeAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in queue.Reader.ReadAllAsync(stoppingToken))
        {
            await RunAsync(job, stoppingToken);
        }
    }

    private async Task RunAsync(CompressionJob job, CancellationToken stoppingToken)
    {
        var video = store.Peek(job.VideoId);
        if (video is null)
        {
            job.Fail("The uploaded video was deleted.");
            return;
        }
        if (!job.TryStart()) return; // canceled while queued

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, job.Cancellation.Token, video.Lifetime.Token);
        var cancellationToken = linked.Token;

        try
        {
            Directory.CreateDirectory(job.WorkFolder);
            var ffmpeg = await tools.RequireAsync(cancellationToken);

            if (job.Plan.TwoPass)
            {
                job.SetStage("Analyzing motion (pass 1 of 2)");
                await RunFfmpegAsync(ffmpeg, FirstPassArguments(job), job, 0, 0.4, cancellationToken);
                job.SetStage("Compressing (pass 2 of 2)");
                await RunFfmpegAsync(ffmpeg, FinalArguments(job, pass: 2), job, 0.4, 1, cancellationToken);
            }
            else
            {
                job.SetStage("Compressing");
                await RunFfmpegAsync(ffmpeg, FinalArguments(job, pass: 0), job, 0, 1, cancellationToken);
            }

            var size = new FileInfo(job.OutputPath).Length;
            job.Complete(size);
            logger.LogInformation("Job {Job} done: {Original} → {Output} bytes ({Summary})", job.Id, job.OriginalBytes, size, job.Plan.Summary);
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            job.MarkCanceled();
            TryDelete(job.OutputPath);
        }
        catch (OperationCanceledException)
        {
            job.Fail("The server is shutting down.");
            TryDelete(job.OutputPath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Job {Job} failed", job.Id);
            job.Fail(ex.Message);
            TryDelete(job.OutputPath);
        }
        finally
        {
            store.TryDeleteFolder(job.WorkFolder);
        }
    }

    private static List<string> CommonArguments() =>
        ["-hide_banner", "-nostdin", "-y", "-loglevel", "error", "-nostats", "-progress", "pipe:1"];

    internal static List<string> FirstPassArguments(CompressionJob job)
    {
        var args = CommonArguments();
        args.AddRange(["-i", job.SourcePath, "-map", $"0:{job.Plan.VideoStreamIndex}"]);
        args.AddRange(job.Plan.VideoArguments(pass: 1));
        args.AddRange(["-an", "-f", "null", NullDevice]);
        return args;
    }

    internal static List<string> FinalArguments(CompressionJob job, int pass)
    {
        var args = CommonArguments();
        args.AddRange(["-i", job.SourcePath]);
        args.AddRange(job.Plan.MapArguments());
        args.AddRange(["-map_metadata", "0"]);
        args.AddRange(job.Plan.VideoArguments(pass));
        args.AddRange(job.Plan.AudioArguments());
        args.AddRange(["-movflags", "+faststart", "-f", "mp4", job.OutputPath]);
        return args;
    }

    private static async Task RunFfmpegAsync(
        FfmpegStatus ffmpeg, IReadOnlyList<string> args, CompressionJob job, double from, double to, CancellationToken cancellationToken)
    {
        double? speed = null;

        void OnProgressLine(string line)
        {
            // -progress output is "key=value" lines; out_time_us (and the misnamed out_time_ms) are microseconds.
            var separator = line.IndexOf('=');
            if (separator <= 0) return;
            var key = line[..separator];
            var value = line[(separator + 1)..].Trim();

            if (key == "speed" && double.TryParse(value.TrimEnd('x'), NumberStyles.Float, CultureInfo.InvariantCulture, out var s))
            {
                speed = s;
            }
            else if (key is "out_time_us" or "out_time_ms"
                     && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var microseconds)
                     && microseconds >= 0
                     && job.DurationSeconds > 0)
            {
                var fraction = Math.Clamp(microseconds / 1_000_000.0 / job.DurationSeconds, 0, 1);
                job.ReportProgress(from + (to - from) * fraction, speed);
            }
        }

        var result = await ProcessRunner.RunAsync(ffmpeg.FfmpegPath!, args, cancellationToken,
            workingDirectory: job.WorkFolder, onStdoutLine: OnProgressLine);
        if (result.ExitCode != 0)
            throw new InvalidOperationException("FFmpeg failed: " + FfmpegErrors.Summarize(result));
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
