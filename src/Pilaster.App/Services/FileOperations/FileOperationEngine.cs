using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Pilaster.App.Localization;
using Pilaster.Shell.Recycle;
using Serilog;

namespace Pilaster.App.Services.FileOperations;

/// <summary>
/// Saját másolási/áthelyezési/törlési motor — NEM a Windows beépített
/// másoló-párbeszédablakát hívja, hanem közvetlen, darabolt (chunk-olt)
/// fájl-I/O-t végez, valódi szüneteltetés/folytatás/megszakítás és
/// haladásjelzés mellett. A <see cref="Jobs"/> gyűjtemény közvetlenül
/// köthető az Aktivitás-központ paneljéhez.
/// </summary>
/// <remarks>
/// Ez a motor kizárólag a HELYI fájlrendszerre dolgozik (nem az
/// <see cref="Pilaster.Core.FileSystem.IFileSystemProvider"/> absztrakción
/// keresztül) — mivel jelenleg csak helyi provider létezik, a chunk-szintű
/// szüneteltetés/haladás-visszajelzés miatt közvetlen <see cref="FileStream"/>-
/// hozzáférés egyszerűbb és gyorsabb, mint egy új interfész-réteg bevezetése
/// egyetlen implementációhoz.
/// </remarks>
public sealed class FileOperationEngine
{
    private const int BufferSize = 1024 * 1024; // 1 MB — elég nagy a torkolattorlódás elkerüléséhez, elég kicsi a reszponzív szüneteltetéshez/megszakításhoz.
    private static readonly TimeSpan SpeedSampleInterval = TimeSpan.FromMilliseconds(400);

    public ObservableCollection<FileOperationJob> Jobs { get; } = [];

    // Háttérszálon indul: a RunAsync első, szinkron szakasza (Directory.Exists,
    // könyvtárbejárás, ugyanazon kötetes File.Move) különben a UI-szálon
    // futna, és egy lassú/hálózati meghajtón befagyasztaná az ablakot. A
    // Jobs-gyűjtemény és a job-állapot módosítása OnUiAsync-on át marad a
    // UI-szálon.
    public void StartCopy(IReadOnlyList<string> sourcePaths, string destinationDirectory) =>
        _ = Task.Run(() => RunAsync(FileOperationKind.Copy, [.. sourcePaths], destinationDirectory));

    public void StartMove(IReadOnlyList<string> sourcePaths, string destinationDirectory) =>
        _ = Task.Run(() => RunAsync(FileOperationKind.Move, [.. sourcePaths], destinationDirectory));

