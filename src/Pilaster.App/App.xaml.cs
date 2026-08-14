using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Pilaster.App.Controls;
using Pilaster.App.Diagnostics;
using Pilaster.App.Localization;
using Pilaster.App.Services;
using Pilaster.App.ViewModels;
using Pilaster.App.Views;
using Pilaster.Core.FileSystem;
using Pilaster.Core.Settings;
using Pilaster.Providers.Local;
using Pilaster.Shell.Imaging;
using Serilog;

namespace Pilaster.App;

public partial class App : Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ConfigureLogging();

        // A hidegindítás a fájlkezelő legfontosabb metrikája, ezért itt
        // szándékosan nem a Generic Host indul: annak konfiguráció-, napló- és
        // környezetbetöltése önmagában több száz ezredmásodperc lenne. Egy sima
        // szolgáltatásgyűjtemény ugyanazt a DI-t adja, észlelhető költség nélkül.
        var services = new ServiceCollection();

        services.AddSingleton<IFileSystemProvider, LocalFileSystemProvider>();
        services.AddSingleton<IShellImageService, ShellImageService>();
        services.AddSingleton<ISettingsService, JsonSettingsService>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<AccentColorService>();
        services.AddSingleton<AnimationService>();
        services.AddSingleton<ShellIntegrationCoordinator>();
        services.AddSingleton<Services.FileOperations.FileOperationEngine>();
        services.AddSingleton<GlassEffectService>();
        services.AddSingleton<QuickActionService>();
        services.AddSingleton<FolderSizeService>();
        services.AddSingleton<FileMetadataService>();
        services.AddSingleton<FilePreviewService>();
        services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(15) });
        services.AddSingleton<IBugReportService, DiscordBugReportService>();
        services.AddSingleton<IUpdateService, GitHubUpdateService>();
        services.AddSingleton<UpdateViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();

        // A Beállítások átmeneti: minden megnyitáskor a friss modellre épül.
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<SettingsWindow>();

        // A Lomtár tartalma is friss betöltést kap minden megnyitáskor.
        services.AddTransient<RecycleBinViewModel>();
        services.AddTransient<RecycleBinWindow>();

        // Total Commander-billentyűkiosztás: F5/F6 megerősítő párbeszéd és
        // F3 előnézet-ablak — mindkettő minden megnyitáskor friss példány.
        services.AddTransient<TransferConfirmWindow>();
        services.AddTransient<FilePreviewWindow>();

        _services = services.BuildServiceProvider();

        var settings = _services.GetRequiredService<ISettingsService>();

        ApplyStartupCulture(settings.Current);
        _services.GetRequiredService<ThemeService>().ApplyInitial();
        _services.GetRequiredService<AccentColorService>().ApplyInitial();
        _services.GetRequiredService<AnimationService>().ApplyInitial();
        _services.GetRequiredService<GlassEffectService>().ApplyInitial();

        var shellIntegration = _services.GetRequiredService<ShellIntegrationCoordinator>();
        shellIntegration.ApplyInitial();

        ShellIconImage.Initialize(_services.GetRequiredService<IShellImageService>());

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        Log.Information(
            "Pilaster {Version} indul ({Os}, {Runtime})",
            AppVersionInfo.Current,
            RuntimeInformation.OSDescription,
            RuntimeInformation.FrameworkDescription);

        var mainWindow = _services.GetRequiredService<MainWindow>();

        // A Win+E hook (amíg a felhasználó bekapcsolta) így hozza előtérbe az
        // ablakot — lásd ShellIntegrationCoordinator/WinEHookService. Csak
        // addig működik, amíg ez a folyamat fut.
        shellIntegration.ActivationRequested += (_, _) => ActivateMainWindow(mainWindow);

        mainWindow.Show();

        // Más programok (jobbklikk-menü, parancssor) egy mappa útvonalával
        // hívhatják meg az appot — lásd a "Mappák megnyitása ebben az appban"
        // rendszerintegrációs kapcsolót. args[0] a saját exe útvonala, a
        // tényleges paraméter az [1]-től kezdődik.
        var args = Environment.GetCommandLineArgs();

        if (args.Length > 1 && Directory.Exists(args[1]))
        {
            var vm = _services.GetRequiredService<MainWindowViewModel>();

            if (vm.SelectedTab is { } tab)
            {
                _ = tab.NavigateCommand.ExecuteAsync(args[1]);
            }
        }

        // Csendes, nem blokkoló frissítés-ellenőrzés induláskor: a hidegindítás
        // idejét nem szabad terhelnie, ezért az ablak megjelenítése UTÁN, meg
        // sem várva indul — hálózati hiba vagy naprakész állapot esetén nem
        // jelenik meg semmi, csak elérhető frissítésnél (lásd UpdateViewModel).
        _ = _services.GetRequiredService<UpdateViewModel>().CheckSilentlyAsync();
    }

    /// <summary>
    /// Az ablak előtérbe hozása háttérből — a sima <c>Activate()</c> a
    /// Windows „előtér-zár" (foreground lock) védelme miatt nem mindig elég,
    /// ha épp egy másik alkalmazásé a fókusz. A <c>Topmost</c> rövid
    /// felvillantása megkerüli ezt, anélkül hogy tartósan legfelül maradna.
    /// </summary>
    private static void ActivateMainWindow(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Show();
        window.Topmost = true;
        window.Topmost = false;
        window.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // A késleltetett mentés még sorban állhat, ezért kilépés előtt kiírjuk.
        _services?.GetService<ISettingsService>()?.Flush();
        _services?.Dispose();

        Log.CloseAndFlush();

        base.OnExit(e);
    }

    /// <summary>
    /// A naplózás beállítása.
    /// </summary>
    /// <remarks>
    /// A napló egyetlen célja jelenleg a hibabejelentés csatolmánya: ha a
    /// felhasználó bekapcsolja a „Naplófájl csatolása" opciót, ez adja a
    /// kontextust a fejlesztőknek. Ezért elég az Information szint és a heti
    /// megőrzés — nem diagnosztikai teljes körű naplózás.
    /// </remarks>
    private static void ConfigureLogging()
    {
        Directory.CreateDirectory(LogFileLocator.LogDirectory);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(LogFileLocator.LogDirectory, "pilaster-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }

    /// <summary>
    /// Az induló nyelv: a mentett választás, vagy annak hiányában a rendszernyelv.
    /// </summary>
    /// <remarks>
    /// A mentett <c>null</c> nem hiányzó adat, hanem tudatos választás: azt
    /// jelenti, hogy a felhasználó a rendszernyelvet akarja követni, tehát a
    /// Windows nyelvének későbbi átállítását is követnie kell.
    /// </remarks>
    private static void ApplyStartupCulture(AppSettings settings) =>
        TranslationSource.Instance.SetLanguage(
            settings.Language ?? TranslationSource.ResolveSystemLanguage());

    /// <summary>
    /// Utolsó védvonal: egy nem kezelt kivétel ne zárja be némán az ablakot a
    /// felhasználó alól, hanem legyen látható és folytatható.
    /// </summary>
    private void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Kezeletlen kivétel");

        MessageBox.Show(
            e.Exception.ToString(),
            "Pilaster",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
    }
}
