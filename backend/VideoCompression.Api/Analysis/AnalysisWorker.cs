using System.Threading.Channels;
using VideoCompression.Api.Storage;

namespace VideoCompression.Api.Analysis;

public sealed class AnalysisQueue
{
    private readonly Channel<string> _channel = Channel.CreateUnbounded<string>();

    public ChannelReader<string> Reader => _channel.Reader;

    public void Enqueue(string videoId) => _channel.Writer.TryWrite(videoId);
}

/// <summary>Runs the test encodes for newly uploaded videos, one video at a time.</summary>
public sealed class AnalysisWorker(AnalysisQueue queue, VideoStore store, VideoAnalyzer analyzer, ILogger<AnalysisWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var id in queue.Reader.ReadAllAsync(stoppingToken))
        {
            var video = store.Peek(id);
            if (video is null) continue; // deleted while waiting

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, video.Lifetime.Token);
            try
            {
                await analyzer.AnalyzeAsync(video, linked.Token);
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                // The video was deleted mid-analysis.
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Analysis failed for {Video}", id);
            }
        }
    }
}
