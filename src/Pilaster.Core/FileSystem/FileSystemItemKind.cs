namespace Pilaster.Core.FileSystem;

/// <summary>
/// Egy fájlrendszer-elem alaptípusa. A nézetek ez alapján döntik el, hogy
/// az elem navigálható-e (mappa/meghajtó) vagy megnyitandó (fájl).
/// </summary>
public enum FileSystemItemKind
{
    /// <summary>Sima fájl.</summary>
    File,

    /// <summary>Mappa, amelybe be lehet navigálni.</summary>
    Directory,

    /// <summary>Meghajtó gyökere (C:\, D:\ …).</summary>
    Drive,

    /// <summary>Szimbolikus link, junction vagy egyéb reparse point.</summary>
    Link,

    /// <summary>
    /// Virtuális elem, aminek nincs valódi útvonala a lemezen — pl. mentett
    /// keresés, címke-gyűjtemény vagy hálózati gyökér.
    /// </summary>
    Virtual,
}
