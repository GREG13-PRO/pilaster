using Pilaster.App.Services.FileOperations;

namespace Pilaster.Tests;

/// <summary>
/// A saját másoló/áthelyező motor adatvesztés elleni védelmei — mindegyik
/// eset korábban valódi fájlt törölt vagy végtelen mappafát épített.
/// </summary>
/// <remarks>
/// A tesztfolyamatban nincs <c>Application.Current</c>, ezért a motor
/// <c>OnUiAsync</c>-ja helyben futtat — a job-állapot így közvetlenül olvasható.
/// </remarks>
public sealed class FileOperationEngineTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pilaster-engine-" + Guid.NewGuid().ToString("N"));

    public FileOperationEngineTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            // Az írásvédett fájlok a rekurzív törlést is megakasztanák.
            foreach (var file in Directory.EnumerateFiles(_root, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 }))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Junction (mappahivatkozás) létrehozása — ehhez, a szimbolikus linkkel ellentétben, nem kell rendszergazdai jog.</summary>
    private static void CreateJunction(string link, string target)
    {
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
        })!;

        process.WaitForExit();
        Assert.True(Directory.Exists(link), "A junction nem jött létre.");
    }

    private string CreateFile(string relative, string content = "tartalom")
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>Megvárja, amíg az engine egyetlen jobja végállapotba ér — az ütközéseket a megadott döntéssel oldja fel.</summary>
    private static async Task<FileOperationJob> WaitForJobAsync(FileOperationEngine engine, FileConflictAction onConflict = FileConflictAction.Skip)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);

        while (DateTime.UtcNow < deadline)
        {
            if (engine.Jobs.FirstOrDefault() is { } job)
            {
                if (job.PendingConflict is { } conflict)
                {
                    conflict.ResolveCommand.Execute(onConflict);
                }

                if (!job.IsActive)
                {
                    return job;
                }
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("A fájlművelet nem fejeződött be időben.");
    }

    [Fact]
    public async Task MasolasUgyanabbaAMappabaMasolatotKeszitEsMegtartjaAzEredetit()
    {
        var file = CreateFile(@"mappa\a.txt", "eredeti");
        var engine = new FileOperationEngine();

        engine.StartCopy([file], Path.Combine(_root, "mappa"));
        var job = await WaitForJobAsync(engine, FileConflictAction.Overwrite);

        Assert.Equal(FileOperationState.Completed, job.State);
        Assert.Equal("eredeti", File.ReadAllText(file));
        Assert.Equal("eredeti", File.ReadAllText(Path.Combine(_root, "mappa", "a (2).txt")));
    }

    [Fact]
    public async Task AthelyezesUgyanabbaAMappabaNemToroliAFajlt()
    {
        var file = CreateFile(@"mappa\a.txt", "eredeti");
        var folder = Path.Combine(_root, "mappa", "almappa");
        CreateFile(@"mappa\almappa\b.txt", "b");
        var engine = new FileOperationEngine();

        engine.StartMove([file, folder], Path.Combine(_root, "mappa"));
        await WaitForJobAsync(engine, FileConflictAction.Overwrite);

        Assert.Equal("eredeti", File.ReadAllText(file));
        Assert.Equal("b", File.ReadAllText(Path.Combine(folder, "b.txt")));
    }

    [Fact]
    public async Task MappaNemMasolhatoOnmagaba()
    {
        var folder = Path.Combine(_root, "forras");
        CreateFile(@"forras\a.txt");
        CreateFile(@"forras\al\b.txt");
        var engine = new FileOperationEngine();

        engine.StartCopy([folder], Path.Combine(folder, "al"));
        var job = await WaitForJobAsync(engine);

        Assert.Equal(FileOperationState.CompletedWithErrors, job.State);
        Assert.False(Directory.Exists(Path.Combine(folder, "al", "forras")));
    }

    [Fact]
    public async Task MappaNemHelyezhetoAtOnmagabaEsMegmarad()
    {
        var folder = Path.Combine(_root, "forras");
        CreateFile(@"forras\a.txt", "a");
        CreateFile(@"forras\al\b.txt");
        var engine = new FileOperationEngine();

        engine.StartMove([folder], Path.Combine(folder, "al"));
        var job = await WaitForJobAsync(engine);

        Assert.Equal(FileOperationState.CompletedWithErrors, job.State);
        Assert.Equal("a", File.ReadAllText(Path.Combine(folder, "a.txt")));
    }

    [Fact]
    public async Task AthelyezesnelKihagyottUtkozesForrasaMegmarad()
    {
        // Egyesítés meglévő célmappába (a gyors Directory.Move itt elbukik,
        // és a másolás+törlés útra vált): a kihagyott fájl forrását
        // korábban a rekurzív mappatörlés vitte magával.
        var source = Path.Combine(_root, "forras", "adat");
        CreateFile(@"forras\adat\utkozo.txt", "forras-valtozat");
        CreateFile(@"forras\adat\uj.txt", "uj");
        CreateFile(@"cel\adat\utkozo.txt", "cel-valtozat");
        var engine = new FileOperationEngine();

        engine.StartMove([source], Path.Combine(_root, "cel"));
        await WaitForJobAsync(engine, FileConflictAction.Skip);

        Assert.Equal("forras-valtozat", File.ReadAllText(Path.Combine(source, "utkozo.txt")));
        Assert.Equal("cel-valtozat", File.ReadAllText(Path.Combine(_root, "cel", "adat", "utkozo.txt")));
        Assert.Equal("uj", File.ReadAllText(Path.Combine(_root, "cel", "adat", "uj.txt")));
        Assert.False(File.Exists(Path.Combine(source, "uj.txt")));
    }

    [Fact]
    public async Task ZaroltForrasNemToroliAMeglevoCelfajlt()
    {
        // Felülírás-döntés után a forrás megnyitása elbukik: a célhelyen lévő
        // régi fájlt korábban „félbemaradt másolatként" törölte a takarítás.
        var source = CreateFile(@"forras\a.txt", "uj");
        var dest = CreateFile(@"cel\a.txt", "regi");
        var engine = new FileOperationEngine();

        using (new FileStream(source, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            engine.StartCopy([source], Path.Combine(_root, "cel"));
            await WaitForJobAsync(engine, FileConflictAction.Overwrite);
        }

        Assert.Equal("regi", File.ReadAllText(dest));
    }

    [Fact]
    public async Task MasolasMegorziAModositasiDatumot()
    {
        var source = CreateFile(@"forras\a.txt");
        var stamp = new DateTime(2020, 5, 17, 10, 30, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(source, stamp);
        var engine = new FileOperationEngine();

        engine.StartCopy([source], Path.Combine(_root, "cel"));
        await WaitForJobAsync(engine);

        Assert.Equal(stamp, File.GetLastWriteTimeUtc(Path.Combine(_root, "cel", "a.txt")));
    }

    [Fact]
    public async Task IrasvedettCelfajlFelulirhato()
    {
        var source = CreateFile(@"forras\a.txt", "uj");
        var dest = CreateFile(@"cel\a.txt", "regi");
        File.SetAttributes(dest, FileAttributes.ReadOnly);
        var engine = new FileOperationEngine();

        engine.StartCopy([source], Path.Combine(_root, "cel"));
        var job = await WaitForJobAsync(engine, FileConflictAction.Overwrite);

        Assert.Equal(FileOperationState.Completed, job.State);
        Assert.Equal("uj", File.ReadAllText(dest));
    }

    [Fact]
    public async Task IrasvedettForrasAthelyezesnelIsTorlodik()
    {
        // Ütközés miatt a gyors File.Move elbukik, és a másolás+törlés útra vált.
        var source = CreateFile(@"forras\a.txt", "uj");
        File.SetAttributes(source, FileAttributes.ReadOnly);
        CreateFile(@"cel\a.txt", "regi");
        var engine = new FileOperationEngine();

        engine.StartMove([source], Path.Combine(_root, "cel"));
        var job = await WaitForJobAsync(engine, FileConflictAction.Overwrite);

        Assert.Equal(FileOperationState.Completed, job.State);
        Assert.False(File.Exists(source));
        Assert.Equal("uj", File.ReadAllText(Path.Combine(_root, "cel", "a.txt")));
    }

    [Fact]
    public async Task AthelyezesNemToroliAJunctionCeljanakFajljait()
    {
        // A meglévő célmappa miatt a gyors Directory.Move elbukik, és a motor
        // fájlonként másol+töröl. A junctionön át korábban a link CÉLJÁBÓL
        // is törölte a fájlokat.
        var kulso = CreateFile(@"kulso\fontos.txt", "fontos");
        var source = Path.Combine(_root, "forras", "adat");
        CreateFile(@"forras\adat\a.txt", "a");
        CreateJunction(Path.Combine(source, "link"), Path.GetDirectoryName(kulso)!);
        Directory.CreateDirectory(Path.Combine(_root, "cel", "adat"));
        var engine = new FileOperationEngine();

        engine.StartMove([source], Path.Combine(_root, "cel"));
        await WaitForJobAsync(engine);

        Assert.Equal("fontos", File.ReadAllText(kulso));
        Assert.Equal("fontos", File.ReadAllText(Path.Combine(_root, "cel", "adat", "link", "fontos.txt")));
        Assert.False(Directory.Exists(Path.Combine(source, "link")));
    }

    [Fact]
    public async Task KorbeMutatoJunctiontKihagyja()
    {
        var source = Path.Combine(_root, "forras");
        CreateFile(@"forras\a.txt", "a");
        CreateJunction(Path.Combine(source, "vissza"), source);
        var engine = new FileOperationEngine();

        engine.StartCopy([source], Path.Combine(_root, "cel"));
        var job = await WaitForJobAsync(engine);

        Assert.Equal(FileOperationState.CompletedWithErrors, job.State);
        Assert.Equal("a", File.ReadAllText(Path.Combine(_root, "cel", "forras", "a.txt")));
        Assert.False(Directory.Exists(Path.Combine(_root, "cel", "forras", "vissza", "vissza")));
    }

    [Theory]
    [InlineData(@"C:\a\b", @"C:\a", true)]
    [InlineData(@"C:\a", @"C:\a\", true)]
    [InlineData(@"C:\A\B\c", @"c:\a\b", true)]
    [InlineData(@"C:\ab", @"C:\a", false)]
    [InlineData(@"C:\a", @"C:\a\b", false)]
    public void IsSameOrInside(string candidate, string folder, bool expected) =>
        Assert.Equal(expected, FileOperationEngine.IsSameOrInside(candidate, folder));
}
