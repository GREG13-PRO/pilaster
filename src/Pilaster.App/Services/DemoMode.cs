using Pilaster.Shell.Network;

namespace Pilaster.App.Services;

/// <summary>
/// Bemutató mód képernyőképekhez és videókhoz: a program nem mutat semmit a
/// valódi gépből, csak azt, amit a környezeti változók megadnak.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>PILASTER_DEMO=1</c> — bekapcsolja; nincs frissítés-ellenőrzés.</item>
/// <item><c>PILASTER_DEMO_DRIVES=C:;D:</c> — csak ezek a meghajtók látszanak
/// (felhőszinkron-meghajtók soha).</item>
/// <item><c>PILASTER_DEMO_CLOUD=Név|Útvonal|Ikonforrás;…</c> — a felhőtárhelyek
/// szekció tartalma a gépen felderített kliensek helyett.</item>
/// </list>
/// Az adatmappát (gyorselérés, címkék, beállítások) a <c>PILASTER_DATA_DIR</c>
/// adja meg — lásd <see cref="AppDataLocator"/>.
/// </remarks>
public static class DemoMode
{
    public static bool IsEnabled { get; } = Environment.GetEnvironmentVariable("PILASTER_DEMO") == "1";

    /// <summary>A megjelenítendő meghajtók gyökerei (pl. <c>C:</c>); <c>null</c>, ha nincs szűrés.</summary>
    public static IReadOnlySet<string>? Drives { get; } = ParseDrives();

    /// <summary>A felhőtárhelyek szekció tartalma bemutató módban.</summary>
    public static IReadOnlyList<CloudStorageRoot> CloudRoots { get; } = ParseCloudRoots();

    private static IReadOnlySet<string>? ParseDrives()
    {
        if (!IsEnabled || Environment.GetEnvironmentVariable("PILASTER_DEMO_DRIVES") is not { Length: > 0 } value)
        {
            return null;
        }

        return value
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(drive => drive.TrimEnd('\\').ToUpperInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<CloudStorageRoot> ParseCloudRoots()
    {
        if (!IsEnabled || Environment.GetEnvironmentVariable("PILASTER_DEMO_CLOUD") is not { Length: > 0 } value)
        {
            return [];
        }

        return
        [
            .. value
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(entry => entry.Split('|', StringSplitOptions.TrimEntries))
                .Where(parts => parts.Length >= 2)
                .Select(parts => new CloudStorageRoot(parts[0], parts[1], parts.Length > 2 && parts[2].Length > 0 ? parts[2] : null)),
        ];
    }
}
