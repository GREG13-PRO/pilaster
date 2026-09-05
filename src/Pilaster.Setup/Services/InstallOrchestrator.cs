using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Pilaster.Setup.Constants;
using Pilaster.Shell.Integration;

namespace Pilaster.Setup.Services;

/// <summary>
/// A telepítés és az eltávolítás teljes lépéssorát fogja össze — a Progress
/// oldal (Views/Pages/ProgressPage) csak ezt hívja, és a beérkező
/// <see cref="IProgress{T}"/> jelentésekből frissíti a UI-t.
/// </summary>
public static class InstallOrchestrator
{
    public static async Task InstallAsync(SetupSession session, IProgress<CopyProgress> progress, CancellationToken cancellationToken)
    {
        var installDir = session.InstallDirectory;
        Directory.CreateDirectory(installDir);

        await PayloadCopier.CopyDirectoryAsync(session.PayloadDirectory, installDir, progress, cancellationToken);

        // A "Payload" a Setup.exe SAJÁT mappájában él (lásd App.xaml.cs —
        // PayloadDirectory), a fenti sor már átmásolta onnan a telepítési
        // helyre — ha itt nem zárnánk ki, a teljes payload MÉG EGYSZER
        // bekerülne a Setup\ almappába is.
        var setupSourceDir = AppContext.BaseDirectory;
        var setupDestDir = Path.Combine(installDir, SetupInfo.SetupSubfolderName);
        await PayloadCopier.CopyDirectoryAsync(
            setupSourceDir, setupDestDir, new Progress<CopyProgress>(), cancellationToken, excludeSubdirectoryName: "Payload");

        var exePath = Path.Combine(installDir, SetupInfo.AppExeName);
        var uninstallExePath = Path.Combine(setupDestDir, SetupInfo.UninstallExeName);

        if (session.CreateStartMenuShortcut)
        {
            var startMenuDir = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu) + @"\Programs";
            ShortcutBuilder.CreateShortcut(
                Path.Combine(startMenuDir, $"{SetupInfo.AppName}.lnk"), exePath, installDir, SetupInfo.AppUserModelId);
        }

        if (session.CreateDesktopShortcut)
        {
            var desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            ShortcutBuilder.CreateShortcut(
                Path.Combine(desktopDir, $"{SetupInfo.AppName}.lnk"), exePath, installDir, SetupInfo.AppUserModelId);
        }

        if (session.EnableContextMenu)
        {
            const string label = "Megnyitás Pilaster-ben";
            ShellIntegrationService.AddContextMenuEntry(exePath, label, exePath);
            ShellIntegrationService.AddContextMenuEntry(
                ShellIntegrationService.BackgroundContextMenuVerbKey, "%V", exePath, label, exePath);
            ShellIntegrationService.AddContextMenuEntry(
                ShellIntegrationService.DriveContextMenuVerbKey, "%1", exePath, label, exePath);
        }

        if (session.EnableFileAssociations)
        {
            SetupRegistration.RegisterFileAssociations(exePath);
        }

        if (session.StartWithWindows)
        {
            SetupRegistration.SetStartWithWindows(exePath);
        }

        if (session.MakeDefaultFileManager)
        {
            var directoryBackup = ShellIntegrationService.BackupDirectoryOpen();
            var driveBackup = ShellIntegrationService.BackupDriveOpen();
            SetupRegistration.SaveDefaultFileManagerBackup(directoryBackup, driveBackup);
            ShellIntegrationService.SetFolderOpenCommand(exePath);
        }

        var estimatedSizeKb = GetDirectorySize(installDir) / 1024;
        SetupRegistration.RegisterUninstaller(installDir, uninstallExePath, estimatedSizeKb);
    }

    public static async Task UninstallAsync(SetupSession session, IProgress<CopyProgress> progress, CancellationToken cancellationToken)
    {
        var installDir = session.InstallDirectory;

        var startMenuLnk = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu) + $@"\Programs\{SetupInfo.AppName}.lnk";
        var desktopLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), $"{SetupInfo.AppName}.lnk");
        TryDeleteFile(startMenuLnk);
        TryDeleteFile(desktopLnk);

        ShellIntegrationService.RemoveContextMenuEntry();
        ShellIntegrationService.RemoveContextMenuEntry(ShellIntegrationService.BackgroundContextMenuVerbKey);
        ShellIntegrationService.RemoveContextMenuEntry(ShellIntegrationService.DriveContextMenuVerbKey);

        SetupRegistration.UnregisterFileAssociations();
        SetupRegistration.RemoveStartWithWindows();

        var defaultFmBackup = SetupRegistration.LoadDefaultFileManagerBackup();

        if (defaultFmBackup is { } backup)
        {
            ShellIntegrationService.RestoreDirectoryOpen(backup.Directory);
            ShellIntegrationService.RestoreDriveOpen(backup.Drive);
            SetupRegistration.RemoveDefaultFileManagerBackup();
        }

        // A cache/logok mindig törlődnek — ugyanaz a viselkedés, mint a
        // korábbi installer/Pilaster.iss [UninstallDelete] szakaszáé.
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        TryDeleteDirectory(Path.Combine(localAppData, "Pilaster", "cache"));
        TryDeleteDirectory(Path.Combine(localAppData, "Pilaster", "logs"));

        if (session.DeleteSettingsOnUninstall)
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            TryDeleteDirectory(Path.Combine(appData, "Pilaster"));
        }

        SetupRegistration.UnregisterUninstaller();

        // A telepítési könyvtárat (a benne futó Setup\Pilaster.Setup.exe
        // KIVÉTELÉVEL) most töröljük — a Setup\ almappa saját magának nem
        // tudja törölni a futó .exe-jét, azt egy leválasztott takarító
        // folyamat végzi (lásd SelfCleanup).
        await Task.Run(() => DeleteInstallDirExceptRunningSetup(installDir), cancellationToken);

        progress.Report(new CopyProgress(string.Empty, 1, 1, null));

        SelfCleanup.ScheduleDirectoryRemoval(Path.Combine(installDir, SetupInfo.SetupSubfolderName));
    }

    private static void DeleteInstallDirExceptRunningSetup(string installDir)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(installDir))
        {
            // Névvel hasonlítunk, nem teljes útvonallal — az útvonal-egyezés
            // MÉRVE nem talált egyezést (valószínűleg trailing-separator
            // eltérés a AppContext.BaseDirectory/Directory.GetParent
            // párosításból), és a fenti Setup\ mappa, benne a JELENLEG
            // FUTÓ Pilaster.Setup.exe saját betöltött DLL-jeivel, bekerült a
            // törlendők közé — UnauthorizedAccessException lett belőle.
            var name = Path.GetFileName(entry.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

            if (string.Equals(name, SetupInfo.SetupSubfolderName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (Directory.Exists(entry))
            {
                Directory.Delete(entry, recursive: true);
            }
            else
            {
                File.Delete(entry);
            }
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static long GetDirectorySize(string path)
    {
        long size = 0;

        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            try
            {
                size += new FileInfo(file).Length;
            }
            catch (IOException)
            {
            }
        }

        return size;
    }
}
