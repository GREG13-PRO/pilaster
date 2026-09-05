using System;
using System.IO;
using Microsoft.Win32;
using Pilaster.Setup.Constants;
using Pilaster.Shell.Integration;

namespace Pilaster.Setup.Services;

/// <summary>
/// Azok a registry-bejegyzések, amiket a korábbi installer/Pilaster.iss
/// [Registry] szakasza kezelt, de amikhez ma nem létezik C# megfelelő
/// (ProgID/fájltársítás, "Indítás a Windowsszal" Run-kulcs, az eltávolítás
/// Vezérlőpult-bejegyzése). A jobbklikk-verbek és az alapértelmezett
/// fájlkezelő átirányítás NEM itt van — azokhoz lásd
/// Pilaster.Shell.Integration.ShellIntegrationService, amit ez az osztály
/// hív, nem duplikál.
/// </summary>
public static class SetupRegistration
{
    private const string ProgIdKey = @"Software\Classes\Pilaster.Folder";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "Pilaster";
    private const string UninstallKeyRoot = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string DefaultFmBackupKey = @"Software\Pilaster\Setup\DefaultFmBackup";

    public static void RegisterFileAssociations(string exePath)
    {
        using var progId = Registry.CurrentUser.CreateSubKey(ProgIdKey);
        progId.SetValue(null, "Pilaster mappa");

        using (var iconKey = progId.CreateSubKey("DefaultIcon"))
        {
            iconKey.SetValue(null, $"{exePath},0");
        }

        using var commandKey = progId.CreateSubKey(@"shell\open\command");
        commandKey.SetValue(null, $"\"{exePath}\" \"%1\"");
    }

    public static void UnregisterFileAssociations() =>
        Registry.CurrentUser.DeleteSubKeyTree(ProgIdKey, throwOnMissingSubKey: false);

    public static void SetStartWithWindows(string exePath)
    {
        using var runKey = Registry.CurrentUser.CreateSubKey(RunKey);
        runKey.SetValue(RunValueName, $"\"{exePath}\"");
    }

    public static void RemoveStartWithWindows()
    {
        using var runKey = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        runKey?.DeleteValue(RunValueName, throwOnMissingValue: false);
    }

    /// <summary>
    /// A korábbi állapot elmentése HKCU alá, hogy eltávolításkor PONTOSAN
    /// visszaállítható legyen (nem csak törölhető) — jobb, mint amit a
    /// korábbi installer valaha is csinált (az csak törölt).
    /// </summary>
    public static void SaveDefaultFileManagerBackup(RegistryBackup directoryBackup, RegistryBackup driveBackup)
    {
        using var backupKey = Registry.CurrentUser.CreateSubKey(DefaultFmBackupKey);
        backupKey.SetValue("DirectoryExisted", directoryBackup.Existed ? 1 : 0);
        backupKey.SetValue("DirectoryCommand", directoryBackup.CommandValue ?? string.Empty);
        backupKey.SetValue("DriveExisted", driveBackup.Existed ? 1 : 0);
        backupKey.SetValue("DriveCommand", driveBackup.CommandValue ?? string.Empty);
    }

    public static (RegistryBackup Directory, RegistryBackup Drive)? LoadDefaultFileManagerBackup()
    {
        using var backupKey = Registry.CurrentUser.OpenSubKey(DefaultFmBackupKey);

        if (backupKey is null)
        {
            return null;
        }

        var directoryExisted = (int)(backupKey.GetValue("DirectoryExisted") ?? 0) != 0;
        var directoryCommand = backupKey.GetValue("DirectoryCommand") as string;
        var driveExisted = (int)(backupKey.GetValue("DriveExisted") ?? 0) != 0;
        var driveCommand = backupKey.GetValue("DriveCommand") as string;

        var directoryBackup = new RegistryBackup(
            Captured: true, Existed: directoryExisted, CommandValue: string.IsNullOrEmpty(directoryCommand) ? null : directoryCommand);
        var driveBackup = new RegistryBackup(
            Captured: true, Existed: driveExisted, CommandValue: string.IsNullOrEmpty(driveCommand) ? null : driveCommand);

        return (directoryBackup, driveBackup);
    }

    public static void RemoveDefaultFileManagerBackup() =>
        Registry.CurrentUser.DeleteSubKeyTree(DefaultFmBackupKey, throwOnMissingSubKey: false);

    public static void RegisterUninstaller(string installDir, string uninstallExePath, long estimatedSizeKb)
    {
        using var key = Registry.CurrentUser.CreateSubKey($@"{UninstallKeyRoot}\{{{SetupInfo.AppId}}}");
        key.SetValue("DisplayName", SetupInfo.AppName);
        key.SetValue("DisplayVersion", System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0");
        key.SetValue("Publisher", SetupInfo.Publisher);
        key.SetValue("DisplayIcon", Path.Combine(installDir, SetupInfo.AppExeName));
        key.SetValue("InstallLocation", installDir);
        key.SetValue("EstimatedSize", (int)estimatedSizeKb, RegistryValueKind.DWord);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("UninstallString", $"\"{uninstallExePath}\" /uninstall");
        key.SetValue("QuietUninstallString", $"\"{uninstallExePath}\" /uninstall /S");
    }

    public static void UnregisterUninstaller() =>
        Registry.CurrentUser.DeleteSubKeyTree($@"{UninstallKeyRoot}\{{{SetupInfo.AppId}}}", throwOnMissingSubKey: false);
}
