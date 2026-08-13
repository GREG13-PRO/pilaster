using System.Globalization;
using Pilaster.Core.Templates;

namespace Pilaster.Tests;

/// <summary>A gyorsgombok névsablonjai.</summary>
public class NameTemplateTests
{
    private static readonly DateTime Reference = new(2026, 8, 13, 21, 45, 3);
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    [Fact]
    public void Expand_HelyorzoNelkulValtozatlan()
    {
        Assert.Equal("Új mappa", NameTemplate.Expand("Új mappa", Reference, Culture));
    }

    [Fact]
    public void Expand_DatumEsIdo()
    {
        Assert.Equal("2026-08-13", NameTemplate.Expand("{date}", Reference, Culture));

        // Az időben szándékosan nincs kettőspont: fájlnévben tiltott karakter.
        Assert.Equal("21-45-03", NameTemplate.Expand("{time}", Reference, Culture));
        Assert.Equal("2026-08-13_21-45-03", NameTemplate.Expand("{datetime}", Reference, Culture));
    }

    [Fact]
    public void Expand_EgyediFormatum()
    {
        Assert.Equal("2026.08.13", NameTemplate.Expand("{date:yyyy.MM.dd}", Reference, Culture));
    }

    [Fact]
    public void Expand_SzovegKozottiHelyorzo()
    {
        Assert.Equal(
            "Jegyzet 2026-08-13 vege",
            NameTemplate.Expand("Jegyzet {date} vege", Reference, Culture));
    }

    /// <summary>
    /// A sorszámot csak az ütközésfeloldás tudja kitölteni, mert ahhoz ismerni
    /// kell a célmappa tartalmát — a behelyettesítés ezért érintetlenül hagyja.
    /// </summary>
    [Fact]
    public void Expand_SorszamotMegNemHelyettesiti()
    {
        Assert.Equal("Kep {n}", NameTemplate.Expand("Kep {n}", Reference, Culture));
    }

    [Fact]
    public void Expand_IsmeretlenKulcsMegmarad()
    {
        // Az elgépelés maradjon látható, ne tűnjön el némán a név egy darabja.
        Assert.Equal("{nincsilyen}", NameTemplate.Expand("{nincsilyen}", Reference, Culture));
    }

    [Fact]
    public void Expand_TiltottKarakterekEltavolitasa()
    {
        Assert.Equal("ab", NameTemplate.Expand("a<>:\"/\\|?*b", Reference, Culture));
    }

    [Fact]
    public void Expand_UresBemenetreUresErtek()
    {
        Assert.Equal(string.Empty, NameTemplate.Expand("   ", Reference, Culture));
    }

    [Fact]
    public void Sanitize_CsakTiltottKarakterekEsetenNevtelen()
    {
        Assert.Equal("Névtelen", NameTemplate.Sanitize("///"));
    }

    [Fact]
    public void ResolveUnique_SzabadNevetValtozatlanulAd()
    {
        using var temp = new TempFolder();

        Assert.Equal("Új mappa", NameTemplate.ResolveUnique(temp.Path, "Új mappa", string.Empty));
    }

    /// <summary>Foglalt névnél a Windows szokása szerinti „(2)" utótag jön.</summary>
    [Fact]
    public void ResolveUnique_UtkozeskorZarojelesSorszam()
    {
        using var temp = new TempFolder();
        Directory.CreateDirectory(Path.Combine(temp.Path, "Új mappa"));

        Assert.Equal("Új mappa (2)", NameTemplate.ResolveUnique(temp.Path, "Új mappa", string.Empty));

        Directory.CreateDirectory(Path.Combine(temp.Path, "Új mappa (2)"));

        Assert.Equal("Új mappa (3)", NameTemplate.ResolveUnique(temp.Path, "Új mappa", string.Empty));
    }

    [Fact]
    public void ResolveUnique_KiterjesztestHozzafuz()
    {
        using var temp = new TempFolder();

        Assert.Equal("jegyzet.txt", NameTemplate.ResolveUnique(temp.Path, "jegyzet", "txt"));
    }

    [Fact]
    public void ResolveUnique_FajlUtkozesnelIsSorszamoz()
    {
        using var temp = new TempFolder();
        File.WriteAllText(Path.Combine(temp.Path, "jegyzet.txt"), string.Empty);

        Assert.Equal("jegyzet (2).txt", NameTemplate.ResolveUnique(temp.Path, "jegyzet", "txt"));
    }

    /// <summary>
    /// Ha a minta tartalmaz <c>{n}</c>-t, a sorszám oda kerül, nem a név végére.
    /// </summary>
    [Fact]
    public void ResolveUnique_SorszamAHelyorzoHelyere()
    {
        using var temp = new TempFolder();

        Assert.Equal("Kep 1.txt", NameTemplate.ResolveUnique(temp.Path, "Kep {n}", "txt"));

        File.WriteAllText(Path.Combine(temp.Path, "Kep 1.txt"), string.Empty);

        Assert.Equal("Kep 2.txt", NameTemplate.ResolveUnique(temp.Path, "Kep {n}", "txt"));
    }

    private sealed class TempFolder : IDisposable
    {
        public TempFolder()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "pilaster-test-" + Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // A takarítás elmaradása nem tesztkudarc.
            }
        }
    }
}
