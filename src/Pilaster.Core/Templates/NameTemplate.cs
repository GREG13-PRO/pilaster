using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Pilaster.Core.Templates;

/// <summary>
/// Fájl- és mappanevek képzése helyőrzős mintából.
/// </summary>
/// <remarks>
/// Támogatott helyőrzők (kis- és nagybetű mindegy):
/// <list type="table">
/// <item><term><c>{date}</c></term><description>2026-08-12</description></item>
/// <item><term><c>{time}</c></term><description>21-45-03 — kettőspont nélkül, mert az fájlnévben tiltott</description></item>
/// <item><term><c>{datetime}</c></term><description>2026-08-12_21-45-03</description></item>
/// <item><term><c>{date:formátum}</c></term><description>tetszőleges .NET dátumformátum, pl. <c>{date:yyyy.MM.dd}</c></description></item>
/// <item><term><c>{n}</c></term><description>sorszám; ide kerül az ütközésfeloldó szám</description></item>
/// </list>
/// </remarks>
public static partial class NameTemplate
{
    [GeneratedRegex(@"\{(?<key>[a-zA-Z]+)(?::(?<format>[^}]+))?\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderRegex { get; }

    /// <summary>
    /// A helyőrzők behelyettesítése. A <c>{n}</c> érintetlenül marad, mert azt
    /// az ütközésfeloldás tölti ki, amikor már ismert a célmappa tartalma.
    /// </summary>
    public static string Expand(string template, DateTime? now = null, CultureInfo? culture = null)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return string.Empty;
        }

        var timestamp = now ?? DateTime.Now;
        culture ??= CultureInfo.CurrentCulture;

        var expanded = PlaceholderRegex.Replace(template, match =>
        {
            var key = match.Groups["key"].Value.ToLowerInvariant();
            var format = match.Groups["format"].Success ? match.Groups["format"].Value : null;

            return key switch
            {
                "date" => timestamp.ToString(format ?? "yyyy-MM-dd", culture),
                "time" => timestamp.ToString(format ?? "HH-mm-ss", culture),
                "datetime" => timestamp.ToString(format ?? "yyyy-MM-dd_HH-mm-ss", culture),

                // A sorszámot az ütközésfeloldás tölti ki, itt még nem tudjuk.
                "n" => match.Value,

                // Ismeretlen kulcs változatlanul marad, hogy a felhasználó lássa
                // az elgépelést, ne pedig némán eltűnjön a neve egy darabja.
                _ => match.Value,
            };
        });

        return Sanitize(expanded);
    }

    /// <summary>
    /// A fájlnévben tiltott karakterek eltávolítása.
    /// </summary>
    /// <remarks>
    /// Nem hibát dobunk, hanem takarítunk: a felhasználó egy névsablont
    /// szerkeszt a beállításokban, és egy elgépelt kettőspont miatt nem
    /// tagadhatjuk meg a gomb működését.
    /// </remarks>
    public static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(name.Length);

        foreach (var c in name)
        {
            if (!invalid.Contains(c))
            {
                builder.Append(c);
            }
        }

        var result = builder.ToString().Trim().TrimEnd('.');

        return result.Length == 0 ? "Névtelen" : result;
    }

    /// <summary>
    /// Szabad név keresése a célmappában.
    /// </summary>
    /// <remarks>
    /// Ha a minta tartalmaz <c>{n}</c>-t, a sorszám oda kerül. Ha nem, és a név
    /// már foglalt, a Windows szokása szerint egy zárójeles sorszám kerül a
    /// végére: „Új mappa", „Új mappa (2)", „Új mappa (3)".
    /// </remarks>
    /// <param name="directory">A célmappa.</param>
    /// <param name="expandedName">A már behelyettesített név (<c>{n}</c> még benne lehet).</param>
    /// <param name="extension">Kiterjesztés pont nélkül, vagy üres mappánál.</param>
    public static string ResolveUnique(string directory, string expandedName, string extension)
    {
        var suffix = string.IsNullOrEmpty(extension) ? string.Empty : "." + extension;
        var hasCounter = expandedName.Contains("{n}", StringComparison.OrdinalIgnoreCase);

        if (!hasCounter)
        {
            var plain = expandedName + suffix;

            if (!Exists(directory, plain))
            {
                return plain;
            }
        }

        for (var i = hasCounter ? 1 : 2; i < 10_000; i++)
        {
            var candidate = hasCounter
                ? PlaceholderRegex.Replace(expandedName, m =>
                    m.Groups["key"].Value.Equals("n", StringComparison.OrdinalIgnoreCase)
                        ? i.ToString(CultureInfo.InvariantCulture)
                        : m.Value) + suffix
                : $"{expandedName} ({i}){suffix}";

            if (!Exists(directory, candidate))
            {
                return candidate;
            }
        }

        // Tízezer ütközés után nem próbálkozunk tovább; az időbélyeg biztosan egyedi.
        return $"{expandedName}_{DateTime.Now:yyyyMMdd_HHmmssfff}{suffix}";
    }

    private static bool Exists(string directory, string fileName)
    {
        var full = Path.Combine(directory, fileName);
        return File.Exists(full) || Directory.Exists(full);
    }
}
