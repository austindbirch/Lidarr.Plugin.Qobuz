using NzbDrone.Core.Indexers.Qobuz;
using Xunit;

public class RelaxAlbumTitleTests
{
    [Theory]
    [InlineData("Dreams EP", "Dreams")]
    [InlineData("Self Care (The Knocks Remix)", "Self Care")]
    [InlineData("Human Learning (Deluxe Edition)", "Human Learning")]
    [InlineData("Album [Remastered]", "Album")]
    [InlineData("Songs - Deluxe", "Songs")]
    public void StripsQualifiersAndBrackets(string input, string expected)
        => Assert.Equal(expected, QobuzSearchQuery.RelaxAlbumTitle(input));

    // Plain titles (incl. numeric/dotted ones that must NOT be mangled) pass through.
    [Theory]
    [InlineData("Human Learning")]
    [InlineData("08.26.18")]
    [InlineData("f.e.a.r.")]
    [InlineData("Girl, Say")]
    public void LeavesPlainTitlesUnchanged(string input)
        => Assert.Equal(input, QobuzSearchQuery.RelaxAlbumTitle(input));

    // Nothing meaningful left → empty, so the caller skips the relaxed tier.
    [Theory]
    [InlineData("(Deluxe)")]
    [InlineData("   ")]
    [InlineData("")]
    public void ReturnsEmptyWhenNothingLeft(string input)
        => Assert.Equal(string.Empty, QobuzSearchQuery.RelaxAlbumTitle(input));

    [Fact]
    public void NormalizeStripsApostropheVariants()
        => Assert.Equal("Ill Explain", QobuzSearchQuery.NormalizeSearchQuery("I’ll Explain"));
}
