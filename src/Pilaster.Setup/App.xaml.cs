using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using Pilaster.Setup.Constants;
using Pilaster.Setup.Services;
using Pilaster.Setup.Views;

namespace Pilaster.Setup;

public partial class App : Application
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SetCurrentProcessExplicitAppUserModelID(
        [MarshalAs(UnmanagedType.LPWStr)] string appId);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        SetCurrentProcessExplicitAppUserModelID(SetupInfo.AppUserModelId);

        var args = e.Args;
        var isUninstall = args.Any(a => a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase));
        var isSilent = args.Any(a => a.Equals("/SILENT", StringComparison.OrdinalIgnoreCase)
            || a.Equals("/S", StringComparison.OrdinalIgnoreCase));
        var deleteSettings = args.Any(a => a.Equals("/DELETESETTINGS", StringComparison.OrdinalIgnoreCase));
        var dirArg = args.FirstOrDefault(a => a.StartsWith("/DIR=", StringComparison.OrdinalIgnoreCase))?[5..].Trim('"');

        var session = new SetupSession
        {
            IsUninstall = isUninstall,
            PayloadDirectory = Path.Combine(AppContext.BaseDirectory, "Payload"),
        };

        if (isUninstall)
        {
            // Az Uninstall.exe a <TelepítésiMappa>\Setup\ alól fut — a szülő a
            // telepítési mappa. Az AppContext.BaseDirectory MINDIG záró
            // elválasztóval tér vissza — Directory.GetParent ilyenkor
            // ÖNMAGÁT adja vissza, nem a szülőt (a záró "\" miatt nincs mit
            // "leválasztania"); a levágás nélkül InstallDirectory tévesen a
            // Setup\ mappára mutatott volna, és az eltávolító a SAJÁT,
            // épp betöltött DLL-jeit próbálta volna törölni.
            var setupDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            session.InstallDirectory = Directory.GetParent(setupDir)?.FullName ?? SetupInfo.DefaultInstallDir;
            session.DeleteSettingsOnUninstall = deleteSettings;
        }
        else if (dirArg is { Length: > 0 })
        {
            session.InstallDirectory = dirArg;
        }

        if (isSilent)
        {
            RunSilently(session);
            return;
        }

        var window = new MainWindow(session);
        window.Show();
    }

    /// <summary>
    /// Csendes telepítés/eltávolítás (a beépített frissítő és a winget így hívja).
    /// </summary>
    /// <remarks>
    /// <c>async void</c>, nem blokkoló várakozás: a másolás folytatásai
    /// szándékosan a UI-szálra térnek vissza (a parancsikonok COM-objektumai
    /// STA szálat kérnek, lásd PayloadCopier). Korábban itt
    /// <c>GetAwaiter().GetResult()</c> blokkolta ugyanezt a szálat, így a
    /// telepítő az első fájlnál örökre megakadt — a frissítés után a Pilaster
    /// nem indult újra, a winget-es telepítés pedig sosem fejeződött be. Ablak
    /// nélkül a Shutdown hívásáig fut tovább az alkalmazás.
    /// </remarks>
    private async void RunSilently(SetupSession session)
    {
        var exitCode = 0;

        try
        {
            using var cts = new CancellationTokenSource();
            var progress = new Progress<CopyProgress>();

            if (session.IsUninstall)
            {
                await InstallOrchestrator.UninstallAsync(session, progress, cts.Token);
            }
            else
            {
                await InstallOrchestrator.InstallAsync(session, progress, cts.Token);
            }
        }
        catch (Exception)
        {
            exitCode = 1;
        }

        Shutdown(exitCode);
    }
}
