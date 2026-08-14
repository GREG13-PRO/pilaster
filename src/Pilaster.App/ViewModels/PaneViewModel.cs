using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pilaster.Core.FileSystem;

namespace Pilaster.App.ViewModels;

/// <summary>
/// Egy fájlpanel teljes állapota: saját fülkészlet és saját aktív fül.
/// </summary>
/// <remarks>
/// <para>
/// Ez a típus a v1.0 kétpaneles nézet lelke. A v0.9-ig a két panel egy-egy
/// magányos <see cref="TabViewModel"/> volt, a fülrendszer pedig ezektől
/// FÜGGETLENÜL, a főablak-modellen élt globális állapotként — emiatt a két
/// panelnek nem lehetett saját füle, és a fülekhez tartozó állapot (előzmény,
/// rendezés, nézetmód) a panelek között összemosódott.
/// </para>
/// <para>
/// Az új felosztás: MINDEN fájllista-állapot panelenként él (fülek, aktív fül,
/// és fülönként útvonal/előzmény/kijelölés/rendezés/nézetmód/görgetés/szűrő —
/// lásd <see cref="TabViewModel"/>). Globálisan csak az „melyik panel az
/// aktív" és az elrendezés adatai maradnak, a <c>MainWindowViewModel</c>-en.
/// </para>
/// <para>
/// Egypaneles nézetben is ez működik: olyankor egyszerűen csak a bal panel
/// látszik. Ezért nem vész el semmi a nézetváltásoknál — a jobb panel fülei
/// és előzményei érintetlenül várnak a visszakapcsolásra.
/// </para>
/// </remarks>
public sealed partial class PaneViewModel : ObservableObject
{
    private readonly Func<TabViewModel> _tabFactory;

    public PaneViewModel(string id, Func<TabViewModel> tabFactory)
    {
        Id = id;
        _tabFactory = tabFactory;
        Tabs = [];
    }

    /// <summary>A panel azonosítója (<c>"left"</c> / <c>"right"</c>) — a mentett munkamenet ezzel párosítja vissza a füleket.</summary>
    public string Id { get; }

    /// <summary>A panel saját füljei. Sosem üres: a <see cref="CloseTab"/> az utolsót nem engedi bezárni.</summary>
    public ObservableCollection<TabViewModel> Tabs { get; }

    [ObservableProperty]
    public partial TabViewModel? ActiveTab { get; set; }

    /// <summary>Igaz, ha ez a panel az aktív — az eszköztár, az útvonalsáv, a keresés és a státuszsor erre hat.</summary>
    [ObservableProperty]
    public partial bool IsActive { get; set; }

    /// <summary>Az aktív fül indexe, vagy <c>-1</c>, ha nincs — a munkamenet mentése ezt tárolja.</summary>
    public int ActiveTabIndex => ActiveTab is null ? -1 : Tabs.IndexOf(ActiveTab);

    /// <summary>Egy új fül jött létre ebben a panelben — a főablak-modell erre köti be a fül figyelését.</summary>
    public event EventHandler<TabViewModel>? TabCreated;

    /// <summary>Egy fül végleg bezárult — a főablak-modell erre bontja le a figyelést.</summary>
    public event EventHandler<TabViewModel>? TabClosed;

    /// <summary>
    /// Új fül a panel végén, azonnal aktívvá téve.
    /// </summary>
    /// <param name="path">A megnyitandó útvonal, vagy <see cref="TabViewModel.HomeMarker"/>.</param>
    /// <param name="viewMode">A fül induló nézetmódja; <c>null</c> esetén a <see cref="TabViewModel"/> alapértéke.</param>
    /// <param name="showHidden">Rejtett elemek megjelenítése ebben a fülben.</param>
    /// <param name="activate">Hamis értékkel a fül létrejön, de a fókusz az aktuálison marad — a munkamenet visszaállítása ezt használja.</param>
    public TabViewModel AddTab(string path, ViewMode? viewMode = null, bool showHidden = false, bool activate = true)
    {
        var tab = _tabFactory();
        tab.ShowHiddenItems = showHidden;

        if (viewMode is { } mode)
        {
            tab.ViewMode = mode;
        }

        Tabs.Add(tab);
        TabCreated?.Invoke(this, tab);

        if (activate || ActiveTab is null)
        {
            ActiveTab = tab;
        }

        _ = tab.NavigateAsync(path);
        return tab;
    }

