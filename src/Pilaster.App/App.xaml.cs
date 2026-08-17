using System.Diagnostics;
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

    /// <summary>
    /// A folyamat teljes élettartama alatt gyűjti a WPF kötési hibákat — lásd
    /// <see cref="BindingErrorTraceListener"/>. Az öntesztek (és igény esetén
    /// egy jövőbeli diagnosztikai nézet) innen olvassák ki.
    /// </summary>
    internal static BindingErrorTraceListener? BindingErrorListener { get; private set; }

    public App()
    {
        BindingErrorListener = BindingErrorTraceListener.Install();
    }

    /// <summary>
    /// A folyamat alkalmazás-azonosítója a Windows shell felé.
    /// </summary>
    /// <remarks>
    /// Enélkül a tálca a folyamatot a futtatható fájl útvonala alapján
    /// csoportosítja, a tálcára rögzítés pedig egy shell által generált,
    /// gyakran ELTÉRŐ (a gyorsítótárból vett, régi vagy általános) ikont
    /// mutat — ez okozta a „rossz ikon a tálcán" hibát. Ugyanennek az
    /// azonosítónak kell szerepelnie a Start menü és az asztali parancsikon
    /// <c>System.AppUserModel.ID</c> tulajdonságában is (lásd installer/Pilaster.iss).
    /// </remarks>
    public const string AppUserModelId = "Obsidix.Pilaster";

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SetCurrentProcessExplicitAppUserModelID(
        [MarshalAs(UnmanagedType.LPWStr)] string appId);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // MINDEN ablak létrehozása előtt kell megtörténnie: az azonosítót a
        // shell az első ablak megjelenésekor rögzíti a folyamathoz, később
        // beállítva már nem hat.
        TrySetAppUserModelId();

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
        services.AddSingleton<ThemeTokenService>();
        services.AddSingleton<AccentColorService>();
        services.AddSingleton<AnimationService>();
        services.AddSingleton<ShellIntegrationCoordinator>();
        services.AddSingleton<Services.FileOperations.FileOperationEngine>();
        services.AddSingleton<GlassEffectService>();
        services.AddSingleton<QuickActionService>();
        services.AddSingleton<QuickAccessService>();
        services.AddSingleton<ShellCrashGuard>();
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

        // Pilaster Classic billentyűkiosztás: F5/F6 megerősítő párbeszéd és
        // F3 előnézet-ablak — mindkettő minden megnyitáskor friss példány.
        services.AddTransient<TransferConfirmWindow>();
        services.AddTransient<FilePreviewWindow>();

        // A beépített szerkesztő EGYETLEN példány: a fülei így élik túl az
        // ablak bezárását-újranyitását, és egy fájl sosem nyílik meg kétszer.
        services.AddSingleton<EditorViewModel>();
        services.AddSingleton<EditorWindow>();

        // A gyorselérés-szerkesztő minden megnyitáskor friss másolatokon dolgozik.
        services.AddTransient<QuickAccessEditorViewModel>();
        services.AddTransient<QuickAccessEditorWindow>();

        _services = services.BuildServiceProvider();

        var settings = _services.GetRequiredService<ISettingsService>();

        // A beragadt shell-jelző felismerése MÉG az első menü előtt (spec P3).
        _services.GetRequiredService<ShellCrashGuard>().CheckOnStartup();

        ApplyStartupCulture(settings.Current);
        _services.GetRequiredService<ThemeService>().ApplyInitial();

        // A sorrend számít: a téma-tokenek adják az alapkészletet, az
        // akcentus-szolgáltatás pedig FELÜLÍRJA belőlük az akcentus-eredetűeket
        // (lásd ThemeTokenService/AccentColorService). Mindkettő feliratkozik a
        // témaváltásra, és a feliratkozás sorrendje ugyanezt tartja meg.
        _services.GetRequiredService<ThemeTokenService>().ApplyInitial();
        _services.GetRequiredService<AccentColorService>().ApplyInitial();
        _services.GetRequiredService<AnimationService>().ApplyInitial();
        _services.GetRequiredService<GlassEffectService>().ApplyInitial();

        var shellIntegration = _services.GetRequiredService<ShellIntegrationCoordinator>();
        shellIntegration.ApplyInitial();

        ShellIconImage.Initialize(_services.GetRequiredService<IShellImageService>());

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // A dispatcher-horog CSAK a UI szálat fedi. Egy háttérszálról vagy egy
        // eldobott taskból elszabaduló kivétel megkerülné, és némán vinné a
        // folyamatot — ezért kell a másik kettő is. A részletes, soronként
        // lemezre író napló a Debug naplószinthez kötött.
        CrashDiagnostics.Install(
            verbose: string.Equals(settings.Current.LogLevel, "Debug", StringComparison.OrdinalIgnoreCase));

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

        // Diagnosztikai önteszt: a shell-munkamenet után KIKÉNYSZERÍTETT
        // véglegesítés. Külön FOLYAMATBAN fut, mert ha apartment-kötött
        // COM-objektum jut el a véglegesítő szálig, az heap-korrupcióval viszi
        // a folyamatot — azt nem lehet kivétellel elkapni, csak kilépési
        // kóddal megfogni. A tesztkészlet ezt indítja (ShellFinalizerTests).
        if (Environment.GetEnvironmentVariable("PILASTER_SELFTEST_FINALIZER") == "1")
        {
            _ = RunFinalizerSelfCheckAsync();
            return;
        }

        // Diagnosztikai önteszt: sorra megnyitja az összes ablakot/dialógust,
        // és kilépési kóddal jelzi, ha közben WPF kötési hiba történt (spec
        // v1.0.1, 3. kör). A tesztkészlet ezt indítja (BindingErrorTests).
        if (Environment.GetEnvironmentVariable("PILASTER_SELFTEST_BINDINGS") == "1")
        {
            RunBindingCheckSelfTest(mainWindow);
            return;
        }

        // Csendes, nem blokkoló frissítés-ellenőrzés induláskor: a hidegindítás
        // idejét nem szabad terhelnie, ezért az ablak megjelenítése UTÁN, meg
        // sem várva indul — hálózati hiba vagy naprakész állapot esetén nem
        // jelenik meg semmi, csak elérhető frissítésnél (lásd UpdateViewModel).
        _ = _services.GetRequiredService<UpdateViewModel>().CheckSilentlyAsync();

        // A shell COM-gépezetének előmelegítése (spec K3). MÉRVE: enélkül az
        // első jobbklikk 2186 ms, vele 1132 ms — a különbség a COM apartment
        // indulása és a bővítmény-DLL-ek betöltése, ami EGYSZERI költség.
        // Alacsony prioritású háttérszálon fut, tehát az indulást nem lassítja.
        // Összeomlás után kimarad, hogy ne ismételjük meg ugyanazt a hibát.
        var crashGuard = _services.GetRequiredService<ShellCrashGuard>();

        if (!crashGuard.CrashDetected && settings.Current.ShellExtensionsEnabled)
        {
            // Az előmelegítés is jelzőt ír: ugyanazokat a bővítményeket tölti
            // be, mint egy valódi menü, tehát ugyanúgy el is tudja vinni a
            // folyamatot — enélkül az indíthatatlanná válna.
            Pilaster.Shell.Menus.ShellMenuSession.WarmUp(crashGuard.MarkInflight, crashGuard.Clear);

            // T1 diagnosztika: a bővítmények EGYENKÉNTI ideje. Csak Debug
            // naplószinten fut (Beállítások → Speciális → Naplózás szintje),
            // mert minden kezelőt betölt, és ez másodpercekig tart.
            if (string.Equals(settings.Current.LogLevel, "Debug", StringComparison.OrdinalIgnoreCase))
            {
                ReportSlowShellHandlers();
            }
        }
    }

    /// <summary>
    /// Shell-munkamenet + KIKÉNYSZERÍTETT véglegesítés. Sikeres lefutásnál a
    /// folyamat 0-val lép ki; ha apartment-kötött COM-objektum jut el a
    /// véglegesítő szálig, a folyamat <c>0xC0000374</c>-gyel meghal.
    /// </summary>
    /// <remarks>
    /// Ez a diagnosztika SZÁNDÉKOSAN benne marad a kiadott kódban: környezeti
    /// változó nélkül soha nem fut, viszont így a tesztkészlet bármikor
    /// ellenőrizheti a folyamat-határon át, hogy nem tért-e vissza a hiba.
    /// A történetét lásd a <c>ShellMenuSession.CreateForItems</c> sorrend-
    /// táblázatában.
    /// </remarks>
    private async Task RunFinalizerSelfCheckAsync()
    {
        var exitCode = 0;

        try
        {
            var file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe");
            var folder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            for (var round = 0; round < 3; round++)
            {
                var items = await Pilaster.Shell.Menus.ShellMenuSession.QueryItemsAsync(
                    [file], false, TimeSpan.FromSeconds(30), []);

                items?.Dispose();

                var background = await Pilaster.Shell.Menus.ShellMenuSession.QueryBackgroundAsync(
                    folder, false, TimeSpan.FromSeconds(30), []);

                background?.Dispose();

                // A Dispose a shell STA szálára POSTÁZZA a takarítást, ezért
                // meg kell várni, mielőtt véglegesítést kényszerítünk.
                await Task.Delay(1500);

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }

            Log.Information("FINALIZER-ONTESZT rendben");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "FINALIZER-ONTESZT kivétellel bukott");
            exitCode = 2;
        }

        Log.CloseAndFlush();
        Environment.Exit(exitCode);
    }

    /// <summary>
    /// Diagnosztikai önteszt: sorra megnyitja az összes ablakot/dialógust, és
    /// a <see cref="BindingErrorTraceListener"/> által eközben begyűjtött
    /// kötési hibákat egy fájlba írja — a kilépési kód 0, ha egy sem volt
    /// (spec v1.0.1, 3. kör).
    /// </summary>
    /// <remarks>
    /// SZÁNDÉKOSAN benne marad a kiadott kódban, ugyanazon okból, mint a
    /// <see cref="RunFinalizerSelfCheckAsync"/>: környezeti változó nélkül
    /// soha nem fut, de a tesztkészlet bármikor elindíthatja a folyamat-
    /// határon át (lásd BindingErrorTests).
    /// </remarks>
    private void RunBindingCheckSelfTest(MainWindow mainWindow)
    {
        var exitCode = 0;

        // MÉRVE: a `Path.GetTempPath()` a tesztkészletből indított
        // (Process.Start-tal létrehozott) gyermekfolyamatban MÁS útvonalra
        // oldódott fel, mint a hívó tesztfolyamatban — feltehetően a VSTest
        // saját, futásonkénti ideiglenes-mappa izolációja miatt. A naplófájl
        // ugyanígy rögzített, `LocalApplicationData`-alapú helyre ír, és az
        // MINDIG megtalálható volt — ezért ez a kimenet is oda kerül.
        var outputPath = Path.Combine(LogFileLocator.LogDirectory, "pilaster-binding-errors.txt");

        // ŐRSZEM, FÜGGETLEN szálon: a `BindingCheckRunner.Run` a Dispatcheren
        // szinkron pumpál, tehát ha egy ablak véletlenül soha nem záródna be,
        // a hívó (UI) szál is örökre benne ragadna — semmilyen Dispatcheren
        // ütemezett időzítő nem futhatna le a MEGMENTÉSÉRE. Ez a szál viszont
        // teljesen a Dispatchertől függetlenül számol vissza. A 3 perces
        // határ bőven a közvetlen futtatásnál mért ~10-25 mp fölött van —
        // egyes környezetekben (pl. szoftveres renderelés) a teljes séta
        // ennél lassabban fut, de véget ér.
        var watchdog = new Thread(() =>
        {
            Thread.Sleep(TimeSpan.FromMinutes(3));

            try
            {
                File.WriteAllText(outputPath, "Az önteszt 3 percen belül sem fejeződött be — egy ablak feltehetően nem záródott be.");
            }
            catch
            {
                // Az őrszem célja a kilépés, nem a napló — ha épp ez sem
                // sikerül, nem számít.
            }

            Environment.Exit(3);
        })
        {
            IsBackground = true,
            Name = "Pilaster.BindingCheckWatchdog",
        };

        watchdog.Start();

        try
        {
            var errors = BindingCheckRunner.Run(_services!, mainWindow);
            File.WriteAllLines(outputPath, errors);

            if (errors.Count > 0)
            {
                Log.Warning("KÖTÉSHIBA-ÖNTESZT {Count} hibát talált", errors.Count);
                exitCode = 1;
            }
            else
            {
                Log.Information("KÖTÉSHIBA-ÖNTESZT rendben");
            }
        }
        catch (Exception ex)
        {
            File.WriteAllText(outputPath, $"Az önteszt kivétellel bukott: {ex}");
            Log.Error(ex, "KÖTÉSHIBA-ÖNTESZT kivétellel bukott");
            exitCode = 2;
        }

        Log.CloseAndFlush();
        Environment.Exit(exitCode);
    }

    /// <summary>
    /// A lassú shell-bővítmények megnevezése a naplóban (spec T1).
    /// </summary>
    /// <remarks>
    /// SZÁNDÉKOSAN csak JAVASOL, nem tilt le semmit: egy kezelő letiltása
    /// funkciót vesz el (a Nextcloud menüje például valódi munkafolyamat), ezt
    /// pedig nem dönthetjük el a felhasználó helyett. A napló megnevezi a
    /// vétkest, a döntés a Beállítások → Jobbklikk-menü → feketelistáé.
    /// </remarks>
    private static void ReportSlowShellHandlers()
    {
        var thread = new Thread(() =>
        {
            try
            {
                var target = Environment.ProcessPath;

                if (string.IsNullOrEmpty(target))
                {
                    return;
                }

                var timings = Pilaster.Shell.Menus.ShellHandlerProbe.Measure(target);

                foreach (var timing in timings.Take(5))
                {
                    Log.Debug(
                        "Shell-bővítmény {Name} ({Dll}, {Clsid}) — létrehozás {CreateMs} ms, lekérdezés {QueryMs} ms, összesen {TotalMs} ms",
                        timing.DisplayName.Length == 0 ? "(névtelen)" : timing.DisplayName,
                        Path.GetFileName(timing.ModulePath),
                        timing.Clsid,
                        timing.CreateMs,
                        timing.QueryMs,
                        timing.TotalMs);
                }

                foreach (var slow in timings.Where(t => t.TotalMs > Pilaster.Shell.Menus.ShellHandlerProbe.SlowThresholdMs))
                {
                    Log.Warning(
                        "LASSÚ shell-bővítmény: {Name} ({Dll}) {TotalMs} ms — ez egymaga késlelteti a jobbklikk-menüt. "
                        + "Ha nincs rá szükséged, a Beállítások → Jobbklikk-menü → Kikapcsolt bővítmények mezőbe felvéve kihagyható.",
                        slow.DisplayName.Length == 0 ? slow.Clsid.ToString() : slow.DisplayName,
                        Path.GetFileName(slow.ModulePath),
                        slow.TotalMs);
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "A shell-bővítmények mérése nem sikerült");
            }
        })
        {
            IsBackground = true,
            Priority = ThreadPriority.Lowest,
            Name = "Pilaster.HandlerReport",
        };

        thread.Start();
    }

    /// <summary>
    /// Az <see cref="AppUserModelId"/> beállítása. Hiba esetén csendben
    /// továbbmegy: egy hibás ikon-csoportosítás kellemetlen, de az indulást
    /// megakasztani miatta aránytalan lenne.
    /// </summary>
    private static void TrySetAppUserModelId()
    {
        try
        {
            SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
        }
        catch (Exception ex) when (ex is COMException or EntryPointNotFoundException or DllNotFoundException)
        {
            // Régebbi/csonkolt shell — marad az alapértelmezett csoportosítás.
        }
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
        // SZABÁLYOS kilépés — tehát ami épp „folyamatban" volt, az nem
        // összeomlás. A jelző törlése nélkül egy gyors kilépés (a shell
        // lekérdezése ilyenkor még futhat) hamis összeomlásnak látszana, és a
        // következő indulás fölöslegesen kapcsolná ki a bővítményeket.
        _services?.GetService<ShellCrashGuard>()?.BeginShutdown();

        // A késleltetett mentés még sorban állhat, ezért kilépés előtt kiírjuk.
        _services?.GetService<ISettingsService>()?.Flush();
        _services?.GetService<QuickAccessService>()?.Flush();
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
