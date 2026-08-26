using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Pilaster.App.ViewModels;
using Pilaster.App.Views;
using Pilaster.Core.FileSystem;

namespace Pilaster.App.Diagnostics;

/// <summary>
/// Sorra megnyitja az összes ablakot és dialógust, hogy a
/// <see cref="BindingErrorScanner"/> ez alatt megtalálhassa a köztük lévő
/// meghibásodott kötéseket (spec v1.0.1, 3. kör).
/// </summary>
/// <remarks>
/// <para>
/// Ez a séta a MEGLÉVŐ, éles kódutakat hívja (DI-ből feloldott ablakok,
/// valódi fájllal betöltött előnézet/szerkesztő) — nem egy külön, csak
/// tesztre írt makett-felület, hogy a talált (vagy hiányzó) hibák a valódi
/// alkalmazást tükrözzék.
/// </para>
/// <para>
/// SZÁNDÉKOSAN SZINKRON, <c>async</c>/<c>await</c> NÉLKÜL. Egy korábbi,
/// <c>Dispatcher.Yield</c>/<c>Task.Delay</c>-alapú változat közvetlen
/// futtatásból mindig lefutott, de a tesztkészletből (<c>Process.Start</c>-tal
/// indított külön folyamatként) MÉRVE sosem — az önteszt a saját 60 mp-es
/// őrszemét sem érte el, ami MAGA IS egy `Task.Delay`-re épült. Ez arra utal,
/// hogy ebben az indítási környezetben a `SynchronizationContext.Post`-tal
/// ütemezett folytatások nem futnak le megbízhatóan. A <see cref="Dispatcher.Invoke(Action, DispatcherPriority)"/>
/// ezzel szemben SZINKRON, közvetlenül a hívó szálon pumpál egy beágyazott
/// üzenetciklust — ugyanaz a mechanizmus, mint a <c>ShowDialog()</c>-é —, és
/// ez MÉRVE megbízhatóan működik ugyanabban a környezetben.
/// </para>
/// </remarks>
public static class BindingCheckRunner
{
    /// <summary>
    /// Végigmegy minden ablakon, és visszaadja az eközben talált kötési
    /// hibaüzeneteket — üres lista, ha egy sem volt. A
    /// <see cref="BindingErrorTraceListener"/> begyűjtését is hozzáfűzi, ha
    /// az adott futáson mégis jelentett volna valamit.
    /// </summary>
    public static IReadOnlyList<string> Run(IServiceProvider services, MainWindow mainWindow)
    {
        var results = new List<string>();

        Pump(mainWindow.Dispatcher);
        results.AddRange(BindingErrorScanner.Scan(mainWindow));

        OpenSettings(services, mainWindow, results);
        OpenQuickAccessEditor(services, mainWindow, results);
        OpenEditor(services, mainWindow, results);
        OpenFilePreview(services, mainWindow, results);
        OpenTransferConfirm(services, mainWindow, results);

        if (App.BindingErrorListener is { } listener)
        {
            results.AddRange(listener.Errors);
        }

        return results;
    }

    private static void OpenSettings(IServiceProvider services, MainWindow mainWindow, List<string> results)
    {
        var window = services.GetRequiredService<SettingsWindow>();
        window.Owner = mainWindow;

        if (window.DataContext is not SettingsViewModel vm)
        {
            return;
        }

        window.Show();
        Pump(window.Dispatcher);

        // Mind a 11 kategória — minden kategória-panel a látótér RÉSZE már
        // az ablak megnyitásakor (Collapsed, nem eltávolítva), de a
        // kiválasztás váltása egy újabb layout-kört is kikényszerít, és ez
        // az, amit egy valódi felhasználó ténylegesen csinál.
        foreach (var category in vm.Categories)
        {
            vm.SelectedCategory = category;
            Pump(window.Dispatcher);
        }

        // Kiosztás-megtekintő — a Billentyűzet kategórián belüli, beágyazott
        // tábla, nem külön ablak.
        vm.SelectedCategory = vm.Categories.First(c => c.Id == SettingsCatalog.Keyboard);
        vm.ToggleKeymapTableCommand.Execute(null);
        Pump(window.Dispatcher);

        // Címkeszerkesztő sor — legalább egy valódi elem kell, hogy a
        // TagEditorViewModel sablonja ténylegesen megjelenjen.
        vm.SelectedCategory = vm.Categories.First(c => c.Id == SettingsCatalog.Tags);
        vm.NewTagName = "Önteszt";
        vm.AddTagCommand.Execute(null);
        Pump(window.Dispatcher);

        // A keresés is saját nézetet vált (találatlista a kategória-tartalom
        // helyett) — ugyanígy valódi felhasználói út.
        vm.SearchText = "téma";
        Pump(window.Dispatcher);
        vm.SearchText = string.Empty;
        Pump(window.Dispatcher);

        results.AddRange(BindingErrorScanner.Scan(window));
        window.Close();
    }

    private static void OpenQuickAccessEditor(IServiceProvider services, MainWindow mainWindow, List<string> results)
    {
        var window = services.GetRequiredService<QuickAccessEditorWindow>();
        window.Owner = mainWindow;
        ScheduleModalScanAndClose(window, results);
        window.ShowDialog();
    }

