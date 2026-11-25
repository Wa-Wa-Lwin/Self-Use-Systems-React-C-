using System.Text;

namespace VideoCompression.Api.Endpoints;

/// <summary>
/// Turns a name the user typed into a safe download name. The name only ever goes into the
/// Content-Disposition header (never into a server path), so this is about producing a name
/// every OS accepts, not about path traversal.
/// </summary>
public static class FileNames
{
    public const int MaxStemLength = 120;

    // Characters Windows forbids in file names (the strictest common OS); '/' also covers macOS/Linux.
    private const string InvalidCharacters = "\\/:*?\"<>|";

    /// <returns>"&lt;clean name&gt;.mp4", or <paramref name="fallback"/> when nothing usable is left.</returns>
    public static string ForDownload(string? requested, string fallback)
    {
        var stem = CleanStem(requested);
        return stem.Length > 0 ? stem + ".mp4" : fallback;
    }

    /// <summary>
    /// Drops a trailing video extension (the output is always MP4), replaces forbidden and control
    /// characters with '_', trims spaces and dots, and limits the length.
    /// </summary>
    public static string CleanStem(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;

        var text = name.Trim();
        var extension = Path.GetExtension(text);
        if (extension.Length > 0 && ApiEndpoints.AcceptedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            text = text[..^extension.Length];

        var builder = new StringBuilder(text.Length);
        foreach (var c in text) builder.Append(char.IsControl(c) || InvalidCharacters.Contains(c) ? '_' : c);

        var stem = builder.ToString().Trim().Trim(' ', '.');
        if (stem.Length > MaxStemLength)
        {
            var cut = char.IsHighSurrogate(stem[MaxStemLength - 1]) ? MaxStemLength - 1 : MaxStemLength; // don't split an emoji
            stem = stem[..cut].TrimEnd(' ', '.');
        }
        return stem;
    }
}
