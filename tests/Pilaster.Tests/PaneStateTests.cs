using Pilaster.App.ViewModels;
using Pilaster.Core.FileSystem;

namespace Pilaster.Tests;

/// <summary>
/// A kétpaneles nézet ÁLLAPOTKEZELÉSE — a v1.0 legsúlyosabb bejelentett
/// hibája (F7).
/// </summary>
/// <remarks>
/// <para>
/// Ezek a tesztek a <see cref="PaneViewModel"/> és a <see cref="TabViewModel"/>
/// állapotszétválasztását vizsgálják, WPF nélkül — ezért futnak a rendes
/// tesztkészletben, nem futásidejű öntesztként. Amit NEM tudnak lefedni (mert
/// valódi vizuális fát igényel): a kijelölés és a görgetési pozíció
/// visszaállítása fülváltáskor, a splitter húzása, és az elrendezés vizuális
/// váltása. Ezek a <c>docs/THEME-CHECKLIST.md</c> melletti
/// <c>docs/DUAL-PANE-CHECKLIST.md</c>-ben kézi ellenőrzésre vannak jelölve.
/// </para>
/// <para>
/// A <see cref="TabViewModel"/> valódi fájlrendszer-szolgáltatást igényelne,
/// ezért itt a fülek NEM navigálnak — az állapot-tulajdonságokat közvetlenül
/// állítjuk. Ez pontosan elég: a vizsgált kérdés az, hogy a két panel
/// állapota SZÉTVÁLIK-e, nem az, hogy a fájllistázás működik-e.
/// </para>
/// </remarks>
public class PaneStateTests
{
    /// <summary>
    /// Fül-gyártó valódi szolgáltatások nélkül. A <see cref="TabViewModel"/>
    /// konstruktora nem nyúl a fájlrendszerhez — csak a navigáció tenné —,
    /// ezért a null-továbbadás itt biztonságos, és nem húz be WPF-függést.
    /// </summary>
    private static PaneViewModel CreatePane(string id) =>
        new(id, () => new TabViewModel(null!, null!, new App.Services.FileMetadataService()), new FakeSettingsService());

    [Fact]
    public void KetPanelFuljeiTeljesenKulonallnak()
    {
        var left = CreatePane("left");
        var right = CreatePane("right");

        left.AddTab(TabViewModel.HomeMarker);
        left.AddTab(TabViewModel.HomeMarker);
        right.AddTab(TabViewModel.HomeMarker);

        Assert.Equal(2, left.Tabs.Count);
        Assert.Single(right.Tabs);
        Assert.DoesNotContain(left.ActiveTab, right.Tabs);
    }

    /// <summary>
    /// Külön kijelölés: az egyik panel kijelölése NEM változik attól, hogy a
    /// másikban változik. (A vizuális visszaállítást a nézet végzi, lásd az
    /// osztály megjegyzését — itt a MODELL szétválását igazoljuk.)
    /// </summary>
    [Fact]
    public void KulonKijeloles()
    {
        var left = CreatePane("left");
        var right = CreatePane("right");

        var leftTab = left.AddTab(TabViewModel.HomeMarker);
        var rightTab = right.AddTab(TabViewModel.HomeMarker);

        leftTab.SelectedPaths = [@"C:\a.txt", @"C:\b.txt"];
        rightTab.SelectedPaths = [@"C:\c.txt"];

        Assert.Equal([@"C:\a.txt", @"C:\b.txt"], leftTab.SelectedPaths);
        Assert.Equal([@"C:\c.txt"], rightTab.SelectedPaths);
    }

    /// <summary>Külön előzmény: a vissza-lépés csak a saját panel előzményét mozgatja.</summary>
    [Fact]
    public void KulonElozmeny()
    {
        var left = CreatePane("left");
        var right = CreatePane("right");

        var leftTab = left.AddTab(TabViewModel.HomeMarker);
        var rightTab = right.AddTab(TabViewModel.HomeMarker);

        // Az AddTab maga is navigál (a Kezdőlapra), ezért mindkét fül
        // előzménye onnan indul — a lényeg a KÜLÖNBSÉG, nem az abszolút mélység.
        leftTab.History.Navigate(@"C:\egy");
        leftTab.History.Navigate(@"C:\ketto");
        rightTab.History.Navigate(@"C:\harom");

        Assert.Equal(@"C:\ketto", leftTab.History.Current);
        Assert.Equal(@"C:\harom", rightTab.History.Current);
        Assert.Equal(2, leftTab.History.BackEntries.Count);
        Assert.Single(rightTab.History.BackEntries);

        // Vissza a BAL panelben...
        Assert.Equal(@"C:\egy", leftTab.History.GoBack());

        // ...a jobb panel előzménye érintetlen: se a helye, se a mélysége nem
        // változott, és nem keletkezett benne „előre" ág.
        Assert.Equal(@"C:\harom", rightTab.History.Current);
        Assert.Single(rightTab.History.BackEntries);
        Assert.False(rightTab.History.CanGoForward);
        Assert.True(leftTab.History.CanGoForward);
    }

    /// <summary>Külön rendezés: a két panel egyszerre rendezhető eltérő oszlop szerint.</summary>
    [Fact]
    public void KulonRendezes()
    {
        var left = CreatePane("left");
        var right = CreatePane("right");

        var leftTab = left.AddTab(TabViewModel.HomeMarker);
        var rightTab = right.AddTab(TabViewModel.HomeMarker);

        leftTab.ApplySort(SortKey.Size, descending: true);
        rightTab.ApplySort(SortKey.Modified, descending: false);

        Assert.Equal(SortKey.Size, leftTab.SortKey);
        Assert.True(leftTab.SortDescending);
        Assert.Equal(SortKey.Modified, rightTab.SortKey);
        Assert.False(rightTab.SortDescending);
    }

