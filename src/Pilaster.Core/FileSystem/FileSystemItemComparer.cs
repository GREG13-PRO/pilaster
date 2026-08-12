using Pilaster.Core.Comparers;

namespace Pilaster.Core.FileSystem;

/// <summary>A lista rendezési szempontja.</summary>
public enum SortKey
{
    Name,
    Size,
    Type,
    Modified,
    Created,
}

/// <summary>
/// Fájllista-rendező. A mappák mindig a fájlok elé kerülnek — ez a Windows és
/// a macOS közös konvenciója, és rendezési iránytól függetlenül érvényes.
/// </summary>
public sealed class FileSystemItemComparer(SortKey key, bool descending)
    : IComparer<FileSystemItem>
{
    public int Compare(FileSystemItem? x, FileSystemItem? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        // A mappa/fájl csoportosítás megelőzi a rendezési szempontot, és a
        // csökkenő irány sem fordítja meg — különben a mappák a lista aljára
        // kerülnének, ami minden fájlkezelőben szokatlan lenne.
        var xGroup = x.IsNavigable ? 0 : 1;
        var yGroup = y.IsNavigable ? 0 : 1;

        if (xGroup != yGroup)
        {
            return xGroup - yGroup;
        }

        var result = key switch
        {
            SortKey.Size => x.SizeBytes.CompareTo(y.SizeBytes),
            SortKey.Type => string.Compare(x.Extension, y.Extension, StringComparison.OrdinalIgnoreCase),
            SortKey.Modified => x.ModifiedUtc.CompareTo(y.ModifiedUtc),
            SortKey.Created => x.CreatedUtc.CompareTo(y.CreatedUtc),
            _ => 0,
        };

        // Azonos érték esetén a név dönt, hogy a sorrend determinisztikus legyen.
        if (result == 0)
        {
            result = NaturalStringComparer.Instance.Compare(x.Name, y.Name);
        }

        return descending ? -result : result;
    }
}
