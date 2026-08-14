using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pilaster.App.Localization;
using Pilaster.App.Services;
using Pilaster.App.Services.FileOperations;
using Pilaster.Core.FileSystem;
using Pilaster.Core.Formatting;
using Pilaster.Core.Settings;
using Pilaster.Providers.Local;
using Pilaster.Shell.Devices;
using Pilaster.Shell.Recycle;
using Wpf.Ui.Controls;

namespace Pilaster.App.ViewModels;

/// <summary>A főablak állapota: fülek, oldalsáv, téma, gyorsgombok.</summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IFileSystemProvider _provider;
    private readonly ISettingsService _settings;
    private readonly ThemeService _theme;
    private readonly QuickActionService _quickActions;
    private readonly FolderSizeService _folderSizes;
    private readonly FileMetadataService _metadata;
    private readonly FileOperationEngine _fileOperations;
    private readonly QuickAccessService _quickAccess;

    /// <summary>Az Aktivitás-központ paneljéhez közvetlenül köthető, futó/befejezett műveletek.</summary>
    public ObservableCollection<FileOperationJob> FileOperationJobs => _fileOperations.Jobs;

    public MainWindowViewModel(
        IFileSystemProvider provider,
        ISettingsService settings,
        ThemeService theme,
        QuickActionService quickActions,
        UpdateViewModel updates,
        FolderSizeService folderSizes,
        FileMetadataService metadata,
        FileOperationEngine fileOperations,
        QuickAccessService quickAccess)
    {
        _provider = provider;
        _settings = settings;
        _theme = theme;
        _quickActions = quickActions;
        Updates = updates;
        _folderSizes = folderSizes;
        _metadata = metadata;
        _fileOperations = fileOperations;
        _quickAccess = quickAccess;

        // A v0.9-es, settings.json-ben tárolt gyorselérés átvétele az új,
        // önálló fájlba — egyszer fut le, az első v1.0-s indításkor.
        _quickAccess.MigrateFromLegacyPins(_settings.Current.QuickAccessPins);
        _quickAccess.Changed += (_, _) =>
        {
            RefreshQuickAccess();
            RefreshRecent();
        };

        Sections = [];

        // A két panel MINDEN fájllista-állapotot birtokol (fülek, aktív fül, és
        // fülönként útvonal/előzmény/kijelölés/rendezés/nézetmód/görgetés/szűrő).
        // Globálisan csak az „melyik az aktív" és az elrendezés marad itt.
        LeftPane = new PaneViewModel("left", CreateTab);
        RightPane = new PaneViewModel("right", CreateTab);

        foreach (var pane in new[] { LeftPane, RightPane })
        {
            pane.TabCreated += OnPaneTabCreated;
            pane.TabClosed += OnPaneTabClosed;
            pane.PropertyChanged += OnPanePropertyChanged;
        }

        BuildSidebar();
        RefreshQuickActions();

        // Egy másolás/áthelyezés/törlés befejeztével frissítjük azokat a
        // nyitott füleket, amiket érinthetett — enélkül a felhasználónak
        // kézzel kellene frissítenie, hogy lássa az új/eltűnt elemeket.
        _fileOperations.Jobs.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is null)
            {
                return;
            }

            foreach (FileOperationJob job in e.NewItems)
            {
                job.PropertyChanged += OnFileOperationJobPropertyChanged;
            }
        };

        // A beállítások bárhonnan módosulhatnak (pl. a Beállítások ablakból),
        // ezért a felső sáv gombjai eseményre frissülnek, nem közvetlen hívásra.
        _settings.Changed += (_, _) =>
        {
            RefreshQuickActions();
            RefreshSidebarDetails();
            OnPropertyChanged(nameof(IsDarkTheme));
            OnPropertyChanged(nameof(ThemeIcon));
            OnPropertyChanged(nameof(ShowFunctionKeyBar));
        };

        // Kedvenc/címke hozzáadása/eltávolítása bárhonnan jöhet (fájlsor szív
        // ikonja, tag-választó, más fül) — az oldalsáv Kedvencek és Címkék
        // szekciója erre frissül.
        _metadata.Changed += (_, _) =>
        {
            RefreshFavorites();
            RefreshTagFilters();
        };

        RefreshTagFilters();

        RestoreSession();

        // A tulajdonságon keresztül állítjuk (nem közvetlen mezőn — a
        // forrásgenerált mező neve nem elérhető innen), ami az OnXChanged
        // miatt visszaírja a beállításba ugyanazt az értéket, amit épp
        // onnan olvasott — ártalmatlan, csak egy felesleges mentés induláskor.
        DualPaneEnabled = _settings.Current.DualPaneEnabled;
        DualPaneVertical = _settings.Current.DualPaneVertical;

        LeftPane.IsActive = true;
    }

    /// <summary>A bal panel — egypaneles nézetben ez az egyetlen látható.</summary>
    public PaneViewModel LeftPane { get; }

    /// <summary>A jobb panel — csak kétpaneles nézetben látszik, de az állapotát olyankor is megőrzi.</summary>
    public PaneViewModel RightPane { get; }

    /// <summary>Igaz, ha a bal panel az aktív. Egypaneles nézetben mindig igaznak számít.</summary>
    [ObservableProperty]
    public partial bool IsLeftPaneActive { get; set; } = true;

    /// <summary>
    /// A jelenleg aktív panel. Egypaneles nézetben MINDIG a bal — így az
    /// eszköztár, az útvonalsáv, a keresés és a státuszsor egyetlen szabály
    /// szerint dolgozik mindkét elrendezésben.
    /// </summary>
    public PaneViewModel ActivePane => DualPaneEnabled && !IsLeftPaneActive ? RightPane : LeftPane;

    public PaneViewModel InactivePane => ReferenceEquals(ActivePane, LeftPane) ? RightPane : LeftPane;

    /// <summary>Az aktív panel aktív füle — a v0.9-es <c>ActivePaneTab</c> utódja.</summary>
    public TabViewModel? ActivePaneTab => ActivePane.ActiveTab;

    public TabViewModel? InactivePaneTab => InactivePane.ActiveTab;

    /// <summary>
    /// Az aktív panel fülei — a felső fülsáv ehhez kötődik.
    /// </summary>
    /// <remarks>
    /// Nem saját gyűjtemény: a fülek a paneleké. Ez a tulajdonság csak
    /// átirányít, hogy a felső fülsáv mindig az aktív panelre hasson (spec F7).
    /// </remarks>
    public ObservableCollection<TabViewModel> Tabs => ActivePane.Tabs;

    partial void OnIsLeftPaneActiveChanged(bool value)
    {
        LeftPane.IsActive = !DualPaneEnabled || value;
        RightPane.IsActive = DualPaneEnabled && !value;

        RaiseActivePaneChanged();
    }

    /// <summary>
    /// Az aktív panelből származó, átirányított tulajdonságok újraértékeltetése.
    /// Panelváltáskor és kétpaneles mód kapcsolásakor egyaránt kell.
    /// </summary>
    private void RaiseActivePaneChanged()
    {
        OnPropertyChanged(nameof(ActivePane));
        OnPropertyChanged(nameof(InactivePane));
        OnPropertyChanged(nameof(ActivePaneTab));
        OnPropertyChanged(nameof(InactivePaneTab));
        OnPropertyChanged(nameof(Tabs));
        OnPropertyChanged(nameof(SelectedTab));
        OnPropertyChanged(nameof(CanEjectCurrentDrive));

        UpdateActiveSidebarItem();
        SyncTagFilterHighlight();
    }

    private void OnPanePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PaneViewModel.ActiveTab) && ReferenceEquals(sender, ActivePane))
        {
            OnPropertyChanged(nameof(SelectedTab));
            OnPropertyChanged(nameof(ActivePaneTab));
            UpdateActiveSidebarItem();
            SyncTagFilterHighlight();
            OnPropertyChanged(nameof(CanEjectCurrentDrive));
        }

        if (e.PropertyName == nameof(PaneViewModel.ActiveTab))
        {
            SaveSession();
        }
    }

    [ObservableProperty]
    public partial bool DualPaneEnabled { get; set; }

    partial void OnDualPaneEnabledChanged(bool value)
    {
        _settings.Current.DualPaneEnabled = value;
        _settings.NotifyChanged();

        // Egypaneles módra váltva a bal panel lesz az aktív, DE a jobb panel
        // fülei/előzményei érintetlenül megmaradnak — visszakapcsoláskor
        // pontosan ott folytatódnak (spec F7).
        LeftPane.IsActive = !value || IsLeftPaneActive;
        RightPane.IsActive = value && !IsLeftPaneActive;

        OnPropertyChanged(nameof(ShowFunctionKeyBar));
        RaiseActivePaneChanged();
    }

    /// <summary>
    /// A kétablakos nézet alján megjelenő, kattintható funkcióbillentyű-sáv
    /// csak akkor látszik, ha MINDKÉT beállítás be van kapcsolva — kétablakos
    /// nézet nélkül nincs értelme (F5/F6 a másik panelre céloz), a Total
    /// billentyűkiosztás nélkül pedig zavaró lenne, ha a gombok
    /// funkciója nem egyezik a megszokott billentyűkkel.
    /// </summary>
    public bool ShowFunctionKeyBar => DualPaneEnabled && _settings.Current.Keymap == KeymapPreset.PilasterClassic;

    [ObservableProperty]
    public partial bool DualPaneVertical { get; set; }

    partial void OnDualPaneVerticalChanged(bool value)
    {
        _settings.Current.DualPaneVertical = value;
        _settings.NotifyChanged();
    }

    /// <summary>A másik panel az aktív panel útvonalára navigál — a Kétablakos nézet „Szinkronizálás" gombja.</summary>
    [RelayCommand]
    private async Task SyncPanesAsync()
    {
        if (ActivePaneTab?.CurrentPath is { } path)
        {
            await InactivePane.NavigateAsync(path);
        }
    }

    /// <summary>Ctrl+U — a két panel tartalmának cseréje (a teljes fülkészletükkel együtt).</summary>
    /// <remarks>
    /// Nem csak az útvonalakat cseréli, hanem a fülek TELJES állapotát: így az
    /// előzmény, a rendezés, a nézetmód és a görgetés is átkerül — ez a
    /// felhasználó által elvárt „a két oldal helyet cserél" viselkedés.
    /// </remarks>
    [RelayCommand]
    private void SwapPanes()
    {
        var leftTabs = LeftPane.Tabs.ToList();
        var leftActive = LeftPane.ActiveTab;
        var rightTabs = RightPane.Tabs.ToList();
        var rightActive = RightPane.ActiveTab;

        LeftPane.Tabs.Clear();
        RightPane.Tabs.Clear();

        foreach (var tab in rightTabs)
        {
            LeftPane.Tabs.Add(tab);
        }

        foreach (var tab in leftTabs)
        {
            RightPane.Tabs.Add(tab);
        }

        LeftPane.ActiveTab = rightActive;
        RightPane.ActiveTab = leftActive;

        RaiseActivePaneChanged();
        SaveSession();
    }

    /// <summary>Ctrl+L — a bal panel útvonala a jobb panelre.</summary>
    [RelayCommand]
    private async Task CopyLeftPathToRightAsync()
    {
        if (LeftPane.ActiveTab?.CurrentPath is { } path)
        {
            await RightPane.NavigateAsync(path);
        }
    }

    /// <summary>Ctrl+R — a jobb panel útvonala a bal panelre.</summary>
    [RelayCommand]
    private async Task CopyRightPathToLeftAsync()
    {
        if (RightPane.ActiveTab?.CurrentPath is { } path)
        {
            await LeftPane.NavigateAsync(path);
        }
    }

    /// <summary>Alt+F5 — mindkét panel aktív fülének frissítése.</summary>
    [RelayCommand]
    private async Task RefreshBothPanesAsync()
    {
        await LeftPane.RefreshAsync();

        if (DualPaneEnabled)
        {
            await RightPane.RefreshAsync();
        }
    }

    /// <summary>Panel-ről panelre húzás/beillesztés — lásd FilePaneView.FilesDropped.</summary>
    public void StartPaneCopy(IReadOnlyList<string> paths, string destinationDir) => _fileOperations.StartCopy(paths, destinationDir);

    public void StartPaneMove(IReadOnlyList<string> paths, string destinationDir) => _fileOperations.StartMove(paths, destinationDir);

    public void StartPaneDelete(IReadOnlyList<string> paths, bool permanent) => _fileOperations.StartDelete(paths, permanent);

    /// <summary>
    /// <c>Alt</c>+húzás: parancsikon(ok) készítése a célmappában. Nem a
    /// <c>FileOperationEngine</c>-en megy át — nincs mit másolni, nincs
    /// értelmes folyamatjelző, és a művelet pillanatszerű.
    /// </summary>
    public void CreateShortcuts(IReadOnlyList<string> paths, string destinationDir)
    {
        var suffix = TranslationSource.Instance["Shortcut_Suffix"];
        var created = false;

        foreach (var path in paths)
        {
            created |= Pilaster.Shell.Integration.ShortcutService.TryCreate(path, destinationDir, suffix) is not null;
        }

        if (!created)
        {
            return;
        }

        foreach (var tab in AllTabs().Where(t => string.Equals(t.CurrentPath, destinationDir, StringComparison.OrdinalIgnoreCase)))
        {
            _ = tab.RefreshCommand.ExecuteAsync(null);
        }
    }

    /// <summary>
    /// Pilaster Classic F5 (másolás)/F6 (áthelyezés) — a felhasználó által a
    /// <c>TransferConfirmWindow</c>-ban megerősített célmappa alapján indítja
    /// a műveletet. Kivétel: pontosan egy kijelölt elem és VÁLTOZATLAN
    /// célmappa esetén ez gyakorlatilag átnevezés — ilyenkor nem indul
    /// másolási/áthelyezési feladat, hanem a szokásos helyben-átnevezés
    /// (<see cref="TabViewModel.BeginRename"/>) nyílik meg, ahogy az F2 is.
    /// </summary>
    public void BeginTransfer(TabViewModel sourceTab, IReadOnlyList<string> sourcePaths, string confirmedTargetDirectory, bool isMove)
    {
        var normalizedTarget = confirmedTargetDirectory.TrimEnd('\\', '/');

        if (sourcePaths.Count == 1
            && string.Equals(Path.GetDirectoryName(sourcePaths[0]), normalizedTarget, StringComparison.OrdinalIgnoreCase))
        {
            if (sourceTab.Items.FirstOrDefault(i => string.Equals(i.FullPath, sourcePaths[0], StringComparison.OrdinalIgnoreCase)) is { } item)
            {
                sourceTab.BeginRename(item);
            }

            return;
        }

        if (isMove)
        {
            _fileOperations.StartMove(sourcePaths, confirmedTargetDirectory);
        }
        else
        {
            _fileOperations.StartCopy(sourcePaths, confirmedTargetDirectory);
        }
    }

    public ObservableCollection<SidebarSection> Sections { get; }

    /// <summary>Az oldalsáv Címkék szekciója — külön listaként, mert nem navigál, hanem szűr.</summary>
    public ObservableCollection<TagFilterItemViewModel> TagFilters { get; } = [];

    /// <summary>Frissítés-ellenőrzés és -telepítés állapota — a sáv és a Beállítások közösen használja.</summary>
    public UpdateViewModel Updates { get; }

    /// <summary>
    /// Az aktív panel aktív füle.
    /// </summary>
    /// <remarks>
    /// Már NEM önálló, globális állapot (v0.9-ig az volt): csak átirányít az
    /// aktív panelre. Így a felső eszköztár, az útvonalsáv, a keresés, a
    /// státuszsor és a menük automatikusan mindig az aktív panelre hatnak, és
    /// egy panelváltás nem igényel semmilyen külön szinkronizálást.
    /// </remarks>
    public TabViewModel? SelectedTab
    {
        get => ActivePane.ActiveTab;
        set
        {
            // A null értéket szándékosan eldobjuk. A felső fülsáv ListBox-a
            // kétirányú kötéssel ül ezen a tulajdonságon, és valahányszor az
            // ItemsSource kicserélődik (panelváltás, panelcsere), előbb null-ra
            // állítja a saját SelectedItem-jét, és ezt VISSZA is írja ide —
            // amitől az aktív panelnek egy pillanatra nem lenne aktív füle.
            // Kijelöletlen fül sosem felhasználói szándék, ezért itt nem is
            // értelmezzük annak.
            if (value is not null)
            {
                ActivePane.ActiveTab = value;
            }
        }
    }

    [ObservableProperty]
    public partial bool IsSidebarVisible { get; set; } = true;

    /// <summary>A felső sáv első gyorsgombjának felirata.</summary>
    [ObservableProperty]
    public partial string QuickAction1Label { get; set; } = string.Empty;

    [ObservableProperty]
    public partial SymbolRegular QuickAction1Icon { get; set; } = SymbolRegular.FolderAdd24;

    [ObservableProperty]
    public partial string QuickAction2Label { get; set; } = string.Empty;

    [ObservableProperty]
    public partial SymbolRegular QuickAction2Icon { get; set; } = SymbolRegular.DocumentAdd24;

    /// <summary>Igaz, ha a felület jelenleg sötét.</summary>
    public bool IsDarkTheme => _theme.IsDark;

    /// <summary>
    /// A téma-kapcsoló ikonja: azt mutatja, amire váltani fog, nem azt, ami
    /// most van — sötét témában napot, hogy a világosra váltás legyen kézenfekvő.
    /// </summary>
    public SymbolRegular ThemeIcon => _theme.IsDark
        ? SymbolRegular.WeatherSunny24
        : SymbolRegular.WeatherMoon24;

    /// <summary>
    /// Igaz, ha az átúsztatások engedélyezettek. A nézet ez alapján dönti el,
    /// lejátssza-e a mappaváltáskori csúszó átmenetet.
    /// </summary>
    public bool AnimationsEnabled => _settings.Current.Animations != AnimationLevel.Off;

    /// <summary>
    /// Igaz, ha az aktív fül jelenlegi mappája egy cserélhető vagy optikai
    /// meghajtón van — ekkor jelenik meg a Kiadás gomb az eszköztárban.
    /// </summary>
    public bool CanEjectCurrentDrive =>
        GetCurrentDriveType() is { } driveType && RemovableDriveService.IsEjectable(driveType);

    /// <summary>Akkor jelez, ha a nézetnek meg kell nyitnia a Beállításokat.</summary>
    public event EventHandler? SettingsRequested;

    /// <summary>Egy Kiadás-kísérlet lezárult — a nézet ez alapján mutat visszajelzést.</summary>
    public event EventHandler<EjectOutcome>? EjectCompleted;

    /// <summary>Ctrl+T — új fül az AKTÍV panelben.</summary>
    [RelayCommand]
    private void NewTab() => ActivePane.NewTabCommand.Execute(null);

    /// <summary>Ctrl+W — fül bezárása abban a panelben, amelyikhez tartozik.</summary>
    [RelayCommand]
    private void CloseTab(TabViewModel? tab)
    {
        var owner = tab is null
            ? ActivePane
            : LeftPane.Tabs.Contains(tab) ? LeftPane : RightPane.Tabs.Contains(tab) ? RightPane : ActivePane;

        owner.CloseTabCommand.Execute(tab);
    }

    /// <summary>Ctrl+Tab — fülváltás az aktív panelen belül.</summary>
    [RelayCommand]
    private void NextTab() => ActivePane.NextTabCommand.Execute(null);

    /// <summary>Ctrl+Shift+Tab — fülváltás visszafelé az aktív panelen belül.</summary>
    [RelayCommand]
    private void PreviousTab() => ActivePane.PreviousTabCommand.Execute(null);

    [RelayCommand]
    private async Task OpenSidebarItemAsync(SidebarItemViewModel? item)
    {
        if (item is not null && SelectedTab is not null)
        {
            await SelectedTab.NavigateAsync(item.Path);
        }
    }

    [RelayCommand]
    private async Task OpenBreadcrumbAsync(BreadcrumbSegment? segment)
    {
        if (segment is not null && SelectedTab is not null)
        {
            await SelectedTab.NavigateAsync(segment.Path);
        }
    }

    [RelayCommand]
    private void OpenSettings() => SettingsRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Az aktuális meghajtó biztonságos leválasztása/kiadása.
    /// </summary>
    /// <remarks>
    /// Siker esetén az aktív fület a kezdőlapra navigálja — a meghajtó
    /// eltűnése után az addigi útvonal érvénytelen lenne —, és újraépíti a
    /// Meghajtók szekciót, hogy a lista azonnal kövesse a változást.
    /// </remarks>
    [RelayCommand]
    private async Task EjectCurrentDriveAsync()
    {
        if (SelectedTab?.CurrentPath is not { } path || GetCurrentDriveRoot(path) is not { } root)
        {
            return;
        }

        var driveType = GetCurrentDriveType();

        if (driveType is null || !RemovableDriveService.IsEjectable(driveType.Value))
        {
            return;
        }

        var result = RemovableDriveService.Eject(root, driveType.Value);

        if (result.Outcome == EjectOutcome.Succeeded)
        {
            await SelectedTab.NavigateAsync(TabViewModel.HomeMarker);
            RefreshDrives();
        }

        EjectCompleted?.Invoke(this, result.Outcome);
    }

    /// <summary>
    /// Kiadás közvetlenül az oldalsáv meghajtó-sorának végén lévő ikonnal —
    /// nem kell előbb megnyitni a meghajtót, mint az eszköztár Kiadás
    /// gombjánál (<see cref="EjectCurrentDriveAsync"/>).
    /// </summary>
    [RelayCommand]
    private async Task EjectDriveAsync(SidebarItemViewModel? item)
    {
        if (item?.Drive is not { } drive || !RemovableDriveService.IsEjectable(drive.DriveType))
        {
            return;
        }

        var result = RemovableDriveService.Eject(drive.Item.FullPath, drive.DriveType);

        if (result.Outcome == EjectOutcome.Succeeded)
        {
            // Ha épp ezen a meghajtón állt egy fül, arról is el kell navigálni,
            // különben egy érvénytelenné vált útvonalon maradna.
            if (SelectedTab?.CurrentPath is { } path
                && path.StartsWith(drive.Item.FullPath, StringComparison.OrdinalIgnoreCase))
            {
                await SelectedTab.NavigateAsync(TabViewModel.HomeMarker);
            }

            RefreshDrives();
        }

        EjectCompleted?.Invoke(this, result.Outcome);
    }

    /// <summary>Az oldalsáv Meghajtók szekciójának újraépítése — pl. kiadás vagy médiaváltás után.</summary>
    public void RefreshDrives()
    {
        var driveSection = Sections.FirstOrDefault(s => s.HeaderKey == "Nav_Drives");

        if (driveSection is null)
        {
            return;
        }

        var index = Sections.IndexOf(driveSection);

        Sections[index] = new SidebarSection
        {
            HeaderKey = "Nav_Drives",
            Header = TranslationSource.Instance["Nav_Drives"],
            Items = BuildDrives(),
        };

        UpdateActiveSidebarItem();
    }

    private DriveType? GetCurrentDriveType() =>
        SelectedTab?.CurrentPath is { } path && GetCurrentDriveRoot(path) is { } root
            ? TryGetDriveType(root)
            : null;

    private static string? GetCurrentDriveRoot(string path)
    {
        try
        {
            return Path.GetPathRoot(path) is { Length: > 0 } root ? root : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static DriveType? TryGetDriveType(string root)
    {
        try
        {
            return new DriveInfo(root).DriveType;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Váltás világos és sötét téma között.</summary>
    [RelayCommand]
    private async Task ToggleThemeAsync(System.Windows.Window? window)
    {
        await _theme.ToggleAsync(window);

        OnPropertyChanged(nameof(IsDarkTheme));
        OnPropertyChanged(nameof(ThemeIcon));
    }

    /// <summary>Az első vagy második gyorsgomb végrehajtása.</summary>
    [RelayCommand]
    private async Task RunQuickActionAsync(string? which)
    {
        var action = which == "2" ? _settings.Current.QuickAction2 : _settings.Current.QuickAction1;
        var result = _quickActions.Execute(action, SelectedTab?.CurrentPath);

        if (!result.Succeeded)
        {
            if (SelectedTab is not null && result.ErrorMessage is { } key)
            {
                SelectedTab.EmptyMessage = TranslationSource.Instance[key];
            }

            return;
        }

        // A frissítés után az új elem a rendezés szerinti helyén jelenik meg.
        if (SelectedTab is not null)
        {
            await SelectedTab.RefreshCommand.ExecuteAsync(null);
        }
    }

    /// <summary>Az üres terület helyi menüjének „Új mappa" pontja.</summary>
    /// <remarks>
    /// Szándékosan NEM a testreszabható gyorsgombokat futtatja: azokat a
    /// felhasználó bármi másra átállíthatja, míg ez a menüpont — ahogy az
    /// Explorerben is — mindig egyszerű, üres mappát/fájlt hoz létre.
    /// </remarks>
    [RelayCommand]
    private async Task CreateNewFolderAsync() => await CreateNewItemAsync(QuickActionKind.Folder, SelectedTab);

    [RelayCommand]
    private async Task CreateNewFileAsync() => await CreateNewItemAsync(QuickActionKind.File, SelectedTab);

    /// <summary>Pilaster Classic F7 — új mappa a MEGADOTT (aktív egy- vagy kétablakos) panelben, nem feltétlenül a fülrendszer aktuális fülében.</summary>
    public async Task CreateNewFolderInTabAsync(TabViewModel tab) => await CreateNewItemAsync(QuickActionKind.Folder, tab);

    private async Task CreateNewItemAsync(QuickActionKind kind, TabViewModel? tab)
    {
        if (tab is null)
        {
            return;
        }

        var action = new QuickActionSettings
        {
            Kind = kind,
            Extension = "txt",
            NameTemplate = TranslationSource.Instance[kind == QuickActionKind.Folder ? "Cmd_NewFolder" : "Cmd_NewFile"],
            Target = QuickActionTarget.CurrentFolder,
        };

        var result = _quickActions.Execute(action, tab.CurrentPath);

        if (!result.Succeeded)
        {
            if (result.ErrorMessage is { } key)
            {
                tab.EmptyMessage = TranslationSource.Instance[key];
            }

            return;
        }

        await tab.RefreshCommand.ExecuteAsync(null);

        // Azonnali átnevezés-mód, mint a Windows 11 Fájlkezelőben: az új elem
        // létrejön alapnévvel, de rögtön szerkeszthető állapotban jelenik meg.
        if (tab.Items.FirstOrDefault(i => string.Equals(i.FullPath, result.CreatedPath, StringComparison.OrdinalIgnoreCase)) is { } created)
        {
            tab.BeginRename(created);
        }
    }

    /// <summary>Kijelölt elemek másolása a vágólapra (Ctrl+C, vagy a jobbklikk-menü).</summary>
    [RelayCommand]
    private void CopySelection(IReadOnlyList<string>? paths)
    {
        if (paths is { Count: > 0 })
        {
            ClipboardFileService.SetClipboard(paths, isCut: false);
        }
    }

    /// <summary>Kijelölt elemek kivágása a vágólapra (Ctrl+X, vagy a jobbklikk-menü).</summary>
    [RelayCommand]
    private void CutSelection(IReadOnlyList<string>? paths)
    {
        if (paths is { Count: > 0 })
        {
            ClipboardFileService.SetClipboard(paths, isCut: true);
        }
    }

    /// <summary>
    /// Fájlok beillesztése a vágólapról — a tényleges másolást/áthelyezést a
    /// <see cref="FileOperationEngine"/> végzi, saját folyamatjelzővel; ez a
    /// parancs csak elindítja és a Kezdőlapon jelzi, ha nincs mit beilleszteni.
    /// </summary>
    [RelayCommand]
    private void Paste()
    {
        if (SelectedTab is not { } tab || tab.IsHome || tab.CurrentPath is not { } targetDirectory)
        {
            return;
        }

        if (!ClipboardFileService.TryGetClipboardFiles(out var paths, out var isCut))
        {
            tab.EmptyMessage = TranslationSource.Instance["Paste_NoFiles"];
            return;
        }

        // Nincs értelme egy elemet önmagába másolni/áthelyezni.
        var filtered = paths.Where(p => !string.Equals(Path.GetDirectoryName(p), targetDirectory, StringComparison.OrdinalIgnoreCase)).ToList();

        if (filtered.Count == 0)
        {
            return;
        }

        if (isCut)
        {
            _fileOperations.StartMove(filtered, targetDirectory);
        }
        else
        {
            _fileOperations.StartCopy(filtered, targetDirectory);
        }
    }

    /// <summary>Kijelölt elemek törlése — alapból Lomtárba (Delete), <paramref name="permanent"/>-tel véglegesen (Shift+Delete).</summary>
    [RelayCommand]
    private void DeleteSelection((IReadOnlyList<string> Paths, bool Permanent) args)
    {
        if (args.Paths.Count > 0)
        {
            _fileOperations.StartDelete(args.Paths, args.Permanent);
        }
    }

    /// <summary>
    /// Egy másolás/áthelyezés/törlés befejeztével frissíti azokat a nyitott
    /// füleket, amiket a művelet érintett — a célmappát (másolás/áthelyezés),
    /// vagy a törölt elemek szülőmappáit (törlés).
    /// </summary>
    private async void OnFileOperationJobPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FileOperationJob.State) || sender is not FileOperationJob job)
        {
            return;
        }

        if (job.State is not (FileOperationState.Completed or FileOperationState.CompletedWithErrors))
        {
            return;
        }

        // Törlésnél nem tudjuk olcsón eldönteni, melyik nyitott fület
        // érintette (a törölt elemek szülőmappái eltérhetnek egymástól is) —
        // ezért egyszerűen minden nyitott fület frissítünk. Másolásnál/
        // áthelyezésnél csak a tényleges célmappát mutató fület. MINDKÉT
        // panel minden füle sorra kerül, mert egy panelek közti átvitel
        // egyszerre két helyen is változást okoz.
        foreach (var tab in AllTabs())
        {
            var affected = job.Kind == FileOperationKind.Delete
                || string.Equals(tab.CurrentPath, job.DestinationDirectory, StringComparison.OrdinalIgnoreCase);

            if (affected)
            {
                await tab.RefreshCommand.ExecuteAsync(null);
            }
        }
    }

    /// <summary>Mindkét panel minden füle — a fájlművelet utáni frissítéshez és a nyelvváltáshoz.</summary>
    private IEnumerable<TabViewModel> AllTabs() => LeftPane.Tabs.Concat(RightPane.Tabs);

    private void RefreshQuickActions()
    {
        var first = _settings.Current.QuickAction1;
        var second = _settings.Current.QuickAction2;

        QuickAction1Label = ResolveLabel(first, "Cmd_NewFolder");
        QuickAction1Icon = ParseIcon(first.Icon, SymbolRegular.FolderAdd24);

        QuickAction2Label = ResolveLabel(second, "Cmd_NewFile");
        QuickAction2Icon = ParseIcon(second.Icon, SymbolRegular.DocumentAdd24);
    }

    /// <summary>
    /// A gomb felirata: ha a felhasználó adott sajátot, az; egyébként a
    /// lefordított alapértelmezés, hogy nyelvváltáskor is helyes maradjon.
    /// </summary>
    private static string ResolveLabel(QuickActionSettings action, string fallbackKey) =>
        string.IsNullOrWhiteSpace(action.Label)
            ? TranslationSource.Instance[fallbackKey]
            : action.Label;

    /// <summary>
    /// Ikonnév feloldása. Elgépelt vagy régi névnél az alapértelmezés lép be —
    /// egy rossz beállítás ne tegye használhatatlanná a gombot.
    /// </summary>
    private static SymbolRegular ParseIcon(string name, SymbolRegular fallback) =>
        Enum.TryParse<SymbolRegular>(name, ignoreCase: true, out var parsed) ? parsed : fallback;

    /// <summary>Új <see cref="TabViewModel"/> a panelek számára — lásd <see cref="PaneViewModel"/> gyártófüggvény-paraméterét.</summary>
    private TabViewModel CreateTab() => new(_provider, _folderSizes, _metadata)
    {
        ShowHiddenItems = _settings.Current.ShowHiddenItems,
        ViewMode = _settings.Current.LastViewMode,
    };

    /// <summary>
    /// Egy újonnan létrejött fül bekötése: a fülenként állítható, de menteni
    /// való beállítások (rejtett elemek, nézetmód) követése, valamint az
    /// oldalsáv-kiemelés és a munkamenet frissen tartása.
    /// </summary>
    private void OnPaneTabCreated(object? sender, TabViewModel tab) => tab.PropertyChanged += OnTabPropertyChanged;

    private void OnPaneTabClosed(object? sender, TabViewModel tab)
    {
        tab.PropertyChanged -= OnTabPropertyChanged;
        SaveSession();
    }

    private void OnTabPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not TabViewModel tab)
        {
            return;
        }

        // A rejtett elemek kapcsolója és a nézetmód fülenként állítható, de
        // a legutóbbi választás menteni való — a következő indításnál/új
        // fülnél azt várja a felhasználó.
        if (e.PropertyName == nameof(TabViewModel.ShowHiddenItems))
        {
            _settings.Current.ShowHiddenItems = tab.ShowHiddenItems;
            _settings.Save();
        }

        if (e.PropertyName == nameof(TabViewModel.ViewMode) && ReferenceEquals(tab, SelectedTab))
        {
            _settings.Current.LastViewMode = tab.ViewMode;
            _settings.Save();
        }

        // Csak az AKTÍV panel aktív füljének útvonalváltása befolyásolja az
        // oldalsáv kiemelését és a Kiadás gomb láthatóságát — egy háttérben
        // navigáló fül (vagy a másik panel) ne rángassa el.
        if (e.PropertyName == nameof(TabViewModel.CurrentPath))
        {
            if (ReferenceEquals(tab, SelectedTab))
            {
                UpdateActiveSidebarItem();
                OnPropertyChanged(nameof(CanEjectCurrentDrive));
                RecordRecent(tab.CurrentPath);
            }

            SaveSession();
        }
    }

    /// <summary>
    /// A mentett munkamenet visszaállítása: mindkét panel összes füle,
    /// útvonallal, nézetmóddal és rendezéssel. Kikapcsolva (vagy első
    /// indításkor) mindkét panel egy Kezdőlap-füllel indul.
    /// </summary>
    private void RestoreSession()
    {
        var session = _settings.Current.RestoreSession ? _settings.Current.Session : null;

        RestorePane(LeftPane, session?.Left);
        RestorePane(RightPane, session?.Right);

        _sessionRestored = true;
    }

    private static void RestorePane(PaneViewModel pane, PaneSession? saved)
    {
        if (saved is not { Tabs.Count: > 0 })
        {
            pane.AddTab(TabViewModel.HomeMarker);
            return;
        }

        foreach (var tab in saved.Tabs)
        {
            pane.AddTab(
                string.IsNullOrWhiteSpace(tab.Path) ? TabViewModel.HomeMarker : tab.Path,
                tab.ViewMode,
                tab.ShowHiddenItems,
                activate: false);
        }

        // A rendezés csak a fül létrejötte UTÁN állítható be, mert az
        // ApplySort azonnal újrarendez — a betöltés viszont még fut.
        for (var i = 0; i < saved.Tabs.Count && i < pane.Tabs.Count; i++)
        {
            pane.Tabs[i].ApplySort(saved.Tabs[i].SortKey, saved.Tabs[i].SortDescending);
        }

        pane.ActiveTab = pane.Tabs[Math.Clamp(saved.ActiveTabIndex, 0, pane.Tabs.Count - 1)];
    }

    /// <summary>
    /// Igaz, amint a visszaállítás lefutott. Enélkül a visszaállítás közben
    /// tüzelő útvonal-változások azonnal FELÜLÍRNÁK a mentett munkamenetet a
    /// félkész állapottal.
    /// </summary>
    private bool _sessionRestored;

    /// <summary>A két panel teljes fülkészletének mentése — minden útvonal-, fül- és panelváltás után.</summary>
    public void SaveSession()
    {
        if (!_sessionRestored)
        {
            return;
        }

        _settings.Current.Session = new AppSession
        {
            Left = CapturePane(LeftPane),
            Right = CapturePane(RightPane),
        };

        _settings.Save();
    }

    private static PaneSession CapturePane(PaneViewModel pane) => new()
    {
        ActiveTabIndex = Math.Max(pane.ActiveTabIndex, 0),
        Tabs = [.. pane.Tabs.Select(t => new TabSession
        {
            Path = t.CurrentPath ?? TabViewModel.HomeMarker,
            ViewMode = t.ViewMode,
            SortKey = t.SortKey,
            SortDescending = t.SortDescending,
            ShowHiddenItems = t.ShowHiddenItems,
        })],
    };

    private void BuildSidebar()
    {
        Sections.Clear();

        Sections.Add(new SidebarSection
        {
            HeaderKey = "Nav_QuickAccess",
            Header = TranslationSource.Instance["Nav_QuickAccess"],
            Items = BuildQuickAccess(),
        });

        Sections.Add(new SidebarSection
        {
            HeaderKey = "QuickAccess_Recent",
            Header = TranslationSource.Instance["QuickAccess_Recent"],
            Items = BuildRecent(),
        });

        Sections.Add(new SidebarSection
        {
            HeaderKey = "Nav_Drives",
            Header = TranslationSource.Instance["Nav_Drives"],
            Items = BuildDrives(),
        });

        Sections.Add(new SidebarSection
        {
            HeaderKey = "Nav_Favorites",
            Header = TranslationSource.Instance["Nav_Favorites"],
            Items = BuildFavorites(),
        });
    }

    /// <summary>A „Legutóbbi" szekció újraépítése.</summary>
    private void RefreshRecent() => ReplaceSection("QuickAccess_Recent", BuildRecent);

    /// <summary>Egy oldalsáv-szekció cseréje friss tartalommal, a kiemelés újraszámolásával.</summary>
    private void ReplaceSection(string headerKey, Func<List<SidebarItemViewModel>> build)
    {
        if (Sections.FirstOrDefault(s => s.HeaderKey == headerKey) is not { } section)
        {
            return;
        }

        Sections[Sections.IndexOf(section)] = new SidebarSection
        {
            HeaderKey = headerKey,
            Header = TranslationSource.Instance[headerKey],
            Items = build(),
        };

        UpdateActiveSidebarItem();
    }

    /// <summary>
    /// A kedvencként megjelölt fájlok/mappák. A már nem létező célok is
    /// megjelennek (halványabban, <see cref="SidebarItemViewModel.IsMissing"/>),
    /// hogy a felhasználó eltávolíthassa őket ahelyett, hogy csendben eltűnnének.
    /// </summary>
    private List<SidebarItemViewModel> BuildFavorites()
    {
        var items = new List<SidebarItemViewModel>();

        foreach (var path in _metadata.GetFavoritePaths().OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var exists = Directory.Exists(path) || File.Exists(path);
            var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));

            items.Add(new SidebarItemViewModel
            {
                Label = string.IsNullOrEmpty(name) ? path : name,
                Path = path,
                Icon = SymbolRegular.Heart24,
                IsMissing = !exists,
                IsRemovable = true,
            });
        }

        return items;
    }

    /// <summary>Az oldalsáv Kedvencek szekciójának újraépítése — kedvenc hozzáadása/eltávolítása után.</summary>
    public void RefreshFavorites()
    {
        var favoritesSection = Sections.FirstOrDefault(s => s.HeaderKey == "Nav_Favorites");

        if (favoritesSection is null)
        {
            return;
        }

        var index = Sections.IndexOf(favoritesSection);

        Sections[index] = new SidebarSection
        {
            HeaderKey = "Nav_Favorites",
            Header = TranslationSource.Instance["Nav_Favorites"],
            Items = BuildFavorites(),
        };

        UpdateActiveSidebarItem();
    }

    /// <summary>
    /// A Gyors elérés szekció (benne a Lomtár „üres" jelzésének) frissítése —
    /// a Lomtár-ablak bezárásakor hívva, hogy az ürítés/visszaállítás/törlés
    /// után a sáv ne mutasson elavult állapotot.
    /// </summary>
    public void RefreshQuickAccess() => ReplaceSection("Nav_QuickAccess", BuildQuickAccess);

    /// <summary>
    /// Egy megnyitott mappa felvétele a „Legutóbbi" szekcióba — az aktív
    /// panel navigációjára hívva.
    /// </summary>
    private void RecordRecent(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            _quickAccess.RecordRecent(path);
        }
    }

    [RelayCommand]
    private void RemoveFavorite(SidebarItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        // A Kedvencek szekcióban ez metaadat-törlés, a Legutóbbi szekcióban
        // viszont a gyorselérés-bejegyzés eldobása — az „x" gomb mindkét
        // helyen ugyanezt a parancsot hívja.
        if (item.EntryId is { } entryId)
        {
            _quickAccess.Remove(entryId);
            return;
        }

        _metadata.SetFavorite(item.Path, false);
    }

    /// <summary>
    /// Egy mappa rögzítése a gyorselérésben — jobb klikkből („Rögzítés a
    /// gyorseléréshez") vagy a mappa ráhúzásából a gyorselérés panelre.
    /// Ha már rögzítve van, nem csinál semmit (nincs duplikáció).
    /// </summary>
    [RelayCommand]
    private void PinToQuickAccess(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
        {
            _quickAccess.Pin(path);
        }
    }

    /// <summary>Egy bejegyzés leoldása a gyorselérésből.</summary>
    [RelayCommand]
    private void UnpinQuickAccess(SidebarItemViewModel? item)
    {
        if (item?.EntryId is { } id)
        {
            _quickAccess.Remove(id);
        }
    }

    /// <summary>Egy gyorselérés-sor mozgatása egy másik sor helyére — húzásos átrendezés az oldalsávban.</summary>
    public void ReorderQuickAccess(string sourceEntryId, string targetEntryId)
    {
        if (sourceEntryId == targetEntryId)
        {
            return;
        }

        var pinned = _quickAccess.Pinned;
        var targetIndex = pinned.ToList().FindIndex(e => e.Id == targetEntryId);

        if (targetIndex >= 0)
        {
            _quickAccess.Reorder(sourceEntryId, targetIndex);
        }
    }

    /// <summary>Egy gyorselérés-sor átnevezése a jobbklikk-menüből.</summary>
    public void RenameQuickAccessEntry(string entryId, string label) =>
        _quickAccess.Update(entryId, e => e.Label = string.IsNullOrWhiteSpace(label) ? null : label.Trim());

    /// <summary>Egy gyorselérés-sor ikonjának cseréje a jobbklikk-menüből.</summary>
    public void SetQuickAccessIcon(string entryId, string icon) =>
        _quickAccess.Update(entryId, e => e.Icon = icon);

    /// <summary>Egy gyorselérés-sor útvonalának javítása, ha a mappa elköltözött.</summary>
    public void FixQuickAccessPath(string entryId, string path) =>
        _quickAccess.Update(entryId, e => e.Path = path);

    /// <summary>Egy sor feljebb/lejjebb mozgatása a jobbklikk-menüből.</summary>
    public void NudgeQuickAccessEntry(string entryId, int delta)
    {
        var pinned = _quickAccess.Pinned.ToList();
        var index = pinned.FindIndex(e => e.Id == entryId);

        if (index >= 0)
        {
            _quickAccess.Reorder(entryId, index + delta);
        }
    }

    /// <summary>
    /// Az oldalsáv Címkék szekciójának újraépítése — címke létrehozása,
    /// átnevezése vagy törlése után (a Beállításokból).
    /// </summary>
    private void RefreshTagFilters()
    {
        var previouslyActive = SelectedTab?.ActiveTagFilterId;

        TagFilters.Clear();

        foreach (var tag in _metadata.Tags.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            TagFilters.Add(new TagFilterItemViewModel
            {
                Id = tag.Id,
                Name = tag.Name,
                Color = tag.Color,
                ColorHex = tag.ColorHex,
                IsActive = tag.Id == previouslyActive,
            });
        }

        // Ha a törölt címke épp aktív szűrő volt, a szűrést is le kell venni.
        if (SelectedTab is { } tab && tab.ActiveTagFilterId is { } id && TagFilters.All(t => t.Id != id))
        {
            tab.ActiveTagFilterId = null;
        }
    }

    /// <summary>Kattintás egy címkére az oldalsávban: szűrés arra a címkére, vagy — ismételt kattintásra — a szűrés levétele.</summary>
    [RelayCommand]
    private void SelectTagFilter(TagFilterItemViewModel? tag)
    {
        if (tag is null || SelectedTab is not { } tab)
        {
            return;
        }

        tab.ActiveTagFilterId = tab.ActiveTagFilterId == tag.Id ? null : tag.Id;
        SyncTagFilterHighlight();
    }

    private void SyncTagFilterHighlight()
    {
        var activeId = SelectedTab?.ActiveTagFilterId;

        foreach (var tag in TagFilters)
        {
            tag.IsActive = tag.Id == activeId;
        }
    }

    private List<SidebarItemViewModel> BuildQuickAccess()
    {
        // A Kezdőlap a virtuális „Ez a gép"-nézetre mutat (lásd
        // TabViewModel.HomeMarker/IsHome), nem egy valódi mappára — ezért
        // ez itt, a rögzített mappáktól külön, elsőként kerül be, és nem
        // távolítható el.
        var items = new List<SidebarItemViewModel>
        {
            new()
            {
                LabelKey = "Nav_Home",
                Label = TranslationSource.Instance["Nav_Home"],
                Path = TabViewModel.HomeMarker,
                Icon = SymbolRegular.Home24,
                IsHomeEntry = true,
            },
        };

        foreach (var entry in _quickAccess.Pinned.Where(e => e.Visible))
        {
            if (entry.Kind == QuickAccessEntryKind.Separator)
            {
                items.Add(new SidebarItemViewModel { Path = string.Empty, IsSeparator = true, EntryId = entry.Id });
                continue;
            }

            var label = !string.IsNullOrWhiteSpace(entry.Label)
                ? entry.Label
                : entry.LabelKey is { } key
                    ? TranslationSource.Instance[key]
                    : Path.GetFileName(Path.TrimEndingDirectorySeparator(entry.Path));

            items.Add(new SidebarItemViewModel
            {
                EntryId = entry.Id,
                LabelKey = string.IsNullOrWhiteSpace(entry.Label) ? entry.LabelKey : null,
                Label = label,
                GroupHeader = entry.Group,
                Path = entry.Path,
                Icon = ParseIcon(entry.Icon, SymbolRegular.Folder24),
                IconColorHex = entry.Color,

                // Nem a Directory.Exists: egy leválasztott hálózati megosztás
                // azon MÁSODPERCEKIG blokkolna a UI-szálon. A szolgáltatás
                // gyorsítótárból felel, és a háttérben ellenőriz.
                IsMissing = !_quickAccess.IsReachable(entry.Path),
                IsUnpinnable = true,
            });
        }

        // A Lomtárnak nincs valódi mappa-útvonala — kattintáskor a
        // SidebarItemViewModel.IsRecycleBin jelzi, hogy navigálás helyett a
        // Lomtár-ablakot kell megnyitni (lásd MainWindow.OnSidebarSelectionChanged).
        items.Add(new SidebarItemViewModel
        {
            LabelKey = "Nav_RecycleBin",
            Label = TranslationSource.Instance["Nav_RecycleBin"],
            Path = string.Empty,
            Icon = SymbolRegular.Delete24,
            IsRecycleBin = true,
            Detail = RecycleBinService.IsEmpty ? TranslationSource.Instance["RecycleBin_EmptyState"] : null,
        });

        return items;
    }

    /// <summary>
    /// A „Legutóbbi" szekció — automatikus, a program tartja karban. Az „x"
    /// gomb (<see cref="SidebarItemViewModel.IsRemovable"/>) SZÁNDÉKOSAN csak
    /// itt jelenik meg: a rögzített elemeket a szerkesztőből lehet
    /// eltávolítani, hogy egy félrekattintás ne tüntessen el egy kézzel
    /// felvett mappát (spec F5).
    /// </summary>
    private List<SidebarItemViewModel> BuildRecent() =>
    [
        .. _quickAccess.Recent.Select(entry => new SidebarItemViewModel
        {
            EntryId = entry.Id,
            Label = Path.GetFileName(Path.TrimEndingDirectorySeparator(entry.Path)),
            Path = entry.Path,
            Icon = SymbolRegular.History24,
            IsMissing = !_quickAccess.IsReachable(entry.Path),
            IsRemovable = true,
        }),
    ];

    private static List<SidebarItemViewModel> BuildDrives()
    {
        var items = new List<SidebarItemViewModel>();

        foreach (var drive in DriveEnumerator.GetDrives())
        {
            var icon = drive.DriveType switch
            {
                DriveType.Removable => SymbolRegular.UsbStick24,
                DriveType.Network => SymbolRegular.CloudArrowUp24,
                DriveType.CDRom => SymbolRegular.Cd16,

                // A HardDrive24 kódpontja (U+F0386) a Segoe Fluent Icons
                // rendszerbetűkészletben nem létezik — a rendszer helyette egy
                // ártalmatlannak tűnő, de félrevezető apró jelet rajzol ki
                // helyette. A Storage24-et lemérve (közvetlen glyph-teszttel)
                // valódi, jól felismerhető meghajtó-ikont ad.
                _ => SymbolRegular.Storage24,
            };

            // Csak akkor van értelme lemezikont keresni, ha ténylegesen van
            // beolvasható lemez — üres tálcánál marad az általános CD-glyph.
            var customIcon = drive.DriveType == DriveType.CDRom && drive.TotalBytes > 0
                ? DiscIconResolver.TryResolve(drive.Item.FullPath)
                : null;

            items.Add(new SidebarItemViewModel
            {
                Label = drive.Label,
                Path = drive.Item.FullPath,
                Icon = icon,
                CustomIcon = customIcon,
                Drive = drive,
                Detail = FormatDriveDetail(drive),
                UsedFraction = drive.UsedFraction,
            });
        }

        return items;
    }

    private static string? FormatDriveDetail(DriveEntry drive) =>
        drive.TotalBytes > 0
            ? string.Format(
                TranslationSource.Instance["Drive_FreeOfTotal"],
                ByteSize.Format(drive.FreeBytes),
                ByteSize.Format(drive.TotalBytes))
            : null;

    /// <summary>
    /// Az oldalsáv feliratai kész szövegek, nem kötések, ezért nyelvváltáskor
    /// külön újra kell képezni őket.
    /// </summary>
    private void RefreshSidebarDetails()
    {
        foreach (var section in Sections)
        {
            section.RefreshLabels();

            foreach (var item in section.Items)
            {
                if (item.Drive is { } drive)
                {
                    item.Detail = FormatDriveDetail(drive);
                }
            }
        }
    }

    /// <summary>
    /// Az oldalsáv kiemelésének frissítése az aktív fül aktuális útvonala
    /// alapján — fülváltáskor, navigáció közben és induláskor egyaránt.
    /// </summary>
    /// <remarks>
    /// Nem csak a pontosan egyező elem emelődik ki, hanem az aktuális útvonal
    /// MINDEN őse is: ha a „Dokumentumok" gyorselérés a
    /// <c>C:\Users\Rego\Documents</c>-re mutat, és éppen a
    /// <c>C:\Users\Rego\Documents\asd</c> mappában vagyunk, a „Dokumentumok"
    /// sor is aktívnak számít — így a felhasználó mélyebbre navigálva sem
    /// veszti el a tájékozódási pontot a bal oldali fában.
    /// </remarks>
    private void UpdateActiveSidebarItem()
    {
        var currentPath = SelectedTab?.CurrentPath;

        foreach (var section in Sections)
        {
            foreach (var item in section.Items)
            {
                item.IsActive = currentPath is not null && IsPathOrAncestor(item.Path, currentPath);
            }
        }
    }

    /// <summary>
    /// Igaz, ha <paramref name="candidateAncestor"/> maga az aktuális útvonal,
    /// vagy annak valódi szülője.
    /// </summary>
    /// <remarks>
    /// A szülő-ellenőrzés elválasztó karakterrel kiegészített előtag-egyezésre
    /// épül, hogy a <c>C:\Users\Rego\Documents</c> ne minősüljön tévesen a
    /// <c>C:\Users\Rego\DocumentsBackup</c> ősének — a puszta
    /// <c>StartsWith</c> ebbe a csapdába esne.
    /// </remarks>
    private static bool IsPathOrAncestor(string candidateAncestor, string currentPath)
    {
        var normalizedAncestor = Path.TrimEndingDirectorySeparator(candidateAncestor);
        var normalizedCurrent = Path.TrimEndingDirectorySeparator(currentPath);

        if (string.Equals(normalizedAncestor, normalizedCurrent, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var prefix = normalizedAncestor + Path.DirectorySeparatorChar;

        return normalizedCurrent.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
