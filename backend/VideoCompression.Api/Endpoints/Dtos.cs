using VideoCompression.Api.Analysis;
using VideoCompression.Api.Compression;
using VideoCompression.Api.Media;
using VideoCompression.Api.Storage;

namespace VideoCompression.Api.Endpoints;

public sealed record CodecDto(
    VideoCodec Id, string Label, bool Available, int CrfMin, int CrfMax, int CrfDefault, string Compatibility, string Description);

public sealed record HealthDto(
    bool FfmpegAvailable,
    string? FfmpegVersion,
    string? Message,
    IReadOnlyList<CodecDto> Codecs,
    long MaxUploadBytes,
    IReadOnlyList<string> AcceptedExtensions);

public sealed record PlanDto(
    string Codec,
    int Width,
    int Height,
    double Fps,
    string RateControl,
    string Audio,
    string Summary,
    string? QualityHint,
    IReadOnlyList<string> Warnings);

public sealed record PresetDto(
    string Id,
    string Name,
    string Tagline,
    string Description,
    bool Recommended,
    CompressionSettings Settings,
    PlanDto? Plan,
    PresetStatus Status,
    SizeEstimate? Estimate,
    string? Error);

public sealed record AnalysisDto(bool Done, int Completed, int Total);

public sealed record VideoDto(
    string Id,
    string FileName,
    long SizeBytes,
    DateTimeOffset UploadedAt,
    MediaInfo Media,
    IReadOnlyList<Insight> Insights,
    IReadOnlyList<PresetDto> Presets,
    AnalysisDto Analysis,
    Verdict? Verdict,
    double MinTargetSizeMb);

public sealed record EstimateDto(bool Valid, string? Error, PlanDto? Plan, SizeEstimate? Estimate, double? MinTargetSizeMb);

public sealed record JobDto(
    string Id,
    string VideoId,
    JobStatus Status,
    double Progress,
    string Stage,
    double? Speed,
    double? EtaSeconds,
    double ElapsedSeconds,
    long OriginalBytes,
    long? OutputBytes,
    double? SavingsPercent,
    string? Error,
    PlanDto Plan,
    string FileName,
    string? DownloadUrl,
    string? PreviewUrl);

public static class DtoMapper
{
    public static PlanDto ToDto(this EncodePlan plan) => new(
        plan.Codec.Label,
        plan.Width,
        plan.Height,
        Math.Round(plan.Fps, 2),
        plan.RateDescription,
        plan.AudioDescription,
        plan.Summary,
        plan.QualityHint,
        plan.Warnings);

    public static VideoDto ToDto(this VideoRecord video)
    {
        lock (video.Sync)
        {
            var presets = video.Presets.Select(p => new PresetDto(
                    p.Definition.Id,
                    p.Definition.Name,
                    p.Definition.Tagline,
                    p.Definition.Description,
                    p.Definition.Recommended,
                    p.Definition.Settings,
                    p.Plan?.ToDto(),
                    p.Status,
                    p.Estimate,
                    p.Error))
                .ToList();
            var completed = video.Presets.Count(p => p.Status is PresetStatus.Done or PresetStatus.Failed or PresetStatus.Unavailable);

            return new VideoDto(
                video.Id,
                video.FileName,
                video.Media.SizeBytes,
                video.UploadedAt,
                video.Media,
                video.Insights,
                presets,
                new AnalysisDto(video.AnalysisDone || completed == video.Presets.Count, completed, video.Presets.Count),
                video.Verdict,
                video.MinTargetSizeMb);
        }
    }

    public static JobDto ToDto(this CompressionJob job)
    {
        var snapshot = job.Snapshot();
        var done = snapshot.Status == JobStatus.Completed;
        double? savings = snapshot.OutputBytes is { } output && job.OriginalBytes > 0
            ? Math.Round((1 - output / (double)job.OriginalBytes) * 100, 1)
            : null;

        return new JobDto(
            job.Id,
            job.VideoId,
            snapshot.Status,
            Math.Round(snapshot.Progress * 100, 1),
            snapshot.Stage,
            snapshot.Speed,
            snapshot.EtaSeconds is { } eta ? Math.Round(eta) : null,
            Math.Round(snapshot.ElapsedSeconds, 1),
            job.OriginalBytes,
            snapshot.OutputBytes,
            savings,
            snapshot.Error,
            job.Plan.ToDto(),
            job.DownloadName,
            done ? $"/api/jobs/{job.Id}/file?download=true" : null,
            done ? $"/api/jobs/{job.Id}/file" : null);
    }
}
