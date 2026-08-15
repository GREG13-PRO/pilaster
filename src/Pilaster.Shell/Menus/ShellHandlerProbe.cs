using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Pilaster.Shell.Menus;

/// <summary>
/// Egy shell-bővítmény mért ideje: mennyibe került a példányosítása és a
/// menüjének lekérdezése.
/// </summary>
/// <param name="Clsid">A bővítmény osztályazonosítója.</param>
/// <param name="DisplayName">A regisztrációs név; üres, ha a bővítmény nem adott meg ilyet.</param>
/// <param name="ModulePath">A kiszolgáló DLL útvonala — ebből derül ki, melyik termékhez tartozik.</param>
/// <param name="CreateMs">A <c>CoCreateInstance</c> ideje ezredmásodpercben.</param>
/// <param name="QueryMs">Az <c>IShellExtInit::Initialize</c> + <c>QueryContextMenu</c> ideje ezredmásodpercben.</param>
/// <param name="Failed">Igaz, ha a bővítmény hibát adott — az ilyen mérés csak a ráfordított időt mutatja.</param>
public sealed record ShellHandlerTiming(
    Guid Clsid,
    string DisplayName,
    string ModulePath,
    long CreateMs,
    long QueryMs,
    bool Failed)
{
    /// <summary>A teljes ráfordított idő — a rangsorolás alapja.</summary>
    public long TotalMs => CreateMs + QueryMs;
}

/// <summary>
/// DIAGNOSZTIKA: a shell-bővítmények EGYENKÉNTI megmérése.
/// </summary>
/// <remarks>
/// <para>
/// A rendes menülekérdezés (<see cref="ShellMenuSession"/>) EGYETLEN
/// <c>IContextMenu</c>-t kap a shelltől, ami magában már összefogja az összes
/// regisztrált bővítményt — abból nem lehet megmondani, melyik mennyibe került.
/// Ez az osztály ezért megkerüli a shellt: a registryből maga szedi össze a
/// kezelőket, egyenként példányosítja őket, és külön méri a
/// <c>CoCreateInstance</c> és a <c>QueryContextMenu</c> idejét.
/// </para>
/// <para>
/// Ez a mérés SZÁNDÉKOSAN nem azonos a rendes úttal (a shell párhuzamosít és
/// gyorsítótáraz), tehát az összeg nem egyezik a menü teljes idejével. Arra
/// való, hogy a KIUGRÓ kezelőket megnevezze — nem teljesítménygaranciának.
/// </para>
/// <para>
/// Csak akkor fut, ha a naplózás szintje <c>Debug</c> (Beállítások →
/// Speciális), mert minden bővítményt betölt, és ez önmagában másodpercekig
/// tarthat.
/// </para>
/// </remarks>
public static class ShellHandlerProbe
{
    private const uint FirstCommandId = 1;
    private const uint LastCommandId = 0x7FFF;
    private const uint CmfNormal = 0;

    /// <summary>Efölött érdemes megfontolni a bővítmény kikapcsolását (spec T1).</summary>
    public const long SlowThresholdMs = 400;

    /// <summary>
    /// Egy fájl vagy mappa shell-kezelőinek egyenkénti megmérése.
    /// </summary>
    /// <param name="path">A megmérendő elem teljes útvonala.</param>
    /// <returns>A kezelők mérései, a leglassabbal kezdve.</returns>
    public static IReadOnlyList<ShellHandlerTiming> Measure(string path)
    {
        var results = new List<ShellHandlerTiming>();
        var isDirectory = Directory.Exists(path);

        // A mérés STA szálat kíván, akárcsak az éles lekérdezés: a
        // shell-bővítmények apartment-kötöttek.
        var thread = new Thread(() =>
        {
            if (NativeMenuInterop.CoInitializeEx(nint.Zero, NativeMenuInterop.CoInitApartmentThreaded) < 0)
            {
                return;
            }

            try
            {
                results.AddRange(MeasureCore(path, isDirectory));
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException or UnauthorizedAccessException)
            {
                // A diagnosztika hibája sosem akadályozhatja a programot.
            }
            finally
            {
                NativeMenuInterop.CoUninitialize();
            }
        })
        {
            IsBackground = true,
            Name = "Pilaster.HandlerProbe",
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(60));

        return [.. results.OrderByDescending(r => r.TotalMs)];
    }

