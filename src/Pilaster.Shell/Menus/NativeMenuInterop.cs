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
/// <summary>
/// A natív menü-ikonokhoz szükséges, PUBLIKUS résznek szánt átalakítás — lásd
/// <see cref="NativeMenuInterop.CreateHBitmapFromPbgra32"/>. Az App réteg
/// (WPF glyph-renderelés) ezen keresztül éri el, mert maga a
/// <see cref="NativeMenuInterop"/> osztály <c>internal</c>, a nyers
/// P/Invoke-deklarációk assembly-határon kívüli közzététele nem indokolt.
/// </summary>
public static class NativeMenuIconInterop
{
    /// <summary>Lásd <see cref="NativeMenuInterop.CreateHBitmapFromPbgra32"/>.</summary>
    public static nint CreateHBitmapFromPbgra32(byte[] pixels, int width, int height) =>
        NativeMenuInterop.CreateHBitmapFromPbgra32(pixels, width, height);

    /// <summary>Egy <see cref="CreateHBitmapFromPbgra32"/>-vel létrehozott HBITMAP felszabadítása.</summary>
    public static void DeleteHBitmap(nint hBitmap)
    {
        if (hBitmap != nint.Zero)
        {
            NativeMenuInterop.DeleteObject(hBitmap);
        }
    }

    /// <summary>
    /// Egy éppen nyitott natív menü (<c>TrackPopupMenuEx</c>) biztonságos,
    /// PROGRAMOZOTT bezárása — <c>WM_CANCELMODE</c> postázásával, valódi
    /// billentyű-/egérszimuláció NÉLKÜL. Kizárólag az öntesztekhez: a
    /// <see cref="ShellMenuSession.ShowNativeAsync"/> <c>onShown</c>
    /// visszahívásából kapott fogantyúval hívható.
    /// </summary>
    public static void CancelActiveMenu(nint ownerWindowHandle) =>
        NativeMenuInterop.PostMessage(ownerWindowHandle, NativeMenuInterop.WM_CANCELMODE, nint.Zero, nint.Zero);

    /// <summary>
    /// Kizárólag az öntesztekhez: egy nyíl-billentyű (le/jobbra/…) POSTÁZÁSA
    /// KIZÁRÓLAG a natív menü ideiglenes tulajdonos-ablakának — ez NEM
    /// globális billentyűszimuláció (nem <c>SendInput</c>/<c>keybd_event</c>),
    /// tehát a felhasználó tényleges fókuszát/egerét NEM érinti, akármelyik
    /// alkalmazás van is épp előtérben. A <c>TrackPopupMenuEx</c> belső
    /// hurokja ugyanazt a szál-üzenetsort olvassa, amibe ez az üzenet kerül,
    /// ezért a menü navigációjaként dolgozza fel.
    /// </summary>
    public static void PostMenuNavigationKey(nint ownerWindowHandle, NativeMenuTestKey key)
    {
        var vk = key switch
        {
            NativeMenuTestKey.Down => 0x28,
            NativeMenuTestKey.Right => 0x27,
            NativeMenuTestKey.Enter => 0x0D,
            _ => 0,
        };

        if (vk == 0)
        {
            return;
        }

        const uint WM_KEYDOWN = 0x0100;
        const uint WM_KEYUP = 0x0101;
        const nint extendedKeyLParam = 0x01000001;

        NativeMenuInterop.PostMessage(ownerWindowHandle, WM_KEYDOWN, vk, extendedKeyLParam);
        NativeMenuInterop.PostMessage(ownerWindowHandle, WM_KEYUP, vk, extendedKeyLParam);
    }
}

