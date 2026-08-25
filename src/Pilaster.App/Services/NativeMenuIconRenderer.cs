using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Pilaster.Shell.Menus;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

namespace Pilaster.App.Services;

/// <summary>
/// A Pilaster saját <c>SymbolRegular</c> glyph-ikonjainak natív
/// <c>HBITMAP</c>-pá renderelése — a natív („Windows") jobbklikk-menübe
/// beszúrt saját parancsok (spec v1.0.3) ikonjaihoz.
/// </summary>
/// <remarks>
/// <para>
/// A <c>SymbolRegular</c> nem kép, hanem egy betűtípus (WPF-UI
/// „FluentSystemIcons") glyph-je — a <see cref="SymbolExtensions.GetString"/>
/// az enum ÉRTÉKÉBŐL állítja elő a Unicode karaktert (lásd a WPF-UI
/// forráskódját: az enum értéke maga a UTF-16 kódpont). Ezt kell
/// legényerelni egy kis, átlátszó hátterű bittérképre, majd
/// <see cref="NativeMenuInterop.CreateHBitmapFromPbgra32"/>-vel natív
/// <c>HBITMAP</c>-pá alakítani.
/// </para>
/// <para>
/// Gyorsítótárazott: ugyanaz a 8 ikon minden natív menünyitáskor újra
/// kellene, feleslegesen — a folyamat élettartamáig cache-eljük.
/// </para>
/// </remarks>
public static class NativeMenuIconRenderer
{
    private const int IconSize = 16;

    private static readonly Dictionary<SymbolRegular, nint> Cache = [];
    private static readonly Lock CacheLock = new();

    /// <summary>
    /// Egy <see cref="SymbolRegular"/> glyph HBITMAP-ja, gyorsítótárazva.
    /// <c>nint.Zero</c>, ha a renderelés bármiért nem sikerült — ilyenkor a
    /// hívó egyszerűen ikon nélküli sort szúr be, ez sosem hiba.
    /// </summary>
    public static nint GetOrRender(SymbolRegular symbol)
    {
        lock (CacheLock)
        {
            if (Cache.TryGetValue(symbol, out var cached))
            {
                return cached;
            }

            var rendered = Render(symbol);
            Cache[symbol] = rendered;
            return rendered;
        }
    }

    private static nint Render(SymbolRegular symbol)
    {
        try
        {
            if (Application.Current?.TryFindResource("FluentSystemIcons") is not FontFamily fontFamily)
            {
                return nint.Zero;
            }

            var glyph = symbol.GetString();

            if (string.IsNullOrEmpty(glyph))
            {
                return nint.Zero;
            }

            var typeface = new Typeface(fontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

            // Fekete: a natív menü SAJÁT SZÍNVISSZAADÁSA (fehér ikon fekete
            // szövegen, mint a shell menüelemek) a klasszikus HBITMAP-alapú
            // hbmpItem-nél nincs — Windows egyszerűen a bittérkép színeit
            // rajzolja ki, változtatás nélkül. Fekete a legszélesebb kontraszt
            // a natív menü világos ÉS sötét témájú változatán is (Windows
            // 11 natív menüje alapból világos hátterű marad, a téma nem
            // követi az appét — ez nem A2/A1 hatóköre).
            var formatted = new FormattedText(
                glyph,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                IconSize * 0.82,
                Brushes.Black,
                VisualTreeHelper.GetDpi(new DrawingVisual()).PixelsPerDip);

            var visual = new DrawingVisual();

            using (var dc = visual.RenderOpen())
            {
                var x = (IconSize - formatted.Width) / 2;
                var y = (IconSize - formatted.Height) / 2;
                dc.DrawText(formatted, new Point(x, y));
            }

            var rtb = new RenderTargetBitmap(IconSize, IconSize, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);

            var stride = IconSize * 4;
            var pixels = new byte[stride * IconSize];
            rtb.CopyPixels(pixels, stride, 0);

            return NativeMenuIconInterop.CreateHBitmapFromPbgra32(pixels, IconSize, IconSize);
        }
        catch (Exception)
        {
            // SZÁNDÉKOSAN minden kivétel — ez egy P/Invoke határ (natív
            // rajzolás/GDI-hívások), és egy hibás ikon-renderelés
            // KIZÁRÓLAG a sor ikonját érinti (szöveg nélküli ikon helyett
            // egyszerű szöveges sor marad). Éles hiba: egy elgépelt DLL-név
            // (GetDC a user32-ben van, nem a gdi32-ben) itt
            // EntryPointNotFoundException-t dobott, ami a szűkebb catch
            // mellett a TELJES jobbklikk-menüt (és vele az appot) elvitte —
            // egy ikon sosem érhet ennyit.
            return nint.Zero;
        }
    }
}
