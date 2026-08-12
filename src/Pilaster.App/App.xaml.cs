using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Pilaster.App.Controls;
using Pilaster.App.Localization;
using Pilaster.App.ViewModels;
using Pilaster.App.Views;
using Pilaster.Core.FileSystem;
using Pilaster.Providers.Local;
using Pilaster.Shell.Imaging;
using Wpf.Ui.Appearance;

namespace Pilaster.App;

public partial class App : Application
{
    /// <summary>Azok a nyelvek, amikhez fordítás van szállítva.</summary>
    private static readonly string[] SupportedLanguages = ["hu", "en"];

    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // A hidegindítás a fájlkezelő legfontosabb metrikája, ezért itt
        // szándékosan nem a Generic Host indul: annak konfiguráció-, napló- és
        // környezetbetöltése önmagában több száz ezredmásodperc lenne. Egy sima
        // szolgáltatásgyűjtemény ugyanazt a DI-t adja, észlelhető költség nélkül.
        var services = new ServiceCollection();

        services.AddSingleton<IFileSystemProvider, LocalFileSystemProvider>();
        services.AddSingleton<IShellImageService, ShellImageService>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();

        _services = services.BuildServiceProvider();

        ApplyStartupCulture();

        // A téma kövesse a Windows beállítását, és váltson vele együtt.
        ApplicationThemeManager.ApplySystemTheme();

        ShellIconImage.Initialize(_services.GetRequiredService<IShellImageService>());

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        _services.GetRequiredService<MainWindow>().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// Az induló nyelv megválasztása.
    /// </summary>
    /// <remarks>
    /// Ha a rendszer nyelvéhez van fordításunk, azt használjuk — ez a
    /// legkevésbé meglepő viselkedés. Egyébként magyarra esünk vissza, mert az
    /// a projekt elsődleges nyelve.
    /// </remarks>
    private static void ApplyStartupCulture()
    {
        var system = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

        var chosen = SupportedLanguages.Contains(system, StringComparer.OrdinalIgnoreCase)
            ? system
            : "hu";

        TranslationSource.Instance.SetLanguage(chosen);
    }

    /// <summary>
    /// Utolsó védvonal: egy nem kezelt kivétel ne zárja be némán az ablakot a
    /// felhasználó alól, hanem legyen látható és folytatható.
    /// </summary>
    private void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            e.Exception.ToString(),
            "Pilaster",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
    }
}
