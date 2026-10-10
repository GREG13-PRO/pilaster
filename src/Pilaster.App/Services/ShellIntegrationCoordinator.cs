using Pilaster.Core.Settings;
using Pilaster.Shell.Integration;

namespace Pilaster.App.Services;

/// <summary>
/// A „Rendszerintegráció" beállítások (Beállítások ablak) és a tényleges
/// registry-műveletek (<see cref="Pilaster.Shell.Integration"/>) közti
/// összekötő réteg: perzisztálja az egyes kapcsolók előtti állapotot, hogy a
/// kikapcsolás pontosan visszaállíthasson.
/// </summary>
/// <remarks>
/// Minden itt végzett registry-írás a <c>HKEY_CURRENT_USER</c> alá esik,
/// tehát admin jog (UAC) NEM szükséges hozzá — ez a Windows hivatalosan
/// támogatott, felhasználónkénti felülbírálási pontja, amit maga az Intéző
/// is figyelembe vesz. A hibakezelés emiatt nem UAC-megtagadásra készül fel,
/// hanem a ténylegesen előforduló hibákra (írásvédett/sérült profil,
/// vállalati csoportházirend-tiltás, víruskereső-beavatkozás stb.) — ezekben
/// az esetekben sem marad félkész állapot: a kapcsoló visszaáll, és a hívó
/// fél hibaüzenetet kap.
/// </remarks>
public sealed class ShellIntegrationCoordinator(ISettingsService settings)
{
    public ShellIntegrationSettings State => settings.Current.ShellIntegration;

