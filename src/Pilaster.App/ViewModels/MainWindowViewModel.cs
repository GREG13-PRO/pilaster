using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pilaster.App.Localization;
using Pilaster.App.Services;
using Pilaster.Core.FileSystem;
using Pilaster.Core.Formatting;
using Pilaster.Core.Settings;
using Pilaster.Providers.Local;
using Pilaster.Shell.Devices;
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

    public MainWindowViewModel(
        IFileSystemProvider provider,
        ISettingsService settings,
        ThemeService theme,
        QuickActionService quickActions,
        UpdateViewModel updates,
        FolderSizeService folderSizes)
    {
        _provider = provider;
        _settings = settings;
        _theme = theme;
        _quickActions = quickActions;
        Updates = updates;
        _folderSizes = folderSizes;

        Tabs = [];
        Sections = [];

        BuildSidebar();
        RefreshQuickActions();

        // A beállítások bárhonnan módosulhatnak (pl. a Beállítások ablakból),
        // ezért a felső sáv gombjai eseményre frissülnek, nem közvetlen hívásra.
        _settings.Changed += (_, _) =>
        {
            RefreshQuickActions();
            RefreshSidebarDetails();
            OnPropertyChanged(nameof(IsDarkTheme));
            OnPropertyChanged(nameof(ThemeIcon));
        };

        AddTab(GetStartupPath());
    }

    partial void OnSelectedTabChanged(TabViewModel? value)
    {
        UpdateActiveSidebarItem();
        OnPropertyChanged(nameof(CanEjectCurrentDrive));
    }

    public ObservableCollection<TabViewModel> Tabs { get; }

    public ObservableCollection<SidebarSection> Sections { get; }

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
    public bool AnimationsEnabled => _settings.Current.AnimationsEnabled;

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
    private void NewTab() => AddTab(GetStartupPath());

    [RelayCommand]
    private void CloseTab(TabViewModel? tab)
    {
        if (tab is null || Tabs.Count <= 1)
        {
            return;
        }

        var index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);

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
            await SelectedTab.NavigateAsync(GetStartupPath());
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
    private async Task CreateNewFolderAsync() => await CreateNewItemAsync(QuickActionKind.Folder);

    [RelayCommand]
    private async Task CreateNewFileAsync() => await CreateNewItemAsync(QuickActionKind.File);

    private async Task CreateNewItemAsync(QuickActionKind kind)
    {
        if (SelectedTab is not { } tab)
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
    }

    /// <summary>
    /// Fájlok beillesztése a vágólapról az üres terület helyi menüjéből.
    /// </summary>
    /// <remarks>
    /// Lásd <see cref="ClipboardFileService"/>: az Intéző saját
    /// másolás/kivágás-formátumát olvassa, tehát onnan másolt vagy kivágott
    /// elemek közvetlenül beilleszthetők.
    /// </remarks>
    [RelayCommand]
    private async Task PasteAsync()
    {
        if (SelectedTab is not { } tab)
        {
            return;
        }

        var result = ClipboardFileService.Paste(tab.CurrentPath);

        if (result.Outcome is ClipboardPasteOutcome.TargetInvalid)
        {
            return;
        }

        if (result.Outcome is ClipboardPasteOutcome.NoFilesOnClipboard)
        {
            tab.EmptyMessage = TranslationSource.Instance["Paste_NoFiles"];
            return;
        }

        // A frissítés törli az EmptyMessage-et, ezért a részleges hibát csak
        // utána állítjuk be, különben a felhasználó sosem látná.
        await tab.RefreshCommand.ExecuteAsync(null);

        if (result.Outcome is ClipboardPasteOutcome.PartiallyFailed)
        {
            tab.EmptyMessage = TranslationSource.Instance["Paste_Failed"];
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
        var tab = new TabViewModel(_provider, _folderSizes)
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

    /// <summary>
    /// Az induló mappa. A felhasználói profil biztonságos alapértelmezés:
    /// mindig létezik, és nem igényel emelt jogosultságot.
    /// </summary>
    private static string GetStartupPath() =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

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
    }

    private static List<SidebarItemViewModel> BuildQuickAccess()
    {
        (Environment.SpecialFolder Folder, string Key, SymbolRegular Icon)[] entries =
        [
            (Environment.SpecialFolder.UserProfile, "Nav_Home", SymbolRegular.Home24),
            (Environment.SpecialFolder.Desktop, "Nav_Desktop", SymbolRegular.Desktop24),
            (Environment.SpecialFolder.MyDocuments, "Nav_Documents", SymbolRegular.Document24),
            (Environment.SpecialFolder.MyPictures, "Nav_Pictures", SymbolRegular.Image24),
            (Environment.SpecialFolder.MyMusic, "Nav_Music", SymbolRegular.MusicNote124),
            (Environment.SpecialFolder.MyVideos, "Nav_Videos", SymbolRegular.Video24),
        ];

        var items = new List<SidebarItemViewModel>();

        foreach (var (folder, key, icon) in entries)
        {
            var path = Environment.GetFolderPath(folder);

            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            {
                items.Add(new SidebarItemViewModel
                {
                    LabelKey = key,
                    Label = TranslationSource.Instance[key],
                    Path = path,
                    Icon = icon,
                });
            }
        }

        // A Letöltések mappának nincs SpecialFolder megfelelője, ezért külön.
        var downloads = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

        if (Directory.Exists(downloads))
        {
            items.Insert(Math.Min(2, items.Count), new SidebarItemViewModel
            {
                LabelKey = "Nav_Downloads",
                Label = TranslationSource.Instance["Nav_Downloads"],
                Path = downloads,
                Icon = SymbolRegular.ArrowDownload24,
            });
        }

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
