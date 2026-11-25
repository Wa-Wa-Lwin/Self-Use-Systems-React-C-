using Microsoft.Extensions.Options;
using VideoCompression.Api.Compression;

namespace VideoCompression.Api.Storage;

/// <summary>Deletes uploads and results that haven't been used for a while, plus leftovers from previous runs.</summary>
public sealed class StorageCleanupService(
    VideoStore store,
    CompressionQueue jobs,
    IOptions<AppOptions> options,
    ILogger<StorageCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Records live in memory, so anything already on disk belongs to an earlier run.
        foreach (var folder in store.OrphanFolders()) store.TryDeleteFolder(folder);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                Sweep();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Storage cleanup failed");
            }
        }
    }

    private void Sweep()
    {
        var lifetime = TimeSpan.FromMinutes(Math.Max(5, options.Value.FileLifetimeMinutes));
        var cutoff = DateTimeOffset.UtcNow - lifetime;

        foreach (var video in store.All)
        {
            if (video.LastAccess >= cutoff || jobs.HasActiveJobs(video.Id)) continue;
            jobs.RemoveForVideo(video.Id);
            store.Remove(video.Id);
            logger.LogInformation("Deleted idle upload {Id} ({Name})", video.Id, video.FileName);
        }

        foreach (var folder in store.OrphanFolders()) store.TryDeleteFolder(folder);
    }
}
