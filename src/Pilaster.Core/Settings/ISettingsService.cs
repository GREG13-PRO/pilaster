namespace Pilaster.Core.Settings;

/// <summary>Az alkalmazás beállításainak betöltése és mentése.</summary>
public interface ISettingsService
{
    /// <summary>Az aktuális beállítások. Mindig érvényes példány, soha nem <c>null</c>.</summary>
    AppSettings Current { get; }

    /// <summary>
    /// Mentés kérése.
    /// </summary>
    /// <remarks>
    /// Nem azonnal ír lemezre: a beállításpanelen egy csúszka húzása vagy egy
    /// szövegmező gépelése másodpercenként több tucat hívást jelentene. A
    /// megvalósítás késleltet és összevon.
    /// </remarks>
    void Save();

    /// <summary>Azonnali, szinkron mentés — kilépéskor.</summary>
    void Flush();

    /// <summary>Akkor jelez, ha bármely beállítás megváltozott.</summary>
    event EventHandler? Changed;

    /// <summary>Értesítés küldése a beállítások módosulásáról.</summary>
    void NotifyChanged();
}
