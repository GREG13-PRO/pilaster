using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vanara.PInvoke;

namespace Pilaster.Shell.Devices;

/// <summary>
/// Egy behelyezett optikai lemez saját ikonjának feloldása.
/// </summary>
/// <remarks>
/// Sorrend: <c>autorun.inf</c> <c>[autorun]</c> szakaszának <c>icon=</c>
/// bejegyzése; ennek hiányában bármelyik <c>.ico</c> fájl a gyökérben;
/// egyébként <c>null</c> — ekkor a hívó az általános CD-ikonnal marad.
/// </remarks>
public static class DiscIconResolver
{
    public static ImageSource? TryResolve(string driveRoot)
    {
        try
        {
            var fromAutorun = TryResolveFromAutorun(driveRoot);

            if (fromAutorun is not null)
            {
                return fromAutorun;
            }

            var fallbackIco = Directory.EnumerateFiles(driveRoot, "*.ico").FirstOrDefault();

            return fallbackIco is not null ? LoadIcoFile(fallbackIco) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static ImageSource? TryResolveFromAutorun(string driveRoot)
    {
        var autorunPath = Path.Combine(driveRoot, "autorun.inf");

        if (!File.Exists(autorunPath))
        {
            return null;
        }

        var iconEntry = ReadAutorunIconEntry(autorunPath);

        if (string.IsNullOrWhiteSpace(iconEntry))
        {
            return null;
        }

        // "setup.exe,0" alak: útvonal + ikonindex egy futtatható/DLL erőforrásából.
        var commaIndex = iconEntry.LastIndexOf(',');

        if (commaIndex > 0 && int.TryParse(iconEntry[(commaIndex + 1)..].Trim(), out var iconIndex))
        {
            var resourcePath = ResolvePath(driveRoot, iconEntry[..commaIndex].Trim());
            return resourcePath is not null ? ExtractIconResource(resourcePath, iconIndex) : null;
        }

        var icoPath = ResolvePath(driveRoot, iconEntry);
        return icoPath is not null ? LoadIcoFile(icoPath) : null;
    }

    /// <summary>
    /// Minimális INI-olvasás: csak az <c>[autorun]</c> szakasz <c>icon=</c>
    /// kulcsát keresi, kis-nagybetűtől függetlenül — nincs szükség teljes
    /// INI-elemzőre egyetlen bejegyzéshez.
    /// </summary>
    private static string? ReadAutorunIconEntry(string autorunPath)
    {
        var inAutorunSection = false;

        foreach (var rawLine in File.ReadLines(autorunPath))
        {
            var line = rawLine.Trim();

            if (line.Length == 0 || line.StartsWith(';'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inAutorunSection = string.Equals(line[1..^1], "autorun", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inAutorunSection)
            {
                continue;
            }

            var equalsIndex = line.IndexOf('=');

            if (equalsIndex <= 0)
            {
                continue;
            }

            var key = line[..equalsIndex].Trim();

            if (string.Equals(key, "icon", StringComparison.OrdinalIgnoreCase))
            {
                return line[(equalsIndex + 1)..].Trim();
            }
        }

        return null;
    }

    private static string? ResolvePath(string driveRoot, string relativeOrAbsolute)
    {
        try
        {
            var full = Path.IsPathRooted(relativeOrAbsolute)
                ? relativeOrAbsolute
                : Path.Combine(driveRoot, relativeOrAbsolute);

            return File.Exists(full) ? full : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static ImageSource? LoadIcoFile(string path)
    {
        try
        {
            var decoder = new IconBitmapDecoder(
                new Uri(path, UriKind.Absolute),
                BitmapCreateOptions.None,
                BitmapCacheOption.OnLoad);

            // A legnagyobb felbontású keret adja a legjobb minőséget kicsinyítve.
            var frame = decoder.Frames.OrderByDescending(f => f.PixelWidth).FirstOrDefault();
            frame?.Freeze();

            return frame;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException)
        {
            return null;
        }
    }

    private static ImageSource? ExtractIconResource(string path, int iconIndex)
    {
        try
        {
            var large = new HICON[1];
            var extracted = Shell32.ExtractIconEx(path, iconIndex, large, null, 1);

            if (extracted == 0 || large[0].IsNull)
            {
                return null;
            }

            try
            {
                var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                    large[0].DangerousGetHandle(),
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());

                source.Freeze();
                return source;
            }
            finally
            {
                User32.DestroyIcon(large[0]);
            }
        }
        catch (Exception ex) when (ex is NotSupportedException or COMException)
        {
            return null;
        }
    }
}
