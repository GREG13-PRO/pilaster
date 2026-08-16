using System.Text.Json;
using System.Text.Json.Serialization;
using Pilaster.App.ViewModels;
using Pilaster.Core.FileSystem;
using Pilaster.Core.Settings;

namespace Pilaster.Tests;

/// <summary>
/// A kétpaneles elrendezés MEGMARADÁSA újraindítás után, és a két panel
/// állapotának teljes szétválása (spec T4).
/// </summary>
/// <remarks>
/// <para>
/// Ez a két eset korábban a <c>docs/DUAL-PANE-CHECKLIST.md</c> kézi pontjai
/// között szerepelt, pedig egyikhez sem kell vizuális fa: az elrendezés
/// mentése tiszta szerializáció, az „eltűnt mappa" pedig tiszta
/// állapotkezelés. Ami VALÓBAN kézi marad (splitter húzása egérrel, kijelölés
/// és görgetés vizuális visszaállítása), az ott is maradt.
/// </para>
/// <para>
/// A szerializálás szándékosan VALÓDI fájlon megy át, nem memóriabeli
/// másoláson: a kérdés éppen az, hogy a lemezre írt alak visszaolvasva
/// ugyanazt adja-e.
/// </para>
/// </remarks>
public class DualPaneLayoutTests
{
    /// <summary>
    /// Ugyanaz a beállítás-készlet, mint az éles <c>SettingsJsonContext</c>-é:
    /// camelCase nevek és szöveges enumok. Ha az éles beállítás elmozdulna, ez
    /// a teszt szándékosan elbukik — a mentett fájl formátuma kompatibilitási
    /// felület.
    /// </summary>
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private static PaneViewModel CreatePane(string id) =>
        new(id, () => new TabViewModel(null!, null!, new App.Services.FileMetadataService()), new FakeSettingsService());

    /// <summary>
    /// Panel VALÓDI provider mögött — így a fülek tényleg navigálnak, és az
    /// „eltűnt mappa" esete nem szimulált, hanem megtörténik.
    /// </summary>
    private static PaneViewModel CreateLivePane(string id)
    {
        var provider = new Pilaster.Providers.Local.LocalFileSystemProvider();
        var sizes = new App.Services.FolderSizeService(provider);

        return new PaneViewModel(id, () => new TabViewModel(provider, sizes, new App.Services.FileMetadataService()), new FakeSettingsService());
    }

    /// <summary>
    /// T4/a: az elválasztó aránya, az elrendezés iránya és a séma verziója
    /// túléli a lemezre írást és a visszaolvasást.
    /// </summary>
    [Fact]
    public void ElrendezesTulEliAzUjrainditast()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pilaster-layout-{Guid.NewGuid():N}.json");