    private static List<ShellHandlerTiming> MeasureCore(string path, bool isDirectory)
    {
        var results = new List<ShellHandlerTiming>();
        var dataObject = CreateDataObject(path);

        foreach (var clsid in EnumerateHandlers(path, isDirectory))
        {
            var (displayName, modulePath) = DescribeClass(clsid);
            var createWatch = Stopwatch.StartNew();
            object? instance = null;
            var failed = false;

            try
            {
                var type = Type.GetTypeFromCLSID(clsid);
                instance = type is null ? null : Activator.CreateInstance(type);
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException or MissingMethodException or TypeLoadException or NotSupportedException)
            {
                failed = true;
            }

            createWatch.Stop();

            if (instance is null)
            {
                results.Add(new ShellHandlerTiming(clsid, displayName, modulePath, createWatch.ElapsedMilliseconds, 0, Failed: true));
                continue;
            }

            var queryWatch = Stopwatch.StartNew();
            var hMenu = NativeMenuInterop.CreatePopupMenu();

            try
            {
                if (instance is IShellExtInit init && dataObject is not null)
                {
                    init.Initialize(nint.Zero, dataObject, nint.Zero);
                }

                if (instance is IContextMenuProbe menu && hMenu != nint.Zero)
                {
                    menu.QueryContextMenu(hMenu, 0, FirstCommandId, LastCommandId, CmfNormal);
                }
                else
                {
                    failed = true;
                }
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException or NotImplementedException or ArgumentException)
            {
                failed = true;
            }
            finally
            {
                queryWatch.Stop();

                if (hMenu != nint.Zero)
                {
                    NativeMenuInterop.DestroyMenu(hMenu);
                }

                if (Marshal.IsComObject(instance))
                {
                    Marshal.ReleaseComObject(instance);
                }
            }

            results.Add(new ShellHandlerTiming(
                clsid, displayName, modulePath, createWatch.ElapsedMilliseconds, queryWatch.ElapsedMilliseconds, failed));
        }

        if (dataObject is not null && Marshal.IsComObject(dataObject))
        {
            Marshal.ReleaseComObject(dataObject);
        }

        return results;
    }

    /// <summary>
    /// A kezelő registry-beli helyei. A shell ugyanezeket a kulcsokat járja
    /// be, ezért a lista együtt mozog a valódi menüvel.
    /// </summary>
    private static IEnumerable<Guid> EnumerateHandlers(string path, bool isDirectory)
    {
        var keys = new List<string>();

        if (isDirectory)
        {
            keys.Add(@"Directory\shellex\ContextMenuHandlers");
            keys.Add(@"Directory\Background\shellex\ContextMenuHandlers");
            keys.Add(@"Folder\shellex\ContextMenuHandlers");
        }
        else
        {
            var extension = Path.GetExtension(path);

            if (extension.Length > 0)
            {
                keys.Add($@"{extension}\shellex\ContextMenuHandlers");
                keys.Add($@"SystemFileAssociations\{extension}\shellex\ContextMenuHandlers");

                // A kiterjesztéshez tartozó ProgID saját kezelői (pl. a
                // „Photo.png" típushoz kötött menüpontok).
                if (Registry.ClassesRoot.OpenSubKey(extension)?.GetValue(null) is string progId && progId.Length > 0)
                {
                    keys.Add($@"{progId}\shellex\ContextMenuHandlers");
                }
            }
        }

        // Minden fájlrendszer-elemre érvényes kezelők.
        keys.Add(@"*\shellex\ContextMenuHandlers");
        keys.Add(@"AllFilesystemObjects\shellex\ContextMenuHandlers");

        var seen = new HashSet<Guid>();

        foreach (var keyPath in keys)
        {
            using var key = Registry.ClassesRoot.OpenSubKey(keyPath);

            if (key is null)
            {
                continue;
            }

            foreach (var name in key.GetSubKeyNames())
            {
                using var entry = key.OpenSubKey(name);

                // A CLSID vagy az alkulcs alapértelmezett értékében áll, vagy
                // maga az alkulcs neve a CLSID.
                var raw = entry?.GetValue(null) as string;
                var candidate = raw is { Length: > 0 } ? raw : name;

                if (Guid.TryParse(candidate.Trim('{', '}'), out var clsid) && seen.Add(clsid))
                {
                    yield return clsid;
                }
            }
        }
    }

