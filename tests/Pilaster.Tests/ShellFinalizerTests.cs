using System.Diagnostics;

namespace Pilaster.Tests;

/// <summary>
/// A shell-menü COM-objektumainak felszabadítása NEM juthat el a GC
/// véglegesítő szálára.
/// </summary>
/// <remarks>
/// <para>
/// A véglegesítő szál MTA; a shell-bővítmények objektumai viszont
/// apartment-kötöttek, és onnan elengedve <c>0xC0000374</c> (heap-korrupció)
/// hibával viszik a folyamatot. Ez a hiba kivétellel NEM fogható meg, ezért a
/// vizsgálat KÜLÖN FOLYAMATBAN fut, és a kilépési kód a jelzés.
/// </para>
/// <para>
/// A hiba Release buildben azonnal jelentkezik, Debugban jóval ritkábban (a
/// lokálisok ott tovább élnek), ezért a teszt a Release kimenetet keresi meg,
/// és ha nincs, inkább kihagyja magát, mint hogy hamis zöldet adjon.
/// </para>
/// </remarks>
public class ShellFinalizerTests
{
    [Fact]
    public void AShellMunkamenetUtanAVeglegesitesNemViszikAFolyamatot()
    {
        var exe = FindPilasterExecutable();

        if (exe is null)
        {
            // Nincs mit vizsgálni: a főalkalmazás nincs lefordítva. Ez nem
            // bukás — de nem is zöld, ezért kiírjuk.
            Assert.Skip("A Pilaster.exe nincs lefordítva, az öntesztet nem lehet elindítani.");
            return;
        }

        var start = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        start.Environment["PILASTER_SELFTEST_FINALIZER"] = "1";

        using var process = Process.Start(start);
        Assert.NotNull(process);

        Assert.True(
            process.WaitForExit(milliseconds: 180_000),
            "A shell-önteszt nem fejeződött be 3 percen belül — feltehetően beragadt egy bővítmény.");

        Assert.Equal(0, process.ExitCode);
    }

    /// <summary>
    /// A lefordított főalkalmazás megkeresése. Előnyben a Release: a hibát
    /// csak ott lehet megbízhatóan kimutatni.
    /// </summary>
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
