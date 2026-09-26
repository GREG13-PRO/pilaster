using System.Collections.Concurrent;
using System.IO;
using System.Windows.Threading;
using Pilaster.Core.FileSystem;

namespace Pilaster.App.Services;

/// <summary>
/// Mappák rekurzív méretének háttérben történő kiszámítása, munkamenet-szintű
/// gyorsítótárral.
/// </summary>
/// <remarks>
/// <para>
/// A gyorsítótár szándékosan csak memóriában él, az alkalmazás
/// élettartamáig — ez elég ahhoz, hogy ugyanannak a mappának a
/// visszalátogatása ne indítson újra egy esetleg lassú, teljes bejárást,
/// de nem kell foglalkozni lemezre írt, elavulható adattal.
/// </para>
/// <para>
/// A párhuzamosság korlátozott: egy nagy mappa több száz almappájának
/// egyidejű, korlátlan bejárása lemez-túlterheléshez és UI-akadáshoz
/// vezetne, ezért egy <see cref="SemaphoreSlim"/> fogja vissza az aktív
/// számításokat.
/// </para>
/// </remarks>
public sealed class FolderSizeService
{
    private readonly IFileSystemProvider _provider;
    private readonly ConcurrentDictionary<string, long> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _concurrency = new(Math.Max(2, Environment.ProcessorCount / 2));

    public FolderSizeService(IFileSystemProvider provider)
    {
        _provider = provider;
    }

    /// <summary>
    /// Biztosítja, hogy az elem <see cref="FileSystemItem.ComputedFolderSize"/>
    /// mezője előbb-utóbb kitöltődjön — azonnal a gyorsítótárból, vagy
    /// háttérben kiszámolva.
    /// </summary>
    public void EnsureComputed(FileSystemItem item, CancellationToken cancellationToken)
    {
        if (item.Kind != FileSystemItemKind.Directory)
        {
            return;
        }

        if (_cache.TryGetValue(item.FullPath, out var cached))
        {
            item.ComputedFolderSize = cached;
            return;
        }

        _ = ComputeAsync(item, cancellationToken);
    }

    /// <summary>
    /// A <paramref name="path"/> alatti ÉS feletti mappák gyorsítótárazott
    /// méretének eldobása — frissítéskor és fájlművelet után. Enélkül egy
    /// mappába másolás után a régi méret az alkalmazás teljes élettartamára
    /// megmaradt, az F5 sem számolta újra.
    /// </summary>
    public void InvalidateTree(string path)
    {
        var normalized = Path.TrimEndingDirectorySeparator(path);

        foreach (var key in _cache.Keys)
        {
            var normalizedKey = Path.TrimEndingDirectorySeparator(key);

            if (IsSameOrUnder(normalizedKey, normalized) || IsSameOrUnder(normalized, normalizedKey))
            {
                _cache.TryRemove(key, out _);
            }
        }
    }

    private static bool IsSameOrUnder(string candidate, string folder) =>
        string.Equals(candidate, folder, StringComparison.OrdinalIgnoreCase)
        || candidate.StartsWith(
            Path.EndsInDirectorySeparator(folder) ? folder : folder + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);

    private async Task ComputeAsync(FileSystemItem item, CancellationToken cancellationToken)
    {
        try
        {
            await _concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            var size = await _provider.GetFolderSizeAsync(item.FullPath, cancellationToken).ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            _cache[item.FullPath] = size;

            await OnUiAsync(() => item.ComputedFolderSize = size).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Elnavigáltunk a mappáról, mielőtt a számítás befejeződött volna.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A mappa időközben eltűnt, vagy megtagadta a hozzáférést — a méret
            // marad kiszámolatlan ("…"), nincs jobb teendő.
        }
        finally
        {
            _concurrency.Release();
        }
    }

    /// <summary>
    /// A <see cref="FileSystemItem"/> WPF-kötésekhez kapcsolódik, ezért a
    /// tulajdonságát csak a UI-szálról biztonságos módosítani.
    /// </summary>
    private static async Task OnUiAsync(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        await dispatcher.InvokeAsync(action, DispatcherPriority.Background);
    }
}
