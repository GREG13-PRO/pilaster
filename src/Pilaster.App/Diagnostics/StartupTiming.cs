using System.Diagnostics;

namespace Pilaster.App.Diagnostics;

/// <summary>
/// Az indulás idejének naplózása (a folyamat indulásától az első megjelenített
/// képkockáig) — a teljesítmény-regressziók nyomon követéséhez.
/// </summary>
public static class StartupTiming
{
    public static void LogFirstFrame()
    {
        var elapsed = DateTime.Now - Process.GetCurrentProcess().StartTime;
        Serilog.Log.Information("Első képkocka {Milliseconds} ms-mal az indulás után", (long)elapsed.TotalMilliseconds);
    }
}
