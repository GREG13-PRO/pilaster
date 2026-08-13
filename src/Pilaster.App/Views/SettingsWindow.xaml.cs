using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Input;
using System.Windows.Navigation;
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

    /// <summary>Lásd BugReportViewModel.RegisterSecretClick: 10 kattintásra felnyílik a fejlesztői panel.</summary>
    private void OnBugReportHeaderClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel)
        {
            viewModel.BugReport.RegisterSecretClick();
        }
    }

    private void OnSupportEmailNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Win32Exception)
        {
            // Nincs alapértelmezett levelezőprogram beállítva — nincs jobb teendő.
        }

        e.Handled = true;
    }
}
