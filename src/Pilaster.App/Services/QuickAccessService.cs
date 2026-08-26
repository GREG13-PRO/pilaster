using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;
using Pilaster.Core.Settings;

namespace Pilaster.App.Services;

/// <summary>A <see cref="QuickAccessDocument"/> forrásgenerált szerializálási környezete.</summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(QuickAccessDocument))]
internal sealed partial class QuickAccessJsonContext : JsonSerializerContext;

/// <summary>
/// A gyorselérés perzisztens tárolása és karbantartása.
/// </summary>
/// <remarks>
/// <para>
/// A v0.9-ig a gyorselérés a <c>settings.json</c>-ban élt egy egyszerű
/// útvonal-listaként, és csak elrejteni lehetett elemeket — a változtatás nem
/// mindig maradt meg. A v1.0 saját, verziózott sémájú fájlt kap
/// (<c>%APPDATA%\Pilaster\quickaccess.json</c>), MINDEN módosítás azonnal (300 ms
/// késleltetéssel összevonva) mentődik, és a régi lista automatikusan
/// átmigrálódik — lásd <see cref="MigrateFromLegacyPins"/>.
/// </para>
/// <para>
/// Az elérhetőség-ellenőrzés aszinkron és időkorlátos: egy leválasztott
/// hálózati megosztás (<c>\\server\share</c>) <see cref="Directory.Exists"/>
/// hívása több MÁSODPERCIG is blokkolhat, ami a UI-szálon fagyásnak látszana.
/// </para>
/// </remarks>
public sealed class QuickAccessService : IDisposable
{
    /// <summary>Lásd <c>JsonSettingsService</c> — a spec 300 ms-os összevonást kér.</summary>
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// Egy útvonal elérhetőségére ennyit várunk. Egy leválasztott hálózati
    /// meghajtónál a rendszer alapértelmezett időkorlátja tíz másodperc
    /// nagyságrendű is lehet — ennyit egyetlen oldalsáv-sor sem ér meg.
    /// </summary>
    private static readonly TimeSpan ReachabilityTimeout = TimeSpan.FromMilliseconds(800);

    private readonly string _filePath;
    private readonly DispatcherTimer _saveTimer;
    private readonly Lock _fileLock = new();

    /// <summary>Útvonalanként gyorsítótárazott elérhetőség — enélkül minden újraépítés újra megvárná a hálózatot.</summary>
    private readonly ConcurrentDictionary<string, bool> _reachability = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Az alapértelmezett hat mappa színe, fordítási kulcs szerint — a
    /// <see cref="BuildDefaults"/> ÉS a <see cref="Migrate"/> is ebből olvas,
    /// hogy a régebbi (szín nélküli) gyorselérés-fájlokban is megjelenjen a
    /// színes ikon utólag, nem csak „Visszaállítás alapértelmezettre" után.
    /// </summary>
    private static readonly Dictionary<string, string> DefaultColors = new(StringComparer.Ordinal)
    {
        ["Nav_Desktop"] = "#0891B2",
        ["Nav_Documents"] = "#2563EB",
        ["Nav_Pictures"] = "#C026D3",
        ["Nav_Music"] = "#EA580C",
        ["Nav_Videos"] = "#7C3AED",
        ["Nav_Downloads"] = "#16A34A",
    };

    /// <summary>A Lomtár oldalsáv-ikonjának színe — lásd <c>MainWindowViewModel.BuildQuickAccess</c>.</summary>
    public const string RecycleBinIconColor = "#2563EB";

    /// <summary>A Felhő meghajtók szekció ikonjainak színe — lásd <c>MainWindowViewModel.BuildCloudDrives</c>.</summary>
    public const string CloudDriveIconColor = "#0284C7";

    private QuickAccessDocument _document;

    /// <param name="storageDirectory">
    /// A tárolás mappája. Éles futásban <c>null</c> (a felhasználói profil);
    /// a paraméter kizárólag azért létezik, hogy a perzisztencia
    /// egységtesztelhető legyen a valódi profil érintése nélkül.
    /// </param>
    public QuickAccessService(string? storageDirectory = null)
    {
        // Hordozható módban a program saját mappája, egyébként %APPDATA% —
        // lásd AppDataLocator.
        var directory = storageDirectory ?? AppDataLocator.Directory;

        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "quickaccess.json");

        _document = Load();

