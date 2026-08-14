using Pilaster.App.Services;
using Pilaster.Core.Settings;

namespace Pilaster.Tests;

/// <summary>
/// A gyorselérés perzisztenciája — ez volt a v0.9-es hiba lényege: „csak egy
/// kis x-szel lehet elrejteni elemeket, és a változtatás nem marad meg".
/// Minden teszt saját, ideiglenes mappán fut, a valódi felhasználói profil
/// érintése nélkül.
/// </summary>
public sealed class QuickAccessServiceTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "pilaster-qa-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // Egy takarítási hiba nem buktathat el egy zöld tesztet.
        }
    }

    private QuickAccessService Create() => new(_directory);

    [Fact]
    public void Pin_UjraBetoltesUtanIsMegmarad()
    {
        using (var service = Create())
        {
            service.Pin(@"C:\Munka", "Munka");
            service.Flush();
        }

        using var reloaded = Create();

        Assert.Contains(reloaded.Pinned, e => e.Path == @"C:\Munka" && e.Label == "Munka");
    }

    [Fact]
    public void Remove_UjraBetoltesUtanIsElvesz()
    {
        string id;

        using (var service = Create())
        {
            service.Pin(@"C:\Munka");
            id = service.Pinned.Single(e => e.Path == @"C:\Munka").Id;
            service.Remove(id);
            service.Flush();
        }

        using var reloaded = Create();

        Assert.DoesNotContain(reloaded.Pinned, e => e.Id == id);
    }

    /// <summary>
    /// Ez a v0.9-es viselkedés legfontosabb javítása: az átrendezés sorrendje
    /// is tartós, nem csak a lista tagsága.
    /// </summary>
    [Fact]
    public void Reorder_SorrendUjraBetoltesUtanIsMegmarad()
    {
        using (var service = Create())
        {
            service.ReplacePinned([]);
            service.Pin(@"C:\Egy");
            service.Pin(@"C:\Ketto");
            service.Pin(@"C:\Harom");

            var third = service.Pinned.Single(e => e.Path == @"C:\Harom").Id;
            service.Reorder(third, 0);
            service.Flush();
        }

        using var reloaded = Create();

        Assert.Equal([@"C:\Harom", @"C:\Egy", @"C:\Ketto"], reloaded.Pinned.Select(e => e.Path));
    }

    [Fact]
    public void Pin_UgyanazAzUtvonalNemKerulBeKetszer()
    {
        using var service = Create();
        service.ReplacePinned([]);

        service.Pin(@"C:\Munka");
        service.Pin(@"C:\Munka");

        Assert.Single(service.Pinned, e => e.Path == @"C:\Munka");
    }

    [Fact]
    public void Update_AtnevezesEsIkonUjraBetoltesUtanIsMegmarad()
    {
        string id;

        using (var service = Create())
        {
            service.Pin(@"C:\Munka");
            id = service.Pinned.Single(e => e.Path == @"C:\Munka").Id;

            service.Update(id, e =>
            {
                e.Label = "Projektek";
                e.Icon = "Briefcase24";
                e.Color = "#0078D4";
                e.Group = "Fejlesztés";
            });

            service.Flush();
        }

        using var reloaded = Create();
        var entry = reloaded.Pinned.Single(e => e.Id == id);

        Assert.Equal("Projektek", entry.Label);
        Assert.Equal("Briefcase24", entry.Icon);
        Assert.Equal("#0078D4", entry.Color);
        Assert.Equal("Fejlesztés", entry.Group);
    }

    /// <summary>
    /// A láthatóság kikapcsolása NEM törlés: a bejegyzés megmarad, csak nem
    /// jelenik meg — így a szerkesztőben visszakapcsolható.
    /// </summary>
    [Fact]
    public void Update_LathatosagKikapcsolasaNemTorliABejegyzest()
    {
        using var service = Create();
        service.Pin(@"C:\Munka");
        var id = service.Pinned.Single(e => e.Path == @"C:\Munka").Id;

        service.Update(id, e => e.Visible = false);

        Assert.Contains(service.Pinned, e => e.Id == id && !e.Visible);
    }

    [Fact]
    public void MigrateFromLegacyPins_AtvesziARegiListat()
    {
        using var service = Create();

        service.MigrateFromLegacyPins(
        [
            new PinnedFolder { Path = @"C:\Regi1", LabelKey = "Nav_Desktop", Icon = "Desktop24" },
            new PinnedFolder { Path = @"C:\Regi2", CustomLabel = "Saját" },
        ]);

        Assert.Equal([@"C:\Regi1", @"C:\Regi2"], service.Pinned.Select(e => e.Path));
        Assert.Equal("Nav_Desktop", service.Pinned[0].LabelKey);
        Assert.Equal("Saját", service.Pinned[1].Label);
    }

    /// <summary>
    /// A migráció csak ÜRES tárolóra fut le — különben egy második indítás
    /// felülírná a felhasználó azóta elvégzett testreszabását.
    /// </summary>
    [Fact]
    public void MigrateFromLegacyPins_NemIrjaFelulAMarLetezoTartalmat()
    {
        using var service = Create();
        service.Pin(@"C:\Sajat");

        service.MigrateFromLegacyPins([new PinnedFolder { Path = @"C:\Regi" }]);

        Assert.Contains(service.Pinned, e => e.Path == @"C:\Sajat");
        Assert.DoesNotContain(service.Pinned, e => e.Path == @"C:\Regi");
    }

    [Fact]
    public void RecordRecent_LegfrissebbElolEsLimitreVag()
    {
        using var service = Create();
        service.ReplacePinned([]);
        service.RecentLimit = 2;

        service.RecordRecent(@"C:\A");
        service.RecordRecent(@"C:\B");
        service.RecordRecent(@"C:\C");

        Assert.Equal([@"C:\C", @"C:\B"], service.Recent.Select(e => e.Path));
    }

    [Fact]
    public void RecordRecent_MarRogzitettMappatNemVeszFel()
    {
        using var service = Create();
        service.Pin(@"C:\Munka");

        service.RecordRecent(@"C:\Munka");

        Assert.Empty(service.Recent);
    }

    [Fact]
    public void ExportImport_KorbeMegyEsMegorziASorrendet()
    {
        var file = Path.Combine(_directory, "export.json");

        using (var service = Create())
        {
            service.ReplacePinned([]);
            service.Pin(@"C:\Egy");
            service.Pin(@"C:\Ketto");
            Assert.True(service.TryExport(file));
        }

        using var target = new QuickAccessService(Path.Combine(_directory, "masik"));
        target.ReplacePinned([]);

        Assert.True(target.TryImport(file));
        Assert.Equal([@"C:\Egy", @"C:\Ketto"], target.Pinned.Select(e => e.Path));
    }

    [Fact]
    public void TryImport_ErvenytelenFajlnalHamis_EsNemBorulABelsoAllapot()
    {
        var file = Path.Combine(_directory, "rossz.json");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(file, "{ ez nem json");

        using var service = Create();
        service.Pin(@"C:\Munka");

        Assert.False(service.TryImport(file));
        Assert.Contains(service.Pinned, e => e.Path == @"C:\Munka");
    }

    /// <summary>
    /// Sérült vagy kézzel szerkesztett fájl: a betöltés nem dobhat, és az
    /// azonosító nélküli bejegyzések kapjanak generáltat, különben nem
    /// lennének átrendezhetők vagy eltávolíthatók.
    /// </summary>
    [Fact]
    public void Load_AzonositoNelkuliBejegyzesKapEgyet()
    {
        Directory.CreateDirectory(_directory);

        File.WriteAllText(
            Path.Combine(_directory, "quickaccess.json"),
            """{"version":1,"entries":[{"id":"","kind":"Folder","path":"C:\\Munka","pinned":true}]}""");

        using var service = Create();

        Assert.All(service.Pinned, e => Assert.False(string.IsNullOrWhiteSpace(e.Id)));
    }
}
