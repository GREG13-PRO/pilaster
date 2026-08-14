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
        FileOperationEngine fileOperations)
    {
        _provider = provider;
        _settings = settings;
        _theme = theme;
        _quickActions = quickActions;
        Updates = updates;
        _folderSizes = folderSizes;
        _metadata = metadata;
        _fileOperations = fileOperations;

        Tabs = [];
        Sections = [];

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

        AddTab(TabViewModel.HomeMarker);

        // A két panel a fülrendszertől FÜGGETLEN, saját TabViewModel-pár —
        // nem kerülnek be a Tabs-ba, nem jelennek meg fülként. Ez a
        // legegyszerűbb módja annak, hogy mindegyiknek saját útvonala,
        // előzménye, kijelölése és nézetmódja legyen: maga a TabViewModel már
        // eleve ezt nyújtja (lásd az oszlopos nézet is ugyanígy épül fel
        // önálló TabViewModel-ekből).
        LeftPaneTab = new TabViewModel(_provider, _folderSizes, _metadata);
        RightPaneTab = new TabViewModel(_provider, _folderSizes, _metadata);
        _ = LeftPaneTab.NavigateAsync(TabViewModel.HomeMarker);
        _ = RightPaneTab.NavigateAsync(TabViewModel.HomeMarker);

        // A tulajdonságon keresztül állítjuk (nem közvetlen mezőn — a
        // forrásgenerált mező neve nem elérhető innen), ami az OnXChanged
        // miatt visszaírja a beállításba ugyanazt az értéket, amit épp
        // onnan olvasott — ártalmatlan, csak egy felesleges mentés induláskor.
        DualPaneEnabled = _settings.Current.DualPaneEnabled;
        DualPaneVertical = _settings.Current.DualPaneVertical;
    }

    /// <summary>A bal panel — a fülrendszertől független, csak Kétablakos nézetben látszik.</summary>
    public TabViewModel LeftPaneTab { get; }

    /// <summary>A jobb panel — a fülrendszertől független, csak Kétablakos nézetben látszik.</summary>
    public TabViewModel RightPaneTab { get; }

    /// <summary>Igaz, ha a bal panel az aktív (kiemelt, a Szinkronizálás és a majdani gyorsbillentyűk erre céloznak).</summary>
    [ObservableProperty]
    public partial bool IsLeftPaneActive { get; set; } = true;

    /// <summary>A jelenleg aktív panel — a majdani Total Commander-billentyűzet (F5/F6/Tab) ezen dolgozik.</summary>
    public TabViewModel ActivePaneTab => IsLeftPaneActive ? LeftPaneTab : RightPaneTab;

    public TabViewModel InactivePaneTab => IsLeftPaneActive ? RightPaneTab : LeftPaneTab;

    [ObservableProperty]
    public partial bool DualPaneEnabled { get; set; }

    partial void OnDualPaneEnabledChanged(bool value)
    {
        _settings.Current.DualPaneEnabled = value;
        _settings.NotifyChanged();
        OnPropertyChanged(nameof(ShowFunctionKeyBar));
    }

    /// <summary>
    /// A kétablakos nézet alján megjelenő, kattintható funkcióbillentyű-sáv
    /// csak akkor látszik, ha MINDKÉT beállítás be van kapcsolva — kétablakos
    /// nézet nélkül nincs értelme (F5/F6 a másik panelre céloz), a Total
    /// Commander-billentyűkiosztás nélkül pedig zavaró lenne, ha a gombok
    /// funkciója nem egyezik a megszokott billentyűkkel.
    /// </summary>
    public bool ShowFunctionKeyBar => DualPaneEnabled && _settings.Current.TotalCommanderKeybindingsEnabled;

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
        if (ActivePaneTab.CurrentPath is { } path)
        {
            await InactivePaneTab.NavigateCommand.ExecuteAsync(path);
        }
    }

    /// <summary>Panel-ről panelre húzás/beillesztés — lásd FilePaneView.FilesDropped.</summary>
    public void StartPaneCopy(IReadOnlyList<string> paths, string destinationDir) => _fileOperations.StartCopy(paths, destinationDir);

    public void StartPaneMove(IReadOnlyList<string> paths, string destinationDir) => _fileOperations.StartMove(paths, destinationDir);

    public void StartPaneDelete(IReadOnlyList<string> paths, bool permanent) => _fileOperations.StartDelete(paths, permanent);

    /// <summary>
    /// Total Commander F5 (másolás)/F6 (áthelyezés) — a felhasználó által a
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

    partial void OnSelectedTabChanged(TabViewModel? value)
    {
        UpdateActiveSidebarItem();
        SyncTagFilterHighlight();
        OnPropertyChanged(nameof(CanEjectCurrentDrive));
    }

    public ObservableCollection<TabViewModel> Tabs { get; }

    public ObservableCollection<SidebarSection> Sections { get; }

    /// <summary>Az oldalsáv Címkék szekciója — külön listaként, mert nem navigál, hanem szűr.</summary>
    public ObservableCollection<TagFilterItemViewModel> TagFilters { get; } = [];

    /// <summary>Frissítés-ellenőrzés és -telepítés állapota — a sáv és a Beállítások közösen használja.</summary>
    public UpdateViewModel Updates { get; }

    [ObservableProperty]
    public partial TabViewModel? SelectedTab { get; set; }

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

    [RelayCommand]
    private void NewTab() => AddTab(TabViewModel.HomeMarker);

    [RelayCommand]
    private void CloseTab(TabViewModel? tab)
    {
        if (tab is null || Tabs.Count <= 1)
        {
            return;
        }

        var index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);
        tab.Detach();

        // A szomszédos fülre lépünk, hogy ne maradjon kijelöletlen a sáv.
        SelectedTab = Tabs[Math.Clamp(index, 0, Tabs.Count - 1)];
    }

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

    /// <summary>Total Commander F7 — új mappa a MEGADOTT (aktív egy- vagy kétablakos) panelben, nem feltétlenül a fülrendszer aktuális fülében.</summary>
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
        // áthelyezésnél csak a tényleges célmappát mutató fület. A Kétablakos
        // nézet két panelje a Tabs-tól KÜLÖN gyűjtemény (lásd LeftPaneTab/
        // RightPaneTab) — enélkül a bennük végzett húzásos másolás/áthelyezés
        // után a panel nem mutatná az új/eltűnt elemeket.
        foreach (var tab in Tabs.Append(LeftPaneTab).Append(RightPaneTab))
        {
            var affected = job.Kind == FileOperationKind.Delete
                || string.Equals(tab.CurrentPath, job.DestinationDirectory, StringComparison.OrdinalIgnoreCase);

            if (affected)
            {
                await tab.RefreshCommand.ExecuteAsync(null);
            }
        }
    }

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

    private void AddTab(string path)
    {
        var tab = new TabViewModel(_provider, _folderSizes, _metadata)
        {
            ShowHiddenItems = _settings.Current.ShowHiddenItems,
            ViewMode = _settings.Current.LastViewMode,
        };

        // A rejtett elemek kapcsolója és a nézetmód fülenként állítható, de
        // a legutóbbi választás menteni való — a következő indításnál/új
        // fülnél azt várja a felhasználó.
        tab.PropertyChanged += (_, e) =>
        {
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

            // Csak az aktív fül útvonalváltása befolyásolja az oldalsáv
            // kiemelését és a Kiadás gomb láthatóságát — egy háttérben
            // navigáló fül ne rángassa el.
            if (e.PropertyName == nameof(TabViewModel.CurrentPath) && ReferenceEquals(tab, SelectedTab))
            {
                UpdateActiveSidebarItem();
                OnPropertyChanged(nameof(CanEjectCurrentDrive));
            }
        };

        Tabs.Add(tab);
        SelectedTab = tab;

        _ = tab.NavigateAsync(path);
    }

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
    public void RefreshQuickAccess()
    {
        var quickAccessSection = Sections.FirstOrDefault(s => s.HeaderKey == "Nav_QuickAccess");

        if (quickAccessSection is null)
        {
            return;
        }

        var index = Sections.IndexOf(quickAccessSection);

        Sections[index] = new SidebarSection
        {
            HeaderKey = "Nav_QuickAccess",
            Header = TranslationSource.Instance["Nav_QuickAccess"],
            Items = BuildQuickAccess(),
        };

        UpdateActiveSidebarItem();
    }

    [RelayCommand]
    private void RemoveFavorite(SidebarItemViewModel? item)
    {
        if (item is not null)
        {
            _metadata.SetFavorite(item.Path, false);
        }
    }

    /// <summary>
    /// Egy mappa rögzítése a gyorselérésben — jobb klikkből („Rögzítés a
    /// gyorseléréshez") vagy a mappa ráhúzásából a gyorselérés panelre.
    /// Ha már rögzítve van, nem csinál semmit (nincs duplikáció).
    /// </summary>
    [RelayCommand]
    private void PinToQuickAccess(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return;
        }

        EnsureQuickAccessPinsSeeded();

        var pins = _settings.Current.QuickAccessPins!;

        if (pins.Any(p => string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        pins.Add(new PinnedFolder
        {
            Path = path,
            CustomLabel = Path.GetFileName(Path.TrimEndingDirectorySeparator(path)),
        });

        _settings.Save();
        RefreshQuickAccess();
    }

    /// <summary>Egy mappa leoldása a gyorselérésből — az „x" gomb a sajátrögzítésű soron.</summary>
    [RelayCommand]
    private void UnpinQuickAccess(SidebarItemViewModel? item)
    {
        if (item is null || _settings.Current.QuickAccessPins is not { } pins)
        {
            return;
        }

        pins.RemoveAll(p => string.Equals(p.Path, item.Path, StringComparison.OrdinalIgnoreCase));
        _settings.Save();
        RefreshQuickAccess();
    }

    /// <summary>
    /// A gyorselérés átrendezése húzással: a <paramref name="sourcePath"/>
    /// mappa a <paramref name="targetPath"/> mappa helyére kerül.
    /// </summary>
    public void ReorderQuickAccess(string sourcePath, string targetPath)
    {
        if (_settings.Current.QuickAccessPins is not { } pins || string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var source = pins.FirstOrDefault(p => string.Equals(p.Path, sourcePath, StringComparison.OrdinalIgnoreCase));
        var targetIndex = pins.FindIndex(p => string.Equals(p.Path, targetPath, StringComparison.OrdinalIgnoreCase));

        if (source is null || targetIndex < 0)
        {
            return;
        }

        pins.Remove(source);
        pins.Insert(targetIndex, source);

        _settings.Save();
        RefreshQuickAccess();
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

    /// <summary>Az alapértelmezett gyorselérés-mappák — csak első használatkor kerülnek be, lásd <see cref="EnsureQuickAccessPinsSeeded"/>.</summary>
    private static (Environment.SpecialFolder Folder, string Key, string Icon)[] DefaultQuickAccessFolders =>
    [
        (Environment.SpecialFolder.Desktop, "Nav_Desktop", nameof(SymbolRegular.Desktop24)),
        (Environment.SpecialFolder.MyDocuments, "Nav_Documents", nameof(SymbolRegular.Document24)),
        (Environment.SpecialFolder.MyPictures, "Nav_Pictures", nameof(SymbolRegular.Image24)),
        (Environment.SpecialFolder.MyMusic, "Nav_Music", nameof(SymbolRegular.MusicNote124)),
        (Environment.SpecialFolder.MyVideos, "Nav_Videos", nameof(SymbolRegular.Video24)),
    ];

    /// <summary>
    /// Első használatkor (amíg a felhasználó soha nem testreszabta) feltölti
    /// a mentett gyorselérést az alapértelmezett hat mappával, és el is
    /// menti — onnantól ez a mentett lista az igazság forrása, törlés/
    /// rögzítés/átrendezés után soha nem áll vissza az alapértelmezésre.
    /// </summary>
    private void EnsureQuickAccessPinsSeeded()
    {
        if (_settings.Current.QuickAccessPins is not null)
        {
            return;
        }

        var pins = new List<PinnedFolder>();

        foreach (var (folder, key, icon) in DefaultQuickAccessFolders)
        {
            var path = Environment.GetFolderPath(folder);

            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            {
                pins.Add(new PinnedFolder { Path = path, LabelKey = key, Icon = icon });
            }
        }

        // A Letöltések mappának nincs SpecialFolder megfelelője, ezért külön.
        var downloads = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

        if (Directory.Exists(downloads))
        {
            pins.Insert(Math.Min(2, pins.Count), new PinnedFolder
            {
                Path = downloads,
                LabelKey = "Nav_Downloads",
                Icon = nameof(SymbolRegular.ArrowDownload24),
            });
        }

        _settings.Current.QuickAccessPins = pins;
        _settings.Save();
    }

    private List<SidebarItemViewModel> BuildQuickAccess()
    {
        EnsureQuickAccessPinsSeeded();

        // A Kezdőlap a virtuális „Ez a gép"-nézetre mutat (lásd
        // TabViewModel.HomeMarker/IsHome), nem egy valódi mappára — ezért
        // ez itt, a rögzített mappáktól külön, elsőként kerül be, és nem
        // old ki (nem IsUnpinnable).
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

        foreach (var pin in _settings.Current.QuickAccessPins ?? [])
        {
            var label = pin.LabelKey is { } key
                ? TranslationSource.Instance[key]
                : pin.CustomLabel ?? Path.GetFileName(Path.TrimEndingDirectorySeparator(pin.Path));

            items.Add(new SidebarItemViewModel
            {
                LabelKey = pin.LabelKey,
                Label = label,
                Path = pin.Path,
                Icon = ParseIcon(pin.Icon, SymbolRegular.Folder24),
                IsMissing = !Directory.Exists(pin.Path),
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
