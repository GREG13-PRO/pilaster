namespace Pilaster.Core.FileSystem;

/// <summary>
/// A fájllista megjelenítési módjai. A beállítás mappánként megjegyződik.
/// </summary>
public enum ViewMode
{
    /// <summary>Részletes lista rendezhető oszlopokkal.</summary>
    Details,

    /// <summary>Ikonrács állítható elemmérettel.</summary>
    Grid,

    /// <summary>Csempék: ikon + név + másodlagos sor.</summary>
    Tiles,

    /// <summary>
    /// Oszlopos nézet macOS Finder módra — a Pilaster fő nézete.
    /// </summary>
    Columns,

    /// <summary>Nagy előnézet alsó filmszalaggal.</summary>
    Gallery,

    /// <summary>Kibontható fastruktúra.</summary>
    Tree,
}
