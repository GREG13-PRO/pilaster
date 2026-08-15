using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Pilaster.Shell.Menus;

/// <summary>
/// A menüfa beolvasásához szükséges natív hívások.
/// </summary>
/// <remarks>
/// Szándékosan kézzel írt P/Invoke: a menü-API-k (<c>GetMenuItemInfo</c>,
/// <c>GetObject</c>) itt egyszerű, jól dokumentált struktúrákkal dolgoznak,
/// és a projekt Vanara-csomagjai ezt a szeletet nem fedik le olyan
/// kényelmesen, hogy az extra függőség megérné.
/// </remarks>
internal static class NativeMenuInterop
{
    internal const uint CoInitApartmentThreaded = 0x2;

    [DllImport("ole32.dll")]
    internal static extern int CoInitializeEx(nint reserved, uint coInit);

    [DllImport("ole32.dll")]
    internal static extern void CoUninitialize();

    [DllImport("user32.dll")]
    internal static extern nint CreatePopupMenu();

    [DllImport("user32.dll")]
    internal static extern bool DestroyMenu(nint hMenu);

    [DllImport("user32.dll")]
    internal static extern int GetMenuItemCount(nint hMenu);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMenuItemInfoW")]
    internal static extern bool GetMenuItemInfo(nint hMenu, uint item, bool byPosition, ref MENUITEMINFO info);

    [DllImport("gdi32.dll")]
    internal static extern int GetObject(nint handle, int size, ref BITMAP bitmap);

    [DllImport("gdi32.dll")]
    internal static extern int GetBitmapBits(nint hBitmap, int count, nint bits);

    // --- A fájlmenü NYERS beszerzési útja ---
    //
    // MÉRVE (tools/ShellCrashRepro): a Vanara
    // ShellContextMenu.CreateFromItems 10 körből már a 0. után 0xC0000374
    // heap-korrupcióval viszi a folyamatot, míg UGYANAZ a menetrend nyers
    // P/Invoke-kal 4×10/10 tisztán fut. Ezért a fájlmenü a shell API-ját
    // közvetlenül hívja.

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern int SHParseDisplayName(string name, nint bindContext, out nint pidl, uint sfgaoIn, out uint sfgaoOut);

    [DllImport("shell32.dll")]
    internal static extern int SHBindToParent(nint pidl, ref Guid riid, out nint ppv, out nint pidlLast);

    [DllImport("shell32.dll")]
    internal static extern void ILFree(nint pidl);

    [DllImport("ole32.dll")]
    internal static extern void CoTaskMemFree(nint pv);

    internal static readonly Guid IID_IShellFolder = new("000214E6-0000-0000-C000-000000000046");

    internal static readonly Guid IID_IContextMenu = new("000214E4-0000-0000-C000-000000000046");

