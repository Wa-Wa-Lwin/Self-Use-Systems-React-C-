using VideoCompression.Api.Endpoints;

namespace VideoCompression.Tests;

public class FileNamesTests
{
    private const string Fallback = "holiday-compressed.mp4";

    [Theory]
    [InlineData("Summer trip", "Summer trip.mp4")]
    [InlineData("  Summer trip  ", "Summer trip.mp4")]
    [InlineData("clip.MP4", "clip.mp4")]                 // no double extension
    [InlineData("clip.mov", "clip.mp4")]                 // the output is MP4 whatever the user typed
    [InlineData("v1.2 final", "v1.2 final.mp4")]         // dots inside the name are fine
    [InlineData("Trip: day 1?", "Trip_ day 1_.mp4")]     // characters Windows forbids
    [InlineData("AC/DC live", "AC_DC live.mp4")]         // slashes don't drop part of the name
    [InlineData("tab\there", "tab_here.mp4")]            // control characters
    [InlineData("...hidden. .", "hidden.mp4")]           // leading/trailing dots and spaces
    [InlineData("café 🎬", "café 🎬.mp4")]               // non-ASCII is kept
    public void Cleans_user_typed_names(string typed, string expected)
    {
        Assert.Equal(expected, FileNames.ForDownload(typed, Fallback));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" . . ")]
    [InlineData(".mp4")]
    public void Falls_back_when_nothing_usable_is_left(string? typed)
    {
        Assert.Equal(Fallback, FileNames.ForDownload(typed, Fallback));
    }

    [Fact]
    public void Limits_the_length_without_splitting_emoji()
    {
        var name = FileNames.ForDownload(new string('a', FileNames.MaxStemLength - 1) + "🎬🎬🎬", Fallback);
        Assert.EndsWith(".mp4", name);
        Assert.True(name.Length <= FileNames.MaxStemLength + 4);
        Assert.False(char.IsHighSurrogate(name[^5]), "a surrogate pair was split");
    }
}
