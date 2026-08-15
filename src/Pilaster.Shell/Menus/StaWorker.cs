using System.Collections.Concurrent;
using System.Threading;

namespace Pilaster.Shell.Menus;

/// <summary>
/// Egy dedikált STA szál, amin COM-munka sorosítva futtatható.
/// </summary>
/// <remarks>
/// <para>
/// A shell-bővítmények <c>IContextMenu</c> példányai apartment-kötöttek: azon
/// a szálon KELL használni őket, amelyiken létrejöttek. A menü lekérdezése és
/// a későbbi parancsvégrehajtás viszont két külön, időben távoli művelet —
/// ezért nem elég egy egyszeri „futtasd STA szálon" hívás, a szálnak életben
/// kell maradnia a munkamenet végéig.
/// </para>
/// <para>
/// Ez egyben az izoláció eszköze is: minden itt futó hívás
/// <see cref="Exception"/>-re le van védve, tehát egy hibás bővítmény kivétele
/// nem terjed át a UI szálra. Hard crash (hozzáférési hiba a natív kódban)
/// ellen csak külön FOLYAMAT védene — ezt a korlátot a
/// <c>docs/CONTEXT-MENU.md</c> nyíltan rögzíti.
/// </para>
/// </remarks>
internal sealed class StaWorker : IDisposable
{
    private static int _createdCount;

    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;

    public StaWorker(string name)
    {
        Interlocked.Increment(ref _createdCount);
        _thread = new Thread(Pump) { IsBackground = true, Name = name };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    /// <summary>
    /// Hány STA szál indult eddig a folyamatban.
    /// </summary>
    /// <remarks>
    /// Diagnosztika: ennek EGY-nek kell maradnia a program élete során, hacsak
    /// egy beragadt bővítmény miatt el nem kellett dobni a közös szálat (lásd
    /// <c>ShellMenuSession.RetireShared</c>). Ha menünként nő, a közös szál
    /// megosztása elromlott.
    /// </remarks>
    internal static int CreatedCount => Volatile.Read(ref _createdCount);

    private void Pump()
    {
        var comInitialized = NativeMenuInterop.CoInitializeEx(0, NativeMenuInterop.CoInitApartmentThreaded) >= 0;

        try
        {
            foreach (var work in _queue.GetConsumingEnumerable())
            {
                try
                {
                    work();
                }
                catch (Exception)
                {
                    // A munkaegység maga jelzi a hibát a saját
                    // TaskCompletionSource-án; itt csak azt biztosítjuk, hogy
                    // a szál ne álljon le egy hibás bővítmény miatt.
                }
            }
        }
        catch (ObjectDisposedException)
        {
            // Öv és nadrágtartó: a sort a Dispose SZÁNDÉKOSAN nem dobja el
            // (lásd ott), de ha egy jövőbeli változtatás mégis megtenné,
            // ebből ne legyen folyamatgyilkos kivétel.
        }
        finally
        {
            if (comInitialized)
            {
                NativeMenuInterop.CoUninitialize();
            }

            // A sort AZ A SZÁL szabadítja fel, amelyik olvassa — így nem
            // fordulhat elő, hogy közben még benne áll.
            _queue.Dispose();
        }
    }

    /// <summary>Egy művelet futtatása az STA szálon, eredménnyel.</summary>
    public Task<T> RunAsync<T>(Func<T> work)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            _queue.Add(() =>
            {
                try
                {
                    completion.TrySetResult(work());
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            });
        }
        catch (InvalidOperationException)
        {
            // A sor már lezárult (Dispose) — a hívó számára ez „nincs eredmény".
            completion.TrySetCanceled();
        }

        return completion.Task;
    }

    public void Dispose()
    {
        // CSAK lezárjuk a sort: ettől a szivattyú szál `GetConsumingEnumerable`
        // ciklusa magától kifogy, és a szál a `Pump` végén szabadítja fel a
        // gyűjteményt.
        //
        // A sor eldobása ITT végzetes hiba volt: a szivattyú szál ilyenkor még
        // BENNE ÁLL az enumerátorban, és `ObjectDisposedException`-t kap — ami
        // a `foreach`-en KÍVÜL keletkezik, tehát kiszökik a `Pump`-ból, és
        // kezeletlen kivételként megöli a folyamatot. MÉRVE: 100 menünyitásból
        // 3 futás halt így meg (0xE0434352), és a jelenség pont akkor jött elő,
        // amikor egy időtúllépés miatt eldobtuk a közös szálat (RetireShared).
        _queue.CompleteAdding();

        // Nem Join-olunk: a szál háttérszál, és egy beragadt bővítmény-hívás
        // itt a bezárást akaszthatná meg.
    }
}
