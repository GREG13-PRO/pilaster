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
using Wpf.Ui.Controls;

namespace Pilaster.App.ViewModels;

/// <summary>A főablak állapota: fülek, oldalsáv, téma, gyorsgombok.</summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IFileSystemProvider _provider;
    private readonly ISettingsService _settings;
    private readonly ThemeService _theme;
    private readonly QuickActionService _quickActions;

    public MainWindowViewModel(
        IFileSystemProvider provider,
        ISettingsService settings,
        ThemeService theme,
        QuickActionService quickActions)
    {
        _provider = provider;
        _settings = settings;
        _theme = theme;
        _quickActions = quickActions;

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

    partial void OnSelectedTabChanged(TabViewModel? value) => UpdateActiveSidebarItem();

    public ObservableCollection<TabViewModel> Tabs { get; }

    public ObservableCollection<SidebarSection> Sections { get; }

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

    /// <summary>Akkor jelez, ha a nézetnek meg kell nyitnia a Beállításokat.</summary>
    public event EventHandler? SettingsRequested;

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
        var tab = new TabViewModel(_provider)
        {
            ShowHiddenItems = _settings.Current.ShowHiddenItems,
        };

        // A rejtett elemek kapcsolója fülenként állítható, de a legutóbbi
        // választás menteni való — a következő indításnál azt várja a felhasználó.
        tab.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TabViewModel.ShowHiddenItems))
            {
                _settings.Current.ShowHiddenItems = tab.ShowHiddenItems;
                _settings.Save();
            }

            // Csak az aktív fül útvonalváltása befolyásolja az oldalsáv
            // kiemelését — egy háttérben navigáló fül ne rángassa el.
            if (e.PropertyName == nameof(TabViewModel.CurrentPath) && ReferenceEquals(tab, SelectedTab))
            {
                UpdateActiveSidebarItem();
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
                _ => SymbolRegular.HardDrive24,
            };

            items.Add(new SidebarItemViewModel
            {
                Label = drive.Label,
                Path = drive.Item.FullPath,
                Icon = icon,
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
    private void UpdateActiveSidebarItem()
    {
        var currentPath = SelectedTab?.CurrentPath;

        foreach (var section in Sections)
        {
            foreach (var item in section.Items)
            {
                item.IsActive = PathsEqual(item.Path, currentPath);
            }
        }
    }

    /// <summary>
    /// Útvonal-egyezés a záró elválasztó karakter figyelmen kívül hagyásával
    /// (a meghajtógyökerek, pl. „C:\", ettől függetlenül helyesen egyeznek).
    /// </summary>
    private static bool PathsEqual(string a, string? b) =>
        b is not null
        && string.Equals(
            Path.TrimEndingDirectorySeparator(a),
            Path.TrimEndingDirectorySeparator(b),
            StringComparison.OrdinalIgnoreCase);
}
