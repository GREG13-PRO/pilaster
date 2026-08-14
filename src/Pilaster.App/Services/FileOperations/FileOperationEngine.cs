using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
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

    public void StartCopy(IReadOnlyList<string> sourcePaths, string destinationDirectory) =>
        _ = RunAsync(FileOperationKind.Copy, sourcePaths, destinationDirectory);

    public void StartMove(IReadOnlyList<string> sourcePaths, string destinationDirectory) =>
        _ = RunAsync(FileOperationKind.Move, sourcePaths, destinationDirectory);

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
        var (totalFiles, totalBytes) = await Task.Run(() => CountFilesAndBytes(sourcePaths));

        var job = new FileOperationJob
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            DestinationDirectory = destinationDirectory,
            TotalFiles = totalFiles,
        };
        job.TotalBytes = totalBytes;

        await OnUiAsync(() => Jobs.Insert(0, job));

        var errors = new List<string>();
        FileConflictAction? applyToAllAction = null;
        var speedTracker = new SpeedTracker();

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

                if (kind == FileOperationKind.Move)
                {
                    // A méretet/fájlszámot a mozgatás ELŐTT kell megállapítani —
                    // egy sikeres File.Move/Directory.Move után a forrás már
                    // nem létezik azon az útvonalon, egy utólagos FileInfo
                    // lekérdezés FileNotFoundException-t dobna.
                    var (fastMoveFiles, fastMoveBytes) = isDirectory ? CountFilesAndBytes([sourcePath]) : (1, new FileInfo(sourcePath).Length);

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

                if (isDirectory)
                {
                    await CopyDirectoryAsync(sourcePath, destPath, job, errors, speedTracker, () => applyToAllAction,
                        v => applyToAllAction = v).ConfigureAwait(false);
                }
                else
                {
                    await CopySingleFileAsync(sourcePath, destPath, job, errors, speedTracker, () => applyToAllAction,
                        v => applyToAllAction = v).ConfigureAwait(false);
                }

                if (kind == FileOperationKind.Move)
                {
                    TryDeleteSourceAfterMove(sourcePath, isDirectory, errors);
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

    private static void TryDeleteSourceAfterMove(string sourcePath, bool isDirectory, List<string> errors)
    {
        try
        {
            if (isDirectory)
            {
                Directory.Delete(sourcePath, recursive: true);
            }
            else
            {
                File.Delete(sourcePath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            errors.Add($"{Path.GetFileName(sourcePath)}: a forrás törlése sikertelen áthelyezés után ({ex.Message})");
        }
    }

    private async Task CopyDirectoryAsync(
        string sourceDir,
        string destDir,
        FileOperationJob job,
        List<string> errors,
        SpeedTracker speedTracker,
        Func<FileConflictAction?> getApplyToAll,
        Action<FileConflictAction?> setApplyToAll)
    {
        Directory.CreateDirectory(destDir);

        foreach (var entry in Directory.EnumerateFileSystemEntries(sourceDir))
        {
            job.Cancellation.Token.ThrowIfCancellationRequested();
            await job.PauseGate.WaitIfPausedAsync().WaitAsync(job.Cancellation.Token).ConfigureAwait(false);

            var name = Path.GetFileName(entry);
            var childDest = Path.Combine(destDir, name);

            if (Directory.Exists(entry))
            {
                await CopyDirectoryAsync(entry, childDest, job, errors, speedTracker, getApplyToAll, setApplyToAll).ConfigureAwait(false);
            }
            else
            {
                await CopySingleFileAsync(entry, childDest, job, errors, speedTracker, getApplyToAll, setApplyToAll).ConfigureAwait(false);
            }
        }
    }

    private async Task CopySingleFileAsync(
        string sourcePath,
        string destPath,
        FileOperationJob job,
        List<string> errors,
        SpeedTracker speedTracker,
        Func<FileConflictAction?> getApplyToAll,
        Action<FileConflictAction?> setApplyToAll)
    {
        await OnUiAsync(() => job.CurrentFileName = Path.GetFileName(sourcePath));

        if (File.Exists(destPath))
        {
            var action = getApplyToAll() ?? await ResolveConflictAsync(job, sourcePath, destPath).ConfigureAwait(false);

            if (job.PendingConflict is { ApplyToAll: true })
            {
                setApplyToAll(action);
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

        try
        {
            await CopyFileChunkedAsync(sourcePath, destPath, job, speedTracker).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            errors.Add($"{Path.GetFileName(sourcePath)}: {ex.Message}");
            Log.Warning(ex, "Másolás sikertelen: {Source} -> {Dest}", sourcePath, destPath);
            TryDeletePartialFile(destPath);
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
        job.CurrentDestinationPath = destPath;

        await using var source = new FileStream(
            sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan | FileOptions.Asynchronous);
        await using var destination = new FileStream(
            destPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, FileOptions.Asynchronous);

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

        foreach (var path in sourcePaths)
        {
            if (Directory.Exists(path))
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    files++;

                    try
                    {
                        bytes += new FileInfo(file).Length;
                    }
                    catch (IOException)
                    {
                        // Fájl eltűnt a leltározás közben — a méret csak becslés, folytatjuk.
                    }
                }
            }
            else if (File.Exists(path))
            {
                files++;
                bytes += new FileInfo(path).Length;
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
