using Pilaster.App.Diagnostics;

namespace Pilaster.Tests;

/// <summary>
/// A frissítés-ellenőrzés verzió-összehasonlítása — tiszta logika, hálózat
/// nélkül tesztelhető.
/// </summary>
public class SemanticVersionTests
{
    [Theory]
    [InlineData("v0.5.0", 0, 5, 0)]
    [InlineData("0.5.0", 0, 5, 0)]
    [InlineData("V1.2.3", 1, 2, 3)]
    [InlineData("2.0", 2, 0, 0)]
    [InlineData("v0.6.0-rc1", 0, 6, 0)]
    [InlineData("v0.6.0+abc123", 0, 6, 0)]
    public void TryParse_ErvenyesAlakokatHelyesenElemzi(string text, int major, int minor, int patch)
    {
        var parsed = SemanticVersion.TryParse(text, out var version);

        Assert.True(parsed);
        Assert.Equal(new SemanticVersion(major, minor, patch), version);
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
}
