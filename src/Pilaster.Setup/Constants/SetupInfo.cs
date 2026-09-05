using System;

namespace Pilaster.Setup.Constants;

/// <summary>
/// Az összes olyan azonosító, aminek szó szerint egyeznie kell a Pilaster.App
/// oldalán lévő megfelelőjével — külön projektek, ezért nincs megosztott
/// állandó, csak fegyelmezett duplikálás egyetlen helyen (ugyanígy tette ezt
/// korábban az installer/Pilaster.iss is).
/// </summary>
public static class SetupInfo
{
    /// <summary>Kell egyeznie: Pilaster.App/App.xaml.cs — AppUserModelId.</summary>
    public const string AppUserModelId = "Obsidix.Pilaster";

    /// <summary>Kell egyeznie: a korábbi installer/Pilaster.iss AppId GUID-jával (frissítés/eltávolítás azonosítója).</summary>
    public static readonly Guid AppId = Guid.Parse("8F3A6C21-5B4E-4D9A-9E27-1C0B7A5D3E64");

    public const string AppName = "Pilaster";
    public const string AppExeName = "Pilaster.exe";
    public const string Publisher = "Pilaster contributors";
    public const string InstallDirName = "Pilaster";
    public const string SetupSubfolderName = "Setup";
    public const string UninstallExeName = "Pilaster.Setup.exe";
    public const string ReleasesUrl = "https://github.com/GREG13-PRO/pilaster/releases";

    public static string DefaultInstallDir =>
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            InstallDirName);
}
