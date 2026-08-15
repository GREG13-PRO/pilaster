using System.IO;
using System.Text;

namespace Pilaster.App.Diagnostics;

/// <summary>
/// Összeomlást TÚLÉLŐ napló és globális kivétel-horgok.
/// </summary>
/// <remarks>
/// <para>
/// A rendes Serilog-napló a legtöbb esetben elég, de egy natív összeomlásnál
/// (heap-korrupció) vagy egy háttérszálról elszabaduló kivételnél a folyamat
/// azonnal meghal, és az utolsó sorok elveszhetnek. Ez az osztály ezért
/// SORONKÉNT ír, és minden sor után átüti az operációs rendszer pufferét is —
/// lassú, ezért csak akkor kapcsol be, ha kifejezetten kérik.
/// </para>
/// <para>
/// A shell-menü hibakeresése három mérést veszített el amiatt, hogy a napló
/// nem élte túl a folyamatot. Ez a mechanizmus ezt előzi meg.
/// </para>
/// </remarks>
public static class CrashDiagnostics
{
    private static readonly Lock Gate = new();
    private static FileStream? _stream;

    /// <summary>A napló helye — a felhasználó ideiglenes mappájában.</summary>
    public static string FilePath { get; } =
        Path.Combine(Path.GetTempPath(), "pilaster-shell-diag.log");

    /// <summary>Bekapcsolva ír csak; egyébként minden hívás azonnal visszatér.</summary>
    public static bool IsEnabled { get; private set; }

    /// <summary>
    /// Bekapcsolás és a globális kivétel-horgok felrakása.
    /// </summary>
    /// <remarks>
    /// A horgok akkor is felkerülnek, ha a részletes napló ki van kapcsolva:
    /// a kezeletlen kivétel megnevezése olyan olcsó, hogy mindig megéri.
    /// </remarks>
    public static void Install(bool verbose)
    {
        IsEnabled = verbose;

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var text = e.ExceptionObject is Exception exception
                ? Describe(exception)
                : e.ExceptionObject?.ToString() ?? "(ismeretlen)";

            Write($"KEZELETLEN KIVÉTEL (a folyamat leáll={e.IsTerminating}): {text}", force: true);
            Serilog.Log.Fatal("Kezeletlen kivétel egy háttérszálon: {Details}", text);
            Serilog.Log.CloseAndFlush();
        };

        // A „fire-and-forget" taskokból elszabaduló kivételek. Ezek magukban
        // nem ölik meg a folyamatot, de a hibát elrejtenék.
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Write($"MEGFIGYELETLEN TASK-KIVÉTEL: {Describe(e.Exception)}", force: true);
            Serilog.Log.Error(e.Exception, "Megfigyeletlen task-kivétel");
            e.SetObserved();
        };
    }

    /// <summary>Egy sor a naplóba, azonnali lemezre írással.</summary>
    public static void Write(string line, bool force = false)
    {
        if (!IsEnabled && !force)
        {
            return;
        }

        try
        {
            lock (Gate)
            {
                _stream ??= new FileStream(
                    FilePath,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.ReadWrite,
                    bufferSize: 1,
                    FileOptions.WriteThrough);

                var text = $"{DateTime.Now:HH:mm:ss.fff} [{Environment.CurrentManagedThreadId,3}] {line}{Environment.NewLine}";
                var bytes = Encoding.UTF8.GetBytes(text);

                _stream.Write(bytes, 0, bytes.Length);

                // true = az operációs rendszer pufferét is átüti, nem csak a
                // sajátunkat. E nélkül egy natív összeomlásnál elveszne a sor.
                _stream.Flush(flushToDisk: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            // A diagnosztika hibája sosem akadályozhatja a programot.
        }
    }

    private static string Describe(Exception exception) =>
        $"{exception.GetType().FullName}: {exception.Message}{Environment.NewLine}{exception}";
}
