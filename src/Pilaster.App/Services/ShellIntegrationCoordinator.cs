using Pilaster.Core.Settings;
using Pilaster.Shell.Integration;

namespace Pilaster.App.Services;

/// <summary>
/// A „Rendszerintegráció" beállítások (Beállítások ablak) és a tényleges
/// Win32/registry-műveletek (<see cref="Pilaster.Shell.Integration"/>) közti
/// összekötő réteg: perzisztálja az egyes kapcsolók előtti állapotot, hogy a
/// kikapcsolás pontosan visszaállíthasson, és kezeli a Win+E hook
/// élettartamát.
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
public sealed class ShellIntegrationCoordinator(ISettingsService settings) : IDisposable
{
    private readonly WinEHookService _winEHook = new();

    /// <summary>A főablakot kell előtérbe hozni — a Win+E hook ezt jelzi, a nézet iratkozik fel rá.</summary>
    public event EventHandler? ActivationRequested;

    public ShellIntegrationSettings State => settings.Current.ShellIntegration;

    /// <summary>
    /// A mentett kapcsolók újraalkalmazása induláskor. A registry-alapú
    /// átirányítások (mappa-megnyitás, jobbklikk-menü) már a registryben
    /// élnek, újraírásra nincs szükség — csak a futásidejű Win+E hookot kell
    /// újraindítani, mert az a folyamat élettartamához kötött.
    /// </summary>
    public void ApplyInitial()
    {
        if (State.WinERedirectEnabled)
        {
            _winEHook.Start();
        }

        _winEHook.WinEPressed += (_, _) => ActivationRequested?.Invoke(this, EventArgs.Empty);
    }

    public (bool success, string? error) SetFolderOpenRedirect(bool enabled, string exePath)
    {
        try
        {
            if (enabled)
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
            }
            else
            {
                ShellIntegrationService.RestoreDirectoryOpen(
                    new RegistryBackup(State.DirectoryBackupCaptured, State.DirectoryBackupExisted, State.DirectoryBackupValue));
                ShellIntegrationService.RestoreDriveOpen(
                    new RegistryBackup(State.DriveBackupCaptured, State.DriveBackupExisted, State.DriveBackupValue));

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

    public void SetWinERedirect(bool enabled)
    {
        if (enabled)
        {
            _winEHook.Start();
        }
        else
        {
            _winEHook.Stop();
        }

        State.WinERedirectEnabled = enabled;
        settings.NotifyChanged();
    }

    /// <summary>„Minden visszaállítása alapértelmezettre" — mindhárom kapcsoló kikapcsolása, pontos visszaállítással.</summary>
    public (bool success, string? error) ResetAll(string exePath)
    {
        var (folderOk, folderError) = SetFolderOpenRedirect(false, exePath);
        var (menuOk, menuError) = SetContextMenuEntry(false, exePath, string.Empty, string.Empty);
        SetWinERedirect(false);

        return folderOk && menuOk ? (true, null) : (false, folderError ?? menuError);
    }

    public void Dispose() => _winEHook.Dispose();
}
