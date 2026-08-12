using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pilaster.App.Localization;
using Pilaster.Core.FileSystem;
using Pilaster.Core.Formatting;
using Pilaster.Providers.Local;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Pilaster.App.ViewModels;

/// <summary>A főablak állapota: fülek, oldalsáv, téma és nyelv.</summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IFileSystemProvider _provider;

    public MainWindowViewModel(IFileSystemProvider provider)
    {
        _provider = provider;
        Tabs = [];
        Sections = [];

        BuildSidebar();
        AddTab(GetStartupPath());
    }

    public ObservableCollection<TabViewModel> Tabs { get; }

    public ObservableCollection<SidebarSection> Sections { get; }

    [ObservableProperty]
    public partial TabViewModel? SelectedTab { get; set; }

    [ObservableProperty]
    public partial bool IsSidebarVisible { get; set; } = true;

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
        if (item is null || SelectedTab is null)
        {
            return;
        }

        await SelectedTab.NavigateAsync(item.Path);
    }

    [RelayCommand]
    private async Task OpenBreadcrumbAsync(BreadcrumbSegment? segment)
    {
        if (segment is null || SelectedTab is null)
        {
            return;
        }

        await SelectedTab.NavigateAsync(segment.Path);
    }

    /// <summary>Nyelvváltás; a felület azonnal átvált, újraindítás nélkül.</summary>
    [RelayCommand]
    private void SetLanguage(string? culture)
    {
        if (!string.IsNullOrWhiteSpace(culture))
        {
            TranslationSource.Instance.SetLanguage(culture);
            RefreshSidebarDetails();
        }
    }

    [RelayCommand]
    private static void SetTheme(string? theme)
    {
        var applied = theme switch
        {
            "light" => ApplicationTheme.Light,
            "dark" => ApplicationTheme.Dark,
            _ => ApplicationTheme.Unknown,
        };

        if (applied == ApplicationTheme.Unknown)
        {
            ApplicationThemeManager.ApplySystemTheme();
            return;
        }

        ApplicationThemeManager.Apply(applied);
    }

    private void AddTab(string path)
    {
        var tab = new TabViewModel(_provider);
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
        // A címkék kulcsok, nem kész szövegek: nyelvváltáskor a nézet kötése
        // fordítja őket újra.
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
}
