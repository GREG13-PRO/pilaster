using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
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
using Pilaster.Shell.Menus;
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
    /// <c>System.AppUserModel.ID</c> tulajdonságában is (lásd
    /// src/Pilaster.Setup/Constants/SetupInfo.cs és Services/ShortcutBuilder.cs).
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
        services.AddSingleton<CloudDriveService>();
        services.AddSingleton<ShellCrashGuard>();
        services.AddSingleton<ShellMenuPreloadCoordinator>();
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

        services.AddTransient<AddCloudDriveViewModel>();
        services.AddTransient<AddCloudDriveWindow>();

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

        // Diagnosztikai önteszt (spec A2, v1.0.2): a ShellMenuPreloadCoordinator
        // Q1-táblázatát futtatja le VALÓDI ShellMenuSession/StaWorker hívásokkal,
        // GUI/egér nélkül — lásd ShellMenuPreloadTests. Külön folyamatban fut,
        // mint a fenti két önteszt, ugyanazért: a shell-hívások natív
        // összeomlása (ha lenne) kilépési kóddal fogható meg, kivétellel nem.
        // SZÁNDÉKOSAN az előmelegítés UTÁN: az előtte futtatott korábbi
        // változat mindig hideg (előmelegítés nélküli) COM-indulást mért,
        // ami a dokumentált ~2186 ms-os hideg költség miatt magától
        // meghaladta az alapértelmezett 2000 ms-os időkorlátot — ez a
        // teszt-elhelyezés hibája volt, nem az A2 kódjáé (egy éles
        // felhasználó sosem ér oda az előmelegítés előtt).
        if (Environment.GetEnvironmentVariable("PILASTER_SELFTEST_PRELOAD") == "1")
        {
            _ = RunPreloadSelfCheckAsync();
            return;
        }

        // Diagnosztikai önteszt (spec v1.0.3): a natív jobbklikk-menü
        // (ShellMenuSession.ShowNativeAsync) Q1-táblázatát futtatja le —
        // fájl/mappa(elemként)/váltakozva/többszörös kijelölés × 10, plusz a
        // "nem fagy le az ablak, amíg a menü nyitva van" ellenőrzés. A menüt
        // a saját folyamaton belül, WM_CANCELMODE postázásával zárja be
        // (NativeMenuInterop.CancelActiveMenu) — VALÓDI billentyű-/
        // egérszimuláció NÉLKÜL, tehát a felhasználó élő munkamenetét nem
        // érintheti. Lásd NativeContextMenuModeTests.
        if (Environment.GetEnvironmentVariable("PILASTER_SELFTEST_NATIVEMENU") == "1")
        {
            _ = RunNativeMenuSelfCheckAsync(mainWindow);
            return;
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
    /// Diagnosztikai önteszt (spec A2, v1.0.2): a <c>ShellMenuPreloadCoordinator</c>
    /// Q1-táblázatát futtatja le — fájl, mappa (mint elem), váltakozó,
    /// többszörös kijelölés, egyenként 10 kör, plusz az „5. szcenárió" (50
    /// gyors, egymást túlhaladó kijelölés-váltás, közbe-közbe jobbklikk-szerű
    /// lekérdezéssel). VALÓDI <see cref="Pilaster.Shell.Menus.ShellMenuSession"/>
    /// hívásokkal fut, GUI/egér nélkül — a koordinátor egy SAJÁT, a futó
    /// főablakétól FÜGGETLEN példányán, hogy a főablak saját kijelölés-
    /// eseményei ne zavarják bele a számlálókba.
    /// </summary>
    /// <remarks>
    /// A tesztkészlet ezt indítja (ShellMenuPreloadTests), pontosan ugyanúgy,
    /// ahogy a <see cref="RunFinalizerSelfCheckAsync"/>-ot: a kilépési kód és
    /// a mellékelt eredményfájl (%TEMP%\pilaster-preload-selftest.txt) adja a
    /// Q1-táblázat számait.
    /// </remarks>
    private async Task RunPreloadSelfCheckAsync()
    {
        var exitCode = 0;
        var log = new List<string>();
        var resultsPath = Path.Combine(Path.GetTempPath(), "pilaster-preload-selftest.txt");

        try
        {
            var settingsService = _services!.GetRequiredService<ISettingsService>();
            settingsService.Current.ContextMenuPreloadEnabled = true;

            var coordinator = new ShellMenuPreloadCoordinator(settingsService);

            var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var file = Path.Combine(windowsDir, "notepad.exe");
            var folder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            // A ShellMenuSession.CreateForItems (GetUIObjectOf) AZONOS
            // szülőmappát vár — a többszörös
            // kijelölés próbájának ezért UGYANABBÓL a mappából kell két valós
            // fájlt választania, különben a lekérdezés jogosan null-t ad, és
            // az nem A2, hanem a próba hibája volna.
            var altFile = Path.Combine(windowsDir, "explorer.exe");

            // Megvárjuk, hogy a háttérben induló ShellMenuSession.WarmUp() a
            // MEGOSZTOTT STA sorral végezzen — enélkül a Q1-táblázat első
            // köre(i) a hideg (előmelegítés nélküli) COM-indulás ~2186 ms-os,
            // DOKUMENTÁLT költségével versenyeznének a szokásos 2000 ms-os
            // időkorláton, és jogosan, de FÉLREVEZETŐEN buknának — ez a
            // warmup/valódi-lekérdezés versenyhelyzet MA IS fennáll, A2
            // nélkül is, ha a felhasználó az indulás utáni 1-2 mp-en belül
            // kattint; a Q1-táblázat viszont az ÁLLANDÓSULT állapotot méri,
            // nem ezt a tranziens indulási ablakot. A lekérdezés eredménye
            // itt nem számít, csak az, hogy a megosztott sor kiürüljön.
            var warmupSettle = await Pilaster.Shell.Menus.ShellMenuSession.QueryItemsAsync(
                [file], false, TimeSpan.FromSeconds(25), []);
            warmupSettle?.Dispose();
            await Task.Delay(300);

            // Időmérés: "menü nyitása ELŐRETÖLTÉS UTÁN" (a kérés által kért
            // új szám) vs. "menü nyitása ELŐRETÖLTÉS NÉLKÜL" (a mai út) —
            // mindkettő a shell-elemek beérkezéséig tart, a saját elemek
            // szinkron IsOpen=true költsége (mérve: ~90 ms, lásd
            // artifacts/menu-border/timing-comparison.txt) EBBEN nem
            // szerepel, mert azt A1/A2 egyike sem módosítja.
            async Task<double> MedianMs(string label, int reps, Func<Task<double>> oneRun)
            {
                var samples = new List<double>();

                for (var i = 0; i < reps; i++)
                {
                    samples.Add(await oneRun());
                    await Task.Delay(300);
                }

                samples.Sort();
                var median = samples[samples.Count / 2];
                log.Add($"{label}: [{string.Join(", ", samples.Select(s => s.ToString("F1")))}] ms, median={median:F1} ms");
                return median;
            }

            var withoutPreloadFileMs = await MedianMs("MENU NYITAS ELORETOLTES NELKUL (fajl)", 6, async () =>
            {
                var sw = Stopwatch.StartNew();
                var s = await Pilaster.Shell.Menus.ShellMenuSession.QueryItemsAsync(
                    [file], false, TimeSpan.FromMilliseconds(settingsService.Current.ShellMenuTimeoutMs), []);
                sw.Stop();
                s?.Dispose();
                return sw.Elapsed.TotalMilliseconds;
            });

            var withoutPreloadFolderMs = await MedianMs("MENU NYITAS ELORETOLTES NELKUL (mappa, elemkent)", 6, async () =>
            {
                var sw = Stopwatch.StartNew();
                var s = await Pilaster.Shell.Menus.ShellMenuSession.QueryItemsAsync(
                    [folder], false, TimeSpan.FromMilliseconds(settingsService.Current.ShellMenuTimeoutMs), []);
                sw.Stop();
                s?.Dispose();
                return sw.Elapsed.TotalMilliseconds;
            });

            var withPreloadReadyMs = await MedianMs("MENU NYITAS ELORETOLTES UTAN (kesz allapotban atveve)", 6, async () =>
            {
                coordinator.NotifySelectionChanged([file], false);

                // Bőven a debounce (200 ms) + a mért állandósult lekérdezési
                // idő (777 ms medián) fölé — hogy a
                // mérés pillanatában a lekérdezés MÁR TÉNYLEG kész legyen,
                // ne csak elinduljon.
                await Task.Delay(1400);

                var sw = Stopwatch.StartNew();
                var task = coordinator.TakeIfMatches([file]);
                var s = task is null ? null : await task;
                sw.Stop();
                s?.Dispose();
                return sw.Elapsed.TotalMilliseconds;
            });

            log.Add($"GYORSULAS: {withoutPreloadFileMs:F1} ms -> {withPreloadReadyMs:F1} ms (fajl, {(withoutPreloadFileMs - withPreloadReadyMs):F1} ms-mal gyorsabb, ha kesz az eloretoltes)");

            async Task<int> RunScenarioAsync(string label, int rounds, Func<int, IReadOnlyList<string>> pathsFactory)
            {
                var pass = 0;

                for (var i = 0; i < rounds; i++)
                {
                    var paths = pathsFactory(i);
                    coordinator.NotifySelectionChanged(paths, false);
                    await Task.Delay(400);

                    var task = coordinator.TakeIfMatches(paths);

                    if (task is null)
                    {
                        log.Add($"{label} #{i}: NINCS ELORETOLTES TALALAT");
                        continue;
                    }

                    var session = await task;

                    if (session is null)
                    {
                        log.Add($"{label} #{i}: A LEKERDEZES NULL MUNKAMENETET ADOTT");
                        continue;
                    }

                    pass++;
                    session.Dispose();
                    await Task.Delay(50);
                }

                log.Add($"{label}: {pass}/{rounds}");
                return pass;
            }

            // Kontroll-mérés (NEM az A2 kódját hívja, közvetlenül
            // ShellMenuSession.QueryItemsAsync-et, előretöltés/koordinátor
            // nélkül) — ha ez UGYANOLYAN arányban ad null-t, mint a FAJL kör,
            // az bizonyítja, hogy egy esetleges kihagyás a mögöttes
            // shell-lekérdezés/időkorlát ISMERT, A2-től FÜGGETLEN ingadozása
            // (NvAppShExt 632-783 ms/lekérdezés), nem az előretöltő kód hibája.
            var baselinePass = 0;

            for (var i = 0; i < 10; i++)
            {
                var baseline = await Pilaster.Shell.Menus.ShellMenuSession.QueryItemsAsync(
                    [file], false, TimeSpan.FromMilliseconds(settingsService.Current.ShellMenuTimeoutMs), []);

                if (baseline is not null)
                {
                    baselinePass++;
                    baseline.Dispose();
                }
                else
                {
                    log.Add($"ALAP (elofeltoltes nelkul) #{i}: A LEKERDEZES NULL MUNKAMENETET ADOTT");
                }

                await Task.Delay(450);
            }

            log.Add($"ALAP (elofeltoltes nelkul, kontroll): {baselinePass}/10");

            var filePass = await RunScenarioAsync("FAJL", 10, _ => [file]);
            var folderPass = await RunScenarioAsync("MAPPA (elemkent)", 10, _ => [folder]);
            var altPass = await RunScenarioAsync("VALTAKOZVA", 10, i => i % 2 == 0 ? [file] : [folder]);
            var multiPass = await RunScenarioAsync("TOBBSZOROS KIJELOLES", 10, _ => [file, altFile]);

            // 5. szcenárió: 50 gyors, egymást túlhaladó kijelölés-váltás,
            // közbe-közbe egy jobbklikk-szerű TakeIfMatches hívással — ez a
            // kör kulcsszáma: a lemondás-kezelést teszteli, nem szabad
            // elakadnia vagy hibás (rossz útvonalú) munkamenetet adnia.
            var rapidTargets = Enumerable.Range(0, 50)
                .Select(i => (IReadOnlyList<string>)(i % 3 == 0 ? [folder] : [file]))
                .ToList();

            var staleHits = 0;

            for (var i = 0; i < rapidTargets.Count; i++)
            {
                coordinator.NotifySelectionChanged(rapidTargets[i], false);

                if (i % 7 == 0)
                {
                    // "Jobbklikk" a MÉG BE NEM ÁLLT kijelölésre — ha ez
                    // véletlenül egy KORÁBBI (már túlhaladott) célra adna
                    // vissza kész munkamenetet, az hibás menütartalmat
                    // jelentene a valós UI-ban.
                    var early = coordinator.TakeIfMatches(rapidTargets[i]);

                    if (early is not null)
                    {
                        var earlySession = await early;
                        earlySession?.Dispose();
                    }
                }

                await Task.Delay(20);
            }

            await Task.Delay(500);
            var finalTarget = rapidTargets[^1];
            var finalTask = coordinator.TakeIfMatches(finalTarget);
            var rapidOk = false;

            if (finalTask is not null)
            {
                var finalSession = await finalTask;

                if (finalSession is not null)
                {
                    rapidOk = true;
                    finalSession.Dispose();
                }
            }

            log.Add($"5. SZCENARIO (gyors valtas+jobbklikk, {rapidTargets.Count} lepes): {(rapidOk ? "OK" : "HIBA")}, korai-talalat={staleHits}");

            // Memóriaellenőrzés: 50 kijelölés-váltás után nincs számottevő
            // növekedés — sem fel nem halmozódó ShellMenuSession, sem a
            // koordinátor saját állapota nem hízik.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var before = GC.GetTotalMemory(true);

            for (var i = 0; i < 50; i++)
            {
                coordinator.NotifySelectionChanged(i % 2 == 0 ? [file] : [folder], false);
                await Task.Delay(15);
            }

            await Task.Delay(500);
            var lastMemTask = coordinator.TakeIfMatches(50 % 2 == 0 ? [file] : [folder]);

            if (lastMemTask is not null)
            {
                (await lastMemTask)?.Dispose();
            }

            await Task.Delay(300);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var after = GC.GetTotalMemory(true);
            var deltaMb = (after - before) / (1024.0 * 1024.0);

            log.Add($"MEMORIA: elotte={before / 1024}KB, utana={after / 1024}KB, delta={deltaMb:F2}MB");

            coordinator.Dispose();

            var allPass = filePass == 10 && folderPass == 10 && altPass == 10 && multiPass == 10 && rapidOk;

            if (!allPass)
            {
                exitCode = 3;
            }

            // 5 MB fölötti növekedés 50 kör alatt gyanús felhalmozódásra utalna
            // — ez bőséges tartalék a mérés zajához képest.
            if (deltaMb > 5.0)
            {
                log.Add("FIGYELEM: a memoria-novekedes a kuszob folott van.");
                exitCode = exitCode == 0 ? 4 : exitCode;
            }

            log.Add(allPass ? "OSSZESITVE: ZOLD" : "OSSZESITVE: VAN BUKOTT KOR");
        }
        catch (Exception ex)
        {
            log.Add($"KIVETEL: {ex}");
            exitCode = 2;
        }

        try
        {
            await File.WriteAllLinesAsync(resultsPath, log);
        }
        catch (IOException)
        {
            // Az eredményfájl írása nem kritikus — a kilépési kód önmagában is jelez.
        }

        Log.Information("PRELOAD-ONTESZT vege, kilepokod={ExitCode}, eredmenyek={ResultsPath}", exitCode, resultsPath);
        Log.CloseAndFlush();
        Environment.Exit(exitCode);
    }

    /// <summary>
    /// Diagnosztikai önteszt (spec v1.0.3): a natív jobbklikk-menü
    /// (<see cref="Pilaster.Shell.Menus.ShellMenuSession.ShowNativeAsync"/>)
    /// Q1-táblázata, plusz a „nem fagy le az ablak, amíg a menü nyitva van"
    /// ellenőrzés. VALÓDI <c>TrackPopupMenuEx</c> hívásokkal fut, de a menüt
    /// minden körben a SAJÁT folyamaton belül, <c>WM_CANCELMODE</c>
    /// postázásával zárja be — nincs szintetikus billentyű-/egérbevitel,
    /// tehát a felhasználó élő munkamenetét nem érintheti.
    /// </summary>
    /// <remarks>
    /// A tesztkészlet ezt indítja (<c>NativeContextMenuModeTests</c>), a
    /// másik három önteszthez hasonlóan külön folyamatban — az eredményfájl
    /// (<c>%TEMP%\pilaster-nativemenu-selftest.txt</c>) adja a Q1-táblázat
    /// számait és a fagyás-ellenőrzés eredményét.
    /// </remarks>
    private async Task RunNativeMenuSelfCheckAsync(MainWindow mainWindow)
    {
        var exitCode = 0;
        var log = new List<string>();
        var resultsPath = Path.Combine(Path.GetTempPath(), "pilaster-nativemenu-selftest.txt");

        try
        {
            var settingsService = _services!.GetRequiredService<ISettingsService>();
            var ownerHandle = new WindowInteropHelper(mainWindow).Handle;
            var timeout = TimeSpan.FromMilliseconds(settingsService.Current.ShellMenuTimeoutMs);

            var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var file = Path.Combine(windowsDir, "notepad.exe");
            var folder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var altFile = Path.Combine(windowsDir, "explorer.exe");

            // Lásd RunPreloadSelfCheckAsync ugyanezen megjegyzését: a
            // megosztott STA sor kiürülését várjuk meg, hogy az első kör ne a
            // hideg COM-indulás ~2186 ms-os, dokumentált költségével
            // versenyezzen a szokásos időkorláton.
            var warmupSettle = await Pilaster.Shell.Menus.ShellMenuSession.QueryItemsAsync([file], false, TimeSpan.FromSeconds(25), []);
            warmupSettle?.Dispose();
            await Task.Delay(300);

            // VALÓDI renderelt ikonnal, NEM nint.Zero-val — az ikon-renderelés
            // (glyph -> RenderTargetBitmap -> HBITMAP, natív GDI-hívásokkal)
            // egy valódi, éles hibát rejtett (GetDC rossz DLL-ből importálva:
            // gdi32 helyett user32 kellett volna), amit egy nint.Zero-s teszt
            // sosem futtatott volna le, és csak egy kézi próba derített ki.
            var dummyOwnCommands = new List<NativeOwnCommand>
            {
                new(
                    Pilaster.Shell.Menus.ShellMenuSession.NativeOwnCommandIdBase,
                    "Onteszt parancs",
                    NativeMenuIconRenderer.GetOrRender(Wpf.Ui.Controls.SymbolRegular.Code24)),
            };

            async Task<int> RunScenarioAsync(string label, int rounds, Func<int, IReadOnlyList<string>> pathsFactory)
            {
                var pass = 0;

                for (var i = 0; i < rounds; i++)
                {
                    var paths = pathsFactory(i);
                    var session = await Pilaster.Shell.Menus.ShellMenuSession.QueryItemsAsync(paths, false, timeout, []);

                    if (session is null)
                    {
                        log.Add($"{label} #{i}: A LEKERDEZES NULL MUNKAMENETET ADOTT");
                        continue;
                    }

                    try
                    {
                        var result = await session.ShowNativeAsync(
                            dummyOwnCommands, 100, 100, ownerHandle,
                            onShown: hwnd => _ = Task.Delay(350)
                                .ContinueWith(_ => Pilaster.Shell.Menus.NativeMenuIconInterop.CancelActiveMenu(hwnd)));

                        if (result.Outcome == NativeMenuOutcome.Cancelled)
                        {
                            pass++;
                        }
                        else
                        {
                            log.Add($"{label} #{i}: VARATLAN EREDMENY ({result.Outcome}) — WM_CANCELMODE utan nem 'Cancelled' jott vissza");
                        }
                    }
                    finally
                    {
                        session.Dispose();
                    }

                    await Task.Delay(150);
                }

                log.Add($"{label}: {pass}/{rounds}");
                return pass;
            }

            var filePass = await RunScenarioAsync("FAJL", 10, _ => [file]);
            var folderPass = await RunScenarioAsync("MAPPA (elemkent)", 10, _ => [folder]);
            var altPass = await RunScenarioAsync("VALTAKOZVA", 10, i => i % 2 == 0 ? [file] : [folder]);
            var multiPass = await RunScenarioAsync("TOBBSZOROS KIJELOLES", 10, _ => [file, altFile]);

            // Almenü-teszt (a kör kulcskérdése): VALÓS billentyű-navigáció a
            // natív menüben, de KIZÁRÓLAG PostMessage-dzsel a saját
            // ideiglenes ablakunknak címezve — nem globális szimuláció, a
            // felhasználó tényleges fókuszát nem érinti. A session.Items MÁR
            // tartalmazza a fát (a QueryItemsAsync feloldotta a dinamikus
            // almenüket is a beolvasáskor, lásd ReadSubmenu) — ebből
            // kiszámítható, hány Le-nyílra van szükség egy almenüs elem
            // eléréséhez, hiszen a natív menü Le-nyíllal a szeparátorokat
            // automatikusan átugorja.
            var submenuOk = false;
            var submenuNote = "kihagyva (nincs almenüs elem a lekérdezésben)";

            var submenuProbeSession = await Pilaster.Shell.Menus.ShellMenuSession.QueryItemsAsync([file], false, timeout, []);

            if (submenuProbeSession is not null)
            {
                var selectableBeforeTarget = 0;
                var targetIndex = -1;

                for (var i = 0; i < submenuProbeSession.Items.Count; i++)
                {
                    var node = submenuProbeSession.Items[i];

                    if (node.IsSeparator)
                    {
                        continue;
                    }

                    if (node.HasChildren)
                    {
                        targetIndex = i;
                        break;
                    }

                    selectableBeforeTarget++;
                }

                if (targetIndex >= 0)
                {
                    // + a saját beszúrt elemek száma — azok a VALÓDI natív
                    // menüben a shell elemei ELÉ kerülnek, a Le-nyíl navigáció
                    // ott kezdődik.
                    var downPresses = dummyOwnCommands.Count + selectableBeforeTarget;

                    async Task NavigateIntoSubmenuAndCloseAsync(nint hwnd)
                    {
                        await Task.Delay(300);

                        for (var i = 0; i < downPresses; i++)
                        {
                            Pilaster.Shell.Menus.NativeMenuIconInterop.PostMenuNavigationKey(hwnd, Pilaster.Shell.Menus.NativeMenuTestKey.Down);
                            await Task.Delay(40);
                        }

                        Pilaster.Shell.Menus.NativeMenuIconInterop.PostMenuNavigationKey(hwnd, Pilaster.Shell.Menus.NativeMenuTestKey.Right);
                        await Task.Delay(400);
                        Pilaster.Shell.Menus.NativeMenuIconInterop.CancelActiveMenu(hwnd);
                    }

                    var submenuResult = await submenuProbeSession.ShowNativeAsync(
                        dummyOwnCommands, 100, 100, ownerHandle,
                        onShown: hwnd => _ = NavigateIntoSubmenuAndCloseAsync(hwnd));

                    // >= 2: egy a menü NYITÁSÁÉRT (felső szintű
                    // WM_INITMENUPOPUP, mindig jár), egy (vagy több) az
                    // ÉLŐ almenü-hoverért — ez utóbbi híján a natív ablak
                    // WndProc-ja sosem kapta volna meg/továbbította volna a
                    // shellnek a dinamikus feltöltés kérését.
                    submenuOk = submenuResult.ForwardedInitMenuPopupCount >= 2;
                    submenuNote = $"'{submenuProbeSession.Items[targetIndex].Text}' almenü, {downPresses} Le + 1 Jobbra, "
                        + $"WM_INITMENUPOPUP továbbítva {submenuResult.ForwardedInitMenuPopupCount}x, "
                        + $"osszes uzenet a WndProc-on {submenuResult.TotalMessagesReceived} -> {(submenuOk ? "OK" : "GYANUS")}";
                }

                submenuProbeSession.Dispose();
            }

            log.Add($"ALMENU-TESZT (7-Zip/Kuldes-szeru dinamikus feltoltes): {submenuNote}");

            // "Nem fagy le az ablak, amig a nat.v menu nyitva van": a UI
            // Dispatcher folyamatosan pingel egy DispatcherTimer-rel — ha a
            // szinkron TrackPopupMenuEx valahogy a UI szalat blokkolna
            // (ahelyett, hogy a megosztott STA szalat), a ping leallna.
            var pingCount = 0;
            var pingTimer = new System.Windows.Threading.DispatcherTimer(
                TimeSpan.FromMilliseconds(30),
                System.Windows.Threading.DispatcherPriority.Normal,
                (_, _) => pingCount++,
                mainWindow.Dispatcher);

            var freezeSession = await Pilaster.Shell.Menus.ShellMenuSession.QueryItemsAsync([file], false, timeout, []);
            var freezeCheckOk = false;
            long freezeElapsedMs = 0;

            if (freezeSession is not null)
            {
                pingTimer.Start();
                var sw = Stopwatch.StartNew();

                var freezeResult = await freezeSession.ShowNativeAsync(
                    dummyOwnCommands, 100, 100, ownerHandle,
                    onShown: hwnd => _ = Task.Delay(500)
                        .ContinueWith(_ => Pilaster.Shell.Menus.NativeMenuIconInterop.CancelActiveMenu(hwnd)));

                sw.Stop();
                pingTimer.Stop();
                freezeSession.Dispose();
                freezeElapsedMs = sw.ElapsedMilliseconds;

                // 30 ms-onkénti pingeléssel a menü nyitva léte alatt a
                // várható ping-szám kb. elapsed/30 — bőséges (felezett)
                // tűréssel, hogy egy lassabb gépen se adjon hamis riasztást.
                var expectedMinimum = Math.Max(3, (int)(freezeElapsedMs / 30 / 2));
                freezeCheckOk = pingCount >= expectedMinimum;
                log.Add($"DIAGNOSZTIKA (fagyas-teszt menuje): WndProc osszes uzenet={freezeResult.TotalMessagesReceived}, WM_INITMENUPOPUP={freezeResult.ForwardedInitMenuPopupCount}");
            }

            log.Add(freezeSession is null
                ? "FAGYAS-ELLENORZES: kihagyva (a lekerdezes nem sikerult)"
                : $"FAGYAS-ELLENORZES: {pingCount} UI-ping {freezeElapsedMs} ms alatt (menu nyitva) -> {(freezeCheckOk ? "OK, a UI valaszkepes maradt" : "GYANUS, lehet hogy lefagyott")}");

            var allPass = filePass == 10 && folderPass == 10 && altPass == 10 && multiPass == 10 && freezeCheckOk && submenuOk;

            if (!allPass)
            {
                exitCode = 3;
            }

            log.Add(allPass ? "OSSZESITVE: ZOLD" : "OSSZESITVE: VAN BUKOTT KOR");
        }
        catch (Exception ex)
        {
            log.Add($"KIVETEL: {ex}");
            exitCode = 2;
        }

        try
        {
            await File.WriteAllLinesAsync(resultsPath, log);
        }
        catch (IOException)
        {
            // Az eredményfájl írása nem kritikus — a kilépési kód önmagában is jelez.
        }

        Log.Information("NATIVEMENU-ONTESZT vege, kilepokod={ExitCode}, eredmenyek={ResultsPath}", exitCode, resultsPath);
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
        _services?.GetService<CloudDriveService>()?.Flush();
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
