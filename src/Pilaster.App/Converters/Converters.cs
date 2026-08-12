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
