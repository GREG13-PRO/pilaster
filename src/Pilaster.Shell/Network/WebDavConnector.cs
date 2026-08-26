using Vanara.PInvoke;

namespace Pilaster.Shell.Network;

public enum WebDavConnectOutcome
{
    Succeeded,

    /// <summary>A megadott cím nem érvényes abszolút HTTP(S) URL.</summary>
    InvalidUrl,

    /// <summary>A szerver elutasította a hitelesítő adatokat, vagy nem elérhető.</summary>
    ConnectFailed,
}

public readonly record struct WebDavConnectResult(WebDavConnectOutcome Outcome, string? UncPath, uint? ErrorCode);

/// <summary>
/// Felhő meghajtók (NextCloud, ownCloud és bármilyen más WebDAV-szerver)
/// csatlakoztatása a Windows saját, beépített WebDAV-átirányítóján
/// (a „WebClient" szolgáltatáson) keresztül.
/// </summary>
/// <remarks>
/// Szándékosan NEM Pilaster-saját WebDAV-kliens (PROPFIND/GET/PUT
/// protokollkezelés, hitelesítőadat-tárolás stb.) — a Windows már tartalmaz
/// egyet, ezért a szerver egy sima UNC-útvonalként (<c>\\host@SSL\path</c>)
/// jelenik meg, amit a meglévő helyi fájlrendszer-navigáció (Directory.*,
/// másolás, címkék stb.) változtatás nélkül kezel. A hátránya a Windows-
/// kliens ismert korlátai (kb. 50 MB-os alap fájlméret-korlát, néha akadozó
/// HTTPS-kezelés) — ezekért cserébe napok, nem hetek a megvalósítás.
/// </remarks>
public static class WebDavConnector
{
    /// <summary>
    /// Egy WebDAV-URL (pl. <c>https://cloud.example.com/remote.php/dav/files/user/</c>)
    /// átalakítása a Windows WebClient-je által elvárt UNC alakra
    /// (<c>\\cloud.example.com@SSL\remote.php\dav\files\user</c>).
    /// </summary>
    public static bool TryBuildUncPath(string url, out string uncPath)
    {
        uncPath = string.Empty;

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            return false;
        }

        var isHttps = uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);
        var defaultPort = isHttps ? 443 : 80;
        var portSuffix = uri.Port != defaultPort ? $"@{uri.Port}" : string.Empty;
        var sslSuffix = isHttps ? "@SSL" : string.Empty;
        var path = uri.AbsolutePath.Trim('/').Replace('/', '\\');

        uncPath = path.Length > 0
            ? $@"\\{uri.Host}{sslSuffix}{portSuffix}\{path}"
            : $@"\\{uri.Host}{sslSuffix}{portSuffix}";

        return true;
    }

    /// <summary>
    /// Kapcsolódás egy WebDAV-szerverhez. A meghajtóbetűjel nélküli
    /// (<c>lpLocalName = null</c>) kapcsolat NEM foglal el betűjelet — a
    /// szerver egyszerűen egy UNC-útvonalként érhető el utána.
    /// </summary>
    public static WebDavConnectResult Connect(string url, string? username, string? password, bool rememberCredentials)
    {
        if (!TryBuildUncPath(url, out var uncPath))
        {
            return new WebDavConnectResult(WebDavConnectOutcome.InvalidUrl, null, null);
        }

        var resource = new Mpr.NETRESOURCE
        {
            dwType = Mpr.NETRESOURCEType.RESOURCETYPE_DISK,
            lpRemoteName = uncPath,
        };

        var flags = rememberCredentials
            ? Mpr.CONNECT.CONNECT_UPDATE_PROFILE
            : (Mpr.CONNECT)0;

        var result = Mpr.WNetAddConnection2(resource, password, username, flags);

        return result.Succeeded
            ? new WebDavConnectResult(WebDavConnectOutcome.Succeeded, uncPath, null)
            : new WebDavConnectResult(WebDavConnectOutcome.ConnectFailed, null, (uint)result);
    }

    /// <summary>Egy korábban létrehozott kapcsolat bontása — a felhő meghajtó eltávolításakor.</summary>
    public static void Disconnect(string uncPath)
    {
        try
        {
            Mpr.WNetCancelConnection2(uncPath, Mpr.CONNECT.CONNECT_UPDATE_PROFILE, true);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A leválasztás legjobb-erőfeszítéses: ha már úgyis nem volt
            // kapcsolódva, vagy a szerver nem érhető el, a bejegyzés akkor is
            // eltűnhet az oldalsávból.
        }
    }
}
