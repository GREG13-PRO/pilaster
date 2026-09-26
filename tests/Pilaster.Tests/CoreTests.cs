using System.Globalization;
using Pilaster.Core.FileSystem;
using Pilaster.Core.Formatting;
using Pilaster.Core.Navigation;

// A System.Globalization is definiál SortKey típust — az álnév egyértelműsíti,
// hogy a rendezési szempontunkról van szó.
using SortKey = Pilaster.Core.FileSystem.SortKey;

namespace Pilaster.Tests;

public class ByteSizeTests
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1048576, "1 MB")]
    [InlineData(1073741824, "1 GB")]
    public void Format_AzExplorerKonvenciojaSzerint(long bytes, string expected)
    {
        Assert.Equal(expected, ByteSize.Format(bytes, Culture));
    }

    /// <summary>
    /// A mappák mérete -1, amíg nincs kiszámolva; ilyenkor gondolatjel jár,
    /// nem „-1 B".
    /// </summary>
    [Fact]
    public void Format_IsmeretlenMeretGondolatjel()
    {
        Assert.Equal("—", ByteSize.Format(-1, Culture));
    }

    /// <summary>
    /// A tizedesjegyek száma a nagyságrenddel csökken, hogy a lista ne
    /// teljen meg jelentés nélküli számjegyekkel.
    /// </summary>
    [Fact]
    public void Format_NagyobbErtekKevesebbTizedes()
    {
        Assert.Equal("150 MB", ByteSize.Format(157_286_400, Culture));
    }
}

public class NavigationHistoryTests
{
    [Fact]
    public void Kezdetben_NincsHovaLepni()
    {
        var history = new NavigationHistory();

        Assert.Null(history.Current);
        Assert.False(history.CanGoBack);
        Assert.False(history.CanGoForward);
    }

    [Fact]
    public void VisszaEsElore_BongeszoSzerint()
    {
        var history = new NavigationHistory();
        history.Navigate(@"C:\a");
        history.Navigate(@"C:\b");

        Assert.Equal(@"C:\a", history.GoBack());
        Assert.True(history.CanGoForward);
        Assert.Equal(@"C:\b", history.GoForward());
    }

    /// <summary>
    /// A böngésző-szemantika lényege: visszalépés után új helyre navigálva az
    /// „előre" ág eldobódik.
    /// </summary>
    [Fact]
    public void UjNavigacioEldobjaAzEloreAgat()
    {
        var history = new NavigationHistory();
        history.Navigate(@"C:\a");
        history.Navigate(@"C:\b");
        history.GoBack();

        Assert.True(history.CanGoForward);

        history.Navigate(@"C:\c");

        Assert.False(history.CanGoForward);
        Assert.Equal(@"C:\c", history.Current);
    }

    [Fact]
    public void UgyanarraNavigalasNemDuplikal()
    {
        var history = new NavigationHistory();
        history.Navigate(@"C:\a");
        history.Navigate(@"C:\A");

        Assert.False(history.CanGoBack);
    }
}

