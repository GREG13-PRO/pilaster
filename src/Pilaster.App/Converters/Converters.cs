using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Pilaster.App.Localization;
using Pilaster.Core.FileSystem;
using Pilaster.Core.Formatting;
using Pilaster.Core.Metadata;
using Pilaster.Core.Settings;

namespace Pilaster.App.Converters;

/// <summary>Bájtszám → olvasható méret. Mappáknál gondolatjel.</summary>
public sealed class ByteSizeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is long bytes ? ByteSize.Format(bytes, culture) : "—";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// A méret oszlop tartalma: fájlnál <see cref="FileSystemItem.SizeBytes"/>,
/// mappánál a háttérben számolt <see cref="FileSystemItem.ComputedFolderSize"/>.
/// </summary>
/// <remarks>
/// Azért <see cref="IMultiValueConverter"/>, nem sima <c>{Binding}</c> az
/// egész elemre: egy útvonal nélküli kötés nem figyeli az almezők
/// PropertyChanged-jét, tehát nem frissülne, amint a mappaméret-számítás
/// később beérkezik. A MultiBinding mindhárom bemenetét külön figyeli.
/// </remarks>
public sealed class FolderAwareSizeConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is not [FileSystemItemKind kind, long sizeBytes, long computedFolderSize])
        {
            return string.Empty;
        }

        if (kind != FileSystemItemKind.Directory)
        {
            return sizeBytes < 0 ? "—" : ByteSize.Format(sizeBytes, culture);
        }

        // A "…" jelzi, hogy a számítás folyamatban van — a "—"-tól
        // szándékosan eltér, hogy a felhasználó lássa: hamarosan érkezik.
        return computedFolderSize < 0 ? "…" : ByteSize.Format(computedFolderSize, culture);
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Elem → típusleírás („Mappa", „PDF-fájl", „Fájl").</summary>
public sealed class FileTypeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not FileSystemItem item)
        {
            return string.Empty;
        }

        var strings = TranslationSource.Instance;

        return item.Kind switch
        {
            FileSystemItemKind.Drive => strings["Type_Drive"],
            FileSystemItemKind.Directory or FileSystemItemKind.Link => strings["Type_Folder"],
            _ when item.Extension.Length > 0 => string.Format(
                strings["Type_FileFormat"],
                item.Extension.ToUpper(culture)),
            _ => strings["Type_File"],
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// UTC időbélyeg → helyi idő, a felület nyelvének formátumában.
/// </summary>
/// <remarks>
/// A modell mindenhol UTC-t tárol, hogy a nyári időszámítás váltása ne mozgassa
/// el a fájlok dátumát; a megjelenítés viszont mindig helyi idő.
/// </remarks>
public sealed class LocalDateConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DateTime utc || utc == default)
        {
            return string.Empty;
        }

        return DateTime.SpecifyKind(utc, DateTimeKind.Utc)
            .ToLocalTime()
            .ToString("g", CultureInfo.CurrentCulture);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Null vagy üres szöveg → rejtett.</summary>
public sealed class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null || (value is string s && s.Length == 0)
            ? Visibility.Collapsed
            : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Logikai érték → láthatóság. A <c>parameter="invert"</c> megfordítja.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is true;

        if (string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase))
        {
            flag = !flag;
        }

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Rejtett elem → halványabb sor, ahogy az Explorerben is.
/// </summary>
public sealed class HiddenItemOpacityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? 0.5 : 1.0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Igaz → kiemelt (Primary) gombmegjelenés — bekapcsolt kapcsolók vizuális jelzésére.</summary>
public sealed class BoolToAppearanceConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Wpf.Ui.Controls.ControlAppearance.Primary : Wpf.Ui.Controls.ControlAppearance.Transparent;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Elemszám → láthatóság: 0 esetén összecsukva, egyébként látható.</summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int count && count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Logikai érték megfordítása (csak megjelenítéshez).</summary>
public sealed class InvertBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;
}

/// <summary>
/// Felsorolás-érték ↔ rádiógomb.
/// </summary>
/// <remarks>
/// <para>
/// A kézenfekvő megoldás — két rádiógomb egyetlen <c>bool</c>-ra kötve,
/// az egyiken invertáló kétirányú konverterrel — törékeny. A rádiógombok
/// csoportkezelése a társ gombot magától kikapcsolja, és a kétirányú kötés ezt
/// azonnal vissza is írja a modellbe. A sablonok felépítési sorrendjétől
/// függően így átmenetileg rossz érték kerülhet a modellbe, ami menteni is
/// mentődhet — ez okozta, hogy a beállításfájlban egyszer <c>File</c> szerepelt
/// ott, ahol a felület helyesen <c>Folder</c>-t mutatott.
/// </para>
/// <para>
/// Ez a konverter ezt kizárja: kikapcsoláskor <see cref="Binding.DoNothing"/>-ot
/// ad vissza, tehát a modellhez CSAK a bekapcsolt gomb nyúl.
/// </para>
/// </remarks>
public sealed class EnumToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null
        && parameter is string name
        && string.Equals(value.ToString(), name, StringComparison.OrdinalIgnoreCase);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // Csak a bekapcsolás jelent szándékot; a kikapcsolást a csoport okozza.
        if (value is not true || parameter is not string name)
        {
            return Binding.DoNothing;
        }

        var enumType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        return Enum.TryParse(enumType, name, ignoreCase: true, out var parsed)
            ? parsed
            : Binding.DoNothing;
    }
}

