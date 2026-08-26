using System.Windows;
using Pilaster.App.ViewModels;
using Wpf.Ui.Controls;

namespace Pilaster.App.Views;

/// <summary>
/// A „Felhő meghajtó hozzáadása" modális ablak (spec: NextCloud-támogatás) —
/// a Felhő meghajtók szekció fejlécének jobbklikk-menüjéből nyílik.
/// </summary>
public partial class AddCloudDriveWindow : FluentWindow
{
    public AddCloudDriveWindow(AddCloudDriveViewModel viewModel)
    {
        DataContext = viewModel;

        InitializeComponent();

        viewModel.Connected += () => Dispatcher.Invoke(Close);
    }

    private async void OnConnectClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is AddCloudDriveViewModel viewModel)
        {
            await viewModel.ConnectAsync(PasswordInput.Password);
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();
}