    /// <summary>
    /// Az <c>IShellFolder</c> saját deklarációja.
    /// </summary>
    /// <remarks>
    /// A név szándékosan „Raw": a <c>Vanara.PInvoke.Shell32</c> is deklarál
    /// <c>IShellFolder</c>-t, és a <c>using static</c> miatt a kettő elfedné
    /// egymást. A vtable sorrendje KÖTELEZŐ — egy metódust sem szabad
    /// kihagyni vagy átrendezni, még a nem használtakat sem.
    /// </remarks>
    [ComImport]
    [Guid("000214E6-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellFolderRaw
    {
        [PreserveSig]
        int ParseDisplayName(nint hwnd, nint pbc, [MarshalAs(UnmanagedType.LPWStr)] string name, ref uint eaten, out nint pidl, ref uint attributes);

        [PreserveSig]
        int EnumObjects(nint hwnd, int flags, out nint enumIdList);

        [PreserveSig]
        int BindToObject(nint pidl, nint pbc, ref Guid riid, out nint ppv);

        [PreserveSig]
        int BindToStorage(nint pidl, nint pbc, ref Guid riid, out nint ppv);

        [PreserveSig]
        int CompareIDs(nint lParam, nint pidl1, nint pidl2);

        [PreserveSig]
        int CreateViewObject(nint hwndOwner, ref Guid riid, out nint ppv);

        [PreserveSig]
        int GetAttributesOf(uint cidl, [In][MarshalAs(UnmanagedType.LPArray)] nint[] apidl, ref uint inOut);

        [PreserveSig]
        int GetUIObjectOf(nint hwndOwner, uint cidl, [In][MarshalAs(UnmanagedType.LPArray)] nint[] apidl, ref Guid riid, nint reserved, out nint ppv);

        [PreserveSig]
        int GetDisplayNameOf(nint pidl, uint flags, nint name);

        [PreserveSig]
        int SetNameOf(nint hwnd, nint pidl, [MarshalAs(UnmanagedType.LPWStr)] string name, uint flags, out nint pidlOut);
    }

    // --- MENUITEMINFO maszkok és típusok ---
    internal const uint MIIM_STATE = 0x00000001;
    internal const uint MIIM_ID = 0x00000002;
    internal const uint MIIM_SUBMENU = 0x00000004;
    internal const uint MIIM_STRING = 0x00000040;
    internal const uint MIIM_BITMAP = 0x00000080;
    internal const uint MIIM_FTYPE = 0x00000100;

    internal const uint MFT_SEPARATOR = 0x00000800;

    internal const uint MFS_GRAYED = 0x00000003;
    internal const uint MFS_CHECKED = 0x00000008;

    /// <summary>Az alapértelmezett parancs — a dupla kattintás ezt indítja.</summary>
    internal const uint MFS_DEFAULT = 0x00001000;

    /// <summary>A menü feltöltésekor küldött üzenet — az <c>IContextMenu3</c> ezt várja a dinamikus almenükhöz.</summary>
    internal const int WM_INITMENUPOPUP = 0x0117;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MENUITEMINFO
    {
        public uint cbSize;
        public uint fMask;
        public uint fType;
        public uint fState;
        public uint wID;
        public nint hSubMenu;
        public nint hbmpChecked;
        public nint hbmpUnchecked;
        public nint dwItemData;
        public nint dwTypeData;
        public uint cch;
        public nint hbmpItem;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public nint bmBits;
    }

    /// <summary>
    /// Egy menüelem <c>HBITMAP</c> ikonjának átalakítása WPF-képpé, az
    /// alfa-csatorna megtartásával.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A kézenfekvő <c>Imaging.CreateBitmapSourceFromHBitmap</c> ELDOBJA az
    /// alfát: a menüikonok így fekete dobozban jelennének meg sötét témában.
    /// Ezért közvetlenül olvassuk ki a bittérképet, és <c>Bgra32</c>
    /// formátumban építjük újra.
    /// </para>
    /// <para>
    /// A shell 32 bites menüikonjai ELŐSZOROZOTT (premultiplied) alfával
    /// érkeznek, a WPF <c>Bgra32</c> viszont nem előszorozott — ezért
    /// visszaosztjuk. Néhány régebbi bővítmény 32 bpp-t ad, de végig nulla
    /// alfával (vagyis „nincs átlátszóság" helyett „teljesen átlátszó"): ezt
    /// külön felismerjük, és ilyenkor átlátszatlanként kezeljük, különben az
    /// ikon láthatatlan lenne.
    /// </para>
    /// </remarks>
    internal static ImageSource? TryConvertBitmap(nint hBitmap)
    {
        if (hBitmap == nint.Zero)
        {
            return null;
        }

        var info = default(BITMAP);

        if (GetObject(hBitmap, Marshal.SizeOf<BITMAP>(), ref info) == 0
            || info.bmWidth <= 0 || info.bmHeight <= 0 || info.bmWidth > 512 || info.bmHeight > 512)
        {
            return null;
        }

        if (info.bmBitsPixel != 32)
        {
            // Nem 32 bites: itt nincs alfa, amit meg kellene őrizni, a
            // beépített átalakító tökéletesen megteszi.
            try
            {
                var plain = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap, nint.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());

                plain.Freeze();
                return plain;
            }
            catch (Exception ex) when (ex is COMException or ArgumentException)
            {
                return null;
            }
        }

        var stride = info.bmWidth * 4;
        var length = stride * info.bmHeight;
        var buffer = new byte[length];
        var unmanaged = Marshal.AllocHGlobal(length);

        try
        {
            if (GetBitmapBits(hBitmap, length, unmanaged) == 0)
            {
                return null;
            }

            Marshal.Copy(unmanaged, buffer, 0, length);
        }
        finally
        {
            Marshal.FreeHGlobal(unmanaged);
        }

        var hasAlpha = false;

        for (var i = 3; i < length; i += 4)
        {
            if (buffer[i] != 0)
            {
                hasAlpha = true;
                break;
            }
        }

        if (!hasAlpha)
        {
            // Végig nulla alfa: a bővítmény nem töltötte ki a csatornát.
            for (var i = 3; i < length; i += 4)
            {
                buffer[i] = 255;
            }
        }
        else
        {
            // Előszorzott → egyenes alfa.
            for (var i = 0; i < length; i += 4)
            {
                var alpha = buffer[i + 3];

                if (alpha is 0 or 255)
                {
                    continue;
                }

                buffer[i] = (byte)Math.Min(255, buffer[i] * 255 / alpha);
                buffer[i + 1] = (byte)Math.Min(255, buffer[i + 1] * 255 / alpha);
                buffer[i + 2] = (byte)Math.Min(255, buffer[i + 2] * 255 / alpha);
            }
        }

        var bitmap = BitmapSource.Create(
            info.bmWidth,
            info.bmHeight,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            buffer,
            stride);

        // A UI szál más szálon jött létre — befagyasztás nélkül nem lenne
        // átadható a WPF-nek.
        bitmap.Freeze();
        return bitmap;
    }
}