    /// <summary>Ctrl+T — új fül a kezdőlapon.</summary>
    [RelayCommand]
    private void NewTab() => AddTab(TabViewModel.HomeMarker, ActiveTab?.ViewMode, ActiveTab?.ShowHiddenItems ?? false);

    /// <summary>
    /// Ctrl+W — a megadott (vagy az aktív) fül bezárása. Az utolsó fül nem
    /// zárható be: egy üres panelnek nincs értelmes állapota, és a
    /// visszaállításához külön „új fül" gombot kellene beszúrni a panelbe.
    /// </summary>
    [RelayCommand]
    private void CloseTab(TabViewModel? tab)
    {
        tab ??= ActiveTab;

        if (tab is null || Tabs.Count <= 1)
        {
            return;
        }

        var index = Tabs.IndexOf(tab);

        if (index < 0)
        {
            return;
        }

        Tabs.RemoveAt(index);
        TabClosed?.Invoke(this, tab);
        tab.Detach();

        // A szomszédos fülre lépünk, hogy ne maradjon kijelöletlen a sáv.
        ActiveTab = Tabs[Math.Clamp(index, 0, Tabs.Count - 1)];
    }

    /// <summary>Ctrl+Tab — körbeforgó fülváltás ezen a panelen belül.</summary>
    [RelayCommand]
    private void NextTab()
    {
        if (Tabs.Count < 2 || ActiveTab is null)
        {
            return;
        }

        ActiveTab = Tabs[(Tabs.IndexOf(ActiveTab) + 1) % Tabs.Count];
    }

    /// <summary>Ctrl+Shift+Tab — visszafelé.</summary>
    [RelayCommand]
    private void PreviousTab()
    {
        if (Tabs.Count < 2 || ActiveTab is null)
        {
            return;
        }

        ActiveTab = Tabs[(Tabs.IndexOf(ActiveTab) - 1 + Tabs.Count) % Tabs.Count];
    }

    /// <summary>Egy fül kijelölése kattintásra — a nézet hívja.</summary>
    [RelayCommand]
    private void SelectTab(TabViewModel? tab)
    {
        if (tab is not null && Tabs.Contains(tab))
        {
            ActiveTab = tab;
        }
    }

    /// <summary>Az aktív fül újratöltése.</summary>
    public Task RefreshAsync() =>
        ActiveTab is { } tab ? tab.RefreshCommand.ExecuteAsync(null) : Task.CompletedTask;

    /// <summary>Navigálás az aktív fülben. Fül híján előbb létrehoz egyet.</summary>
    public Task NavigateAsync(string path)
    {
        if (ActiveTab is not { } tab)
        {
            AddTab(path);
            return Task.CompletedTask;
        }

        return tab.NavigateAsync(path);
    }

    /// <summary>Minden fül leválasztása — az ablak bezárásakor, a szolgáltatás-feliratkozások elengedéséhez.</summary>
    public void DetachAll()
    {
        foreach (var tab in Tabs)
        {
            TabClosed?.Invoke(this, tab);
            tab.Detach();
        }
    }

    partial void OnActiveTabChanged(TabViewModel? oldValue, TabViewModel? newValue)
    {
        OnPropertyChanged(nameof(ActiveTabIndex));

        foreach (var tab in Tabs)
        {
            tab.IsActiveInPane = ReferenceEquals(tab, newValue);
        }
    }
}