/// <summary>Téma-mód → lefordított név a legördülőben.</summary>
public sealed class ThemeModeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var strings = TranslationSource.Instance;

        return value switch
        {
            Core.Settings.ThemeMode.Light => strings["Theme_Light"],
            Core.Settings.ThemeMode.Dark => strings["Theme_Dark"],
            _ => strings["Theme_System"],
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Billentyűkiosztás → lefordított név. Sehol nem szerepel idegen terméknév.</summary>
public sealed class KeymapNameConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Core.Settings.KeymapPreset preset
            ? TranslationSource.Instance[preset.ResourceKey()]
            : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class AnimationLevelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var strings = TranslationSource.Instance;

        return value switch
        {
            Core.Settings.AnimationLevel.Reduced => strings["Animations_Reduced"],
            Core.Settings.AnimationLevel.Off => strings["Animations_Off"],
            _ => strings["Animations_Full"],
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// A címkék színpalettája — egyetlen forrás a színminta, a fájllista, a
/// szűrő legördülő és a Beállítások színválasztója számára.
/// </summary>
/// <remarks>
/// Fix, nem téma-függő színek: a címke színének MINDIG ugyanazt kell
/// mutatnia világos és sötét témában is, mint a macOS Finderben. Ez az
/// egyetlen szándékos kivétel a téma-tokenek alól — ezért kap a színminta
/// mindig <c>TokenBorderStrong</c> szegélyt, hogy a nagyon világos címkék se
/// olvadjanak bele a világos háttérbe (lásd B2).
/// </remarks>
public static class TagPalette
{
    private static readonly IReadOnlyDictionary<TagColor, SolidColorBrush> Brushes =
        new Dictionary<TagColor, SolidColorBrush>
        {
            [TagColor.Red] = Freeze(0xE8, 0x11, 0x23),
            [TagColor.Orange] = Freeze(0xF7, 0x63, 0x0C),
            [TagColor.Amber] = Freeze(0xE0, 0x93, 0x00),
            [TagColor.Yellow] = Freeze(0xFF, 0xB9, 0x00),
            [TagColor.Lime] = Freeze(0x76, 0xB9, 0x00),
            [TagColor.Green] = Freeze(0x10, 0x93, 0x54),
            [TagColor.Teal] = Freeze(0x00, 0x99, 0x8A),
            [TagColor.Cyan] = Freeze(0x00, 0xB7, 0xC3),
            [TagColor.Blue] = Freeze(0x00, 0x78, 0xD4),
            [TagColor.Indigo] = Freeze(0x4F, 0x4F, 0xC4),
            [TagColor.Purple] = Freeze(0x88, 0x64, 0xC7),
            [TagColor.Pink] = Freeze(0xE3, 0x00, 0x8C),
            [TagColor.Gray] = Freeze(0x8A, 0x8A, 0x8A),
        };

    /// <summary>A színválasztó rácsának 12 előre definiált színe.</summary>
    public static IReadOnlyList<TagColor> Presets { get; } =
    [
        TagColor.Red, TagColor.Orange, TagColor.Amber, TagColor.Yellow,
        TagColor.Lime, TagColor.Green, TagColor.Teal, TagColor.Cyan,
        TagColor.Blue, TagColor.Indigo, TagColor.Purple, TagColor.Pink,
    ];

    /// <summary>Egy paletta-szín ecsetje.</summary>
    public static SolidColorBrush Resolve(TagColor color) =>
        Brushes.TryGetValue(color, out var brush) ? brush : Brushes[TagColor.Gray];

    /// <summary>
    /// Egy címke tényleges ecsetje: az egyedi hex (ha van és érvényes),
    /// egyébként a paletta-szín. Érvénytelen hexnél a paletta-színre esik
    /// vissza — egy elrontott érték soha ne tegye láthatatlanná a mintát.
    /// </summary>
    public static SolidColorBrush Resolve(TagColor color, string? customHex)
    {
        if (string.IsNullOrWhiteSpace(customHex))
        {
            return Resolve(color);
        }

        try
        {
            if (ColorConverter.ConvertFromString(customHex.Trim()) is Color parsed)
            {
                var brush = new SolidColorBrush(parsed);
                brush.Freeze();
                return brush;
            }
        }
        catch (FormatException)
        {
            // Elgépelt hex — a paletta-szín lép be alább.
        }

        return Resolve(color);
    }

    private static SolidColorBrush Freeze(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

/// <summary>Címkeszín → tömör ecset. Csak a paletta-értéket veszi figyelembe.</summary>
public sealed class TagColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is TagColor color ? TagPalette.Resolve(color) : TagPalette.Resolve(TagColor.Gray);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Címke → tömör ecset, az egyedi hexet is figyelembe véve.
/// </summary>
/// <remarks>
/// Bemenete <c>[TagColor, string?]</c> — azért <see cref="IMultiValueConverter"/>,
/// mert a szín két, egymástól független tulajdonságból (paletta-érték és
/// egyedi hex) áll össze, és mindkettő változását követnie kell.
/// </remarks>
public sealed class TagBrushConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values is [TagColor color, var hex]
            ? TagPalette.Resolve(color, hex as string)
            : TagPalette.Resolve(TagColor.Gray);

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Szín → tömör ecset — az akcentus-paletta swatch-jeihez.</summary>
public sealed class ColorToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Color color ? new SolidColorBrush(color) : Brushes.Transparent;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// <c>#RRGGBB</c> szöveg → ecset. Üres/érvénytelen értéknél a téma
/// elsődleges szövegszínére esik vissza, hogy egy hiányzó egyedi szín
/// sose tegye láthatatlanná az ikont.
/// </summary>
public sealed class HexToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            try
            {
                if (ColorConverter.ConvertFromString(hex.Trim()) is Color color)
                {
                    var brush = new SolidColorBrush(color);
                    brush.Freeze();
                    return brush;
                }
            }
            catch (FormatException)
            {
                // Elgépelt hex — az örökölt szín lép be alább.
            }
        }

        return Application.Current?.Resources[Services.ThemeTokenService.TextPrimary] ?? Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>WPF-UI ikonnév (szöveg) → <c>SymbolRegular</c>. Ismeretlen névnél mappaikon.</summary>
/// <remarks>
/// Akkor kell, ha az ikon a felhasználó beállításából, szövegként érkezik —
/// a XAML <c>Symbol="{Binding}"</c> önmagában nem tud szövegből felsorolást
/// képezni, ha a kötés forrása <c>string</c>.
/// </remarks>
public sealed class IconNameConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string name && Enum.TryParse<Wpf.Ui.Controls.SymbolRegular>(name, ignoreCase: true, out var parsed)
            ? parsed
            : Wpf.Ui.Controls.SymbolRegular.Folder24;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Egyezik-e az érték a paraméterrel → láthatóság. A Beállítások
/// kategóriaváltása ezzel dönti el, melyik szakasz látszik.
/// </summary>
/// <remarks>
/// Egyetlen, mindig felépített vizuális fa marad, csak a láthatóság vált —
/// így a mélyhivatkozás (deep link) meg tudja találni és felvillantani a
/// célvezérlőt akkor is, ha a kategóriája épp nem az aktív.
/// </remarks>
public sealed class EqualsToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Nem-null → igaz. Egy részletpanel engedélyezéséhez, ha van kijelölt elem.</summary>
public sealed class NotNullConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// „Látható" jelölő → átlátszatlanság. A <see cref="HiddenItemOpacityConverter"/>
/// fordítottja: ott a <c>true</c> jelenti a rejtettséget, itt a láthatóságot.
/// </summary>
public sealed class VisibleFlagOpacityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? 1.0 : 0.45;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Erőforráskulcs → lefordított felirat.</summary>
/// <remarks>
/// Akkor kell, amikor a kulcs futásidőben derül ki, tehát a <c>{loc:Loc}</c>
/// kiterjesztés nem használható (az fordításkor várja a kulcsot).
/// </remarks>
public sealed class LocalizeKeyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string key && key.Length > 0 ? TranslationSource.Instance[key] : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Egy útvonal kötetének szabad helye, „X GB szabad" alakban (spec K7,
/// v1.0.1) — a kétpaneles nézet állapotsorán. Csendben üres szöveget ad
/// vissza hálózati vagy pillanatnyilag el nem érhető köteteknél, nem hibát.
/// </summary>
public sealed class DriveFreeSpaceConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrEmpty(path))
        {
            return string.Empty;
        }

        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(path) ?? path);

            return drive.IsReady
                ? string.Format(culture, TranslationSource.Instance["Status_FreeSpace"], ByteSize.Format(drive.AvailableFreeSpace, culture))
                : string.Empty;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