    /// <summary>
    /// A bekapcsolt átirányítások frissítése induláskor a JELENLEGI
    /// programútvonalra. Így egy áthelyezett vagy frissített Pilaster után sem
    /// mutatnak régi helyre, és a v1.2.2 előtti beállítások is átállnak: a
    /// mappák alapértelmezett igéje és a registry-alapú Win+E.
    /// </summary>
    /// <remarks>
    /// Tesztelő/bemutató példány (külön adatmappa) és Store-csomag nem nyúl a
    /// rendszerhez: azok beállításai nem a felhasználó valódi választásai.
    /// </remarks>
    public void ApplyInitial(string exePath)
    {
        if (PackageInfo.IsPackaged || Environment.GetEnvironmentVariable(AppDataLocator.OverrideVariable) is { Length: > 0 })
        {
            return;
        }

        try
        {
            // A telepítő „alapértelmezett fájlkezelő" opciója a saját mentésével
            // kapcsolja be — a Beállítások kapcsolója ilyenkor is bekapcsoltat mutasson.
            if (!State.FolderOpenRedirectEnabled && ShellIntegrationService.IsFolderOpenRedirectedTo(exePath))
            {
                State.FolderOpenRedirectEnabled = true;
                settings.NotifyChanged();
            }

            if (State.FolderOpenRedirectEnabled)
            {
                ShellIntegrationService.SetFolderOpenCommand(exePath);
            }

            if (State.WinERedirectEnabled && !ShellIntegrationService.IsExplorerLaunchRedirectedTo(exePath))
            {
                ShellIntegrationService.SetExplorerLaunchCommand(exePath);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
        {
            Serilog.Log.Warning(ex, "A rendszerintegráció frissítése induláskor nem sikerült");
        }
    }

    public (bool success, string? error) SetFolderOpenRedirect(bool enabled, string exePath)
    {
        try
        {
            if (enabled && ShellIntegrationService.IsFolderOpenRedirectedTo(exePath))
            {
                // Már ránk mutat (pl. a telepítő kapcsolta be): az „eredeti"
                // állapotként a saját átirányításunkat mentenénk el, és a
                // kikapcsolás ide állna vissza. A meglévő mentés marad.
                ShellIntegrationService.SetFolderOpenCommand(exePath);
            }
            else if (enabled)
            {
                var dirBackup = ShellIntegrationService.BackupDirectoryOpen();
                var driveBackup = ShellIntegrationService.BackupDriveOpen();

                ShellIntegrationService.SetFolderOpenCommand(exePath);

                State.DirectoryBackupCaptured = dirBackup.Captured;
                State.DirectoryBackupExisted = dirBackup.Existed;
                State.DirectoryBackupValue = dirBackup.CommandValue;
                State.DriveBackupCaptured = driveBackup.Captured;
                State.DriveBackupExisted = driveBackup.Existed;
                State.DriveBackupValue = driveBackup.CommandValue;
                State.DirectoryDefaultVerbCaptured = dirBackup.DefaultVerbCaptured;
                State.DirectoryDefaultVerbValue = dirBackup.DefaultVerbValue;
                State.DriveDefaultVerbCaptured = driveBackup.DefaultVerbCaptured;
                State.DriveDefaultVerbValue = driveBackup.DefaultVerbValue;
            }
            else if (!State.DirectoryBackupCaptured)
            {
                // Nem mi mentettük el az előző állapotot (a telepítő kapcsolta
                // be): a Windows alapértelmezésére állunk vissza.
                ShellIntegrationService.RemoveFolderOpenRedirectsTo(System.IO.Path.GetDirectoryName(exePath) ?? exePath);
            }
            else
            {
                ShellIntegrationService.RestoreDirectoryOpen(new RegistryBackup(
                    State.DirectoryBackupCaptured, State.DirectoryBackupExisted, State.DirectoryBackupValue,
                    State.DirectoryDefaultVerbCaptured, State.DirectoryDefaultVerbValue));
                ShellIntegrationService.RestoreDriveOpen(new RegistryBackup(
                    State.DriveBackupCaptured, State.DriveBackupExisted, State.DriveBackupValue,
                    State.DriveDefaultVerbCaptured, State.DriveDefaultVerbValue));

                State.DirectoryBackupCaptured = false;
                State.DriveBackupCaptured = false;
            }

            State.FolderOpenRedirectEnabled = enabled;
            settings.NotifyChanged();
            return (true, null);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
        {
            return (false, ex.Message);
        }
    }

    public (bool success, string? error) SetContextMenuEntry(bool enabled, string exePath, string displayLabel, string iconPath)
    {
        try
        {
            if (enabled)
            {
                ShellIntegrationService.AddContextMenuEntry(exePath, displayLabel, iconPath);
            }
            else
            {
                ShellIntegrationService.RemoveContextMenuEntry();
            }

            State.ContextMenuEntryEnabled = enabled;
            settings.NotifyChanged();
            return (true, null);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// A Win+E és az Intéző „Új ablak" parancsa a Pilastert nyissa meg. A
    /// korábbi billentyűzet-hook csak futás közben hatott, és egy akadó
    /// felület alatt a gép összes billentyűleütését késleltette.
    /// </summary>
    public (bool success, string? error) SetWinERedirect(bool enabled, string exePath)
    {
        try
        {
            if (enabled)
            {
                ShellIntegrationService.SetExplorerLaunchCommand(exePath);
            }
            else
            {
                ShellIntegrationService.RemoveExplorerLaunchCommand();
            }

            State.WinERedirectEnabled = enabled;
            settings.NotifyChanged();
            return (true, null);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>„Minden visszaállítása alapértelmezettre" — mindhárom kapcsoló kikapcsolása, pontos visszaállítással.</summary>
    public (bool success, string? error) ResetAll(string exePath)
    {
        var (folderOk, folderError) = SetFolderOpenRedirect(false, exePath);
        var (menuOk, menuError) = SetContextMenuEntry(false, exePath, string.Empty, string.Empty);
        var (winEOk, winEError) = SetWinERedirect(false, exePath);

        return folderOk && menuOk && winEOk ? (true, null) : (false, folderError ?? menuError ?? winEError);
    }
}