    /// <summary>A bővítmény olvasható neve és kiszolgáló DLL-je a registryből.</summary>
    private static (string DisplayName, string ModulePath) DescribeClass(Guid clsid)
    {
        var key = $@"CLSID\{{{clsid:D}}}";

        using var classKey = Registry.ClassesRoot.OpenSubKey(key);

        if (classKey is null)
        {
            return (string.Empty, string.Empty);
        }

        var displayName = classKey.GetValue(null) as string ?? string.Empty;

        using var server = classKey.OpenSubKey("InprocServer32");
        var modulePath = server?.GetValue(null) as string ?? string.Empty;

        return (displayName, modulePath);
    }

    /// <summary>
    /// A kijelölést leíró <c>IDataObject</c> — a kezelők ebből tudják meg,
    /// mire kellene menüt adniuk.
    /// </summary>
    private static object? CreateDataObject(string path)
    {
        try
        {
            var itemGuid = typeof(IShellItemProbe).GUID;

            if (SHCreateItemFromParsingName(path, nint.Zero, itemGuid, out var item) < 0 || item is null)
            {
                return null;
            }

            try
            {
                var arrayGuid = new Guid("B63EA76D-1F85-456F-A19C-48159EFA858B"); // IID_IShellItemArray

                if (SHCreateShellItemArrayFromShellItem(item, arrayGuid, out var array) < 0 || array is null)
                {
                    return null;
                }

                try
                {
                    var dataGuid = new Guid("0000010E-0000-0000-C000-000000000046"); // IID_IDataObject
                    var handlerGuid = new Guid("B8C0BD9F-ED24-455C-83E6-D5390C4FE8C4"); // BHID_DataObject

                    return array.BindToHandler(nint.Zero, handlerGuid, dataGuid, out var data) >= 0 ? data : null;
                }
                finally
                {
                    Marshal.ReleaseComObject(array);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(item);
            }
        }
        catch (Exception ex) when (ex is COMException or ArgumentException)
        {
            return null;
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(
        string path,
        nint bindContext,
        in Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemProbe? item);

    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int SHCreateShellItemArrayFromShellItem(
        IShellItemProbe item,
        in Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemArrayProbe? array);

    /// <summary>
    /// Az <c>IContextMenu</c> minimális deklarációja.
    /// </summary>
    /// <remarks>
    /// Saját deklaráció, mert itt csak a <c>QueryContextMenu</c> kell, és így a
    /// mérés nem függ attól, hogy egy bővítmény a többi metódust helyesen
    /// valósítja-e meg.
    /// </remarks>
    [ComImport]
    [Guid("000214E4-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenuProbe
    {
        [PreserveSig]
        int QueryContextMenu(nint hMenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint flags);
    }

    /// <summary>A bővítmény ezen keresztül kapja meg, mire adjon menüt.</summary>
    [ComImport]
    [Guid("000214E8-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellExtInit
    {
        [PreserveSig]
        int Initialize(nint pidlFolder, [MarshalAs(UnmanagedType.Interface)] object dataObject, nint hkeyProgId);
    }

    [ComImport]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemProbe
    {
        [PreserveSig]
        int BindToHandler(nint bindContext, in Guid bhid, in Guid riid, [MarshalAs(UnmanagedType.Interface)] out object? result);

        [PreserveSig]
        int GetParent([MarshalAs(UnmanagedType.Interface)] out IShellItemProbe? parent);

        [PreserveSig]
        int GetDisplayName(uint sigdnName, out nint name);

        [PreserveSig]
        int GetAttributes(uint mask, out uint attributes);

        [PreserveSig]
        int Compare(IShellItemProbe other, uint hint, out int order);
    }

    [ComImport]
    [Guid("B63EA76D-1F85-456F-A19C-48159EFA858B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemArrayProbe
    {
        [PreserveSig]
        int BindToHandler(nint bindContext, in Guid bhid, in Guid riid, [MarshalAs(UnmanagedType.Interface)] out object? result);
    }
}
