using System.Globalization;

namespace VideoCompression.Api;

/// <summary>Human-readable formatting for messages shown to the user (1 MB = 1,000,000 bytes, like the UI).</summary>
public static class Units
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static string Bytes(double bytes)
    {
        string[] units = ["KB", "MB", "GB", "TB"];
        if (bytes < 1000) return $"{bytes.ToString("0", Invariant)} B";
        var value = bytes / 1000;
        var unit = 0;
        while (value >= 1000 && unit < units.Length - 1)
        {
            value /= 1000;
            unit++;
        }
        var format = value >= 100 ? "0" : value >= 10 ? "0.#" : "0.##";
        return $"{value.ToString(format, Invariant)} {units[unit]}";
    }

    public static string Megabytes(double megabytes) => Bytes(megabytes * 1_000_000);

    public static string Bitrate(double bitsPerSecond) => bitsPerSecond >= 1_000_000
        ? $"{(bitsPerSecond / 1_000_000).ToString(bitsPerSecond >= 10_000_000 ? "0" : "0.#", Invariant)} Mbps"
        : $"{(bitsPerSecond / 1000).ToString("0", Invariant)} kbps";

    public static string Fps(double fps) => Math.Abs(fps - Math.Round(fps)) < 0.01
        ? Math.Round(fps).ToString(Invariant)
        : fps.ToString("0.##", Invariant);

    public static string Duration(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Round(seconds));
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{time.Minutes}:{time.Seconds:00}";
    }

    public static string Percent(double percent) => $"{Math.Round(percent).ToString("0", Invariant)}%";

    /// <summary>"4K", "1080p", "720p"… based on the shorter side, so portrait videos are labelled correctly.</summary>
    public static string Resolution(int width, int height)
    {
        var shortSide = Math.Min(width, height);
        return shortSide switch
        {
            >= 2000 => "4K",
            >= 1400 => "1440p",
            >= 1000 => "1080p",
            >= 700 => "720p",
            >= 520 => "540p",
            >= 470 => "480p",
            >= 350 => "360p",
            _ => $"{shortSide}p",
        };
    }

    /// <summary>Formats a number for an FFmpeg argument (always '.' as the decimal separator).</summary>
    public static string Arg(double value) => value.ToString("0.###", Invariant);
}
