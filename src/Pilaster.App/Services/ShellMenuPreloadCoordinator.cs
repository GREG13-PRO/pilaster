using Pilaster.Core.Settings;
using Pilaster.Shell.Menus;

namespace Pilaster.App.Services;

/// <summary>
/// A2 (v1.0.2): kijelöléskor, rövid debounce után ELŐRE elindítja a shell-menü
/// lekérdezését a kijelölt elem(ek)re, hogy egy rákövetkező jobbklikk a KÉSZ
/// eredménnyel nyithassa meg a menüt — a szokásos „Bővítmények betöltése…"
/// csúszás nélkül.
/// </summary>
/// <remarks>
/// <para>
/// Ez a kódrész a <c>ShellMenuSession</c>/<c>StaWorker</c> öt körös
/// heap-korrupciós előzménye miatt SZÁNDÉKOSAN szigorú korlátok között mozog:
/// </para>
/// <list type="bullet">
/// <item>Nem indít külön szálat — a <see cref="ShellMenuSession.QueryItemsAsync"/>
/// magától a MEGLÉVŐ, megosztott <c>StaWorker</c> sorára teszi a munkát.</item>
/// <item>Egyszerre legfeljebb EGY előretöltött munkamenetet tart életben — az
/// előzőt mindig eldobja, MIELŐTT az újat elindítaná.</item>
/// <item>A felszabadítás MINDIG az STA szálon történik — ezt a
/// <see cref="ShellMenuSession.Dispose"/> már önmagában garantálja (a
/// takarítást a saját <c>_worker.RunAsync</c>-jára teszi, bármelyik hívó
/// szálról hívva is).</item>
/// </list>
/// <para>
/// „Prioritás" jobbklikkre: mivel a megosztott sor egyetlen FIFO
/// <c>BlockingCollection</c>, egy MÁR ELINDULT (dequeue-olt) COM-hívást nem
/// lehet biztonságosan megszakítani — ez ma is így van a előmelegítés és egy
/// valódi lekérdezés versenyénél is (lásd <c>ShellMenuSession.RentShared</c>
/// megjegyzését). Ami viszont igen: egy MÉG SORBAN ÁLLÓ, de időközben
/// túlhaladott előretöltés az <c>isStillWanted</c> ellenőrzéssel a drága
/// COM-munka (factory/Populate) ELŐTT azonnal null-lal tér vissza, így a
/// mögötte sorba álló jobbklikk szinte azonnal futhat. Ez minden olyan
/// esetben egyenértékű egy valódi sor-újrarendezéssel, amikor az egyáltalán
/// segíthetne — a már elindult hívásnál semmilyen sorbeli megoldás nem
/// segítene.
/// </para>
/// </remarks>
public sealed class ShellMenuPreloadCoordinator : IDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan Expiry = TimeSpan.FromSeconds(30);

    private readonly ISettingsService _settings;
    private readonly Lock _gate = new();
    private readonly Timer _debounceTimer;
    private readonly Timer _expiryTimer;

    private int _generation;
    private IReadOnlyList<string>? _pendingPaths;
    private bool _pendingExtendedVerbs;

    private Task<ShellMenuSession?>? _preloadTask;
    private IReadOnlyList<string>? _preloadPaths;

    public ShellMenuPreloadCoordinator(ISettingsService settings)
    {
        _settings = settings;
        _debounceTimer = new Timer(OnDebounceElapsed, null, Timeout.Infinite, Timeout.Infinite);
        _expiryTimer = new Timer(OnExpiryElapsed, null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// A fájllista kijelölés-változása hívja. Nem foglalkozik azzal, hogy a
    /// kijelölés fájlokra vagy mappa-háttérre vonatkozik-e — csak a
    /// fájl-alapú (<see cref="ShellMenuSession.QueryItemsAsync"/>) útvonalat
    /// támogatja, mert a jobbklikk-menü döntő többsége erre esik, és a
    /// mappa-háttér (üres terület) kijelölés-független.
    /// </summary>
    public void NotifySelectionChanged(IReadOnlyList<string> paths, bool extendedVerbs)
    {
        if (!_settings.Current.ContextMenuPreloadEnabled)
        {
            return;
        }

        lock (_gate)
        {
            _generation++;
            _pendingPaths = paths;
            _pendingExtendedVerbs = extendedVerbs;

            if (paths.Count == 0)
            {
                _debounceTimer.Change(Timeout.Infinite, Timeout.Infinite);
                return;
            }

            _debounceTimer.Change(Debounce, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>
    /// Ha van (kész vagy még folyamatban lévő) előretöltés PONTOSAN erre a
    /// kijelölésre, átadja — a hívó (a jobbklikk-kezelő) ettől kezdve maga
    /// felel érte, ugyanúgy, mint egy friss lekérdezés eredményéért (a menü
    /// <c>Closed</c> eseménye dobja el).
    /// </summary>
    public Task<ShellMenuSession?>? TakeIfMatches(IReadOnlyList<string> paths)
    {
        lock (_gate)
        {
            if (_preloadTask is null || _preloadPaths is null || !PathsMatch(_preloadPaths, paths))
            {
                return null;
            }

            var task = _preloadTask;
            _preloadTask = null;
            _preloadPaths = null;
            _expiryTimer.Change(Timeout.Infinite, Timeout.Infinite);
            return task;
        }
    }

    private void OnDebounceElapsed(object? state)
    {
        IReadOnlyList<string> paths;
        bool extendedVerbs;
        int generation;

        lock (_gate)
        {
            if (_pendingPaths is not { Count: > 0 } pending)
            {
                return;
            }

            paths = pending;
            extendedVerbs = _pendingExtendedVerbs;
            generation = _generation;

            // Legfeljebb EGY előretöltött munkamenet — a régit eldobjuk,
            // MIELŐTT az újat elindítjuk (spec A2, kötelező).
            ReleaseCurrentLocked();

            _preloadTask = ShellMenuSession.QueryItemsAsync(
                paths,
                extendedVerbs,
                TimeSpan.FromMilliseconds(_settings.Current.ShellMenuTimeoutMs),
                _settings.Current.ShellHandlerBlacklist,
                isStillWanted: () => Volatile.Read(ref _generation) == generation);
            _preloadPaths = paths;

            _expiryTimer.Change(Expiry, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnExpiryElapsed(object? state)
    {
        lock (_gate)
        {
            ReleaseCurrentLocked();
        }
    }

    /// <summary>A <see cref="_gate"/> zár alatt hívandó — a régi munkamenet eldobása, ha van.</summary>
    private void ReleaseCurrentLocked()
    {
        _expiryTimer.Change(Timeout.Infinite, Timeout.Infinite);

        var task = _preloadTask;
        _preloadTask = null;
        _preloadPaths = null;

        if (task is null)
        {
            return;
        }

        // A Dispose() önmagában is az STA szálra teszi a takarítást (lásd
        // ShellMenuSession megjegyzését) — bármelyik hívó szálról biztonságos.
        _ = task.ContinueWith(
            t => t.Result?.Dispose(),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.Default);
    }

    private static bool PathsMatch(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (!string.Equals(a[i], b[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    public void Dispose()
    {
        _debounceTimer.Dispose();
        _expiryTimer.Dispose();

        lock (_gate)
        {
            ReleaseCurrentLocked();
        }
    }
}
