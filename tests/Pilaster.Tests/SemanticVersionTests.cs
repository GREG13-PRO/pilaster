using Pilaster.App.Diagnostics;

namespace Pilaster.Tests;

/// <summary>
/// A frissítés-ellenőrzés verzió-összehasonlítása — tiszta logika, hálózat
/// nélkül tesztelhető.
/// </summary>
public class SemanticVersionTests
{
    [Theory]
    [InlineData("v0.5.0", 0, 5, 0, 0)]
    [InlineData("0.5.0", 0, 5, 0, 0)]
    [InlineData("V1.2.3", 1, 2, 3, 0)]
    [InlineData("2.0", 2, 0, 0, 0)]
    [InlineData("v0.6.0-rc1", 0, 6, 0, 0)]
    [InlineData("v0.6.0+abc123", 0, 6, 0, 0)]
    [InlineData("v0.9.0.1", 0, 9, 0, 1)]
    [InlineData("0.9.0.12", 0, 9, 0, 12)]
    public void TryParse_ErvenyesAlakokatHelyesenElemzi(string text, int major, int minor, int patch, int revision)
    {
        var parsed = SemanticVersion.TryParse(text, out var version);

        Assert.True(parsed);
        Assert.Equal(new SemanticVersion(major, minor, patch, revision), version);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("dev")]
    [InlineData("v")]
    [InlineData("abc.def")]
    public void TryParse_ErvenytelenAlakoknalHamisAdVissza(string? text)
    {
        Assert.False(SemanticVersion.TryParse(text, out _));
    }

    [Fact]
    public void CompareTo_NagyobbMajorNyer()
    {
        var current = new SemanticVersion(0, 9, 9);
        var next = new SemanticVersion(1, 0, 0);

        Assert.True(next > current);
    }

    [Fact]
    public void CompareTo_NagyobbPatchNyerAzonosMajorMinorMellett()
    {
        var current = new SemanticVersion(0, 5, 0);
        var next = new SemanticVersion(0, 5, 1);

        Assert.True(next > current);
    }

    [Fact]
    public void CompareTo_AzonosVerzioNemNagyobb()
    {
        var a = new SemanticVersion(0, 5, 0);
        var b = new SemanticVersion(0, 5, 0);

        Assert.False(a > b);
        Assert.False(b > a);
        Assert.True(a >= b);
    }

    /// <summary>
    /// Ez pontosan azt az esetet fedi, amikor a GitHubUpdateService eldönti,
    /// hogy a legfrissebb kiadás újabb-e a jelenlegi verziónál.
    /// </summary>
    [Fact]
    public void CompareTo_KisebbVerzioNemSzamitFrissitesnek()
    {
        SemanticVersion.TryParse("v0.4.0", out var latest);
        SemanticVersion.TryParse("0.5.0", out var current);

        Assert.False(latest > current);
    }

    /// <summary>
    /// Valódi hiba volt: a negyedik (revízió) tag hiánya miatt a
    /// GitHubUpdateService "v0.9.0.1"-et és a futó "0.9.0"-t egyaránt
    /// "0.9.0"-ra csonkolta, ezért egyenlőnek látta őket, és a frissítés-
    /// ellenőrzés hamisan "naprakész"-t jelentett a felhasználónak.
    /// </summary>
    [Fact]
    public void CompareTo_NegyedikRevizioTagNyerAzonosMajorMinorPatchMellett()
    {
        SemanticVersion.TryParse("0.9.0", out var current);
        SemanticVersion.TryParse("v0.9.0.1", out var latest);

        Assert.True(latest > current);
        Assert.False(latest <= current);
    }

    [Fact]
    public void ToString_HaromReszreRovidulHaNincsRevizio()
    {
        var version = new SemanticVersion(0, 9, 0);

        Assert.Equal("0.9.0", version.ToString());
    }

    [Fact]
    public void ToString_NegyReszesHaVanRevizio()
    {
        var version = new SemanticVersion(0, 9, 0, 1);

        Assert.Equal("0.9.0.1", version.ToString());
    }
}