    private static void OpenEditor(IServiceProvider services, MainWindow mainWindow, List<string> results)
    {
        var window = services.GetRequiredService<EditorWindow>();
        window.Owner = mainWindow;
        window.Show();

        var tempFile = Path.Combine(Path.GetTempPath(), $"pilaster-bindingcheck-{Guid.NewGuid():N}.txt");
        File.WriteAllText(tempFile, "Pilaster kötéshiba-önteszt.");

        try
        {
            // A produkciós OpenAsync valódi async I/O-t végez — ez az egyetlen
            // hely, ahol a sétának mégis meg kell várnia egy Task-ot. EGYSZERŰ
            // `.GetAwaiter().GetResult()` MEGAKASZTANÁ a szálat: az `OpenAsync`
            // belső `await`-jei ugyanerre a Dispatcherre ütemeznék vissza a
            // folytatást, ami sosem futna le, amíg ez a hívás blokkol —
            // klasszikus holtpont. A `PumpUntilCompleted` ehelyett a Task
            // befejeződéséig ISMÉTELTEN pumpálja a Dispatchert.
            PumpUntilCompleted(window.Dispatcher, services.GetRequiredService<EditorViewModel>().OpenAsync(tempFile));
        }
        finally
        {
            File.Delete(tempFile);
        }

        results.AddRange(BindingErrorScanner.Scan(window));

        // Singleton (lásd MainWindow.OpenInEditorAsync) — élesben sem záródik
        // véglegesen, csak elrejtjük, nehogy a folyamat végén nyitva maradjon.
        window.Hide();
    }

    private static void OpenFilePreview(IServiceProvider services, MainWindow mainWindow, List<string> results)
    {
        var window = services.GetRequiredService<FilePreviewWindow>();
        window.Owner = mainWindow;
        window.Show();

        if (Environment.ProcessPath is { Length: > 0 } self)
        {
            var item = new FileSystemItem
            {
                FullPath = self,
                Name = Path.GetFileName(self),
                Kind = FileSystemItemKind.File,
            };

            PumpUntilCompleted(window.Dispatcher, window.LoadAsync(item));
        }

        Pump(window.Dispatcher);
        results.AddRange(BindingErrorScanner.Scan(window));
        window.Close();
    }

    private static void OpenTransferConfirm(IServiceProvider services, MainWindow mainWindow, List<string> results)
    {
        var window = services.GetRequiredService<TransferConfirmWindow>();
        window.Owner = mainWindow;
        window.Initialize(isMove: false, itemCount: 1, targetDirectory: Path.GetTempPath());
        ScheduleModalScanAndClose(window, results);
        window.ShowDialog();
    }

    /// <summary>
    /// Egy MODÁLIS ablak (<c>ShowDialog()</c>) vizsgálata és bezárása — a
    /// <see cref="Window.ContentRendered"/> eseményre kötve, ami PONTOSAN
    /// akkor tüzel, amikor az ablak megjelent és a tartalma renderelődött.
    /// Ez a WPF renderelési csővezeték RÉSZE, nem egy külön ütemezett
    /// művelet, ezért nem függ semmilyen `SynchronizationContext`-től.
    /// </summary>
    private static void ScheduleModalScanAndClose(Window window, List<string> results)
    {
        window.ContentRendered += (_, _) =>
        {
            results.AddRange(BindingErrorScanner.Scan(window));
            window.Close();
        };
    }

    /// <summary>
    /// Szinkron dispatcher-pumpálás: beágyazott üzenetciklust indít
    /// (ugyanúgy, mint a <c>ShowDialog()</c>), és a hívó szálon VÁRJA meg,
    /// amíg minden `Background` vagy magasabb prioritású munka lefut —
    /// ide tartozik a `Loaded` esemény és az azt követő kötés-kiértékelés.
    /// </summary>
    private static void Pump(Dispatcher dispatcher) =>
        dispatcher.Invoke(() => { }, DispatcherPriority.Background);

    /// <summary>
    /// Egy valódi async I/O-t végző <see cref="Task"/> bevárása a Dispatcher
    /// szálán ANÉLKÜL, hogy holtpontba futna.
    /// </summary>
    /// <remarks>
    /// A <c>task.GetAwaiter().GetResult()</c> itt holtpontot okozna: a Task
    /// belső <c>await</c>-jei (alapértelmezett <c>ConfigureAwait(true)</c>
    /// mellett) ugyanerre a Dispatcher-szálra ütemeznék vissza a
    /// folytatásukat, ami viszont blokkolva várna a Task-ra — kör. Ehelyett
    /// ISMÉTELTEN pumpáljuk a Dispatchert, amíg a Task be nem fejeződik: a
    /// pumpálás közben a Task saját folytatásai is sorra kerülnek és
    /// lefutnak, tehát végül halad előre.
    /// </remarks>
    private static void PumpUntilCompleted(Dispatcher dispatcher, Task task)
    {
        while (!task.IsCompleted)
        {
            dispatcher.Invoke(() => { }, DispatcherPriority.Background);
        }

        // Kivétel esetén itt dobja újra — így a hívó try/catch-e ugyanúgy
        // látja, mintha simán await-elték volna.
        task.GetAwaiter().GetResult();
    }
}
