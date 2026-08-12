namespace Pilaster.Core.FileSystem;

/// <summary>
/// Egy navigálható tárhely absztrakciója.
/// </summary>
/// <remarks>
/// A helyi lemez csak EGY implementáció. Ugyanezen a felületen keresztül fog
/// később bejönni az archívum-mint-mappa (zip/7z), az FTP/SFTP, az S3 és a
/// WebDAV, valamint a virtuális helyek (mentett keresések, címke-gyűjtemények).
/// Ezért nem szabad sehol a UI-ban közvetlenül <c>System.IO</c>-t hívni —
/// minden listázás ezen az interfészen megy át.
/// </remarks>
public interface IFileSystemProvider
{
    /// <summary>
    /// A séma, amit ez a provider kezel — pl. <c>file</c>, <c>zip</c>, <c>sftp</c>.
    /// </summary>
    string Scheme { get; }

    /// <summary>Igaz, ha ez a provider kezelni tudja a megadott útvonalat.</summary>
    bool CanHandle(string path);

    /// <summary>
    /// A mappa tartalmának listázása, streamelve.
    /// </summary>
    /// <remarks>
    /// Szándékosan <see cref="IAsyncEnumerable{T}"/>: egy nagy mappánál a UI
    /// már az első néhány száz elemet kirajzolhatja, miközben a többi még jön.
    /// Az implementáció soha ne gyűjtsön előbb teljes listát memóriába.
    /// </remarks>
    IAsyncEnumerable<FileSystemItem> EnumerateAsync(
        string path,
        ListingOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>Egyetlen elem lekérése, vagy <c>null</c>, ha nem létezik.</summary>
    ValueTask<FileSystemItem?> GetItemAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>A szülőmappa útvonala, vagy <c>null</c>, ha ez már a gyökér.</summary>
    string? GetParentPath(string path);
}

/// <summary>
/// Listázási beállítások.
/// </summary>
/// <remarks>
/// Szándékosan nem <c>EnumerationOptions</c> a neve: az ütközne a
/// <see cref="System.IO.EnumerationOptions"/> típussal, amit az implicit
/// <c>using System.IO;</c> minden fájlba behúz.
/// </remarks>
/// <param name="IncludeHidden">Rejtett elemek is jöjjenek-e.</param>
/// <param name="IncludeSystem">Rendszerelemek is jöjjenek-e.</param>
public readonly record struct ListingOptions(
    bool IncludeHidden = false,
    bool IncludeSystem = false)
{
    public static ListingOptions Default => new();
}
