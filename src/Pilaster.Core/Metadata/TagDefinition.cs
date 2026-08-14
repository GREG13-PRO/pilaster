namespace Pilaster.Core.Metadata;

/// <summary>
/// Az előre definiált címkeszínek — 12 érték, a Beállítások színválasztójának
/// rácsa pontosan ezt tükrözi.
/// </summary>
/// <remarks>
/// A tagok NEVE szerializálódik (lásd <c>UseStringEnumConverter</c> a
/// <c>FileMetadataJsonContext</c>-ben), nem a sorszáma — ezért az eredeti hét
/// név (Red, Orange, Yellow, Green, Blue, Purple, Gray) változatlanul itt
/// marad, és a régebbi <c>metadata.json</c> további migráció nélkül betöltődik.
/// Az új tagok bárhová beszúrhatók.
/// </remarks>
public enum TagColor
{
    Red,
    Orange,
    Amber,
    Yellow,
    Lime,
    Green,
    Teal,
    Cyan,
    Blue,
    Indigo,
    Purple,
    Pink,

    /// <summary>
    /// Az egyedi hex értékek gyűjtőhelye is: ha a <see cref="TagDefinition.ColorHex"/>
    /// ki van töltve, az élvez elsőbbséget ezzel a taggal szemben.
    /// </summary>
    Gray,
}

/// <summary>Egy felhasználó által létrehozott címke.</summary>
public sealed class TagDefinition
{
    /// <summary>Stabil azonosító — ez köti a fájlokhoz, nem a név (átnevezhető).</summary>
    public required string Id { get; set; }

    public required string Name { get; set; }

    public TagColor Color { get; set; }

    /// <summary>
    /// Egyedi szín <c>"#RRGGBB"</c> alakban, vagy <c>null</c>, ha a
    /// <see cref="Color"/> paletta-értéke érvényes.
    /// </summary>
    /// <remarks>
    /// Kitöltve MINDIG felülírja a <see cref="Color"/>-t. Így a paletta
    /// bővítése sosem írja felül a felhasználó egyedi választását, és a
    /// visszaváltás a palettára egyszerűen ennek nullázása.
    /// </remarks>
    public string? ColorHex { get; set; }
}

/// <summary>Egyetlen fájl/mappa metaadata: rajta lévő címkék és kedvenc-jelölés.</summary>
public sealed class FileMetadataEntry
{
    public List<string> TagIds { get; set; } = [];

    public bool IsFavorite { get; set; }

    /// <summary>Igaz, ha az elemnek nincs már tárolásra érdemes adata — ekkor a bejegyzés törölhető.</summary>
    public bool IsEmpty => !IsFavorite && TagIds.Count == 0;
}

/// <summary>
/// A teljes címke-/kedvenc-adatbázis, ahogy lemezre íródik.
/// </summary>
/// <remarks>
/// Az <see cref="Items"/> kulcsa a fájl/mappa teljes elérési útja. Windowson
/// az útvonalak kis-nagybetű-érzéketlenek, ezért a szótár betöltéskor mindig
/// <see cref="StringComparer.OrdinalIgnoreCase"/> összehasonlítóval épül újra
/// — lásd <c>FileMetadataService</c>.
/// </remarks>
public sealed class FileMetadataDocument
{
    public List<TagDefinition> Tags { get; set; } = [];

    public Dictionary<string, FileMetadataEntry> Items { get; set; } = [];
}