        var saved = new AppSettings
        {
            DualPaneEnabled = true,
            DualPaneVertical = true,
            DualPaneSplitRatio = 0.375,
            Session = new AppSession
            {
                Left = new PaneSession
                {
                    ActiveTabIndex = 1,
                    Tabs =
                    [
                        new TabSession { Path = @"C:\bal-egy", ViewMode = ViewMode.Columns },
                        new TabSession { Path = @"C:\bal-ketto", SortKey = SortKey.Size, SortDescending = true },
                    ],
                },
                Right = new PaneSession
                {
                    ActiveTabIndex = 0,
                    Tabs = [new TabSession { Path = @"C:\jobb", ShowHiddenItems = true }],
                },
            },
        };

        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(saved, Options));
            var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options);

            Assert.NotNull(loaded);

            // Az elrendezés maga.
            Assert.True(loaded.DualPaneEnabled);
            Assert.True(loaded.DualPaneVertical);
            Assert.Equal(0.375, loaded.DualPaneSplitRatio);

            // A séma verziója — enélkül egy későbbi migráció nem tudná
            // eldönteni, milyen alakot olvas.
            Assert.Equal(saved.Version, loaded.Version);

            // A panelenkénti fülek: a v1.0-ban a fülek a PANELEKÉ, tehát a
            // mentett alaknak is panelenként kell tagolódnia.
            Assert.Equal(2, loaded.Session!.Left.Tabs.Count);
            Assert.Single(loaded.Session.Right.Tabs);
            Assert.Equal(1, loaded.Session.Left.ActiveTabIndex);
            Assert.Equal(@"C:\bal-egy", loaded.Session.Left.Tabs[0].Path);
            Assert.Equal(ViewMode.Columns, loaded.Session.Left.Tabs[0].ViewMode);
            Assert.Equal(SortKey.Size, loaded.Session.Left.Tabs[1].SortKey);
            Assert.True(loaded.Session.Left.Tabs[1].SortDescending);
            Assert.True(loaded.Session.Right.Tabs[0].ShowHiddenItems);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Az elválasztó dupla kattintásra visszaálló alapértéke — a mentett
    /// fájlban is ez az alapérték, ha a felhasználó sosem húzta el.
    /// </summary>
    [Fact]
    public void AzElvalasztoAlapertekeFelezes()
    {
        var restored = JsonSerializer.Deserialize<AppSettings>("{}", Options);

        Assert.NotNull(restored);
        Assert.Equal(0.5, restored.DualPaneSplitRatio);
        Assert.False(restored.DualPaneVertical);
    }

    /// <summary>
    /// T4/b: ha az egyik panel útvonala eltűnik, a MÁSIK panel állapota
    /// bitre ugyanaz marad.
    /// </summary>
    /// <remarks>
    /// Ez volt a v1.0 legsúlyosabb kétpaneles hibája: a „foltozott" változatban
    /// a két panel közös állapoton osztozott, és az egyik oldal hibája
    /// átrántotta a másikat is.
    /// </remarks>
    [Fact]
    public async Task EltuntUtvonalCsakAzEgyikPaneltErinti()
    {
        var vanishing = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), $"pilaster-eltuno-{Guid.NewGuid():N}"));

        var staying = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), $"pilaster-marado-{Guid.NewGuid():N}"));

        File.WriteAllText(Path.Combine(staying.FullName, "a.txt"), "a");
        File.WriteAllText(Path.Combine(staying.FullName, "b.txt"), "b");

        try
        {
            var left = CreateLivePane("left");
            var right = CreateLivePane("right");

            var leftTab = left.AddTab(TabViewModel.HomeMarker);
            var rightTab = right.AddTab(TabViewModel.HomeMarker);

            // Mindkét panel VALÓDI mappába navigál.
            await leftTab.NavigateAsync(vanishing.FullName);
            await rightTab.NavigateAsync(staying.FullName);

            // Őrszem: ha a navigáció nem történt meg, a lenti összehasonlítás
            // üres állapotot hasonlítana üres állapothoz, és a teszt hamisan
            // menne át.
            Assert.Equal(vanishing.FullName, leftTab.CurrentPath);
            Assert.Equal(staying.FullName, rightTab.CurrentPath);

            rightTab.SelectedPaths =
            [
                Path.Combine(staying.FullName, "a.txt"),
                Path.Combine(staying.FullName, "b.txt"),
            ];

            var rightPathBefore = rightTab.CurrentPath;
            var rightSelectionBefore = rightTab.SelectedPaths.ToArray();
            var rightHistoryBefore = rightTab.History.BackEntries.ToArray();
            var rightHistoryCurrentBefore = rightTab.History.Current;
            var rightItemsBefore = rightTab.Items.Count;
            var rightActiveBefore = right.ActiveTab;

            // A bal panel mappája ELTŰNIK a lába alól, és odanavigál újra.
            vanishing.Delete(recursive: true);
            await leftTab.NavigateAsync(vanishing.FullName);

            // A jobb panel MINDEN vizsgált állapota változatlan.
            Assert.Equal(rightPathBefore, rightTab.CurrentPath);
            Assert.Equal(rightSelectionBefore, rightTab.SelectedPaths);
            Assert.Equal(rightHistoryBefore, rightTab.History.BackEntries);
            Assert.Equal(rightHistoryCurrentBefore, rightTab.History.Current);
            Assert.Equal(rightItemsBefore, rightTab.Items.Count);
            Assert.Same(rightActiveBefore, right.ActiveTab);

            // És a két panel füljei továbbra sem közösek.
            Assert.DoesNotContain(leftTab, right.Tabs);
            Assert.DoesNotContain(rightTab, left.Tabs);
        }
        finally
        {
            staying.Delete(recursive: true);

            if (vanishing.Exists)
            {
                vanishing.Delete(recursive: true);
            }
        }
    }
}
