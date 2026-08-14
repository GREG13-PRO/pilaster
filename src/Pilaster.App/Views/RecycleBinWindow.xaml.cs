using System.Windows;
using Pilaster.App.Localization;
using Pilaster.App.ViewModels;
using Wpf.Ui.Controls;

namespace Pilaster.App.Views;

public partial class RecycleBinWindow : FluentWindow
{
    public RecycleBinWindow(RecycleBinViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }

    /// <summary>
    /// Végleges törlés csak megerősítés után — natív MessageBox, ugyanaz a
    /// minta, mint a frissítés-újraindítás megerősítésénél (lásd
    /// MainWindow.OnUpdateRestartRequested).
    /// </summary>
    private void OnDeleteItemClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not RecycledItemViewModel item
            || DataContext is not RecycleBinViewModel viewModel)
        {
            return;
        }

        var strings = TranslationSource.Instance;

        var result = System.Windows.MessageBox.Show(
            string.Format(strings["RecycleBin_ConfirmDelete"], item.Name),
            strings["RecycleBin_Title"],
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        viewModel.DeleteCommand.Execute(item);
    }

    private void OnEmptyClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not RecycleBinViewModel viewModel)
        {
            return;
        }

        var strings = TranslationSource.Instance;

        var result = System.Windows.MessageBox.Show(
            strings["RecycleBin_ConfirmEmpty"],
            strings["RecycleBin_Title"],
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        viewModel.EmptyCommand.Execute(null);
    }
}