public class FileSystemItemComparerTests
{
    private static FileSystemItem Folder(string name) =>
        new() { FullPath = @"C:\" + name, Name = name, Kind = FileSystemItemKind.Directory };

    private static FileSystemItem File(string name, long size = 0) =>
        new() { FullPath = @"C:\" + name, Name = name, Kind = FileSystemItemKind.File, SizeBytes = size };

    /// <summary>
    /// A mappák a fájlok elé kerülnek, és ezt a csökkenő irány sem fordítja meg.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MappakMindigElol(bool descending)
    {
        var items = new List<FileSystemItem> { File("a.txt"), Folder("z") };
        items.Sort(new FileSystemItemComparer(SortKey.Name, descending));

        Assert.Equal("z", items[0].Name);
    }

    /// <summary>
    /// Természetes rendezés: a „kép9" a „kép10" ELÉ kerül, mert a számjegy-
    /// sorozatokat számként hasonlítjuk össze, ahogy az Explorer is.
    /// </summary>
    [Fact]
    public void TermeszetesSorrendSzamokkal()
    {
        var items = new List<FileSystemItem> { File("kep10.jpg"), File("kep9.jpg"), File("kep1.jpg") };
        items.Sort(new FileSystemItemComparer(SortKey.Name, descending: false));

        Assert.Equal(["kep1.jpg", "kep9.jpg", "kep10.jpg"], items.Select(i => i.Name));
    }

    [Fact]
    public void MeretSzerintiRendezes()
    {
        var items = new List<FileSystemItem> { File("nagy", 900), File("kicsi", 10) };
        items.Sort(new FileSystemItemComparer(SortKey.Size, descending: false));

        Assert.Equal("kicsi", items[0].Name);
    }

    /// <summary>
    /// Mappáknál a <see cref="FileSystemItem.SizeBytes"/> mindig -1 (amíg ki
    /// nem számoltuk), ezért a méret szerinti rendezésnek a háttérben számolt
    /// <see cref="FileSystemItem.ComputedFolderSize"/>-ot kell használnia —
    /// enélkül minden mappa holtversenyben lenne, és a rendezés névre esne
    /// vissza a valódi méret helyett.
    /// </summary>
    [Fact]
    public void MappaMeretSzerintiRendezesSzamoltMeretetHasznal()
    {
        var kicsi = Folder("kicsi");
        kicsi.ComputedFolderSize = 10;

        var nagy = Folder("nagy");
        nagy.ComputedFolderSize = 900;

        var items = new List<FileSystemItem> { nagy, kicsi };
        items.Sort(new FileSystemItemComparer(SortKey.Size, descending: false));

        Assert.Equal(["kicsi", "nagy"], items.Select(i => i.Name));
    }

    /// <summary>A „Mappák elöl" kikapcsolva: mappák és fájlok vegyesen, csak név szerint.</summary>
    [Fact]
    public void MappakElolKikapcsolva_VegyesRendezes()
    {
        var items = new List<FileSystemItem> { Folder("b"), File("a.txt"), File("c.txt") };
        items.Sort(new FileSystemItemComparer(SortKey.Name, descending: false, foldersFirst: false));

        Assert.Equal(["a.txt", "b", "c.txt"], items.Select(i => i.Name));
    }

    [Fact]
    public void HozzaferesSzerintiRendezes()
    {
        var items = new List<FileSystemItem>
        {
            new() { FullPath = @"C:\uj", Name = "uj", Kind = FileSystemItemKind.File, AccessedUtc = new DateTime(2026, 1, 2) },
            new() { FullPath = @"C:\regi", Name = "regi", Kind = FileSystemItemKind.File, AccessedUtc = new DateTime(2025, 1, 2) },
        };
        items.Sort(new FileSystemItemComparer(SortKey.Accessed, descending: false));

        Assert.Equal(["regi", "uj"], items.Select(i => i.Name));
    }
}

/// <summary>Az Intéző láthatósági szabálya — lásd <see cref="ListingOptions.IsVisible"/>.</summary>
public class ListingOptionsTests
{
    [Fact]
    public void CsakRendszerAttributumuMappaMindigLatszik()
    {
        // Pl. a Nextcloud/OneDrive szinkronizált mappája: System, de nem Hidden.
        var attributes = System.IO.FileAttributes.Directory | System.IO.FileAttributes.System | System.IO.FileAttributes.ReparsePoint;

        Assert.True(new ListingOptions(IncludeHidden: false, IncludeSystem: false).IsVisible(attributes));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    public void RejtettElemARejtettKapcsolotKoveti(bool includeHidden, bool includeSystem, bool expected) =>
        Assert.Equal(expected, new ListingOptions(includeHidden, includeSystem).IsVisible(System.IO.FileAttributes.Hidden));

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    public void VedettRendszerElemCsakMindketKapcsolovalLatszik(bool includeHidden, bool includeSystem, bool expected) =>
        Assert.Equal(expected, new ListingOptions(includeHidden, includeSystem).IsVisible(System.IO.FileAttributes.Hidden | System.IO.FileAttributes.System));
}
