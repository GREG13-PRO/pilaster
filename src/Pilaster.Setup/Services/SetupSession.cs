using CommunityToolkit.Mvvm.ComponentModel;
using Pilaster.Setup.Constants;

namespace Pilaster.Setup.Services;

/// <summary>
/// A varázsló futása alatt gyűjtött összes választás és állapot — az oldalak
/// (Views/Pages) ezen keresztül kommunikálnak egymással és az orchestrátorral.
/// Egyetlen példány él a folyamat végéig, a MainWindow hozza létre és adja
/// tovább minden oldalnak.
/// </summary>
public sealed partial class SetupSession : ObservableObject
{
    /// <summary>Igaz, ha a folyamat eltávolítás módban indult (lásd App — <c>/uninstall</c> argumentum).</summary>
    public bool IsUninstall { get; init; }

    /// <summary>
    /// A telepítendő fájlok forrása — a Setup.exe mellett lévő "Payload" mappa.
    /// Eltávolítás módban nem használt.
    /// </summary>
    public required string PayloadDirectory { get; init; }

    [ObservableProperty]
    public partial string InstallDirectory { get; set; } = SetupInfo.DefaultInstallDir;

    [ObservableProperty]
    public partial bool CreateDesktopShortcut { get; set; } = true;

    [ObservableProperty]
    public partial bool CreateStartMenuShortcut { get; set; } = true;

    [ObservableProperty]
    public partial bool EnableContextMenu { get; set; } = true;

    [ObservableProperty]
    public partial bool EnableFileAssociations { get; set; }

    [ObservableProperty]
    public partial bool StartWithWindows { get; set; }

    [ObservableProperty]
    public partial bool MakeDefaultFileManager { get; set; }

    [ObservableProperty]
    public partial bool LaunchAfterFinish { get; set; } = true;

    /// <summary>Eltávolításkor: törölje-e a felhasználó beállításait/címkéit is. Alapértéken NEM (spec: nem szabad regresszálnia a korábbi hibát).</summary>
    [ObservableProperty]
    public partial bool DeleteSettingsOnUninstall { get; set; }

    /// <summary>A telepítés/eltávolítás közben mutatott folyamat-szöveg (aktuális fájl, sebesség stb.).</summary>
    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double ProgressFraction { get; set; }

    [ObservableProperty]
    public partial bool OperationSucceeded { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }
}
