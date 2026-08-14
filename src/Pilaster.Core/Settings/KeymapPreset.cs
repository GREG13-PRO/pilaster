namespace Pilaster.Core.Settings;

/// <summary>
/// A választható billentyűkiosztások.
/// </summary>
/// <remarks>
/// <para>
/// A v0.9-ig a kétpaneles kiosztás egy idegen termék nevén futott. A v1.0-ban
/// a preset saját, semleges nevet kapott — a viselkedése VÁLTOZATLAN, csak a
/// megjelenő felirata más. A régi konfigurációs értékeket a
/// <see cref="TryParse"/> automatikusan átképezi, tehát a felhasználó nem
/// veszíti el a beállítását.
/// </para>
/// <para>
/// A tagok NEVE szerializálódik (<c>UseStringEnumConverter</c>), ezért a
/// sorrendjük szabadon változtatható, de a nevük nem.
/// </para>
/// </remarks>
public enum KeymapPreset
{
    /// <summary>„Pilaster Modern (Explorer-szerű)" — a Windows Intézőjének megszokott billentyűi.</summary>
    Explorer,

    /// <summary>„Pilaster Classic (kétpaneles)" — F3 Megtekint, F4 Szerkeszt, F5 Másol, F6 Áthelyez, F7 Új mappa, F8 Töröl.</summary>
    PilasterClassic,

    /// <summary>„Egyedi" — a felhasználó saját, a Beállításokban szerkesztett kiosztása.</summary>
    Custom,
}

/// <summary>A <see cref="KeymapPreset"/> elemzése és migrációja.</summary>
public static class KeymapPresetParser
{
    /// <summary>
    /// A régi, immár elhagyott konfigurációs értékek, amiket a
    /// <see cref="KeymapPreset.PilasterClassic"/>-ra kell képezni.
    /// </summary>
    /// <remarks>
    /// Kis-nagybetűre és a szóelválasztóra (szóköz, kötőjel, aláhúzás)
    /// érzéketlenül illeszkedik, mert az érték kézzel szerkesztett
    /// <c>settings.json</c>-ből is érkezhet.
    /// </remarks>
    private static readonly string[] LegacyClassicAliases =
    [
        "totalcommander",
        "tc",
        "total_commander",
        "total commander",
        "total-commander",
        "pilaster-classic",
        "pilasterclassic",
        "classic",
    ];

    private static readonly string[] LegacyExplorerAliases =
    [
        "explorer",
        "pilaster-modern",
        "pilastermodern",
        "modern",
        "windows",
    ];

    /// <summary>
    /// Elemzés szövegből, a régi értékek automatikus átképezésével.
    /// Ismeretlen érték esetén az <see cref="KeymapPreset.Explorer"/> lép be —
    /// ez a biztonságosabb alapértelmezés, mert nem foglal le funkcióbillentyűket.
    /// </summary>
    public static KeymapPreset Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return KeymapPreset.Explorer;
        }

        var normalized = value.Trim().ToLowerInvariant();

        if (LegacyClassicAliases.Contains(normalized))
        {
            return KeymapPreset.PilasterClassic;
        }

        if (LegacyExplorerAliases.Contains(normalized))
        {
            return KeymapPreset.Explorer;
        }

        if (normalized is "custom" or "egyedi")
        {
            return KeymapPreset.Custom;
        }

        return Enum.TryParse<KeymapPreset>(normalized, ignoreCase: true, out var parsed)
            ? parsed
            : KeymapPreset.Explorer;
    }

    /// <summary>A preset fordítási kulcsa — a felületen sehol nem szerepel idegen terméknév.</summary>
    public static string ResourceKey(this KeymapPreset preset) => preset switch
    {
        KeymapPreset.PilasterClassic => "Keymap_PilasterClassic",
        KeymapPreset.Custom => "Keymap_Custom",
        _ => "Keymap_PilasterModern",
    };
}
