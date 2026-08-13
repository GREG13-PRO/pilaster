using System.Globalization;

namespace Pilaster.App.Diagnostics;

/// <summary>
/// Egyszerű, három tagú verziószám (major.minor.patch) — a Pilaster kiadásai
/// mindig ilyen alakúak (<c>v0.5.0</c>), teljes SemVer előfeltétel-/build-
/// metaadat kezelésre itt nincs szükség.
/// </summary>
public readonly record struct SemanticVersion(int Major, int Minor, int Patch) : IComparable<SemanticVersion>
{
    /// <summary>
    /// Elemzés szöveges alakból. Elfogadja a <c>v</c> előtagot, és mindent
    /// levág egy esetleges <c>-</c> (előfeltétel) vagy <c>+</c> (build-
    /// metaadat) után, mert a kiadási címkékben ilyen amúgy sincs, de egy
    /// jövőbeli "v0.6.0-rc1"-szerű címke se dobjon hibát, csak a
    /// figyelembe nem vett részt hagyja el.
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

        version = new SemanticVersion(major, minor, patch);
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

        return minor != 0 ? minor : Patch.CompareTo(other.Patch);
    }

    public static bool operator >(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) > 0;

    public static bool operator <(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) < 0;

    public static bool operator >=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) >= 0;

    public static bool operator <=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) <= 0;

    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}