    /// <summary>
    /// Két útvonal ugyanarra az elemre mutat-e (kis-/nagybetű- és záró
    /// elválasztó-függetlenül).
    /// </summary>
    internal static bool IsSamePath(string a, string b) =>
        string.Equals(NormalizePath(a), NormalizePath(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Igaz, ha <paramref name="candidate"/> maga <paramref name="folder"/>, vagy
    /// annak (akármilyen mély) almappája — egy mappát önmagába másolni/áthelyezni
    /// végtelen rekurzió lenne.
    /// </summary>
    internal static bool IsSameOrInside(string candidate, string folder)
    {
        var normalizedCandidate = NormalizePath(candidate);
        var normalizedFolder = NormalizePath(folder);

        return string.Equals(normalizedCandidate, normalizedFolder, StringComparison.OrdinalIgnoreCase)
            || normalizedCandidate.StartsWith(normalizedFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Path.TrimEndingDirectorySeparator(path);
        }
    }

    private static bool IsSameVolume(string a, string b) =>
        string.Equals(Path.GetPathRoot(NormalizePath(a)), Path.GetPathRoot(NormalizePath(b)), StringComparison.OrdinalIgnoreCase);

    /// <summary>Törlés — alapból Lomtárba, <paramref name="permanent"/> esetén azonnal véglegesen.</summary>
    public void StartDelete(IReadOnlyList<string> sourcePaths, bool permanent) =>
        _ = RunDeleteAsync(sourcePaths, permanent);

    private async Task RunDeleteAsync(IReadOnlyList<string> sourcePaths, bool permanent)
    {
        var job = new FileOperationJob
        {
            Id = Guid.NewGuid(),
            Kind = FileOperationKind.Delete,
            DestinationDirectory = string.Empty,
            SourcePaths = [.. sourcePaths],
            TotalFiles = sourcePaths.Count,
        };

        await OnUiAsync(() => Jobs.Insert(0, job));

        var errors = new List<string>();

        foreach (var path in sourcePaths)
        {
            if (job.Cancellation.IsCancellationRequested)
            {
                break;
            }

            await OnUiAsync(() => job.CurrentFileName = Path.GetFileName(Path.TrimEndingDirectorySeparator(path)));

            try
            {
                // A Shell COM törlés (IFileOperation) STA szálat követel meg —
                // a WPF UI-szál STA, egy Task.Run-nal indított háttérszál
                // viszont MTA, és ThreadStateException-nel elszállna. Ezért
                // ezt KIFEJEZETTEN a UI-szálon hívjuk, nem háttérben.
                await OnUiAsync(() =>
                {
                    if (permanent)
                    {
                        RecycleBinService.DeletePermanently(path);
                    }
                    else
                    {
                        RecycleBinService.SendToRecycleBin(path);
                    }
                });
            }
            catch (Exception ex)
            {
                // Nemcsak IOException/UnauthorizedAccessException: a
                // ShellFileOperations COM-hívás más kivétellel is
                // elszállhat — bármelyik itt kötjön ki, hogy a job biztosan
                // véglegesállapotba jusson, ne ragadjon "Running"-on örökre.
                errors.Add($"{Path.GetFileName(path)}: {ex.Message}");
                Log.Warning(ex, "Törlés sikertelen: {Path}", path);
            }

            await OnUiAsync(() => job.FilesCompleted++);
        }

        await OnUiAsync(() =>
        {
            if (job.Cancellation.IsCancellationRequested)
            {
                job.State = FileOperationState.Cancelled;
            }
            else if (errors.Count > 0)
            {
                job.ErrorSummary = string.Join(Environment.NewLine, errors);
                job.State = FileOperationState.CompletedWithErrors;
            }
            else
            {
                job.State = FileOperationState.Completed;
            }
        });
    }

    private async Task RunAsync(FileOperationKind kind, IReadOnlyList<string> sourcePaths, string destinationDirectory)
    {
        var (totalFiles, totalBytes) = CountFilesAndBytes(sourcePaths);

        var job = new FileOperationJob
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            DestinationDirectory = destinationDirectory,
            SourcePaths = sourcePaths,
            TotalFiles = totalFiles,
        };
        job.TotalBytes = totalBytes;

        await OnUiAsync(() => Jobs.Insert(0, job));

        var errors = new List<string>();
        FileConflictAction? applyToAllAction = null;
        var speedTracker = new SpeedTracker();
        var context = new CopyContext(job, errors, speedTracker, () => applyToAllAction, v => applyToAllAction = v, kind == FileOperationKind.Move);

        try
        {
            foreach (var sourcePath in sourcePaths)
            {
                job.Cancellation.Token.ThrowIfCancellationRequested();
                await job.PauseGate.WaitIfPausedAsync().WaitAsync(job.Cancellation.Token).ConfigureAwait(false);

                var isDirectory = Directory.Exists(sourcePath);
                var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(sourcePath));
                var destPath = Path.Combine(destinationDirectory, name);

                await OnUiAsync(() => job.CurrentFileName = name);

                if (!isDirectory && !File.Exists(sourcePath))
                {
                    // Közben törölték/átnevezték — egy hiányzó forrás ne állítsa
                    // le a teljes műveletet, csak ez az egy elem hiúsuljon meg.
                    errors.Add($"{name}: {TranslationSource.Instance["FileOp_SourceMissing"]}");
                    continue;
                }

                // Egy mappa önmagába/saját almappájába másolása vagy áthelyezése
                // végtelen rekurzió lenne (áthelyezésnél ráadásul a forrás
                // törlésével) — az Intéző is megtagadja.
                if (isDirectory && IsSameOrInside(destinationDirectory, sourcePath))
                {
                    errors.Add($"{name}: {TranslationSource.Instance["FileOp_IntoItself"]}");
                    continue;
                }

                if (IsSamePath(sourcePath, destPath))
                {
                    if (kind == FileOperationKind.Move)
                    {
                        // Ugyanoda áthelyezni: nincs mit tenni. Korábban ez a
                        // másolás+forrástörlés útra esett, és ELVESZÍTETTE a fájlt.
                        var (sameFiles, sameBytes) = CountFilesAndBytes([sourcePath]);
                        await OnUiAsync(() =>
                        {
                            job.FilesCompleted += sameFiles;
                            job.BytesCompleted += sameBytes;
                        });
                        continue;
                    }

                    // Ugyanabba a mappába másolás: másolat új néven, mint az
                    // Intézőben — sosem önmagára írás.
                    destPath = MakeUniqueDestination(destPath);
                }

                if (kind == FileOperationKind.Move && IsSameVolume(sourcePath, destinationDirectory))
                {
                    // A méretet/fájlszámot a mozgatás ELŐTT kell megállapítani —
                    // egy sikeres File.Move/Directory.Move után a forrás már
                    // nem létezik azon az útvonalon, egy utólagos FileInfo
                    // lekérdezés FileNotFoundException-t dobna.
                    var (fastMoveFiles, fastMoveBytes) = CountFilesAndBytes([sourcePath]);

                    if (TryFastMove(sourcePath, destPath, isDirectory))
                    {
                        await OnUiAsync(() =>
                        {
                            job.FilesCompleted += fastMoveFiles;
                            job.BytesCompleted += fastMoveBytes;
                        });
                        continue;
                    }
                }

                // Kötetek közti (vagy ütköző) áthelyezés: másolás, majd a forrás
                // FÁJLONKÉNTI törlése — csak az sikeresen átmásolt fájloké. Egy
                // kihagyott/hibás fájl forrása így megmarad (korábban a teljes
                // forrásmappa rekurzívan törlődött, a kihagyott fájlokkal együtt).
                if (isDirectory)
                {
                    await CopyDirectoryAsync(sourcePath, destPath, context).ConfigureAwait(false);
                }
                else
                {
                    await CopySingleFileAsync(sourcePath, destPath, context).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // A megszakítás pillanatában épp írt célfájl hiányos maradt —
            // ez takarítja el, hogy ne maradjon látszólag kész, de valójában
            // csonka fájl a cél mappában.
            if (job.CurrentDestinationPath is { } partialPath)
            {
                TryDeletePartialFile(partialPath);
            }

            await OnUiAsync(() => job.State = FileOperationState.Cancelled);
            return;
        }
        catch (Exception ex)
        {
            // Védőháló: bármi előre nem látott (hálózati meghajtó megszakad,
            // váratlan jogosultsági hiba stb.) itt köt ki, HA valamiért nem a
            // fájlonkénti try/catch fogta el. Enélkül a job örökre „Running"
            // állapotban ragadna — a felhasználó egy soha be nem fejeződő
            // folyamatsávot látna, a Szünet/Megszakítás gombok pedig
            // használhatatlanok maradnának rajta.
            Log.Error(ex, "Fájlművelet váratlan hibája: {Kind} -> {Destination}", kind, destinationDirectory);
            errors.Add(ex.Message);
        }

        await OnUiAsync(() =>
        {
            job.BytesPerSecond = 0;

            if (errors.Count > 0)
            {
                job.ErrorSummary = string.Join(Environment.NewLine, errors);
                job.State = FileOperationState.CompletedWithErrors;
            }
            else
            {
                job.State = FileOperationState.Completed;
            }
        });
    }

    /// <summary>Ugyanazon köteten belüli áthelyezés — atomi, azonnali, nem igényel másolást.</summary>
    private static bool TryFastMove(string sourcePath, string destPath, bool isDirectory)
    {
        try
        {
            if (isDirectory)
            {
                Directory.Move(sourcePath, destPath);
            }
            else
            {
                File.Move(sourcePath, destPath, overwrite: false);
            }

            return true;
        }
        catch (IOException)
        {
            // Kötetek közötti áthelyezés, vagy már létező cél — a hívó a
            // lassabb, ütközést is kezelő másolás+forrás-törlés útra vált.
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Egy másolás/áthelyezés futás közbeni, fájlokon átívelő állapota.</summary>
    private sealed record CopyContext(
        FileOperationJob Job,
        List<string> Errors,
        SpeedTracker SpeedTracker,
        Func<FileConflictAction?> GetApplyToAll,
        Action<FileConflictAction?> SetApplyToAll,
        bool DeleteSourceAfterCopy);

    private async Task CopyDirectoryAsync(string sourceDir, string destDir, CopyContext context)
    {
        var job = context.Job;
        string[] entries;

        // Egy olvashatatlan almappa (jogosultság, közben törölt mappa) csak
        // saját magát hiúsítsa meg — korábban a kivétel a teljes műveletet
        // leállította a legfelső szintű catch-ben.
        try
        {
            Directory.CreateDirectory(destDir);
            entries = Directory.GetFileSystemEntries(sourceDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            context.Errors.Add($"{Path.GetFileName(sourceDir)}: {ex.Message}");
            Log.Warning(ex, "Mappa másolása sikertelen: {Source} -> {Dest}", sourceDir, destDir);
            return;
        }

        foreach (var entry in entries)
        {
            job.Cancellation.Token.ThrowIfCancellationRequested();
            await job.PauseGate.WaitIfPausedAsync().WaitAsync(job.Cancellation.Token).ConfigureAwait(false);

            var name = Path.GetFileName(entry);
            var childDest = Path.Combine(destDir, name);

            if (Directory.Exists(entry))
            {
                await CopyDirectoryAsync(entry, childDest, context).ConfigureAwait(false);
            }
            else
            {
                await CopySingleFileAsync(entry, childDest, context).ConfigureAwait(false);
            }
        }

        if (context.DeleteSourceAfterCopy)
        {
            // Csak ha már üres: ha bármelyik fájl kimaradt/hibára futott, a
            // forrásmappa (benne az a fájl) megmarad.
            try
            {
                Directory.Delete(sourceDir, recursive: false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private async Task CopySingleFileAsync(string sourcePath, string destPath, CopyContext context)
    {
        var job = context.Job;

        await OnUiAsync(() => job.CurrentFileName = Path.GetFileName(sourcePath));

        if (File.Exists(destPath))
        {
            var action = context.GetApplyToAll() ?? await ResolveConflictAsync(job, sourcePath, destPath).ConfigureAwait(false);

            if (job.PendingConflict is { ApplyToAll: true })
            {
                context.SetApplyToAll(action);
            }

            await OnUiAsync(() => job.PendingConflict = null);

            switch (action)
            {
                case FileConflictAction.Skip:
                    await OnUiAsync(() => job.FilesCompleted++);
                    return;

                case FileConflictAction.KeepBoth:
                    destPath = MakeUniqueDestination(destPath);
                    break;

                case FileConflictAction.Overwrite:
                default:
                    break;
            }
        }

        var copied = false;

        try
        {
            await CopyFileChunkedAsync(sourcePath, destPath, job, context.SpeedTracker).ConfigureAwait(false);
            copied = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            context.Errors.Add($"{Path.GetFileName(sourcePath)}: {ex.Message}");
            Log.Warning(ex, "Másolás sikertelen: {Source} -> {Dest}", sourcePath, destPath);

            // Csak akkor töröljük, ha MI hoztuk létre (a célfájl már írásra
            // meg volt nyitva) — egy a forrás megnyitásánál elbukó másolás
            // korábban a célhelyen lévő, érintetlen régi fájlt törölte.
            if (job.CurrentDestinationPath is { } partial)
            {
                job.CurrentDestinationPath = null;
                TryDeletePartialFile(partial);
            }
        }

        if (copied && context.DeleteSourceAfterCopy)
        {
            try
            {
                File.Delete(sourcePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                context.Errors.Add($"{Path.GetFileName(sourcePath)}: {string.Format(TranslationSource.Instance["FileOp_SourceDeleteFailed"], ex.Message)}");
            }
        }

        await OnUiAsync(() => job.FilesCompleted++);
    }

    private async Task<FileConflictAction> ResolveConflictAsync(FileOperationJob job, string sourcePath, string destPath)
    {
        var sourceInfo = new FileInfo(sourcePath);
        var destInfo = new FileInfo(destPath);

        var prompt = new FileConflictPrompt
        {
            SourcePath = sourcePath,
            DestinationPath = destPath,
            SourceSize = sourceInfo.Length,
            DestinationSize = destInfo.Length,
            SourceModifiedUtc = sourceInfo.LastWriteTimeUtc,
            DestinationModifiedUtc = destInfo.LastWriteTimeUtc,
        };

        await OnUiAsync(() => job.PendingConflict = prompt);

        return await prompt.Result.Task.WaitAsync(job.Cancellation.Token).ConfigureAwait(false);
    }

    private async Task CopyFileChunkedAsync(string sourcePath, string destPath, FileOperationJob job, SpeedTracker speedTracker)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

        var sourceInfo = new FileInfo(sourcePath);

        await using (var source = new FileStream(
            sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan | FileOptions.Asynchronous))
        {
            await using var destination = new FileStream(
                destPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, FileOptions.Asynchronous);

            // Csak a célfájl sikeres megnyitása UTÁN számít „félbemaradtnak" —
            // lásd CopySingleFileAsync hibaágát.
            job.CurrentDestinationPath = destPath;

            var buffer = new byte[BufferSize];
            int bytesRead;

            while ((bytesRead = await source.ReadAsync(buffer, job.Cancellation.Token).ConfigureAwait(false)) > 0)
            {
                job.Cancellation.Token.ThrowIfCancellationRequested();
                await job.PauseGate.WaitIfPausedAsync().WaitAsync(job.Cancellation.Token).ConfigureAwait(false);

                await destination.WriteAsync(buffer.AsMemory(0, bytesRead), job.Cancellation.Token).ConfigureAwait(false);

                var speed = speedTracker.RecordAndGetRate(bytesRead);

                await OnUiAsync(() =>
                {
                    job.BytesCompleted += bytesRead;

                    if (speed is { } bps)
                    {
                        job.BytesPerSecond = bps;
                    }
                });
            }
        }

        // Az Intéző megőrzi a módosítás dátumát és az attribútumokat — enélkül
        // minden másolat „most módosított" lenne, ami a dátum szerinti
        // rendezést és a biztonsági mentéseket is összezavarja.
        try
        {
            File.SetLastWriteTimeUtc(destPath, sourceInfo.LastWriteTimeUtc);
            File.SetAttributes(destPath, sourceInfo.Attributes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Debug(ex, "Időbélyeg/attribútum átvitele sikertelen: {Dest}", destPath);
        }

        // Sikeresen befejeződött — a fájl teljes, nem "félbemaradt" többé.
        // Ha a törlést itt nem nulláznánk, egy a KÖVETKEZŐ fájl másolása
        // közben érkező megszakítás tévedésből ezt a már kész fájlt törölné.
        job.CurrentDestinationPath = null;
    }

    private static void TryDeletePartialFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Megszakított/hibás másolat takarítása csak legjobb-erőfeszítés — ha
            // ez sem sikerül, a felhasználó a hibaüzenetben már látta az okot.
        }
    }

    private static string MakeUniqueDestination(string destPath)
    {
        var directory = Path.GetDirectoryName(destPath)!;
        var nameWithoutExt = Path.GetFileNameWithoutExtension(destPath);
        var extension = Path.GetExtension(destPath);

        for (var i = 2; ; i++)
        {
            var candidate = Path.Combine(directory, $"{nameWithoutExt} ({i}){extension}");

            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    private static (int Files, long Bytes) CountFilesAndBytes(IReadOnlyList<string> sourcePaths)
    {
        var files = 0;
        long bytes = 0;

        // Az IgnoreInaccessible nélkül egyetlen olvashatatlan almappa
        // UnauthorizedAccessException-t dobott, és a művelet még el sem
        // indult — a felhasználó csak annyit látott, hogy semmi sem történik.
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = 0,
        };

        foreach (var path in sourcePaths)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", options))
                    {
                        files++;
                        bytes += file.Length;
                    }
                }
                else if (File.Exists(path))
                {
                    files++;
                    bytes += new FileInfo(path).Length;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Közben eltűnt/elérhetetlenné vált — a méret csak becslés, folytatjuk.
            }
        }

        return (files, bytes);
    }

    private static Task OnUiAsync(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return dispatcher.InvokeAsync(action, DispatcherPriority.Background).Task;
    }

    /// <summary>
    /// Átviteli sebesség becslése rövid ablakban összegzett bájtokból — egy
    /// darab (chunk) mérete önmagában túl zajos lenne pillanatnyi sebességnek.
    /// </summary>
    private sealed class SpeedTracker
    {
        private DateTime _windowStart = DateTime.UtcNow;
        private long _windowBytes;

        public double? RecordAndGetRate(int bytes)
        {
            _windowBytes += bytes;
            var elapsed = DateTime.UtcNow - _windowStart;

            if (elapsed < SpeedSampleInterval)
            {
                return null;
            }

            var rate = _windowBytes / elapsed.TotalSeconds;
            _windowStart = DateTime.UtcNow;
            _windowBytes = 0;
            return rate;
        }
    }
}
