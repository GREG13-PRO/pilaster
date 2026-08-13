using Pilaster.App.ViewModels;
using Wpf.Ui.Controls;

namespace Pilaster.App.Views;

public partial class SettingsWindow : FluentWindow
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        DataContext = viewModel;

        // A témaváltás átúsztatása ezen az ablakon fusson, hogy a felhasználó
        // ott lássa a hatást, ahol épp állítja.
        viewModel.AnimationHost = this;

        InitializeComponent();
    }
}