/// <summary>Lásd <see cref="NativeMenuIconInterop.PostMenuNavigationKey"/>.</summary>
public enum NativeMenuTestKey
{
    Down,
    Right,
    Enter,
}

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

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "InsertMenuItemW")]
    internal static extern bool InsertMenuItem(nint hMenu, uint item, bool byPosition, ref MENUITEMINFO info);

    [DllImport("gdi32.dll")]
    internal static extern int GetObject(nint handle, int size, ref BITMAP bitmap);

    [DllImport("gdi32.dll")]
    internal static extern int GetBitmapBits(nint hBitmap, int count, nint bits);

    [DllImport("gdi32.dll")]
    internal static extern bool DeleteObject(nint hObject);

    // --- v1.0.3: natív ("Windows") menümegjelenítés — TrackPopupMenuEx a
    // MEGLÉVŐ, nyersen épített HMENU-n. Lásd ShellMenuSession.ShowNativeAsync.

    internal const uint TPM_LEFTBUTTON = 0x0000;
    internal const uint TPM_RIGHTBUTTON = 0x0002;
    internal const uint TPM_LEFTALIGN = 0x0000;
    internal const uint TPM_TOPALIGN = 0x0000;
    internal const uint TPM_VERTICAL = 0x0040;
    internal const uint TPM_RETURNCMD = 0x0100;

    [DllImport("user32.dll")]
    internal static extern int TrackPopupMenuEx(nint hMenu, uint flags, int x, int y, nint hwnd, nint lptpm);

    [DllImport("user32.dll")]
    internal static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "PostMessageW")]
    internal static extern bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);

    internal const uint WM_NULL = 0x0000;
    internal const uint WM_CANCELMODE = 0x001F;
    internal const int WM_DRAWITEM = 0x002B;
    internal const int WM_MEASUREITEM = 0x002C;
    internal const int WM_MENUCHAR = 0x0120;

    // --- Egy minimális, saját natív ablak a WM_INITMENUPOPUP/WM_DRAWITEM/
    // WM_MEASUREITEM/WM_MENUCHAR üzenetek IContextMeno3::HandleMenuMsg2-höz
    // továbbításához — lásd NativeMenuOwnerWindow. A TrackPopupMenuEx-nek
    // UGYANAZON a szálon kell futnia, ahol az IContextMenu létrejött, ezért
    // ez az ablak SOSEM a WPF főablak, hanem egy erre a hívásra, a megosztott
    // STA szálon létrehozott, láthatatlan segédablak.

    internal delegate nint WndProcDelegate(nint hWnd, uint msg, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public WndProcDelegate lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        public nint hIconSm;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegisterClassExW", SetLastError = true)]
    internal static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateWindowExW", SetLastError = true)]
    internal static extern nint CreateWindowEx(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

    [DllImport("user32.dll")]
    internal static extern bool DestroyWindow(nint hWnd);

    internal const int GWLP_WNDPROC = -4;

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    internal static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint newProc);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "DefWindowProcW")]
    internal static extern nint DefWindowProc(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint GetModuleHandle(string? lpModuleName);

    internal static readonly nint HWND_MESSAGE = new(-3);

    // --- v1.0.3: saját ikon (glyph) átalakítása HBITMAP-pá a natív menü
    // beszúrt elemeihez — a fordítottja a TryConvertBitmap-nak.

    // GetDC/ReleaseDC klasszikus Win32-csapda: historikusan MINDKETTŐ a
    // user32.dll-ből exportálódik, NEM a gdi32.dll-ből — a device context
    // maga GDI-fogalom, de a kérés/elengedés API-ja user32. Hibás DLL esetén
    // EntryPointNotFoundException-t dob, MINDEN natív menünyitáskor (az
    // ikon-renderelés minden hívásnál lefut) — ez élesben app-szintű
    // összeomlást okozott, amíg egy kézi próba ki nem derítette.
    [DllImport("user32.dll")]
    internal static extern nint GetDC(nint hWnd);

    [DllImport("user32.dll")]
    internal static extern int ReleaseDC(nint hWnd, nint hDC);

    [DllImport("gdi32.dll")]
    internal static extern nint CreateDIBSection(nint hdc, ref BITMAPINFO_HEADER_ONLY bmi, uint usage, out nint ppvBits, nint hSection, uint offset);

    internal const uint DIB_RGB_COLORS = 0;

    [StructLayout(LayoutKind.Sequential)]
    internal struct BITMAPINFO_HEADER_ONLY
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    /// <summary>
    /// Egy 32 bites, ELŐSZOROZOTT alfájú, felülről-lefelé sorolt pixeltömb
    /// (WPF <c>Pbgra32</c>-formátum) átalakítása natív <c>HBITMAP</c>-pá, egy
    /// menüelem <c>hbmpItem</c>-jéhez. A hívó felelős a <see cref="DeleteObject"/>
    /// meghívásáért — a menü-API-k NEM szabadítják fel automatikusan.
    /// </summary>
    internal static nint CreateHBitmapFromPbgra32(byte[] pixels, int width, int height)
    {
        var bmi = new BITMAPINFO_HEADER_ONLY
        {
            biSize = (uint)Marshal.SizeOf<BITMAPINFO_HEADER_ONLY>(),
            biWidth = width,
            // Negatív magasság = felülről-lefelé sorolt forrás — pontosan
            // úgy, ahogy a WPF RenderTargetBitmap.CopyPixels adja.
            biHeight = -height,
            biPlanes = 1,
            biBitCount = 32,
            biCompression = 0, // BI_RGB
        };

        var hdc = GetDC(nint.Zero);

        try
        {
            var hBitmap = CreateDIBSection(hdc, ref bmi, DIB_RGB_COLORS, out var bits, nint.Zero, 0);

            if (hBitmap == nint.Zero || bits == nint.Zero)
            {
                return nint.Zero;
            }

            Marshal.Copy(pixels, 0, bits, pixels.Length);
            return hBitmap;
        }
        finally
        {
            if (hdc != nint.Zero)
            {
                ReleaseDC(nint.Zero, hdc);
            }
        }
    }

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
