using System.IO.Enumeration;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Pilaster.Core.FileSystem;

namespace Pilaster.Providers.Local;

/// <summary>
/// A helyi (és SMB-n csatolt) fájlrendszer providere.
/// </summary>
/// <remarks>
/// <para>
/// Teljesítmény-kritikus osztály: ez fut le minden mappaváltáskor, és egy
/// 200 000 elemű mappánál ez dönti el, hogy a nézet azonnal megjelenik-e vagy
/// másodpercekig áll.
/// </para>
/// <para>
/// Két döntés hozza a sebességet:
/// </para>
/// <list type="number">
/// <item>
/// <see cref="FileSystemEnumerable{TResult}"/> a <c>DirectoryInfo</c> helyett.
/// A <c>DirectoryInfo.EnumerateFileSystemInfos</c> minden elemhez létrehoz egy
/// <c>FileSystemInfo</c> objektumot; ez a low-level API a Win32 találati
/// rekordból közvetlenül képez, így elemenként egy allokációt spórol.
/// </item>
/// <item>
/// A szinkron Win32-bejárás háttérszálon fut és egy <see cref="Channel{T}"/>-be
/// termel, amit a hívó aszinkron olvas. Így a UI-szál soha nem blokkol, és már
/// az első pár száz elem kirajzolható, miközben a bejárás még tart.
/// </item>
/// </list>
/// </remarks>
public sealed class LocalFileSystemProvider : IFileSystemProvider
{
    /// <summary>
    /// Ennyi elem várakozhat a csatornában, mielőtt a termelő visszafogja magát.
    /// Elég nagy, hogy a bejárás ne akadjon meg a UI renderelése miatt, de nem
    /// akkora, hogy egy hatalmas mappa listája bent ragadjon a memóriában.
    /// </summary>
    private const int ChannelCapacity = 4096;

    public string Scheme => "file";

