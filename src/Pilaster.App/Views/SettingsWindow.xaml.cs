using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Navigation;
using Pilaster.App.Diagnostics;
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

        viewModel.NavigateToSettingRequested += OnNavigateToSettingRequested;

        InitializeComponent();
    }

    /// <summary>A legutóbbi naplófájl megnyitása a társított programmal.</summary>
    private void OnOpenLogClick(object sender, RoutedEventArgs e)
    {
        var latest = Directory.Exists(LogFileLocator.LogDirectory)
            ? new DirectoryInfo(LogFileLocator.LogDirectory)
                .GetFiles("*.log")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault()
            : null;

        OpenWithShell(latest?.FullName ?? LogFileLocator.LogDirectory);
    }

    /// <summary>A konfigurációs mappa megnyitása (settings.json, metadata.json, quickaccess.json).</summary>
    private void OnOpenConfigFolderClick(object sender, RoutedEventArgs e) =>
        OpenWithShell(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pilaster"));

    private static void OpenWithShell(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            // Nincs társított program, vagy a mappa nem létezik — mindkettő
            // ártalmatlan; egy hibaüzenet itt aránytalan lenne.
        }
    }

    /// <summary>
    /// Mélyhivatkozás: a megadott azonosítójú beállításhoz görget, és rövid
    /// felvillantással kiemeli (spec F6).
    /// </summary>
    private void OnNavigateToSettingRequested(object? sender, string settingId)
    {
        // Background prioritás: a kategóriaváltás láthatóság-változásai csak a
        // következő elrendezési körben érvényesülnek, addig a célvezérlő
        // mérete nulla lenne, és a görgetés rossz helyre vinne.
        _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
        {
            if (FindByTag(ContentScroll, settingId) is not { } target)
            {
                return;
            }

            target.BringIntoView();
            Flash(target);
        });
    }

    private static FrameworkElement? FindByTag(DependencyObject root, string tag)
    {
        if (root is FrameworkElement { Tag: string value } element && value == tag)
        {
            return element;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            if (FindByTag(VisualTreeHelper.GetChild(root, i), tag) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>Rövid felvillantás — csak vizuális, semmilyen állapotot nem módosít.</summary>
    private static void Flash(FrameworkElement element)
    {
        var animation = new DoubleAnimation(1.0, 0.35, TimeSpan.FromMilliseconds(280))
        {
            AutoReverse = true,
            RepeatBehavior = new RepeatBehavior(2),
        };

        // Az animáció leválasztása után az Opacity újra szabadon állítható,
        // különben a rögzített érték „beragadna".
        animation.Completed += (_, _) =>
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = 1;
        };

        element.BeginAnimation(UIElement.OpacityProperty, animation);
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
