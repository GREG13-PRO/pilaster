using System.Runtime.InteropServices;

namespace Pilaster.Shell.Interop;

/// <summary>
/// A shell ikon- és bélyegkép-kinyeréshez szükséges Win32/COM deklarációk.
/// </summary>
internal static partial class NativeMethods
{
    /// <summary>
    /// Útvonalból <c>IShellItemImageFactory</c>-t készít.
    /// </summary>
    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Error)]
    internal static partial int SHCreateItemFromParsingName(
        string pszPath,
        nint pbc,
        in Guid riid,
        out nint ppv);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeleteObject(nint hObject);

    /// <summary>
    /// A gdi32 nem exportál sima <c>GetObject</c> nevet, csak <c>GetObjectA</c>
    /// és <c>GetObjectW</c> változatot. A <c>LibraryImport</c> — a régi
    /// <c>DllImport</c>-tal ellentétben — nem próbálkozik a W/A utótaggal,
    /// ezért a belépési pontot kifejezetten meg kell adni.
    /// </summary>
    [LibraryImport("gdi32.dll", EntryPoint = "GetObjectW")]
    internal static partial int GetObject(nint hgdiobj, int cbBuffer, ref Bitmap lpvObject);

    [LibraryImport("gdi32.dll")]
    internal static partial int GetDIBits(
        nint hdc,
        nint hbm,
        uint start,
        uint cLines,
        [Out] byte[] lpvBits,
        ref BitmapInfo lpbmi,
        uint usage);

    [LibraryImport("user32.dll")]
    internal static partial nint GetDC(nint hWnd);

    [LibraryImport("user32.dll")]
    internal static partial int ReleaseDC(nint hWnd, nint hDC);

    internal const uint DibRgbColors = 0;
    internal const uint BiRgb = 0;

    /// <summary>IShellItemImageFactory interfész-azonosítója.</summary>
    internal static readonly Guid IidShellItemImageFactory =
        new("bcc18b79-ba16-442f-80c4-8a59c30c463b");

    [StructLayout(LayoutKind.Sequential)]
    internal struct Bitmap
    {
        public int Type;
        public int Width;
        public int Height;
        public int WidthBytes;
        public ushort Planes;
        public ushort BitsPixel;
        public nint Bits;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BitmapInfoHeader
    {
        public int Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    /// <summary>
    /// A <c>BITMAPINFO</c> a fejléc után színtáblát is tartalmazhat. 32 bites
    /// BI_RGB képnél nincs paletta, de a struktúrát a GDI akkor is
    /// megcímezheti, ezért egy RGBQUAD-nyi helyet hagyunk utána.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint FirstPaletteEntry;
    }

    /// <summary>A shell képkérés viselkedését szabályozó zászlók (SIIGBF).</summary>
    [Flags]
    internal enum ShellImageFlags
    {
        ResizeToFit = 0x00000000,

        /// <summary>Ne nagyítsa fel a kisebb képet a kért méretre.</summary>
        BiggerSizeOk = 0x00000001,

        /// <summary>Csak gyorsítótárból — ne generáljon most bélyegképet.</summary>
        MemoryOnly = 0x00000002,

        /// <summary>Mindig ikon, soha ne bélyegkép.</summary>
        IconOnly = 0x00000004,

        /// <summary>Mindig bélyegkép; ha nincs, sikertelen.</summary>
        ThumbnailOnly = 0x00000008,

        /// <summary>Ne érje el a lassú tárolót (hálózat, alvó lemez).</summary>
        InCacheOnly = 0x00000010,

        /// <summary>Ne alkalmazzon shell-átfedést (parancsikon-nyíl, megosztás).</summary>
        IconBackground = 0x00000080,

        /// <summary>Vágás helyett méretezés, a képarány megtartásával.</summary>
        ScaleUp = 0x00000100,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Size
    {
        public int Width;
        public int Height;
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellItemImageFactory
    {
        /// <summary>
        /// Bélyegkép vagy ikon kérése HBITMAP-ként. A hívó felelős a
        /// <see cref="DeleteObject"/> meghívásáért.
        /// </summary>
        [PreserveSig]
        int GetImage(Size size, ShellImageFlags flags, out nint phbm);
    }
}
