using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Pilaster.Shell.Imaging;

/// <summary>
/// Ikon betöltése egy registry-stílusú <c>"fájl,index"</c> ikonerőforrásból
/// (pl. <c>OneDrive.exe,5</c>) — pontosan abból, amit az Intéző is kirajzol egy
/// navigációs-panel bejegyzéshez (<c>DefaultIcon</c>) vagy szinkrongyökérhez
/// (<c>IconResource</c>).
/// </summary>
/// <remarks>
/// Az <c>IShellItemImageFactory</c> egy <c>::{CLSID}</c> névtér-elemre nem
/// mindig a kliens ikonját adja (a OneDrive-nál MÉRVE egy üres
/// dokumentum-ikont) — az erőforrásból közvetlenül kinyert ikon viszont mindig
/// az, amit a kliens regisztrált.
/// </remarks>
public static class IconResourceLoader
{
    /// <summary>
    /// A megadott erőforrás ikonja a kért pixelméretben, vagy <c>null</c>, ha
    /// az erőforrás nem olvasható. Negatív index erőforrás-azonosítót jelent
    /// (a Windows konvenciója szerint).
    /// </summary>
    public static ImageSource? Load(string? resource, int size)
    {
        if (!TryParse(resource, out var path, out var index))
        {
            return null;
        }

        var icons = new nint[1];
        var ids = new uint[1];

        if (PrivateExtractIcons(path, index, size, size, icons, ids, 1, 0) == 0 || icons[0] == 0)
        {
            return null;
        }

        try
        {
            var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(icons[0], Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
        finally
        {
            DestroyIcon(icons[0]);
        }
    }

    /// <summary><c>"C:\x\app.exe,-5"</c> → útvonal és index; idézőjeleket és környezeti változókat is kezel.</summary>
    public static bool TryParse(string? resource, out string path, out int index)
    {
        path = string.Empty;
        index = 0;

        if (string.IsNullOrWhiteSpace(resource))
        {
            return false;
        }

        var text = resource.Trim();
        var comma = text.LastIndexOf(',');

        if (comma > 0 && int.TryParse(text[(comma + 1)..].Trim(), out var parsed))
        {
            index = parsed;
            text = text[..comma];
        }

        path = Environment.ExpandEnvironmentVariables(text.Trim().Trim('"'));

        if (File.Exists(path))
        {
            return true;
        }

        // A kliens frissítése után a regisztrált exe néha már nem létezik — a
        // OneDrive MÉRVE átnevezte magát (OneDrive.exe → OneDrive.App.exe), de a
        // DefaultIcon és a desktop.ini még a régire mutat, így az Intéző is
        // üres ikont rajzol. Ilyenkor az ugyanabban a mappában lévő, azonos
        // nevű utódot használjuk — annak a FŐ ikonjával, mert a régi index
        // az új fájlban mást jelentene.
        if (TryFindRenamedSuccessor(path) is { } successor)
        {
            path = successor;
            index = 0;
            return true;
        }

        return false;
    }

    private static string? TryFindRenamedSuccessor(string missingPath)
    {
        try
        {
            var directory = Path.GetDirectoryName(missingPath);
            var stem = Path.GetFileNameWithoutExtension(missingPath);
            var extension = Path.GetExtension(missingPath);

            if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(stem) || !Directory.Exists(directory))
            {
                return null;
            }

            return Directory.EnumerateFiles(directory, $"{stem}.*{extension}").FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint PrivateExtractIcons(string szFileName, int nIconIndex, int cxIcon, int cyIcon, nint[] phicon, uint[] piconid, uint nIcons, uint flags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(nint hIcon);
}
