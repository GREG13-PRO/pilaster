namespace Pilaster.Core.Metadata;

/// <summary>Előre definiált címkeszínek — macOS Finder mintájára.</summary>
public enum TagColor
{
    Red,
    Orange,
    Yellow,
    Green,
    Blue,
    Purple,
    Gray,
}

/// <summary>Egy felhasználó által létrehozott címke.</summary>
public sealed class TagDefinition
{
    /// <summary>Stabil azonosító — ez köti a fájlokhoz, nem a név (átnevezhető).</summary>
    public required string Id { get; set; }

    public required string Name { get; set; }

    public TagColor Color { get; set; }
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
