using System.Diagnostics;

namespace Pilaster.Tests;

/// <summary>
/// A2 (v1.0.2) Q1-táblázat: a <c>ShellMenuPreloadCoordinator</c> a Beállítások
/// → Jobbklikk menü alatt kikapcsolható előretöltés-mechanizmusa VALÓDI
/// <c>ShellMenuSession</c>/<c>StaWorker</c> hívásokkal, GUI/egér nélkül.
/// </summary>
/// <remarks>
/// Ugyanaz a mintázat, mint a <see cref="ShellFinalizerTests"/>: a kompilált
/// főalkalmazást indítja külön folyamatként (<c>PILASTER_SELFTEST_PRELOAD=1</c>),
/// mert a mögöttes shell-hívások natív összeomlása (ha lenne) csak kilépési
/// kóddal fogható meg, kivétellel nem. Az eredményfájl
/// (<c>%TEMP%\pilaster-preload-selftest.txt</c>) tartalmazza a
/// fájl/mappa/váltakozó/többszörös kijelölés 10-10 körét és az „5. szcenáriót"
/// (50 gyors kijelölés-váltás, közbe-közbe jobbklikk-szerű lekérdezéssel).
/// </remarks>
public class ShellMenuPreloadTests
{
    [Fact]
    public void AzElofeltoltesQ1TablazataZoldEredmennyelFutLe()
    {
        var exe = FindPilasterExecutable();

        if (exe is null)
        {
            Assert.Skip("A Pilaster.exe nincs lefordítva, az öntesztet nem lehet elindítani.");
            return;
        }

        var resultsPath = Path.Combine(Path.GetTempPath(), "pilaster-preload-selftest.txt");

        if (File.Exists(resultsPath))
        {
            File.Delete(resultsPath);
        }

        var start = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        start.Environment["PILASTER_SELFTEST_PRELOAD"] = "1";

        using var process = Process.Start(start);
        Assert.NotNull(process);

        Assert.True(
            process.WaitForExit(milliseconds: 180_000),
            "A shell-előretöltés önteszt nem fejeződött be 3 percen belül.");

        var results = File.Exists(resultsPath) ? File.ReadAllText(resultsPath) : "(nincs eredményfájl)";

        Assert.True(
            process.ExitCode == 0,
            $"Az előretöltés önteszt kilépési kódja {process.ExitCode} volt (0 helyett). Eredmények:{Environment.NewLine}{results}");

        Assert.Contains("OSSZESITVE: ZOLD", results);
    }

    private static string? FindPilasterExecutable()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            return null;
        }

        foreach (var configuration in new[] { "Release", "Debug" })
        {
            var candidate = Path.Combine(
                directory.FullName, "src", "Pilaster.App", "bin", configuration, "net10.0-windows", "Pilaster.exe");

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
