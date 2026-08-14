using System.Globalization;

namespace Pilaster.App.Diagnostics;

/// <summary>
/// Verziószám (major.minor.patch[.revision]) — a Pilaster kiadásai jellemzően
/// három tagúak (<c>v0.5.0</c>), de egy negyedik, opcionális revízió-tag is
/// előfordulhat (<c>v0.9.0.1</c>, kisebb javításhoz új főfunkció nélkül).
/// Teljes SemVer előfeltétel-/build-metaadat kezelésre itt nincs szükség.
/// </summary>
public readonly record struct SemanticVersion(int Major, int Minor, int Patch, int Revision = 0) : IComparable<SemanticVersion>
{
    /// <summary>
    /// Elemzés szöveges alakból. Elfogadja a <c>v</c> előtagot, egy negyedik
    /// (revízió) tagot, és mindent levág egy esetleges <c>-</c> (előfeltétel)
    /// vagy <c>+</c> (build-metaadat) után, mert a kiadási címkékben ilyen
    /// amúgy sincs, de egy jövőbeli "v0.6.0-rc1"-szerű címke se dobjon hibát,
    /// csak a figyelembe nem vett részt hagyja el.
    /// </summary>
    public static bool TryParse(string? text, out SemanticVersion version)
    {
        version = default;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();

        if (trimmed.Length > 0 && (trimmed[0] == 'v' || trimmed[0] == 'V'))
        {
            trimmed = trimmed[1..];
        }

        var coreEnd = trimmed.IndexOfAny(['-', '+']);

        if (coreEnd >= 0)
        {
            trimmed = trimmed[..coreEnd];
        }

        var parts = trimmed.Split('.');

        if (parts.Length < 2)
        {
            return false;
        }

        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor))
        {
            return false;
        }

        var patch = 0;

        if (parts.Length >= 3 && !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out patch))
        {
            return false;
        }

        var revision = 0;

        if (parts.Length >= 4 && !int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out revision))
        {
            return false;
        }

        version = new SemanticVersion(major, minor, patch, revision);
        return true;
    }

    public int CompareTo(SemanticVersion other)
    {
        var major = Major.CompareTo(other.Major);

        if (major != 0)
        {
            return major;
        }

        var minor = Minor.CompareTo(other.Minor);

        if (minor != 0)
        {
            return minor;
        }

        var patch = Patch.CompareTo(other.Patch);

        return patch != 0 ? patch : Revision.CompareTo(other.Revision);
    }

    public static bool operator >(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) > 0;

    public static bool operator <(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) < 0;

    public static bool operator >=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) >= 0;

    public static bool operator <=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) <= 0;

    /// <summary>
    /// Három tagra rövidül, ha nincs revízió (a megszokott <c>0.9.0</c> alak
    /// marad a felhasználó előtt) — a negyedik tag csak akkor jelenik meg,
    /// ha ténylegesen van (<c>0.9.0.1</c>).
    /// </summary>

    public override string ToString() =>
        Revision == 0 ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}.{Patch}.{Revision}";
}
