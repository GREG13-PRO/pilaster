using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Serilog;

namespace Pilaster.App.Services;

/// <summary>
/// Egypéldányos futás (Beállítások → Általános → „Egypéldányos futás").
/// </summary>
/// <remarks>
/// <para>
/// Az ELSŐ példány egy nevesített mutexet birtokol, és egy nevesített csövön
/// (named pipe) hallgat. Egy második indítás a csövön átadja a parancssori
/// útvonalát (ha van), majd kilép — az első példány előtérbe hozza az ablakát,
/// és az útvonalat új fülön megnyitja. Így viselkedik az Intéző kiváltásakor
/// is: egy mappára dupla kattintás nem nyit új ablakot, hanem a meglévőben
/// jelenik meg.
/// </para>
/// <para>
/// A mutex és a cső neve az ADATMAPPÁBÓL képzett (lásd <see cref="AppDataLocator"/>):
/// egy hordozható példány így nem akad össze a telepítettel, és a
/// tesztkészlet elszigetelt adatmappájú öntesztjei sem a futó alkalmazással.
/// A „Local\" névtér munkamenetenkénti, így más bejelentkezett felhasználót
/// sem érint.
/// </para>
/// </remarks>
public sealed class SingleInstanceService : IDisposable
{
    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _stop = new();

    private SingleInstanceService(Mutex mutex, string pipeName)
    {
        _mutex = mutex;
        _pipeName = pipeName;
    }

    /// <summary>
    /// Megpróbál ELSŐ példánnyá válni. Ha már fut egy, átadja neki az
    /// útvonalat, és <c>null</c>-t ad — a hívónak ilyenkor ki kell lépnie.
    /// Ha az átadás nem sikerül (pl. a futó példány épp nem válaszol), a
    /// visszaadott <paramref name="handedOff"/> hamis, és a hívó nyugodtan
    /// elindulhat önállóan — egy második ablak jobb, mint egy néma kilépés.
    /// </summary>
    public static SingleInstanceService? TryAcquire(string? pathArgument, out bool handedOff)
    {
        handedOff = false;
        var key = BuildKey();
        var mutex = new Mutex(initiallyOwned: true, $@"Local\Pilaster.SingleInstance.{key}", out var createdNew);

        if (createdNew)
        {
            return new SingleInstanceService(mutex, $"Pilaster.SingleInstance.{key}");
        }

        mutex.Dispose();
        handedOff = TryHandOff($"Pilaster.SingleInstance.{key}", pathArgument);
        return null;
    }

    /// <summary>
    /// Hallgatózás a további indítások üzeneteire. A <paramref name="onActivate"/>
    /// háttérszálon hívódik; a paramétere az átadott útvonal, vagy <c>null</c>.
    /// </summary>
    public void StartListening(Action<string?> onActivate) => _ = ListenAsync(onActivate, _stop.Token);

    private async Task ListenAsync(Action<string?> onActivate, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                await server.WaitForConnectionAsync(token).ConfigureAwait(false);

                using var reader = new StreamReader(server, Encoding.UTF8);
                var message = await reader.ReadToEndAsync(token).ConfigureAwait(false);

                onActivate(string.IsNullOrWhiteSpace(message) ? null : message.Trim());
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException ex)
            {
                // Egy félbeszakadt kapcsolat csak azt az egy üzenetet viszi el —
                // a hallgatózás folytatódik.
                Log.Debug(ex, "Egypéldányos cső: sikertelen üzenetfogadás");
            }
        }
    }

    private static bool TryHandOff(string pipeName, string? pathArgument)
    {
        try
        {
            // Az ELŐTÉRBE hozás joga a most indított (a felhasználó által épp
            // elindított) folyamaté — enélkül a Windows előtérzár-szabálya miatt
            // a futó példány ablaka csak villogna a tálcán.
            AllowSetForegroundWindow(AsfwAny);

            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(timeout: 2000);

            var bytes = Encoding.UTF8.GetBytes(pathArgument ?? string.Empty);
            client.Write(bytes);
            client.Flush();
            return true;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "Egypéldányos átadás sikertelen — önálló indítás");
            return false;
        }
    }

    private static string BuildKey()
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(AppDataLocator.Directory.ToUpperInvariant()));
        return Convert.ToHexString(hash, 0, 8);
    }

    public void Dispose()
    {
        _stop.Cancel();

        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Nem ez a szál birtokolja (pl. a kilépés más szálon fut) — a
            // folyamat vége úgyis felszabadítja.
        }

        _mutex.Dispose();
        _stop.Dispose();
    }

    private const int AsfwAny = -1;

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);
}
