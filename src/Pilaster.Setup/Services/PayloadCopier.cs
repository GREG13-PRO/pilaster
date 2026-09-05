using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Pilaster.Setup.Services;

/// <summary>Egy folyamatban lévő másolás pillanatnyi állapota, a Progress oldal ezt jeleníti meg.</summary>
public sealed record CopyProgress(string CurrentFileName, long BytesCopied, long TotalBytes, double? BytesPerSecond)
{
    public double Fraction => TotalBytes == 0 ? 1.0 : Math.Clamp((double)BytesCopied / TotalBytes, 0, 1);
}

/// <summary>
/// A teljes payload-mappa (a Setup.exe mellé csomagolt, publikált Pilaster.App
/// kimenet) átmásolása a célkönyvtárba, folyamatjelzéssel és megszakítással.
///
/// Ugyanaz a mintázat (1 MB-os pufferelt aszinkron I/O, sebesség-mérő ablak),
/// mint a Pilaster.App saját FileOperationEngine.CopyFileChunkedAsync-jáé
/// (src/Pilaster.App/Services/FileOperations/FileOperationEngine.cs) —
/// leegyszerűsítve: nincs szünet/folytatás (a telepítőnek csak megszakítás
/// kell), és Dispatcher-pumpálás helyett egyszerű IProgress&lt;T&gt;.
/// </summary>
public static class PayloadCopier
{
    private const int BufferSize = 1024 * 1024;
    private static readonly TimeSpan SpeedSampleInterval = TimeSpan.FromMilliseconds(400);

    public static async Task CopyDirectoryAsync(
        string sourceDir,
        string destDir,
        IProgress<CopyProgress> progress,
        CancellationToken cancellationToken,
        string? excludeSubdirectoryName = null)
    {
        var files = new List<string>(Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories));

        if (excludeSubdirectoryName is not null)
        {
            var excludedPrefix = Path.Combine(sourceDir, excludeSubdirectoryName) + Path.DirectorySeparatorChar;
            files.RemoveAll(f => f.StartsWith(excludedPrefix, StringComparison.OrdinalIgnoreCase));
        }

        long totalBytes = 0;

        foreach (var file in files)
        {
            totalBytes += new FileInfo(file).Length;
        }

        long bytesCopied = 0;
        var windowStart = DateTime.UtcNow;
        long windowBytes = 0;

        try
        {
            foreach (var sourcePath in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var relativePath = Path.GetRelativePath(sourceDir, sourcePath);
                var destPath = Path.Combine(destDir, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

                await using var source = new FileStream(
                    sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize,
                    FileOptions.SequentialScan | FileOptions.Asynchronous);
                await using var destination = new FileStream(
                    destPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, FileOptions.Asynchronous);

                var buffer = new byte[BufferSize];
                int bytesRead;

                while ((bytesRead = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    // SZÁNDÉKOSAN NINCS ConfigureAwait(false): a hívó InstallOrchestrator ez
                    // után COM shell-objektumokat hoz létre (ShortcutBuilder — IShellLinkW/
                    // IPropertyStore), amik az STA UI-szálhoz kötöttek. Egy .ConfigureAwait(false)
                    // itt a folytatást szálkészletbeli (MTA/inicializálatlan) szálra vinné át,
                    // ahol a COM-hívás RPC_E_WRONG_THREAD-del elszállna — ez okozta a
                    // "Ismeretlen hiba történt" bukást a helyszíni teszten.
                    await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);

                    bytesCopied += bytesRead;
                    windowBytes += bytesRead;

                    var elapsed = DateTime.UtcNow - windowStart;
                    double? speed = null;

                    if (elapsed >= SpeedSampleInterval)
                    {
                        speed = windowBytes / elapsed.TotalSeconds;
                        windowStart = DateTime.UtcNow;
                        windowBytes = 0;
                    }

                    progress.Report(new CopyProgress(relativePath, bytesCopied, totalBytes, speed));
                }
            }
        }
        catch
        {
            // Megszakítás VAGY hiba esetén egyaránt: a félbemaradt célkönyvtár
            // nem maradhat ott (se hiányos telepítésként, se a következő
            // próbálkozást zavaró régi fájlokként) — az eredeti kivétel
            // változatlanul tovább megy.
            TryDeleteDirectory(destDir);
            throw;
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Legjobb-erőfeszítés takarítás — ha ez sem sikerül, a hívó már látta az eredeti hibát.
        }
    }
}
