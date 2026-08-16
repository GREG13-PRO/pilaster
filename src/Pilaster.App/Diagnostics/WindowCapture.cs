using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Pilaster.App.Diagnostics;

/// <summary>Egy ablak tartalmának képként való rögzítése hibabejelentéshez.</summary>
public static class WindowCapture
{
    /// <summary>
    /// Az ablak tartalmának PNG-je.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Elsődlegesen a natív <c>PrintWindow</c>-t hívja <c>PW_RENDERFULLCONTENT</c>
    /// jelzővel (spec E3, v1.0.1) — ez a TÉNYLEGES, DWM által összeállított
    /// felületet adja vissza, Mica/Acrylic háttérrel együtt, méghozzá akkor is,
    /// ha az ablakot épp egy másik takarja ki a képernyőn.
    /// </para>
    /// <para>
    /// MÉRVE (spec A1 v1.0.0-audit): a korábbi, kizárólag
    /// <see cref="RenderTargetBitmap"/>-alapú út a WPF vizuális fát rendereli
    /// közvetlenül, DWM-kompozitálás NÉLKÜL — ez a legtöbb ablaknál
    /// pixelpontos, de a félig áttetsző, Mica-rétegre épülő felületeken (pl. a
    /// főablak "liquid glass" oldalsávja, <c>GlassPanelBrush</c>) sötét
    /// témában derengő, majdnem üres eredményt adott: a valódi Mica-alapszín
    /// hiányában az alfa-keverés a HÁTTÉR NÉLKÜLI feloldásra esik vissza. A
    /// <c>PrintWindow</c> ezt elkerüli, mert a MÁR ÖSSZEÁLLÍTOTT felületet
    /// másolja.
    /// </para>
    /// <para>
    /// Ha a natív hívás bármiért sikertelen (pl. régebbi Windows-verzió,
    /// ismeretlen meghajtó-különlegesség), a metódus visszaesik az eredeti
    /// <see cref="RenderTargetBitmap"/>-útra — így sosem ad kevesebbet, mint
    /// a v1.0.0-ban.
    /// </para>
    /// </remarks>
    public static byte[]? CapturePng(Window window)
    {
        if (window.ActualWidth <= 0 || window.ActualHeight <= 0)
        {
            return null;
        }

        var dpi = VisualTreeHelper.GetDpi(window);
        var pixelWidth = (int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX);
        var pixelHeight = (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY);

        if (pixelWidth <= 0 || pixelHeight <= 0)
        {
            return null;
        }

        return TryCapturePrintWindow(window, pixelWidth, pixelHeight, dpi)
            ?? CaptureRenderTargetBitmap(window, pixelWidth, pixelHeight, dpi);
    }

    private static byte[]? TryCapturePrintWindow(Window window, int pixelWidth, int pixelHeight, DpiScale dpi)
    {
        var hwnd = new WindowInteropHelper(window).Handle;

        if (hwnd == nint.Zero)
        {
            return null;
        }

        var screenDc = nint.Zero;
        var memDc = nint.Zero;
        var bitmap = nint.Zero;
        var oldBitmap = nint.Zero;

        try
        {
            screenDc = NativeMethods.GetDC(nint.Zero);

            if (screenDc == nint.Zero)
            {
                return null;
            }

            memDc = NativeMethods.CreateCompatibleDC(screenDc);

            if (memDc == nint.Zero)
            {
                return null;
            }

            var info = new NativeMethods.BITMAPINFO
            {
                bmiHeader = new NativeMethods.BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
                    biWidth = pixelWidth,
                    // Negatív magasság: felülről lefelé haladó DIB, ugyanaz a
                    // sorrend, amit a BitmapSource.Create is vár — enélkül a
                    // kép fejjel lefelé állna.
                    biHeight = -pixelHeight,
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = 0, // BI_RGB
                },
            };

            bitmap = NativeMethods.CreateDIBSection(memDc, ref info, 0, out var bits, nint.Zero, 0);

            if (bitmap == nint.Zero || bits == nint.Zero)
            {
                return null;
            }

            oldBitmap = NativeMethods.SelectObject(memDc, bitmap);

            if (!NativeMethods.PrintWindow(hwnd, memDc, NativeMethods.PW_RENDERFULLCONTENT))
            {
                return null;
            }

            var stride = pixelWidth * 4;
            var buffer = new byte[stride * pixelHeight];
            Marshal.Copy(bits, buffer, 0, buffer.Length);

            // A GDI nem tölti ki értelmesen az alfa csatornát — egy
            // képernyőmentés úgyis mindig teljesen átlátszatlan.
            for (var i = 3; i < buffer.Length; i += 4)
            {
                buffer[i] = 0xFF;
            }

            var source = BitmapSource.Create(
                pixelWidth,
                pixelHeight,
                96 * dpi.DpiScaleX,
                96 * dpi.DpiScaleY,
                PixelFormats.Bgra32,
                null,
                buffer,
                stride);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));

            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }
        catch (Exception ex) when (ex is COMException or ExternalException or ArgumentException)
        {
            return null;
        }
        finally
        {
            if (memDc != nint.Zero && oldBitmap != nint.Zero)
            {
                NativeMethods.SelectObject(memDc, oldBitmap);
            }

            if (bitmap != nint.Zero)
            {
                NativeMethods.DeleteObject(bitmap);
            }

            if (memDc != nint.Zero)
            {
                NativeMethods.DeleteDC(memDc);
            }

            if (screenDc != nint.Zero)
            {
                NativeMethods.ReleaseDC(nint.Zero, screenDc);
            }
        }
    }

    /// <summary>
    /// Az eredeti, v1.0.0-s út — csak akkor fut, ha a <c>PrintWindow</c>
    /// valamiért sikertelen. A WPF-es renderelést fényképezi le közvetlenül,
    /// nem a képernyőt.
    /// </summary>
    private static byte[]? CaptureRenderTargetBitmap(Window window, int pixelWidth, int pixelHeight, DpiScale dpi)
    {
        var bitmap = new RenderTargetBitmap(
            pixelWidth,
            pixelHeight,
            96 * dpi.DpiScaleX,
            96 * dpi.DpiScaleY,
            PixelFormats.Pbgra32);

        bitmap.Render(window);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = new MemoryStream();
        encoder.Save(stream);

        return stream.ToArray();
    }

    private static class NativeMethods
    {
        public const uint PW_RENDERFULLCONTENT = 0x00000002;

        [DllImport("user32.dll")]
        public static extern nint GetDC(nint hWnd);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(nint hWnd, nint hDC);

        [DllImport("gdi32.dll")]
        public static extern nint CreateCompatibleDC(nint hdc);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteDC(nint hdc);

        [DllImport("gdi32.dll")]
        public static extern nint CreateDIBSection(nint hdc, ref BITMAPINFO pbmi, uint usage, out nint ppvBits, nint hSection, uint offset);

        [DllImport("gdi32.dll")]
        public static extern nint SelectObject(nint hdc, nint hgdiobj);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(nint hObject);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PrintWindow(nint hwnd, nint hdcBlt, uint nFlags);

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
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

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFO
        {
            public BITMAPINFOHEADER bmiHeader;

            // RGBQUAD[1] helyettesítője — 32 bpp BI_RGB-nél nincs paletta,
            // de a natív struktúra elvárja a mezőt a helyes mérethez.
            public uint bmiColors;
        }
    }
}