        _saveTimer = new DispatcherTimer { Interval = SaveDelay };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            Flush();
        };
    }

    /// <summary>Bármilyen változás után tüzel — az oldalsáv erre épül újra.</summary>
    public event EventHandler? Changed;

    /// <summary>A rögzített bejegyzések, sorrendben.</summary>
    public IReadOnlyList<QuickAccessEntry> Pinned =>
        [.. _document.Entries.Where(e => e.Pinned).OrderBy(e => e.Order)];

    /// <summary>A „Legutóbbi" szekció elemei, a legfrissebbel az élen.</summary>
    public IReadOnlyList<QuickAccessEntry> Recent =>
        _document.RecentEnabled
            ? [.. _document.Entries
                .Where(e => !e.Pinned && e.Kind == QuickAccessEntryKind.Folder)
                .OrderByDescending(e => e.LastOpenedUtc ?? DateTimeOffset.MinValue)
                .Take(_document.RecentLimit)]
            : [];

    public bool RecentEnabled
    {
        get => _document.RecentEnabled;
        set
        {
            if (_document.RecentEnabled == value)
            {
                return;
            }

            _document.RecentEnabled = value;
            NotifyChanged();
        }
    }

    public int RecentLimit
    {
        get => _document.RecentLimit;
        set
        {
            var clamped = Math.Clamp(value, 1, 40);

            if (_document.RecentLimit == clamped)
            {
                return;
            }

            _document.RecentLimit = clamped;
            NotifyChanged();
        }
    }

    /// <summary>
    /// A rögzített bejegyzések teljes cseréje — a szerkesztő „Mentés" gombja
    /// ezt hívja. A „Legutóbbi" elemek érintetlenül maradnak.
    /// </summary>
    public void ReplacePinned(IEnumerable<QuickAccessEntry> entries)
    {
        var recent = _document.Entries.Where(e => !e.Pinned).ToList();
        var pinned = entries.ToList();

        for (var i = 0; i < pinned.Count; i++)
        {
            pinned[i].Pinned = true;
            pinned[i].Order = i;
        }

        _document.Entries = [.. pinned, .. recent];
        NotifyChanged();
    }

    /// <summary>Egy mappa rögzítése. Már rögzített útvonalnál nem csinál semmit (nincs duplikáció).</summary>
    public void Pin(string path, string? label = null, int? index = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (_document.Entries.Any(e => e.Pinned && string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var pinned = Pinned.ToList();

        pinned.Insert(
            Math.Clamp(index ?? pinned.Count, 0, pinned.Count),
            new QuickAccessEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Path = path,
                Label = label ?? Path.GetFileName(Path.TrimEndingDirectorySeparator(path)),
            });

        ReplacePinned(pinned);
    }

    /// <summary>Egy bejegyzés eltávolítása azonosító alapján — rögzített és legutóbbi elemre egyaránt.</summary>
    public void Remove(string id)
    {
        if (_document.Entries.RemoveAll(e => e.Id == id) > 0)
        {
            NotifyChanged();
        }
    }

    /// <summary>Egy rögzített bejegyzés áthelyezése a lista adott pozíciójára — húzásos átrendezés.</summary>
    public void Reorder(string id, int newIndex)
    {
        var pinned = Pinned.ToList();
        var entry = pinned.FirstOrDefault(e => e.Id == id);

        if (entry is null)
        {
            return;
        }

        pinned.Remove(entry);
        pinned.Insert(Math.Clamp(newIndex, 0, pinned.Count), entry);
        ReplacePinned(pinned);
    }

    /// <summary>Egy bejegyzés mezőinek frissítése (átnevezés, ikon, szín, láthatóság, útvonal-javítás).</summary>
    public void Update(string id, Action<QuickAccessEntry> mutate)
    {
        if (_document.Entries.FirstOrDefault(e => e.Id == id) is not { } entry)
        {
            return;
        }

        mutate(entry);
        _reachability.TryRemove(entry.Path, out _);
        NotifyChanged();
    }

    /// <summary>
    /// Egy megnyitott mappa felvétele a „Legutóbbi" szekcióba. A már
    /// rögzített mappák kimaradnak — nem lenne értelme kétszer szerepelniük.
    /// </summary>
    public void RecordRecent(string path)
    {
        if (!_document.RecentEnabled || string.IsNullOrWhiteSpace(path) || path.StartsWith("pilaster:", StringComparison.Ordinal))
        {
            return;
        }

        if (_document.Entries.Any(e => e.Pinned && string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        if (_document.Entries.FirstOrDefault(e => !e.Pinned && string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase)) is { } existing)
        {
            existing.LastOpenedUtc = DateTimeOffset.UtcNow;
        }
        else
        {
            _document.Entries.Add(new QuickAccessEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Path = path,
                Pinned = false,
                LastOpenedUtc = DateTimeOffset.UtcNow,
            });
        }

        // A limiten túli, legrégebbi elemek eldobása, hogy a fájl ne hízzon.
        var stale = _document.Entries
            .Where(e => !e.Pinned)
            .OrderByDescending(e => e.LastOpenedUtc ?? DateTimeOffset.MinValue)
            .Skip(_document.RecentLimit)
            .ToList();

        foreach (var entry in stale)
        {
            _document.Entries.Remove(entry);
        }

        NotifyChanged();
    }

    /// <summary>A teljes „Legutóbbi" szekció ürítése.</summary>
    public void ClearRecent()
    {
        if (_document.Entries.RemoveAll(e => !e.Pinned) > 0)
        {
            NotifyChanged();
        }
    }

    /// <summary>Az alapértelmezett gyorselérés visszaállítása — a szerkesztő gombja.</summary>
    public void ResetToDefaults()
    {
        _document.Entries = [.. BuildDefaults(), .. _document.Entries.Where(e => !e.Pinned)];
        NotifyChanged();
    }

    /// <summary>Export JSON-fájlba. Hamis, ha az írás nem sikerült.</summary>
    public bool TryExport(string path)
    {
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(_document, QuickAccessJsonContext.Default.QuickAccessDocument));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>Import JSON-fájlból, a jelenlegi tartalom teljes cseréjével. Hamis, ha a fájl nem olvasható vagy nem értelmezhető.</summary>
    public bool TryImport(string path)
    {
        try
        {
            var loaded = JsonSerializer.Deserialize(File.ReadAllText(path), QuickAccessJsonContext.Default.QuickAccessDocument);

            if (loaded is null)
            {
                return false;
            }

            _document = Migrate(loaded);
            NotifyChanged();
            return true;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Egy útvonal elérhetősége a gyorsítótárból. Ismeretlen útvonalnál
    /// optimistán igazat ad, és a háttérben indít egy ellenőrzést — a sor így
    /// azonnal megjelenik, és legfeljebb egy pillanattal később szürkül el.
    /// </summary>
    public bool IsReachable(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith("pilaster:", StringComparison.Ordinal))
        {
            return true;
        }

        if (_reachability.TryGetValue(path, out var cached))
        {
            return cached;
        }

        _ = ProbeAsync(path);
        return true;
    }

    private async Task ProbeAsync(string path)
    {
        // A Directory.Exists egy leválasztott hálózati megosztáson MÁSODPERCEKIG
        // blokkol. Háttérszálon indítjuk, és időkorláttal várjuk meg — a szál
        // ottmaradhat, de a UI nem áll meg miatta.
        var probe = Task.Run(() =>
        {
            try
            {
                return Directory.Exists(path) || File.Exists(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        });

        var reachable = await Task.WhenAny(probe, Task.Delay(ReachabilityTimeout)) == probe && probe.Result;

        if (_reachability.TryGetValue(path, out var previous) && previous == reachable)
        {
            return;
        }

        _reachability[path] = reachable;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Az elérhetőség-gyorsítótár ürítése — kézi Frissítés vagy meghajtó-csatlakoztatás után.</summary>
    public void InvalidateReachability()
    {
        _reachability.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// A v0.9-es <c>settings.json</c>-ben tárolt gyorselérés átvétele.
    /// Egyszer fut le, az első v1.0-s indításkor: ha a saját fájl még üres,
    /// de a régi lista létezik, azt vesszük át — így a felhasználó nem
    /// veszíti el a testreszabását.
    /// </summary>
    public void MigrateFromLegacyPins(List<PinnedFolder>? legacy)
    {
        if (_document.Entries.Count > 0)
        {
            return;
        }

        if (legacy is not { Count: > 0 })
        {
            _document.Entries = BuildDefaults();
            NotifyChanged();
            return;
        }

        _document.Entries =
        [
            .. legacy.Select((pin, index) => new QuickAccessEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Path = pin.Path,
                LabelKey = pin.LabelKey,
                Label = pin.CustomLabel,
                Icon = pin.Icon,
                Order = index,
            }),
        ];

        NotifyChanged();
    }

    /// <summary>Az alapértelmezett hat mappa — csak első használatkor, illetve a „Visszaállítás" gombra.</summary>
    private static List<QuickAccessEntry> BuildDefaults()
    {
        (Environment.SpecialFolder Folder, string Key, string Icon)[] defaults =
        [
            (Environment.SpecialFolder.Desktop, "Nav_Desktop", "Desktop24"),
            (Environment.SpecialFolder.MyDocuments, "Nav_Documents", "Document24"),
            (Environment.SpecialFolder.MyPictures, "Nav_Pictures", "Image24"),
            (Environment.SpecialFolder.MyMusic, "Nav_Music", "MusicNote124"),
            (Environment.SpecialFolder.MyVideos, "Nav_Videos", "Video24"),
        ];

        var entries = new List<QuickAccessEntry>();

        foreach (var (folder, key, icon) in defaults)
        {
            var path = Environment.GetFolderPath(folder);

            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            {
                entries.Add(new QuickAccessEntry
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Path = path,
                    LabelKey = key,
                    Icon = icon,
                    Color = DefaultColors[key],
                });
            }
        }

        // A Letöltések mappának nincs SpecialFolder megfelelője, ezért külön.
        var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

        if (Directory.Exists(downloads))
        {
            entries.Insert(Math.Min(2, entries.Count), new QuickAccessEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Path = downloads,
                LabelKey = "Nav_Downloads",
                Icon = "ArrowDownload24",
                Color = DefaultColors["Nav_Downloads"],
            });
        }

        for (var i = 0; i < entries.Count; i++)
        {
            entries[i].Order = i;
        }

        return entries;
    }

    private void NotifyChanged()
    {
        Changed?.Invoke(this, EventArgs.Empty);
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void Flush()
    {
        lock (_fileLock)
        {
            try
            {
                var json = JsonSerializer.Serialize(_document, QuickAccessJsonContext.Default.QuickAccessDocument);
                var temporary = _filePath + ".tmp";
                File.WriteAllText(temporary, json);
                File.Move(temporary, _filePath, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Lásd JsonSettingsService.Flush: egy sikertelen mentés soha ne
                // akassza meg a programot.
            }
        }
    }

    private QuickAccessDocument Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return new QuickAccessDocument();
            }

            var loaded = JsonSerializer.Deserialize(File.ReadAllText(_filePath), QuickAccessJsonContext.Default.QuickAccessDocument);

            return loaded is null ? new QuickAccessDocument() : Migrate(loaded);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new QuickAccessDocument();
        }
    }

    /// <summary>
    /// Séma-migráció. Jelenleg egyetlen verzió létezik, de a belépési pont
    /// megvan: egy jövőbeli mezőátnevezésnél ide kerül a konverzió, és a
    /// felhasználó adata nem vész el.
    /// </summary>
    private static QuickAccessDocument Migrate(QuickAccessDocument document)
    {
        // Jövőbeli lépcsők helye:  if (document.Version < 2) { ...; document.Version = 2; }

        // Védekezés a kézzel szerkesztett vagy sérült fájl ellen: az azonosító
        // nélküli bejegyzések kapjanak egyet, különben nem lennének
        // eltávolíthatók vagy átrendezhetők.
        foreach (var entry in document.Entries.Where(e => string.IsNullOrWhiteSpace(e.Id)))
        {
            entry.Id = Guid.NewGuid().ToString("N");
        }

        // Utólagos színezés (spec: oldalsáv-redesign): a régebbi, szín nélkül
        // mentett fájlokban is megjelenjen a színes ikon a hat alapértelmezett
        // mappánál, nem csak új telepítésnél vagy „Visszaállítás" után. Csak
        // akkor nyúl hozzá, ha a felhasználó még nem állított be sajátot.
        foreach (var entry in document.Entries)
        {
            if (entry.LabelKey is { } key && string.IsNullOrEmpty(entry.Color) && DefaultColors.TryGetValue(key, out var color))
            {
                entry.Color = color;
            }
        }

        document.Version = QuickAccessDocument.CurrentVersion;
        return document;
    }

    public void Dispose()
    {
        _saveTimer.Stop();
        Flush();
    }
}
