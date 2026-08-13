using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Pilaster.App.Localization;
using Pilaster.Core.FileSystem;
using Pilaster.Core.Formatting;

namespace Pilaster.App.Converters;

/// <summary>Bájtszám → olvasható méret. Mappáknál gondolatjel.</summary>
public sealed class ByteSizeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is long bytes ? ByteSize.Format(bytes, culture) : "—";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
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
