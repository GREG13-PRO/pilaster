using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;
using Pilaster.App.Localization;
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

    /// <summary>Lásd SettingsViewModel.RegisterVersionClick: 7 kattintásra rejtett üzenet bukkan fel.</summary>
    private void OnVersionClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel)
        {
            viewModel.RegisterVersionClick();
        }
    }

    /// <summary>
    /// A Rendszerintegráció kapcsolói szándékosan <c>Mode=OneWay</c>
    /// kötésűek: a kattintást ITT, előre elkapjuk (<c>e.Handled = true</c>
    /// minden ágon), hogy a vezérlő saját belső állapota SOHA ne térjen el a
    /// ViewModelben ténylegesen érvényesült állapottól — bekapcsolás előtt
    /// jóváhagyó párbeszéddel, sikertelen registry-művelet esetén pedig a
    /// ViewModel saját maga állítja vissza (lásd
    /// SettingsViewModel.OnFolderOpenRedirectEnabledChanged).
    /// </summary>
    private void OnFolderOpenRedirectPreviewClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;

        if (DataContext is not SettingsViewModel viewModel)
        {
            return;
        }

        var turningOn = !viewModel.FolderOpenRedirectEnabled;

        if (turningOn && !ConfirmEnable(TranslationSource.Instance["ShellIntegration_ConfirmFolderOpen"]))
        {
            return;
        }

        viewModel.FolderOpenRedirectEnabled = turningOn;
    }

    private void OnWinERedirectPreviewClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;

        if (DataContext is not SettingsViewModel viewModel)
        {
            return;
        }

        var turningOn = !viewModel.WinERedirectEnabled;

        if (turningOn && !ConfirmEnable(TranslationSource.Instance["ShellIntegration_ConfirmWinE"]))
        {
            return;
        }

        viewModel.WinERedirectEnabled = turningOn;
    }

    private void OnContextMenuEntryPreviewClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;

        if (DataContext is not SettingsViewModel viewModel)
        {
            return;
        }

        var turningOn = !viewModel.ContextMenuEntryEnabled;

        if (turningOn && !ConfirmEnable(TranslationSource.Instance["ShellIntegration_ConfirmContextMenu"]))
        {
            return;
        }

        viewModel.ContextMenuEntryEnabled = turningOn;
    }

    private static bool ConfirmEnable(string message) =>
        System.Windows.MessageBox.Show(
            message,
            TranslationSource.Instance["ShellIntegration_ConfirmTitle"],
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning) == System.Windows.MessageBoxResult.Yes;

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
