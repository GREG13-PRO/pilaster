using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace Pilaster.App.Services;

/// <summary>Egy beragadt shell-lekérdezés adatai — a flag fájl tartalma.</summary>
/// <param name="Path">Melyik útvonalon indult a lekérdezés.</param>
/// <param name="Kind">Elemek menüje (<c>items</c>) vagy háttér-menü (<c>background</c>).</param>
/// <param name="StartedUtc">Mikor indult.</param>
public sealed record ShellInflightRecord(string Path, string Kind, DateTimeOffset StartedUtc);

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ShellInflightRecord))]
internal sealed partial class ShellInflightJsonContext : JsonSerializerContext;

/// <summary>
/// Összeomlás-felismerés a shell-bővítmények körül.
/// </summary>
/// <remarks>
/// <para>
/// A shell-menü lekérdezése ma egy folyamaton belül fut (a folyamat-izoláció
/// v1.1). A kivétel, a beragadás és a hibás menüfa ellen védve vagyunk, de egy
/// natív hozzáférési hiba (AV) a bővítmény kódjában viszi a folyamatot — azt
/// elkapni nem lehet.
/// </para>
/// <para>
/// Ez az osztály a KÖVETKEZŐ indulást menti meg: a lekérdezés ELŐTT kiírunk
/// egy jelzőfájlt, utána töröljük. Ha induláskor ott találjuk, az azt jelenti,
/// hogy a legutóbbi lekérdezés közben halt meg a folyamat — ilyenkor
/// shell-bővítmények NÉLKÜL indulunk, és megmondjuk, melyik útvonalon
/// történt, hogy a felhasználó ki tudja feketelistázni a bűnöst.
/// </para>
/// </remarks>
public sealed class ShellCrashGuard
{
    private readonly string _flagPath;

    public ShellCrashGuard()
    {
        // A jelző a gyorsítótár-jellegű adatok közé, a LocalAppData alá megy —
        // nem a felhasználó tartalma, és nem is hordozandó.
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Pilaster");

        Directory.CreateDirectory(directory);
        _flagPath = Path.Combine(directory, "shell.inflight");
    }

    /// <summary>
    /// Igaz, ha az előző futás egy shell-lekérdezés közben halt meg. A nézet
    /// ez alapján jelzi a menü tetején, miért hiányoznak az elemek.
    /// </summary>
    public bool CrashDetected { get; private set; }

    /// <summary>A beragadt lekérdezés adatai, ha volt ilyen.</summary>
    public ShellInflightRecord? LastCrash { get; private set; }

    /// <summary>
    /// Induláskori ellenőrzés. Beragadt jelzőnél kikapcsolja a
    /// shell-bővítményeket, és eltakarítja a jelzőt.
    /// </summary>
    public void CheckOnStartup()
    {
        if (!File.Exists(_flagPath))
        {
            return;
        }

        try
        {
            LastCrash = JsonSerializer.Deserialize(
                File.ReadAllText(_flagPath),
                ShellInflightJsonContext.Default.ShellInflightRecord);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A jelző puszta LÉTEZÉSE a jel; a tartalma csak kényelmi adat.
        }

        CrashDetected = true;
        Clear();
    }

    /// <summary>
    /// A jelző írását és törlését védi. Az „ellenőrzés + írás" nem lehet két
    /// külön lépés: az előmelegítés háttérszála és a kilépő UI-szál
    /// egyszerre nyúlnak a jelzőhöz.
    /// </summary>
    private readonly Lock _gate = new();

    private bool _shuttingDown;

    /// <summary>
    /// Szabályos kilépés kezdete: a jelzőt töröljük, és többé nem írunk újat.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A puszta törlés NEM elég: az előmelegítés háttérszála a törlés UTÁN is
    /// kiírhatná a következő jelzőt, és akkor egy szabályos kilépés hamis
    /// összeomlásnak látszana — a következő indulás fölöslegesen kapcsolná ki
    /// a bővítményeket. Ezért a jelzőbit és a törlés EGY záron belül van, és a
    /// <see cref="MarkInflight"/> ugyanazt a zárat kéri: a jelölés vagy a
    /// kilépés elé kerül (és akkor a törlés elviszi), vagy mögé (és akkor
    /// kimarad).
    /// </para>
    /// <para>
    /// MÉRVE a javítás után: 1, 2, 3, 4 és 6 másodperc után bezárva —
    /// 5-ből 0 hamis jelző.
    /// </para>
    /// </remarks>
    public void BeginShutdown()
    {
        lock (_gate)
        {
            _shuttingDown = true;
            DeleteFlag();
        }
    }

    /// <summary>A lekérdezés megkezdésének jelzése.</summary>
    public void MarkInflight(string path, string kind)
    {
        lock (_gate)
        {
            if (_shuttingDown)
            {
                return;
            }

            try
            {
                File.WriteAllText(
                    _flagPath,
                    JsonSerializer.Serialize(
                        new ShellInflightRecord(path, kind, DateTimeOffset.UtcNow),
                        ShellInflightJsonContext.Default.ShellInflightRecord));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Ha nem tudunk jelzőt írni, a védelem kimarad — de a menü működik.
            }
        }
    }

    /// <summary>A lekérdezés sikeres befejezésének jelzése.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            DeleteFlag();
        }
    }

    private void DeleteFlag()
    {
        try
        {
            if (File.Exists(_flagPath))
            {
                File.Delete(_flagPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// A felhasználó újra bekapcsolta a bővítményeket a Beállításokban —
    /// a figyelmeztetés eltűnik a menüből.
    /// </summary>
    public void Acknowledge()
    {
        CrashDetected = false;
        LastCrash = null;
    }
}
