namespace Pilaster.App.Diagnostics;

/// <summary>Egy elérhető frissítés adatai — a legfrissebb GitHub Release-ből.</summary>
public sealed record UpdateInfo(
    string Version,
    string ReleaseUrl,
    string InstallerDownloadUrl,
    string InstallerChecksumUrl,
    string InstallerFileName);

public enum UpdateCheckStatus
{
    UpToDate,
    UpdateAvailable,
    NetworkError,
    RateLimited,

    /// <summary>A kiadáshoz nincs a jelenlegi architektúrának megfelelő telepítő.</summary>
    NoAssetForPlatform,
    Error,
}

public readonly record struct UpdateCheckResult(UpdateCheckStatus Status, UpdateInfo? Update);

public enum UpdateDownloadStatus
{
    Succeeded,
    NetworkError,
    ChecksumMismatch,
    Error,
}

/// <param name="Status">Az eredmény.</param>
/// <param name="InstallerPath">A letöltött telepítő helyi útvonala, siker esetén.</param>
public readonly record struct UpdateDownloadResult(UpdateDownloadStatus Status, string? InstallerPath)
{
    public bool Succeeded => Status == UpdateDownloadStatus.Succeeded;
}

/// <summary>Frissítés-ellenőrzés és -telepítés a GitHub Releases API-n keresztül.</summary>
public interface IUpdateService
{
    Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// A telepítő letöltése és ellenőrzőösszeg-egyeztetése — magát a
    /// telepítést nem indítja el, csak előkészíti.
    /// </summary>
    Task<UpdateDownloadResult> DownloadInstallerAsync(UpdateInfo update, CancellationToken cancellationToken = default);

    /// <summary>
    /// Egy háttérben futó segédfolyamat indítása, ami megvárja, amíg a
    /// Pilaster kilép, csendben lefuttatja a telepítőt, majd újraindítja az
    /// alkalmazást. A hívó felelőssége, hogy ez után ténylegesen bezárja az
    /// alkalmazást — enélkül a segédfolyamat örökké várakozna.
    /// </summary>
    void BeginInstall(string installerPath);
}
