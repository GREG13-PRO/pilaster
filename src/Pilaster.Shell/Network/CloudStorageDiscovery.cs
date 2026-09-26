using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace Pilaster.Shell.Network;

/// <summary>Egy felhőszinkron-kliens (OneDrive, Nextcloud, Dropbox, iCloud, Box …) helyi gyökérmappája.</summary>
/// <param name="Name">Megjelenített név, ahogy az Intéző navigációs paneljén is látszik.</param>
/// <param name="Path">A szinkronizált mappa helyi útvonala.</param>
/// <param name="IconResource">
/// A kliens által regisztrált ikon <c>"fájl,index"</c> alakban (lásd
/// <see cref="Pilaster.Shell.Imaging.IconResourceLoader"/>), vagy <c>null</c> —
/// ilyenkor magának a mappának a shell-ikonja.
/// </param>
public sealed record CloudStorageRoot(string Name, string Path, string? IconResource);

/// <summary>
/// A telepített felhőszinkron-kliensek felderítése — pontosan azokból a
/// forrásokból, amelyekből a Windows Intéző navigációs panelje is építkezik.
/// </summary>
/// <remarks>
/// <para>
/// A kliensek KÉT módon jelennek meg az Intézőben, és a legtöbb mindkettőt
/// használja:
/// </para>
/// <list type="number">
/// <item>
/// <b>Navigációs-panel névtér-bejegyzés</b>
/// (<c>HKCU\…\Explorer\Desktop\NameSpace\{CLSID}</c>, <c>System.IsPinnedToNameSpaceTree=1</c>):
/// a célmappa vagy közvetlenül (<c>TargetFolderPath</c>), vagy ismert mappaként
/// (<c>TargetKnownFolder</c> — így a OneDrive) adott. Innen jön a kliens saját
/// neve és ikonja.
/// </item>
/// <item>
/// <b>Cloud Files szinkrongyökér</b>
/// (<c>HKLM\…\Explorer\SyncRootManager\…\UserSyncRoots\{SID}</c>): minden
/// „fájlok igény szerint" kliens (OneDrive, Nextcloud VFS, Dropbox, iCloud, Box)
/// ide regisztrál — azok is, amelyek nem kérnek saját navigációs-panel sort.
/// </item>
/// </list>
/// <para>
/// A két forrás útvonal szerint összefésülődik. Csak LÉTEZŐ helyi mappára
/// mutató bejegyzés kerül be — így kimaradnak a navigációs panel mappa nélküli
/// elemei (Kezdőlap, Galéria, Linux).
/// </para>
/// </remarks>
public static class CloudStorageDiscovery
{
    private const string NameSpaceKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace";
    private const string SyncRootManagerKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\SyncRootManager";

    public static IReadOnlyList<CloudStorageRoot> Discover()
    {
        var results = new List<CloudStorageRoot>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(CloudStorageRoot root)
        {
            var normalized = System.IO.Path.TrimEndingDirectorySeparator(root.Path);

            if (normalized.Length > 0 && Directory.Exists(normalized) && seen.Add(normalized))
            {
                results.Add(root with { Path = normalized });
            }
        }

        // A névtér-bejegyzések elsőbbséget élveznek: ezekhez tartozik a kliens
        // saját neve és ikonja, ahogy az Intézőben látszik.
        foreach (var root in ReadNameSpaceEntries())
        {
            Add(root);
        }

        foreach (var root in ReadSyncRoots())
        {
            Add(root);
        }

        return results;
    }

    private static IEnumerable<CloudStorageRoot> ReadNameSpaceEntries()
    {
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            string[] clsids;

            try
            {
                using var nameSpace = hive.OpenSubKey(NameSpaceKey);
                clsids = nameSpace?.GetSubKeyNames() ?? [];
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                continue;
            }

            foreach (var clsid in clsids)
            {
                if (TryReadNameSpaceEntry(clsid) is { } root)
                {
                    yield return root;
                }
            }
        }
    }

    private static CloudStorageRoot? TryReadNameSpaceEntry(string clsid)
    {
        try
        {
            // HKCR = a HKCU és HKLM Classes összefésült nézete — a kliensek
            // jellemzően felhasználói szinten (HKCU\Software\Classes) regisztrálnak.
            using var key = Registry.ClassesRoot.OpenSubKey($@"CLSID\{clsid}");

            if (key is null || !IsPinned(key.GetValue("System.IsPinnedToNameSpaceTree")))
            {
                return null;
            }

            using var bag = key.OpenSubKey(@"Instance\InitPropertyBag");
            var path = bag?.GetValue("TargetFolderPath") as string;

            if (string.IsNullOrWhiteSpace(path)
                && bag?.GetValue("TargetKnownFolder") is string knownFolder
                && Guid.TryParse(knownFolder, out var folderId))
            {
                path = TryGetKnownFolderPath(folderId);
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            path = Environment.ExpandEnvironmentVariables(path);
            var name = ResolveIndirectString(key.GetValue(null) as string) ?? System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(path));

            using var defaultIcon = key.OpenSubKey("DefaultIcon");

            return new CloudStorageRoot(name, path, defaultIcon?.GetValue(null) as string);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    private static IEnumerable<CloudStorageRoot> ReadSyncRoots()
    {
        string? sid;

        try
        {
            sid = WindowsIdentity.GetCurrent().User?.Value;
        }
        catch (System.Security.SecurityException)
        {
            yield break;
        }

        if (sid is null)
        {
            yield break;
        }

        string[] ids;

        try
        {
            using var manager = Registry.LocalMachine.OpenSubKey(SyncRootManagerKey);
            ids = manager?.GetSubKeyNames() ?? [];
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            yield break;
        }

        foreach (var id in ids)
        {
            CloudStorageRoot? root = null;

            try
            {
                using var key = Registry.LocalMachine.OpenSubKey($@"{SyncRootManagerKey}\{id}");
                using var roots = key?.OpenSubKey("UserSyncRoots");

                if (roots?.GetValue(sid) is string path && path.Length > 0)
                {
                    var name = ResolveIndirectString(key!.GetValue("DisplayNameResource") as string)
                        ?? System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(path));

                    root = new CloudStorageRoot(name, path, key.GetValue("IconResource") as string);
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
            }

            if (root is not null)
            {
                yield return root;
            }
        }
    }

    private static bool IsPinned(object? value) => value switch
    {
        int i => i != 0,
        string s => s == "1",
        _ => false,
    };

    /// <summary>Egy <c>@dll,-id</c> alakú, indirekt erőforrás-szöveg feloldása; közvetlen szöveget változatlanul ad vissza.</summary>
    private static string? ResolveIndirectString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!value.StartsWith('@'))
        {
            return value;
        }

        var buffer = new StringBuilder(512);

        return SHLoadIndirectString(value, buffer, buffer.Capacity, 0) == 0 && buffer.Length > 0
            ? buffer.ToString()
            : null;
    }

    private static string? TryGetKnownFolderPath(Guid folderId)
    {
        // KF_FLAG_DONT_VERIFY nélkül a hívás meghiúsul, ha a mappa (még) nem
        // létezik — ezt úgyis a hívó Directory.Exists-e szűri.
        if (SHGetKnownFolderPath(folderId, 0, 0, out var pathPtr) != 0)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUni(pathPtr);
        }
        finally
        {
            Marshal.FreeCoTaskMem(pathPtr);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath(in Guid rfid, uint dwFlags, nint hToken, out nint ppszPath);

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int SHLoadIndirectString(string pszSource, StringBuilder pszOutBuf, int cchOutBuf, nint ppvReserved);
}
