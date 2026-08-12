using Pilaster.Core.FileSystem;

namespace Pilaster.Providers.Local;

/// <summary>Egy csatolt meghajtó adatai az oldalsávhoz.</summary>
/// <param name="Item">A meghajtó mint fájlrendszer-elem.</param>
/// <param name="Label">Megjelenített címke, pl. „Rendszer (C:)".</param>
/// <param name="TotalBytes">Teljes kapacitás, vagy 0, ha ismeretlen.</param>
/// <param name="FreeBytes">Szabad hely, vagy 0, ha ismeretlen.</param>
/// <param name="DriveType">A meghajtó típusa (fix, cserélhető, hálózati…).</param>
public sealed record DriveEntry(
    FileSystemItem Item,
    string Label,
    long TotalBytes,
    long FreeBytes,
    DriveType DriveType)
{
    /// <summary>A kihasználtság 0 és 1 között, a sávdiagramhoz.</summary>
    public double UsedFraction => TotalBytes > 0
        ? Math.Clamp((TotalBytes - FreeBytes) / (double)TotalBytes, 0, 1)
        : 0;
}

/// <summary>A csatolt meghajtók listázása.</summary>
public static class DriveEnumerator
{
    /// <summary>
    /// Az elérhető meghajtók lekérése.
    /// </summary>
    /// <remarks>
    /// A nem kész (<c>IsReady == false</c>) meghajtók is bekerülnek — egy üres
    /// DVD-meghajtó vagy leválasztott hálózati megosztás is látszódjon, csak
    /// méretadat nélkül. Az egyes meghajtók lekérdezése külön try-blokkban fut,
    /// mert egy időtúllépő hálózati meghajtó nem akaszthatja meg a többit.
    /// </remarks>
    public static IReadOnlyList<DriveEntry> GetDrives()
    {
        var results = new List<DriveEntry>();

        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                var ready = drive.IsReady;
                var root = drive.RootDirectory.FullName;

                var label = ready && !string.IsNullOrWhiteSpace(drive.VolumeLabel)
                    ? $"{drive.VolumeLabel} ({drive.Name.TrimEnd(Path.DirectorySeparatorChar)})"
                    : drive.Name.TrimEnd(Path.DirectorySeparatorChar);

                results.Add(new DriveEntry(
                    new FileSystemItem
                    {
                        FullPath = root,
                        Name = label,
                        Kind = FileSystemItemKind.Drive,
                    },
                    label,
                    ready ? drive.TotalSize : 0,
                    ready ? drive.AvailableFreeSpace : 0,
                    drive.DriveType));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Elérhetetlen meghajtó — egyszerűen kimarad a listából.
            }
        }

        return results;
    }
}
