using VideoCompression.Api.Compression;

namespace VideoCompression.Api.Analysis;

public enum InsightTone { Positive, Neutral, Warning }

/// <summary>One observation about the source file that explains why it will (or won't) compress well.</summary>
public sealed record Insight(InsightTone Tone, string Title, string Detail);

public enum VerdictLevel { High, Good, Low, Minimal, None, Unknown }

/// <summary>The overall answer to "can this be compressed, and by how much?".</summary>
public sealed record Verdict(
    VerdictLevel Level,
    string Headline,
    string Summary,
    double RecommendedSavingsPercent,
    double BestSavingsPercent,
    string? SuggestedPresetId);

public sealed record PresetDefinition(
    string Id,
    string Name,
    string Tagline,
    string Description,
    bool Recommended,
    CompressionSettings Settings);

public enum PresetStatus { Pending, Measuring, Done, Failed, Unavailable }

/// <summary>A preset resolved against one video, with its measured estimate once available.</summary>
public sealed class PresetState(PresetDefinition definition, EncodePlan? plan, string? planError)
{
    public PresetDefinition Definition { get; } = definition;
    public EncodePlan? Plan { get; } = plan;
    public PresetStatus Status { get; set; } = plan is null ? PresetStatus.Unavailable : PresetStatus.Pending;
    public SizeEstimate? Estimate { get; set; }
    public string? Error { get; set; } = planError;
}
