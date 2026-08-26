namespace Pilaster.Core.Settings;

/// <summary>
/// Egyetlen csatlakoztatott felhő meghajtó (NextCloud, ownCloud vagy
/// bármilyen más WebDAV-szerver).
/// </summary>
/// <remarks>
/// Szándékosan NEM tárol jelszót — a hitelesítést a Windows saját
/// hitelesítőadat-tárolója (Credential Manager) végzi a kapcsolódáskor,
/// lásd <c>Pilaster.Shell.Network.WebDavConnector</c>. Csak a már
/// UNC-alakra fordított útvonal (<see cref="UncPath"/>) és a megjelenítési
/// adatok élnek itt — ugyanaz a fájlrendszer-navigáció kezeli ezután, mint
/// bármely más mappát.
/// </remarks>
public sealed class CloudDriveEntry
{
    public required string Id { get; set; }

    /// <summary>A felhasználó által megadott név (pl. „Munkahelyi NextCloud").</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Az eredeti szerver-URL, amit a felhasználó megadott — csak megjelenítésre/szerkesztésre.</summary>
    public string ServerUrl { get; set; } = string.Empty;

    /// <summary>A csatlakoztatott UNC-útvonal (pl. <c>\\cloud.example.com@SSL\remote.php\webdav</c>) — ez a tényleges navigációs útvonal.</summary>
    public required string UncPath { get; set; }

    /// <summary>Sorrend a szekción belül.</summary>
    public int Order { get; set; }
}

/// <summary>A felhő meghajtók teljes, lemezre írt állapota — <c>%APPDATA%\Pilaster\clouddrives.json</c>.</summary>
public sealed class CloudDriveDocument
{
    public int Version { get; set; } = CurrentVersion;

    public const int CurrentVersion = 1;

    public List<CloudDriveEntry> Entries { get; set; } = [];
}
