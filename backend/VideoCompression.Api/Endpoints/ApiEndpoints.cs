using System.Buffers;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using VideoCompression.Api.Analysis;
using VideoCompression.Api.Compression;
using VideoCompression.Api.Ffmpeg;
using VideoCompression.Api.Media;
using VideoCompression.Api.Storage;

namespace VideoCompression.Api.Endpoints;

public static class ApiEndpoints
{
    public static readonly IReadOnlyList<string> AcceptedExtensions =
    [
        ".mp4", ".m4v", ".mov", ".mkv", ".webm", ".avi", ".wmv", ".asf", ".flv", ".f4v", ".3gp", ".3g2",
        ".mpg", ".mpeg", ".m2v", ".ts", ".mts", ".m2ts", ".vob", ".ogv", ".mxf", ".dv", ".divx",
    ];

    private static readonly HashSet<string> AcceptedExtensionSet = new(AcceptedExtensions, StringComparer.OrdinalIgnoreCase);

    public static void MapApi(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/health", GetHealth);
        api.MapPost("/videos", UploadVideo);
        api.MapGet("/videos/{id}", GetVideo);
        api.MapDelete("/videos/{id}", DeleteVideo);
        api.MapPost("/videos/{id}/estimate", Estimate);
        api.MapPost("/videos/{id}/jobs", StartJob);
        api.MapGet("/jobs/{id}", GetJob);
        api.MapPost("/jobs/{id}/cancel", CancelJob);
        api.MapGet("/jobs/{id}/file", GetJobFile);

        // Unknown API routes get a JSON 404 instead of the SPA's index.html.
        api.Map("/{**path}", () => Problem(StatusCodes.Status404NotFound, "Not found", "There is no such API endpoint."));
    }

    private static async Task<HealthDto> GetHealth(FfmpegTools tools, IOptions<AppOptions> options, CancellationToken cancellationToken)
    {
        var ffmpeg = await tools.GetStatusAsync(cancellationToken);
        var codecs = Codecs.All
            .Select(c => new CodecDto(c.Id, c.Label, ffmpeg.HasEncoder(c.Encoder), c.CrfMin, c.CrfMax, c.CrfDefault, c.Compatibility, c.Description))
            .ToList();
        return new HealthDto(ffmpeg.Available, ffmpeg.Version, ffmpeg.Available ? null : ffmpeg.Message, codecs,
            options.Value.MaxUploadBytes, AcceptedExtensions);
    }

    /// <summary>
    /// Upload = the raw file as the request body (application/octet-stream), name in ?fileName=.
    /// Streaming straight to disk avoids buffering multi-GB multipart bodies.
    /// </summary>
    private static async Task<IResult> UploadVideo(
        HttpContext http,
        [FromQuery] string? fileName,
        FfmpegTools tools,
        MediaProbe probe,
        VideoStore store,
        VideoAnalyzer analyzer,
        AnalysisQueue analysisQueue,
        IOptions<AppOptions> options,
        CancellationToken cancellationToken)
    {
        var ffmpeg = await tools.GetStatusAsync(cancellationToken);
        if (!ffmpeg.Available)
            return Problem(StatusCodes.Status503ServiceUnavailable, "FFmpeg is not installed on the server", ffmpeg.Message);

        var name = Path.GetFileName(fileName ?? string.Empty).Trim();
        if (name.Length == 0) name = "video.mp4";
        var extension = Path.GetExtension(name).ToLowerInvariant();
        if (!AcceptedExtensionSet.Contains(extension))
            return Problem(StatusCodes.Status415UnsupportedMediaType, "Unsupported file type",
                $"\"{extension}\" files aren't supported. Try MP4, MOV, MKV, WebM, AVI or another common video format.");

        var maxBytes = options.Value.MaxUploadBytes;
        if (http.Request.ContentLength > maxBytes) return TooLarge(maxBytes);

        var sizeLimit = http.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeLimit is { IsReadOnly: false }) sizeLimit.MaxRequestBodySize = maxBytes;

