using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Extensions.DependencyInjection;
using Pilaster.App.Controls;
using Pilaster.App.Diagnostics;
using Pilaster.App.Localization;
using Pilaster.App.Services;
using Pilaster.App.ViewModels;
using Pilaster.Core.FileSystem;
using Wpf.Ui.Controls;

// A WPF-UI saját ListView/ListBox/Panel típusokat is szállít ugyanezekkel a
// nevekkel. A XAML a WPF beépített vezérlőit példányosítja, ezért a kódban is
// azokra hivatkozunk — az álnév egyértelműsíti, melyikről van szó.
using GridViewColumnHeader = System.Windows.Controls.GridViewColumnHeader;
using ItemsControl = System.Windows.Controls.ItemsControl;
using ListBox = System.Windows.Controls.ListBox;
using ListView = System.Windows.Controls.ListView;
using Panel = System.Windows.Controls.Panel;
using SelectionChangedEventArgs = System.Windows.Controls.SelectionChangedEventArgs;

namespace Pilaster.App.Views;

public partial class MainWindow : FluentWindow
{
    private readonly MainWindowViewModel _viewModel;
    private readonly IServiceProvider _services;
    private readonly ThemeService _theme;

    /// <summary>A Beállítások ablak, amíg nyitva van — hogy ne nyíljon kettő.</summary>
    private SettingsWindow? _settingsWindow;

    /// <summary>
    /// A fül, amelynek <c>CurrentPath</c> változását épp figyeljük — a csúszó
    /// átmenet ehhez van feliratkozva. Fülváltáskor át kell iratkozni.
    /// </summary>
    private TabViewModel? _trackedTab;

    public MainWindow(MainWindowViewModel viewModel, IServiceProvider services, ThemeService theme)
    {
        _viewModel = viewModel;
        _services = services;
        _theme = theme;
        DataContext = viewModel;

        viewModel.SettingsRequested += OnSettingsRequested;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        viewModel.Updates.RestartRequested += OnUpdateRestartRequested;

        InitializeComponent();

        // A rendszertéma figyelése: „rendszerkövető" módban a Windows
        // világos/sötét váltása menet közben is átszínezi a felületet.
        Loaded += (_, _) => _theme.WatchSystemTheme(this);

        // Húzásos kijelölés: a WPF ListView/ListBox natívan nem támogatja,
        // lásd Controls/MarqueeSelector.cs. Mindkét nézet (részletes + rács)
        // ugyanazt a viselkedést és átfedő téglalapot osztja meg, mivel
        // egyszerre csak az egyik látható.
        var marquee = new MarqueeSelector(FileListHost, MarqueeRectangle);
        marquee.Attach(DetailsView);
        marquee.Attach(GridViewList);

        TrackTab(_viewModel.SelectedTab);
    }

