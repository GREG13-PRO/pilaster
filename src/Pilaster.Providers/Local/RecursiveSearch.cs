using System.IO.Enumeration;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Pilaster.Core.FileSystem;

namespace Pilaster.Providers.Local;

/// <summary>
/// Név szerinti keresés egy mappában és az ÖSSZES almappájában (bármilyen
/// mélységben) — a „Keresés ebben a mappában" mező rekurzív része.
/// </summary>
/// <remarks>
/// A bejárás ugyanazzal a <see cref="FileSystemEnumerable{TResult}"/>
/// mechanizmussal fut, mint a sima mappalistázás, csak
/// <c>RecurseSubdirectories</c>-szal; a nem egyező elemekből nem készül
/// <see cref="FileSystemItem"/>, így nagy fáknál is olcsó. Az elérhetetlen
/// almappák (jogosultság) csendben kimaradnak.
/// </remarks>
public static class RecursiveSearch
{
    /// <summary>
    /// A találatok felső korlátja — egy „a" keresés a teljes C: meghajtón
    /// milliós listát adna, ami a felületet is megfojtaná.
    /// </summary>
    public const int MaxResults = 10_000;

    /// <summary>
    /// A <paramref name="root"/> alatti (a gyökérmappát is beleértve) minden
    /// olyan elem, amelynek nevében szerepel a <paramref name="query"/>
    /// (kis-nagybetű érzéketlenül). A <see cref="FileSystemItem.SearchLocation"/>
    /// a találat szülőmappája a gyökérhez képest.
    /// </summary>
    public static async IAsyncEnumerable<FileSystemItem> SearchAsync(
        string root,
        string query,
        ListingOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateBounded<FileSystemItem>(new BoundedChannelOptions(512)
        {
            SingleReader = true,
            SingleWriter = true,
        });

        var producer = Task.Run(() => Produce(root, query, options, channel.Writer, cancellationToken), cancellationToken);

        await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }

        await producer.ConfigureAwait(false);
    }

    private static async Task Produce(
        string root,
        string query,
        ListingOptions options,
        ChannelWriter<FileSystemItem> writer,
        CancellationToken cancellationToken)
    {
        Exception? failure = null;
        var rootFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));

        try
        {
            var enumerable = new FileSystemEnumerable<FileSystemItem>(
                rootFull,
                (ref FileSystemEntry entry) =>
                {
                    var item = LocalFileSystemProvider.ToItem(ref entry);
                    var parent = entry.Directory.ToString();
                    item.SearchLocation = parent.Length > rootFull.Length
                        ? parent[(rootFull.Length + 1)..]
                        : string.Empty;
                    return item;
                },
                new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = 0,
                })
            {
                // A megszakítást itt is figyelni kell, nem csak találatnál:
                // ritka keresőszónál a bejárás percekig futhat egyetlen
                // találat nélkül, és gépelés közben minden elavult keresés
                // tovább pörgette volna a lemezt a háttérben.
                ShouldIncludePredicate = (ref FileSystemEntry entry) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return IsVisible(entry.Attributes, options)
                        && entry.FileName.Contains(query, StringComparison.CurrentCultureIgnoreCase);
                },

                // Rejtett/rendszermappába csak akkor megyünk le, ha az elemei
                // egyébként is látszanának; junctionbe/symlinkbe soha — azok
                // körbe mutathatnak. A OneDrive/Dropbox mappái is reparse
                // pointok, de nem linkek (LinkTarget == null), azokba lemegyünk.
                ShouldRecursePredicate = (ref FileSystemEntry entry) =>
                    IsVisible(entry.Attributes, options)
                    && (!entry.Attributes.HasFlag(FileAttributes.ReparsePoint) || !IsLink(entry.ToFullPath())),
            };

            var count = 0;

            foreach (var item in enumerable)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await writer.WriteAsync(item, cancellationToken).ConfigureAwait(false);

                if (++count >= MaxResults)
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            writer.Complete(failure);
        }
    }

    private static bool IsVisible(FileAttributes attributes, ListingOptions options) => options.IsVisible(attributes);

    private static bool IsLink(string path)
    {
        try
        {
            return new DirectoryInfo(path).LinkTarget is not null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }
}
