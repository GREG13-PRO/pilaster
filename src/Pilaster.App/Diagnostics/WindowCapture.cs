using System.IO;
using System.Windows;
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
    /// A WPF-es renderelést fényképezi le közvetlenül, nem a képernyőt — így
    /// akkor is a valódi felületi állapotot kapjuk, ha épp egy másik ablak
    /// (pl. maga a Beállítások) takarja ki a főablakot a képernyőn.
    /// </remarks>
    public static byte[]? CapturePng(Window window)
    {
        if (window.ActualWidth <= 0 || window.ActualHeight <= 0)
        {
            return null;
        }

        var dpi = VisualTreeHelper.GetDpi(window);

        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX),
            (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY),
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
}
