using System;
using System.Diagnostics;
using System.IO;

namespace Pilaster.Setup.Services;

/// <summary>
/// Egy futó folyamat nem tudja törölni a saját .exe-jét tartalmazó mappát —
/// eltávolításkor ezért egy leválasztott <c>cmd.exe</c> végzi a takarítást,
/// miután megvárta, hogy ez a folyamat ténylegesen kilépjen. Szabványos,
/// jól bevált minta (ugyanígy oldja meg a legtöbb önmagát eltávolító
/// telepítő/eltávolító).
/// </summary>
public static class SelfCleanup
{
    public static void ScheduleDirectoryRemoval(string directory)
    {
        var pid = Environment.ProcessId;

        // "directory" a <TelepítésiMappa>\Setup — a szülő maga a telepítési
        // mappa, aminek üresen (a Setup\ eltűnése után nincs benne más) nincs
        // értelme ottmaradnia. A sima (nem /s) rmdir csak ÜRES mappát töröl —
        // ha a felhasználó tett bele saját fájlt, csendben nem csinál semmit.
        var parentDirectory = Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? directory;

        // MÉRVE: egyetlen fix, ~1 mp-es várakozás (ping -n 2) UTÁN a
        // rmdir /s /q néhány fájlnál (pl. a .NET futtatókörnyezet és a WPF
        // saját leállási/GC-lépései miatt) még "hozzáférés megtagadva"
        // hibával ELSZÁLLT, néhány fájlt/mappát (a jelen esetben épp a
        // Setup\ maradékát) érintetlenül hagyva — a cmd rmdir-je az ELSŐ
        // hibás fájlnál megáll, nem próbálja tovább a többit. A javítás:
        // (1) ténylegesen a folyamat KILÉPÉSÉRE várunk (tasklist-tel
        // ellenőrizve), nem csak egy fix időre, és (2) a törlést magát is
        // néhányszor újrapróbáljuk, mielőtt feladnánk — mindkettő
        // korlátozott ciklusszámmal, hogy sosem fusson örökre.
        var scriptPath = Path.Combine(Path.GetTempPath(), $"pilaster-uninstall-cleanup-{Guid.NewGuid():N}.bat");

        var script = $"""
            @echo off
            set /a waited=0
            :waitexit
            tasklist /fi "PID eq {pid}" 2>nul | find "{pid}" >nul
            if not errorlevel 1 if %waited% lss 30 (
                set /a waited+=1
                ping -n 2 127.0.0.1 >nul
                goto waitexit
            )
            set /a tries=0
            :retrydelete
            rmdir /s /q "{directory}" 2>nul
            if exist "{directory}" if %tries% lss 15 (
                set /a tries+=1
                ping -n 2 127.0.0.1 >nul
                goto retrydelete
            )
            rmdir "{parentDirectory}" 2>nul
            del "%~f0"
            """;

        File.WriteAllText(scriptPath, script);

        Process.Start(new ProcessStartInfo("cmd.exe")
        {
            Arguments = $"/c \"{scriptPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        });
    }
}
