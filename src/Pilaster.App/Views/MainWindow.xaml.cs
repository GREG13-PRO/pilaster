using System.Diagnostics;
using System.Windows;
using System.Windows.Controls.Primitives;
using Pilaster.App.Controls;
using Pilaster.App.ViewModels;
using Pilaster.Core.FileSystem;
using Wpf.Ui.Controls;

// A WPF-UI saját ListView/ListBox típusokat is szállít ugyanezekkel a nevekkel.
// A XAML a WPF beépített vezérlőit példányosítja, ezért a kódban is azokra
// hivatkozunk — az álnév egyértelműsíti, melyikről van szó.
using GridViewColumnHeader = System.Windows.Controls.GridViewColumnHeader;
using ListBox = System.Windows.Controls.ListBox;
using ListView = System.Windows.Controls.ListView;
using SelectionChangedEventArgs = System.Windows.Controls.SelectionChangedEventArgs;

namespace Pilaster.App.Views;

public partial class MainWindow : FluentWindow
{
    private readonly MainWindowViewModel _viewModel;

    /// <summary>Az az oszlopfejléc, amelyik jelenleg nyilat mutat.</summary>
    private GridViewColumnHeader? _sortedHeader;

    public MainWindow(MainWindowViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;

        InitializeComponent();
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
        if (e.OriginalSource is not GridViewColumnHeader { Column: { } column } header)
        {
            return;
        }

        if (_viewModel.SelectedTab is not { } tab)
        {
            return;
        }

        var key = GridViewSort.GetSortKey(column);
        var descending = key == tab.SortKey && !tab.SortDescending;

        tab.ApplySort(key, descending);

        // Egyszerre csak egy oszlop mutathat nyilat.
        if (_sortedHeader is not null && !ReferenceEquals(_sortedHeader, header))
        {
            GridViewSort.SetIndicator(_sortedHeader, SortIndicator.None);
        }

        GridViewSort.SetIndicator(
            header,
            descending ? SortIndicator.Descending : SortIndicator.Ascending);

        _sortedHeader = header;
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