        var (id, folder) = store.Reserve();
        var added = false;
        try
        {
            var sourcePath = Path.Combine(folder, "source" + extension);
            var written = await SaveBodyAsync(http.Request.Body, sourcePath, maxBytes, cancellationToken);
            if (written < 0) return TooLarge(maxBytes);
            if (written == 0) return Problem(StatusCodes.Status400BadRequest, "Empty upload", "The file was empty.");

            var media = await probe.ProbeAsync(sourcePath, written, cancellationToken);
            if (media is null)
                return Problem(StatusCodes.Status422UnprocessableEntity, "Not a readable video",
                    "No video stream could be read from this file. It may be damaged or not a video.");

            var video = analyzer.CreateRecord(id, name, folder, sourcePath, media, ffmpeg);
            store.Add(video);
            added = true;
            analysisQueue.Enqueue(video.Id);
            return Results.Created($"/api/videos/{video.Id}", video.ToDto());
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return TooLarge(maxBytes);
        }
        finally
        {
            if (!added) store.Release(id, folder);
        }
    }

    /// <returns>Bytes written, or -1 when the body exceeded <paramref name="maxBytes"/>.</returns>
    private static async Task<long> SaveBodyAsync(Stream body, string path, long maxBytes, CancellationToken cancellationToken)
    {
        const int bufferSize = 1 << 20;
        var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        try
        {
            await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize, FileOptions.Asynchronous);
            long written = 0;
            int read;
            while ((read = await body.ReadAsync(buffer.AsMemory(0, bufferSize), cancellationToken)) > 0)
            {
                written += read;
                if (written > maxBytes) return -1;
                await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
            return written;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static IResult GetVideo(string id, VideoStore store) =>
        FindVideo(store, id) is { } video ? Results.Ok(video.ToDto()) : VideoNotFound();

    private static IResult DeleteVideo(string id, VideoStore store, CompressionQueue jobs)
    {
        if (!VideoStore.IsValidId(id)) return VideoNotFound();
        jobs.RemoveForVideo(id);
        store.Remove(id);
        return Results.NoContent();
    }

    private static async Task<IResult> Estimate(
        string id, CompressionSettings settings, VideoStore store, FfmpegTools tools, SizeEstimator estimator,
        ILoggerFactory loggers, CancellationToken cancellationToken)
    {
        if (FindVideo(store, id) is not { } video) return VideoNotFound();

        var ffmpeg = await tools.RequireAsync(cancellationToken);
        var plan = EncodePlanner.Create(video.Media, settings, ffmpeg);
        if (!plan.Ok) return Results.Ok(new EstimateDto(false, plan.Error, null, null, plan.MinTargetSizeMb));

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, video.Lifetime.Token);
        try
        {
            var estimate = await estimator.EstimateAsync(video, plan.Plan, linked.Token);
            return Results.Ok(new EstimateDto(true, null, plan.Plan.ToDto(), estimate, plan.MinTargetSizeMb));
        }
        catch (InvalidOperationException ex)
        {
            loggers.CreateLogger("Estimate").LogWarning(ex, "Estimate failed for {Video}", id);
            return Results.Ok(new EstimateDto(false, ex.Message, plan.Plan.ToDto(), null, plan.MinTargetSizeMb));
        }
    }

    private static async Task<IResult> StartJob(
        string id, CompressionSettings settings, VideoStore store, FfmpegTools tools, CompressionQueue jobs, CancellationToken cancellationToken)
    {
        if (FindVideo(store, id) is not { } video) return VideoNotFound();

        var ffmpeg = await tools.RequireAsync(cancellationToken);
        var plan = EncodePlanner.Create(video.Media, settings, ffmpeg);
        if (!plan.Ok) return Problem(StatusCodes.Status400BadRequest, "Invalid settings", plan.Error);

        var jobId = Guid.NewGuid().ToString("N");
        var job = new CompressionJob
        {
            Id = jobId,
            VideoId = video.Id,
            Plan = plan.Plan,
            SourcePath = video.SourcePath,
            OutputPath = Path.Combine(video.Folder, $"{jobId}.mp4"),
            WorkFolder = Path.Combine(video.Folder, $"work-{jobId}"),
            DownloadName = DownloadName(video.FileName),
            OriginalBytes = video.Media.SizeBytes,
            DurationSeconds = video.Media.DurationSeconds,
        };
        jobs.Enqueue(job);
        return Results.Accepted($"/api/jobs/{jobId}", job.ToDto());
    }

    private static IResult GetJob(string id, CompressionQueue jobs, VideoStore store)
    {
        if (jobs.Get(id) is not { } job) return JobNotFound();
        store.Get(job.VideoId); // polling keeps the upload alive
        return Results.Ok(job.ToDto());
    }

    private static IResult CancelJob(string id, CompressionQueue jobs)
    {
        if (jobs.Get(id) is not { } job) return JobNotFound();
        job.RequestCancel();
        return Results.Ok(job.ToDto());
    }

    /// <param name="name">Download only: the file name the user chose (cleaned; ".mp4" is added).</param>
    private static IResult GetJobFile(string id, bool? download, string? name, CompressionQueue jobs, VideoStore store)
    {
        if (jobs.Get(id) is not { } job || job.Snapshot().Status != JobStatus.Completed || !File.Exists(job.OutputPath))
            return JobNotFound();
        store.Get(job.VideoId);

        return download == true
            ? Results.File(job.OutputPath, "video/mp4", FileNames.ForDownload(name, job.DownloadName), enableRangeProcessing: true)
            : Results.File(job.OutputPath, "video/mp4", enableRangeProcessing: true); // inline, for the preview player
    }

    private static VideoRecord? FindVideo(VideoStore store, string id) => VideoStore.IsValidId(id) ? store.Get(id) : null;

    /// <summary>The suggested name: "holiday.mov" → "holiday-compressed.mp4".</summary>
    private static string DownloadName(string original) =>
        FileNames.ForDownload(Path.GetFileNameWithoutExtension(original) + "-compressed", "video-compressed.mp4");

    private static IResult VideoNotFound() => Problem(StatusCodes.Status404NotFound, "Video not found",
        "This upload no longer exists. Uploads are removed after a period of inactivity, so please upload it again.");

    private static IResult JobNotFound() => Problem(StatusCodes.Status404NotFound, "Job not found", "This compression job no longer exists.");

    private static IResult TooLarge(long maxBytes) => Problem(StatusCodes.Status413PayloadTooLarge, "File too large",
        $"The maximum upload size is {Units.Bytes(maxBytes)}.");

    private static IResult Problem(int status, string title, string? detail) =>
        Results.Problem(statusCode: status, title: title, detail: detail);
}
