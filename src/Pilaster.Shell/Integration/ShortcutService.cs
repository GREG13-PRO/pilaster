using System.IO;
using System.Runtime.InteropServices;
using Vanara.Windows.Shell;

namespace Pilaster.Shell.Integration;

/// <summary>
/// Parancsikonok (<c>.lnk</c>) létrehozása — az <c>Alt</c>+húzás művelete a
/// panelek között (spec F7).
/// </summary>
public static class ShortcutService
{
    /// <summary>
    /// Parancsikon készítése <paramref name="targetPath"/>-ra a megadott
    /// mappában, az Intézőével azonos elnevezéssel („Név – parancsikon.lnk"),
    /// ütközés esetén sorszámozva.
    /// </summary>
    /// <returns>A létrejött parancsikon útvonala, vagy <c>null</c> hiba esetén.</returns>
    public static string? TryCreate(string targetPath, string destinationDirectory, string shortcutSuffix)
    {
        try
        {
            var baseName = Path.GetFileNameWithoutExtension(Path.TrimEndingDirectorySeparator(targetPath));

            if (string.IsNullOrEmpty(baseName))
            {
                baseName = Path.TrimEndingDirectorySeparator(targetPath).Replace(":", string.Empty);
            }

            var linkPath = MakeUniquePath(destinationDirectory, $"{baseName} - {shortcutSuffix}", ".lnk");

            using var link = new ShellLink(linkPath, targetPath)
            {
                WorkingDirectory = Directory.Exists(targetPath)
                    ? targetPath
                    : Path.GetDirectoryName(targetPath) ?? string.Empty,
            };

            link.SaveAs(linkPath);
            return linkPath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException or ArgumentException)
        {
            // A parancsikon-készítés kényelmi funkció — egy írásvédett
            // célmappa vagy hosszú útvonal miatt nem szabad hibaüzenettel
            // megszakítani a húzást.
            return null;
        }
    }

    /// <summary>Ütközésmentes fájlnév: „név.lnk", „név (2).lnk", …</summary>
    private static string MakeUniquePath(string directory, string baseName, string extension)
    {
        var candidate = Path.Combine(directory, baseName + extension);

        for (var index = 2; File.Exists(candidate) && index < 1000; index++)
        {
            candidate = Path.Combine(directory, $"{baseName} ({index}){extension}");
        }

        return candidate;
    }
}
