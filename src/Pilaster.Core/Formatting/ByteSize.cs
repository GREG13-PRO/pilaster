using System.Globalization;

namespace Pilaster.Core.Formatting;

/// <summary>Fájlméretek ember által olvasható formázása.</summary>
public static class ByteSize
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB", "PB"];

    /// <summary>
    /// Bájtszám formázása, a Windows Explorer konvenciója szerint (1 KB = 1024 B).
    /// </summary>
    /// <remarks>
    /// A tizedesjegyek száma a nagyságrenddel csökken: 1,5 GB olvashatóbb, mint
    /// 1,50 GB, de 950 KB-nál a tizedes már zajt vinne a listába.
    /// </remarks>
    /// <param name="bytes">A méret bájtban. Negatív érték = ismeretlen.</param>
    /// <param name="culture">A számformázás nyelve; alapértelmezésben az aktuális.</param>
    public static string Format(long bytes, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;

        if (bytes < 0)
        {
            return "—";
        }

        if (bytes < 1024)
        {
            return string.Format(culture, "{0} {1}", bytes, Units[0]);
        }

        double value = bytes;
        var unit = 0;

        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        var decimals = value switch
        {
            >= 100 => 0,
            >= 10 => 1,
            _ => 2,
        };

        return string.Format(culture, "{0} {1}", Math.Round(value, decimals), Units[unit]);
    }
}