    private void OnSettingsRequested(object? sender, EventArgs e)
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = _services.GetRequiredService<SettingsWindow>();
        _settingsWindow.Owner = this;
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.SelectedTab))
        {
            TrackTab(_viewModel.SelectedTab);
        }
    }

    /// <summary>
    /// A csúszó átmenet forrását a mindenkori aktív fülre állítja át.
    /// </summary>
    private void TrackTab(TabViewModel? tab)
    {
        if (_trackedTab is not null)
        {
            _trackedTab.PropertyChanged -= OnTrackedTabPropertyChanged;
        }

        _trackedTab = tab;

        if (_trackedTab is not null)
        {
            _trackedTab.PropertyChanged += OnTrackedTabPropertyChanged;
        }
    }

    private void OnTrackedTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TabViewModel.CurrentPath))
        {
            PlayContentTransition();
        }
    }

    /// <summary>
    /// Csúszó-elhalványuló átmenet lejátszása a fájlterületen, valahányszor a
    /// megnyitott mappa változik — gyorselérésre kattintás, breadcrumb,
    /// vissza/előre, vagy dupla kattintás egy mappára.
    /// </summary>
    private void PlayContentTransition()
    {
        if (!_viewModel.AnimationsEnabled)
        {
            return;
        }

        if (Resources["SlideInFileArea"] is Storyboard storyboard)
        {
            storyboard.Begin(FileAreaBorder);
        }
    }

    /// <summary>
    /// Az overflow („több") gomb bal kattintásra nyissa a menüjét.
    /// </summary>
    /// <remarks>
    /// A <c>ContextMenu</c> alapból csak jobb gombra nyílik, itt viszont a
    /// gomb egyetlen funkciója a menü megnyitása — a felhasználó bal kattintást
    /// várna, és jobb kattintással sosem próbálkozna.
    /// </remarks>
    private void OnOverflowMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { ContextMenu: { } menu } element)
        {
            menu.PlacementTarget = element;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    /// <summary>
    /// Dupla kattintás: mappába lépés, fájlnál megnyitás a társított programmal.
    /// </summary>
    private async void OnItemDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not Selector { SelectedItem: FileSystemItem item })
        {
            return;
        }

        await OpenItemAsync(item);
    }

    /// <summary>
    /// Jobb kattintás fájlelemen: ha a kattintott sor még nincs kijelölve, a
    /// helyi menü kizárólag arra vonatkozzon — ez az Explorer és a legtöbb
    /// fájlkezelő megszokott viselkedése. Ha már a kijelölés része, a meglévő
    /// (esetleg többelemes) kijelölés érintetlen marad.
    /// </summary>
    private void OnItemPreviewRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: FileSystemItem item } container)
        {
            return;
        }

        if (ItemsControl.ItemsControlFromItemContainer(container) is not Selector selector)
        {
            return;
        }

        var alreadySelected = selector switch
        {
            ListView view => view.SelectedItems.Contains(item),
            ListBox box => box.SelectedItems.Contains(item),
            _ => false,
        };

        if (!alreadySelected)
        {
            selector.SelectedItem = item;
        }
    }

    private async void OnOpenItemClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is FileSystemItem item)
        {
            await OpenItemAsync(item);
        }
    }

    private void OnCopyPathClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not FileSystemItem item)
        {
            return;
        }

        try
        {
            Clipboard.SetText(item.FullPath);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // A vágólapot időnként egy másik folyamat zárolja — nincs jobb
            // teendő, mint csendben kihagyni, mintsem hibaüzenettel zavarni.
        }
    }

    private void OnShowInExplorerClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not FileSystemItem item)
        {
            return;
        }

        try
        {
            Process.Start("explorer.exe", $"/select,\"{item.FullPath}\"");
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }

    private void OnShowPropertiesClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not FileSystemItem item)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(item.FullPath) { UseShellExecute = true, Verb = "properties" });
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }

    /// <summary>
    /// Egy elem megnyitása: mappánál/meghajtónál navigáció, fájlnál a
    /// társított programmal indítás. Ezt hívja a dupla kattintás és a helyi
    /// menü „Megnyitás" pontja is, hogy a viselkedés egy helyen éljen.
    /// </summary>
    private async Task OpenItemAsync(FileSystemItem item)
    {
        if (item.IsNavigable)
        {
            if (_viewModel.SelectedTab is { } tab)
            {
                await tab.NavigateAsync(item.FullPath);
            }

            return;
        }

        OpenWithShell(item.FullPath);
    }

    /// <summary>
    /// Fájl megnyitása az alapértelmezett társított alkalmazással.
    /// </summary>
    /// <remarks>
    /// A <c>UseShellExecute</c> szándékos: enélkül a .NET közvetlenül próbálná
    /// futtatni a fájlt, ami csak végrehajtható állományoknál működne.
    /// </remarks>
    private static void OpenWithShell(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Nincs társított program, vagy a felhasználó elvetette a
            // „Megnyitás ezzel" párbeszédet — mindkettő normális eset.
        }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel.SelectedTab is not { } tab || sender is not Selector selector)
        {
            return;
        }

        var selected = selector switch
        {
            ListView view => view.SelectedItems.Cast<FileSystemItem>().ToList(),
            ListBox box => box.SelectedItems.Cast<FileSystemItem>().ToList(),
            _ => [],
        };

        // A mappák -1 méretet hordoznak, amíg nincs kiszámolva a tartalmuk;
        // azt nem szabad beleszámolni az összegbe.
        var totalBytes = selected
            .Where(item => item.SizeBytes > 0)
            .Sum(item => item.SizeBytes);

        tab.UpdateStatus(selected.Count, totalBytes);
    }

    private async void OnSidebarSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox { SelectedItem: SidebarItemViewModel item })
        {
            return;
        }

        if (_viewModel.SelectedTab is { } tab)
        {
            await tab.NavigateAsync(item.Path);
        }

        // A kijelölés elengedése, hogy ugyanarra a helyre ismét lehessen lépni,
        // és hogy ne maradjon két szekcióban egyszerre kiemelt sor.
        ((ListBox)sender).SelectedItem = null;
    }

    /// <summary>
    /// Oszlopfejléc-kattintás: rendezés az oszlop szempontja szerint.
    /// </summary>
    /// <remarks>
    /// Ugyanarra az oszlopra kattintva az irány fordul, más oszlopra kattintva
    /// növekvővel indul — ez a Windows és a macOS közös viselkedése, és a
    /// felhasználó ezt várja anélkül, hogy meg kellene tanulnia.
    /// </remarks>
    private void OnColumnHeaderClick(object sender, RoutedEventArgs e)
    {
        // A GridView jobb szélén ül egy „töltelék" fejléc, aminek nincs oszlopa.
        if (e.OriginalSource is not GridViewColumnHeader { Column: { } column })
        {
            return;
        }

        if (_viewModel.SelectedTab is not { } tab)
        {
            return;
        }

        var key = GridViewSort.GetSortKey(column);
        ApplySort(tab, key);
    }

    /// <summary>Az üres terület helyi menüjének „Rendezés" almenüje.</summary>
    private void OnSortByClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not SortKey key || _viewModel.SelectedTab is not { } tab)
        {
            return;
        }

        ApplySort(tab, key);
    }

    private void ApplySort(TabViewModel tab, SortKey key)
    {
        var descending = key == tab.SortKey && !tab.SortDescending;
        tab.ApplySort(key, descending);
        SyncColumnHeaderIndicators();
    }

    /// <summary>
    /// Az oszlopfejléc-nyilak összhangba hozása az aktuális rendezéssel.
    /// </summary>
    /// <remarks>
    /// A rendezés nem csak oszlopfejléc-kattintásra változhat — a „Rendezés"
    /// almenü is beállíthatja —, ezért a nyíl a jelenlegi <c>SortKey</c>/
    /// <c>SortDescending</c> tiszta függvénye, nem egy külön kézzel
    /// karbantartott mezőé.
    /// </remarks>
    private void SyncColumnHeaderIndicators()
    {
        if (_viewModel.SelectedTab is not { } tab)
        {
            return;
        }

        foreach (var header in FindVisualChildren<GridViewColumnHeader>(DetailsView))
        {
            if (header.Column is not { } column)
            {
                continue;
            }

            var key = GridViewSort.GetSortKey(column);

            GridViewSort.SetIndicator(
                header,
                key != tab.SortKey
                    ? SortIndicator.None
                    : tab.SortDescending ? SortIndicator.Descending : SortIndicator.Ascending);
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);

            if (child is T typed)
            {
                yield return typed;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    /// <summary>
    /// A frissítés letöltve, telepítésre kész — megerősítés kérése az
    /// újraindításhoz. A tényleges fájlcsere csak azután történhet, hogy a
    /// Pilaster.exe kilépett és elengedte a saját fájljainak zárolását, ezért
    /// előbb ezt kell megerősíteni, nem lehet csendben, azonnal újraindítani.
    /// </summary>
    private void OnUpdateRestartRequested(object? sender, EventArgs e)
    {
        var strings = TranslationSource.Instance;

        // Fajlagosan a System.Windows verzió: a Wpf.Ui.Controls névtérnek
        // (ami ebben a fájlban is be van húzva) saját MessageBox-hoz tartozó
        // azonos nevű típusai vannak, amik enélkül ütköznének.
        var result = System.Windows.MessageBox.Show(
            string.Format(strings["Update_ConfirmRestartMessage"], _viewModel.Updates.PendingVersion),
            "Pilaster",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);

        if (result != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        _viewModel.Updates.BeginInstallAndExit();
        System.Windows.Application.Current.Shutdown();
    }

    private void OnSetViewDetails(object sender, RoutedEventArgs e) => ApplyViewMode(ViewMode.Details);

    private void OnSetViewGrid(object sender, RoutedEventArgs e) => ApplyViewMode(ViewMode.Grid);

    private void ApplyViewMode(ViewMode mode)
    {
        if (_viewModel.SelectedTab is { } tab)
        {
            tab.ViewMode = mode;
        }

        DetailsView.Visibility = mode == ViewMode.Details ? Visibility.Visible : Visibility.Collapsed;
        GridViewList.Visibility = mode == ViewMode.Grid ? Visibility.Visible : Visibility.Collapsed;
    }
}
