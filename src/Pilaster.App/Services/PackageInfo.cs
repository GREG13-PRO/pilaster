using System.Runtime.InteropServices;

namespace Pilaster.App.Services;

/// <summary>
/// Megmondja, hogy MSIX-csomagból (Microsoft Store) futunk-e.
/// </summary>
/// <remarks>
/// Csomagolt módban a frissítést a Store végzi, a HKCU\Software\Classes alá
/// írt registry-kulcsok pedig a csomag saját, virtualizált registryjébe
/// kerülnek — az Intéző nem látja őket. Ezért ilyenkor a beépített frissítő
/// és a registry-alapú rendszerintegráció (mappák megnyitása, Intéző
/// jobbklikk-bejegyzés) ki van kapcsolva. Ugyanaz a bináris fut mindkét
/// módban, külön fordítás nem kell.
/// </remarks>
public static class PackageInfo
{
    private const int AppModelErrorNoPackage = 15700;

    private static readonly Lazy<bool> LazyIsPackaged = new(Detect);

    public static bool IsPackaged => LazyIsPackaged.Value;

    /// <summary>A winget-csomag azonosítója — a frissítési tipp ezt mutatja.</summary>
    public const string WingetPackageId = "GREG13-PRO.Pilaster";

    /// <summary>
    /// Igaz, ha a winget telepítette (hordozható csomagként, a
    /// <c>…\WinGet\Packages\</c> mappába). Ilyenkor a frissítést is a winget
    /// végzi: a beépített frissítő a saját telepítőjét futtatná a winget
    /// mappájába, ami összekeverné a winget nyilvántartását.
    /// </summary>
    public static bool IsWingetInstall { get; } =
        Environment.ProcessPath is { } exe && exe.Contains(@"\WinGet\Packages\", StringComparison.OrdinalIgnoreCase);

    private static bool Detect()
    {
        try
        {
            var length = 0;
            return GetCurrentPackageFullName(ref length, null) != AppModelErrorNoPackage;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, char[]? packageFullName);
}