    /// <summary>Külön nézetmód és görgetési pozíció.</summary>
    [Fact]
    public void KulonNezetmodEsGorgetes()
    {
        var left = CreatePane("left");
        var right = CreatePane("right");

        var leftTab = left.AddTab(TabViewModel.HomeMarker);
        var rightTab = right.AddTab(TabViewModel.HomeMarker);

        leftTab.ViewMode = ViewMode.Grid;
        leftTab.ScrollOffset = 1234;
        rightTab.ViewMode = ViewMode.Details;
        rightTab.ScrollOffset = 0;

        Assert.Equal(ViewMode.Grid, leftTab.ViewMode);
        Assert.Equal(1234, leftTab.ScrollOffset);
        Assert.Equal(ViewMode.Details, rightTab.ViewMode);
        Assert.Equal(0, rightTab.ScrollOffset);
    }

    /// <summary>
    /// A fülváltás megőrzi az elhagyott fül állapotát — ez a
    /// „visszaváltáskor ott folytatódik" ígéret modell-oldali fele.
    /// </summary>
    [Fact]
    public void FulvaltasMegorziAzElhagyottFulAllapotat()
    {
        var pane = CreatePane("left");

        var first = pane.AddTab(TabViewModel.HomeMarker);
        first.SelectedPaths = [@"C:\megjegyzendo.txt"];
        first.ScrollOffset = 555;
        first.ApplySort(SortKey.Type, descending: true);

        var second = pane.AddTab(TabViewModel.HomeMarker);
        Assert.Same(second, pane.ActiveTab);

        pane.SelectTabCommand.Execute(first);

        Assert.Same(first, pane.ActiveTab);
        Assert.Equal([@"C:\megjegyzendo.txt"], first.SelectedPaths);
        Assert.Equal(555, first.ScrollOffset);
        Assert.Equal(SortKey.Type, first.SortKey);
    }

    /// <summary>Az utolsó fül nem zárható be — egy üres panelnek nincs értelmes állapota.</summary>
    [Fact]
    public void AzUtolsoFulNemZarhatoBe()
    {
        var pane = CreatePane("left");
        pane.AddTab(TabViewModel.HomeMarker);

        pane.CloseTabCommand.Execute(pane.ActiveTab);

        Assert.Single(pane.Tabs);
    }

    /// <summary>Fül bezárása után a szomszédos fül lesz az aktív, nem marad kijelöletlen állapot.</summary>
    [Fact]
    public void FulBezarasaUtanASzomszedLeszAzAktiv()
    {
        var pane = CreatePane("left");

        var first = pane.AddTab(TabViewModel.HomeMarker);
        var second = pane.AddTab(TabViewModel.HomeMarker);
        var third = pane.AddTab(TabViewModel.HomeMarker);

        pane.CloseTabCommand.Execute(second);

        Assert.Equal(2, pane.Tabs.Count);
        Assert.NotNull(pane.ActiveTab);
        Assert.Contains(pane.ActiveTab, new[] { first, third });
    }

    /// <summary>Ctrl+Tab körbeforog a panelen belül, és nem lép át a másik panelbe.</summary>
    [Fact]
    public void KorbeforgoFulvaltasAPanelenBelulMarad()
    {
        var pane = CreatePane("left");
        var other = CreatePane("right");
        other.AddTab(TabViewModel.HomeMarker);

        var first = pane.AddTab(TabViewModel.HomeMarker);
        var second = pane.AddTab(TabViewModel.HomeMarker);

        pane.ActiveTab = first;
        pane.NextTabCommand.Execute(null);
        Assert.Same(second, pane.ActiveTab);

        pane.NextTabCommand.Execute(null);
        Assert.Same(first, pane.ActiveTab);

        pane.PreviousTabCommand.Execute(null);
        Assert.Same(second, pane.ActiveTab);

        // A másik panel érintetlen.
        Assert.Single(other.Tabs);
    }

    /// <summary>
    /// Az aktív fül jelölése a MODELLEN él, nem a fülsáv kijelölésén — így két
    /// panel két fülsávja egyszerre mutathat aktív fület.
    /// </summary>
    [Fact]
    public void MindketPanelnekVanSajatAktivFulJeloltje()
    {
        var left = CreatePane("left");
        var right = CreatePane("right");

        var leftFirst = left.AddTab(TabViewModel.HomeMarker);
        var leftSecond = left.AddTab(TabViewModel.HomeMarker);
        var rightOnly = right.AddTab(TabViewModel.HomeMarker);

        Assert.False(leftFirst.IsActiveInPane);
        Assert.True(leftSecond.IsActiveInPane);
        Assert.True(rightOnly.IsActiveInPane);
    }

    [Fact]
    public void AktivFulIndexeAMunkamenetMentesehez()
    {
        var pane = CreatePane("left");

        pane.AddTab(TabViewModel.HomeMarker);
        var second = pane.AddTab(TabViewModel.HomeMarker);
        pane.AddTab(TabViewModel.HomeMarker);

        pane.ActiveTab = second;

        Assert.Equal(1, pane.ActiveTabIndex);
    }
}
