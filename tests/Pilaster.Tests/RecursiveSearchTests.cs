using Pilaster.Core.FileSystem;
using Pilaster.Providers.Local;

namespace Pilaster.Tests;

/// <summary>A keresőmező rekurzív része: a mappa ÉS minden almappája, bármilyen mélységben.</summary>
public class RecursiveSearchTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pilaster-search-" + Guid.NewGuid().ToString("N"));

    public RecursiveSearchTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "a", "b", "c"));
        File.WriteAllText(Path.Combine(_root, "jelentes.txt"), "x");
        File.WriteAllText(Path.Combine(_root, "a", "Jelentes-2.txt"), "x");
        File.WriteAllText(Path.Combine(_root, "a", "b", "c", "mely-jelentes.md"), "x");
        File.WriteAllText(Path.Combine(_root, "a", "b", "mas.txt"), "x");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task MindenMelysegbenTalal_KisNagybetuErzeketlenul()
    {
        var results = new List<FileSystemItem>();

        await foreach (var item in RecursiveSearch.SearchAsync(_root, "jelentes", new ListingOptions(false, false), TestContext.Current.CancellationToken))
        {
            results.Add(item);
        }

        Assert.Equal(
            ["Jelentes-2.txt", "jelentes.txt", "mely-jelentes.md"],
            results.Select(r => r.Name).Order(StringComparer.OrdinalIgnoreCase));

        var deep = results.Single(r => r.Name == "mely-jelentes.md");
        Assert.Equal(Path.Combine("a", "b", "c"), deep.SearchLocation);
        Assert.Null(results.Single(r => r.Name == "jelentes.txt").SearchLocationDisplay);
    }

    [Fact]
    public async Task MegszakithatoKeresés()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in RecursiveSearch.SearchAsync(_root, "a", new ListingOptions(false, false), cancellation.Token))
            {
            }
        });
    }
}