    public bool CanHandle(string path) =>
        !string.IsNullOrWhiteSpace(path)
        && (Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal));

    public async IAsyncEnumerable<FileSystemItem> EnumerateAsync(
        string path,
        ListingOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateBounded<FileSystemItem>(
            new BoundedChannelOptions(ChannelCapacity)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait,
            });

        // A bejárás szinkron Win32-hívásokból áll, ezért külön szálra tesszük.
        var producer = Task.Run(
            () => ProduceAsync(path, options, channel.Writer, cancellationToken),
            cancellationToken);

        await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }

        // Ha a termelő hibára futott, itt csap ki — különben némán elnyelnénk.
        await producer.ConfigureAwait(false);
    }

    private static async Task ProduceAsync(
        string path,
        ListingOptions options,
        ChannelWriter<FileSystemItem> writer,
        CancellationToken cancellationToken)
    {
        Exception? failure = null;

        try
        {
            var enumerable = new FileSystemEnumerable<FileSystemItem>(
                path,
                static (ref FileSystemEntry entry) => ToItem(ref entry),
                new System.IO.EnumerationOptions
                {
                    RecurseSubdirectories = false,

                    // Nem a keretrendszerrel szűretünk, mert a rejtett/rendszer
                    // elemek megjelenítése futásidőben kapcsolható — a saját
                    // predikátumunk olvassa ki a beállítást.
                    AttributesToSkip = 0,

                    // Egy megtagadott alkönyvtár ne szakítsa félbe az egész listát.
                    IgnoreInaccessible = true,
                })
            {
                ShouldIncludePredicate = (ref FileSystemEntry entry) =>
                {
                    var attributes = entry.Attributes;

                    if (!options.IncludeHidden && attributes.HasFlag(FileAttributes.Hidden))
                    {
                        return false;
                    }

                    if (!options.IncludeSystem && attributes.HasFlag(FileAttributes.System))
                    {
                        return false;
                    }

                    return true;
                },
            };

            foreach (var item in enumerable)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await writer.WriteAsync(item, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            // A Complete mindig lefut, különben a fogyasztó örökre várna.
            writer.Complete(failure);
        }
    }

    internal static FileSystemItem ToItem(ref FileSystemEntry entry)
    {
        var name = entry.FileName.ToString();
        var isDirectory = entry.IsDirectory;
        var attributes = entry.Attributes;

        // A ReparsePoint attribútum NEM jelenti, hogy „link": a OneDrive MINDEN
        // (felhőbe szinkronizált) fájlja és mappája is reparse point, ahogy a
        // junction-ök és szimbolikus linkek is. Korábban ezek Link típust
        // kaptak — a mappák így nem voltak megnyithatók, a fájlok pedig
        // „Mappa" típusként jelentek meg. Mindkettő úgy viselkedjen, mint az
        // Intézőben: a mappa mappa, a fájl fájl.
        var kind = isDirectory
            ? FileSystemItemKind.Directory
            : FileSystemItemKind.File;

        return new FileSystemItem
        {
            FullPath = entry.ToFullPath(),
            Name = name,
            Kind = kind,
            Extension = isDirectory ? string.Empty : GetExtensionLowerInvariant(name),

            // Mappáknál a méret értelmetlen, amíg rekurzívan ki nem számoljuk;
            // -1 jelzi a nézetnek, hogy „—” jelenjen meg szám helyett.
            SizeBytes = isDirectory ? -1 : entry.Length,

            CreatedUtc = entry.CreationTimeUtc.UtcDateTime,
            ModifiedUtc = entry.LastWriteTimeUtc.UtcDateTime,
            AccessedUtc = entry.LastAccessTimeUtc.UtcDateTime,
            Attributes = attributes,
        };
    }

    private static string GetExtensionLowerInvariant(string name)
    {
        var dot = name.LastIndexOf('.');

        // A vezető pont nem kiterjesztés-elválasztó (".gitignore" → nincs kiterjesztés).
        return dot <= 0 || dot == name.Length - 1
            ? string.Empty
            : name[(dot + 1)..].ToLowerInvariant();
    }

    public ValueTask<FileSystemItem?> GetItemAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            if (Directory.Exists(path))
            {
                var info = new DirectoryInfo(path);
                var isRoot = info.Parent is null;

                return ValueTask.FromResult<FileSystemItem?>(new FileSystemItem
                {
                    FullPath = info.FullName,
                    Name = isRoot ? info.FullName : info.Name,
                    Kind = isRoot ? FileSystemItemKind.Drive : FileSystemItemKind.Directory,
                    CreatedUtc = info.CreationTimeUtc,
                    ModifiedUtc = info.LastWriteTimeUtc,
                    AccessedUtc = info.LastAccessTimeUtc,
                    Attributes = info.Attributes,
                });
            }

            if (File.Exists(path))
            {
                var info = new FileInfo(path);

                return ValueTask.FromResult<FileSystemItem?>(new FileSystemItem
                {
                    FullPath = info.FullName,
                    Name = info.Name,
                    Kind = FileSystemItemKind.File,
                    Extension = GetExtensionLowerInvariant(info.Name),
                    SizeBytes = info.Length,
                    CreatedUtc = info.CreationTimeUtc,
                    ModifiedUtc = info.LastWriteTimeUtc,
                    AccessedUtc = info.LastAccessTimeUtc,
                    Attributes = info.Attributes,
                });
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nem létező vagy elérhetetlen útvonal — a hívó null-t kap.
        }

        return ValueTask.FromResult<FileSystemItem?>(null);
    }

    public string? GetParentPath(string path)
    {
        try
        {
            return Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public Task<string> RenameAsync(string path, string newName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var trimmed = Path.TrimEndingDirectorySeparator(path);
        var directory = Path.GetDirectoryName(trimmed);

        if (string.IsNullOrEmpty(directory))
        {
            throw new IOException("A szülőmappa nem található.");
        }

        var destination = Path.Combine(directory, newName);

        if (Directory.Exists(path))
        {
            Directory.Move(path, destination);
        }
        else
        {
            File.Move(path, destination);
        }

        return Task.FromResult(destination);
    }

    public Task<long> GetFolderSizeAsync(string path, CancellationToken cancellationToken = default) =>
        Task.Run(() => ComputeFolderSize(path, cancellationToken), cancellationToken);

    /// <summary>
    /// A rekurzív bejárás ugyanazt a <see cref="FileSystemEnumerable{TResult}"/>
    /// alapú, allokáció-szegény mintát követi, mint a listázás — csak
    /// <c>RecurseSubdirectories = true</c> mellett, és csak a fájlméreteket
    /// összegzi. A szimbolikus link/junction mappákba NEM lép be (végtelen
    /// ciklus ellen), a többi reparse pointba (OneDrive felhőfájlok/-mappák)
    /// viszont igen — korábban MINDEN reparse point kimaradt, így egy
    /// OneDrive-mappa mérete mindig 0 B lett.
    /// </summary>
    private static long ComputeFolderSize(string path, CancellationToken cancellationToken)
    {
        var enumerable = new FileSystemEnumerable<long>(
            path,
            static (ref FileSystemEntry entry) => entry.IsDirectory ? 0 : entry.Length,
            new System.IO.EnumerationOptions
            {
                RecurseSubdirectories = true,
                AttributesToSkip = 0,
                IgnoreInaccessible = true,
            })
        {
            ShouldRecursePredicate = static (ref FileSystemEntry entry) =>
                !entry.Attributes.HasFlag(FileAttributes.ReparsePoint) || !IsLinkDirectory(entry.ToFullPath()),
        };

        var total = 0L;

        foreach (var size in enumerable)
        {
            cancellationToken.ThrowIfCancellationRequested();
            total += size;
        }

        return total;
    }

    /// <summary>
    /// Igaz, ha a mappa szimbolikus link vagy junction (van link-célja) —
    /// felhő-helyőrzőknél (OneDrive) és más reparse típusoknál hamis.
    /// </summary>
    private static bool IsLinkDirectory(string path)
    {
        try
        {
            return new DirectoryInfo(path).LinkTarget is not null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Kétes esetben inkább kihagyjuk — a pontatlan méret jobb, mint a végtelen ciklus.
            return true;
        }
    }
}
