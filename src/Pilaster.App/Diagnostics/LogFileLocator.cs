using System.IO;

namespace Pilaster.App.Diagnostics;

/// <summary>A naplófájlok helye és a legutóbbi megkeresése.</summary>
public static class LogFileLocator
{
    /// <summary>
    /// A naplók a helyi (nem roaming) profilban élnek, mert géphez kötöttek és
    /// idővel nagyra nőhetnek — ezzel szemben a beállítások (<c>%APPDATA%</c>)
    /// szándékosan roaming, mert azokat érdemes gépek közt vinni.
    /// </summary>
    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Pilaster",
        "logs");

    /// <summary>A legutóbb módosított naplófájl, vagy <c>null</c>, ha nincs.</summary>
    public static string? FindLatest()
    {
        if (!Directory.Exists(LogDirectory))
        {
            return null;
        }

        return new DirectoryInfo(LogDirectory)
            .EnumerateFiles("pilaster-*.log")
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault()
            ?.FullName;
    }
}
