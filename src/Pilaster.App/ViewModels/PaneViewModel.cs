using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pilaster.Core.FileSystem;
using Pilaster.Core.Settings;

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
    private readonly ISettingsService _settings;
    private readonly bool _isLeft;

    public PaneViewModel(string id, Func<TabViewModel> tabFactory, ISettingsService settings)
    {
        Id = id;
        _tabFactory = tabFactory;
        _settings = settings;
        _isLeft = id == "left";
        Tabs = [];

        // K1 (v1.0.1): oszlopszélesség panelenként külön mentve — lásd az
        // AppSettings.LeftPane…/RightPane… párokat.
        SizeColumnWidth = _isLeft ? settings.Current.LeftPaneSizeColumnWidth : settings.Current.RightPaneSizeColumnWidth;
        TypeColumnWidth = _isLeft ? settings.Current.LeftPaneTypeColumnWidth : settings.Current.RightPaneTypeColumnWidth;
        ModifiedColumnWidth = _isLeft ? settings.Current.LeftPaneModifiedColumnWidth : settings.Current.RightPaneModifiedColumnWidth;

        // K3 (v1.0.1): páros/páratlan sorok csíkozása — globális, nem
        // panelenkénti beállítás.
        RowStriping = settings.Current.DualPaneRowStriping;

        // A Beállítások ablak (Panelek kategória) innen tudja azonnal
        // érvényesíteni a csíkozás-kapcsolót és az „Alapértelmezettek
        // visszaállítása" gombot az oszlopszélességekre — enélkül a
        // konstruktorban egyszer beolvasott érték csak új panel
        // létrehozásakor frissülne. A LeftPane/RightPane egy-egy példánya az
        // app teljes élettartamára él, ezért a feliratkozást sosem kell
        // leválasztani.
        settings.Changed += OnSettingsChanged;
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        RowStriping = _settings.Current.DualPaneRowStriping;

        SizeColumnWidth = _isLeft ? _settings.Current.LeftPaneSizeColumnWidth : _settings.Current.RightPaneSizeColumnWidth;
        TypeColumnWidth = _isLeft ? _settings.Current.LeftPaneTypeColumnWidth : _settings.Current.RightPaneTypeColumnWidth;
        ModifiedColumnWidth = _isLeft ? _settings.Current.LeftPaneModifiedColumnWidth : _settings.Current.RightPaneModifiedColumnWidth;
    }

    [ObservableProperty]
    public partial double SizeColumnWidth { get; set; }

    partial void OnSizeColumnWidthChanged(double value)
    {
        if (_isLeft)
        {
            _settings.Current.LeftPaneSizeColumnWidth = value;
        }
        else
        {
            _settings.Current.RightPaneSizeColumnWidth = value;
        }

        _settings.NotifyChanged();
    }

    [ObservableProperty]
    public partial double TypeColumnWidth { get; set; }

    partial void OnTypeColumnWidthChanged(double value)
    {
        if (_isLeft)
        {
            _settings.Current.LeftPaneTypeColumnWidth = value;
        }
        else
        {
            _settings.Current.RightPaneTypeColumnWidth = value;
        }

        _settings.NotifyChanged();
    }

    [ObservableProperty]
    public partial double ModifiedColumnWidth { get; set; }

    partial void OnModifiedColumnWidthChanged(double value)
    {
        if (_isLeft)
        {
            _settings.Current.LeftPaneModifiedColumnWidth = value;
        }
        else
        {
            _settings.Current.RightPaneModifiedColumnWidth = value;
        }

        _settings.NotifyChanged();
    }

    /// <summary>Páros/páratlan sorok csíkozása (spec K3, v1.0.1) — lásd a konstruktor megjegyzését.</summary>
    [ObservableProperty]
    public partial bool RowStriping { get; set; }

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

    /// <summary>
    /// Igaz, amíg ez a panel kétpaneles nézetben (<see cref="FilePaneView"/>-n
    /// keresztül) jelenik meg (spec E1, v1.0.1) — ilyenkor a <see cref="NewTab"/>
    /// NEM a virtuális Kezdőlapot nyitja meg, hanem egy valódi mappát, mert a
    /// Kezdőlap-irányítópultnak (kártyák, meghajtók) nincs kétpaneles
    /// megfelelője, és üresen maradna. Egypaneles nézetben (ahol VAN
    /// irányítópult) ez hamis, és a Kezdőlap továbbra is elérhető marad.
    /// A <see cref="MainWindowViewModel"/> állítja be a <c>DualPaneEnabled</c>
    /// váltásakor.
    /// </summary>
    public bool SuppressHomeTab { get; set; }

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

    /// <summary>
    /// Ctrl+T — új fül. Egypaneles nézetben a Kezdőlapon, kétpaneles
    /// nézetben egy valódi mappán — lásd <see cref="SuppressHomeTab"/>.
    /// </summary>
    [RelayCommand]
    private void NewTab() => AddTab(
        SuppressHomeTab ? DefaultRealFolder() : TabViewModel.HomeMarker,
        ActiveTab?.ViewMode,
        ActiveTab?.ShowHiddenItems ?? false);

    /// <summary>
    /// Valódi mappa a virtuális Kezdőlap helyett (spec E1, v1.0.1): a
    /// beállított kezdőmappa, vagy ennek híján a felhasználói profil —
    /// pontosan az, amit a <c>StartupFolder</c> beállítás egyébként induláskor
    /// is használna (lásd <c>AppSettings.StartupFolder</c>).
    /// </summary>
    private string DefaultRealFolder() =>
        _settings.Current.StartupFolder is { Length: > 0 } folder
            ? folder
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

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
