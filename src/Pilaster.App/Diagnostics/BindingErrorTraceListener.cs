using System.Diagnostics;
using System.Windows;

namespace Pilaster.App.Diagnostics;

/// <summary>
/// WPF kötési (binding) hibák begyűjtése a hivatalos
/// <see cref="PresentationTraceSources"/> csatornán keresztül.
/// </summary>
/// <remarks>
/// <para>
/// A WPF alapból a <see cref="PresentationTraceSources.DataBindingSource"/>
/// forráson keresztül JELENTHETNÉ a kötési hibákat, de csak akkor, ha valaki
/// kifejezetten bekapcsolja a nyomkövetést — enélkül az üzenetek NÉMÁK.
/// Pontosan ez okozta, hogy a v1.0.1 2. körében a Beállítások „Panelek"
/// kategóriájának egyik kapcsolója (<c>DualPaneVertical</c>) egy nemlétező
/// tulajdonságra kötött, és senki nem vette észre.
/// </para>
/// <para>
/// MÉRVE (v1.0.1, 3. kör): ezen a futtatókörnyezeten (net10.0-windows) a WPF
/// belső kötés-motorja a gyakorlatban NEM hívja meg ezt a csatornát egy
/// tipikus „a tulajdonság nem található" hibánál — sem egyszintű, sem
/// többszintű útvonalnál, `SourceLevels.All` mellett és a lehető
/// legkorábban (az <see cref="App"/> KONSTRUKTORÁBAN) felszerelve sem —,
/// miközben egy kézzel kiváltott <c>TraceEvent</c>-hívás bizonyítottan eljut
/// idáig. A tényleges, megbízható felderítést ezért a
/// <see cref="BindingErrorScanner"/> végzi, ami a vizuális fát járja be és a
/// <see cref="System.Windows.Data.BindingExpression.Status"/> tulajdonságot
/// nézi — ez a hivatalos csatorna itt csak KIEGÉSZÍTI, a specifikáció
/// betűje szerint: ha egy jövőbeli .NET-verzió mégis megszólaltatja, ennek
/// az eredménye is belekerül az összesített listába (lásd
/// <see cref="BindingCheckRunner.RunAsync"/>).
/// </para>
/// </remarks>
public sealed class BindingErrorTraceListener : TraceListener
{
    private readonly Lock _gate = new();
    private readonly List<string> _errors = [];

    /// <summary>Az eddig összegyűjtött kötési hibaüzenetek, sorrendben.</summary>
    public IReadOnlyList<string> Errors
    {
        get
        {
            lock (_gate)
            {
                return _errors.ToArray();
            }
        }
    }

    public override void Write(string? message) => Append(message);

    public override void WriteLine(string? message) => Append(message);

    public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? message)
    {
        Append(message);
        base.TraceEvent(eventCache, source, eventType, id, message);
    }

    public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? format, params object?[]? args)
    {
        Append(format is null ? null : (args is null ? format : string.Format(format, args)));
        base.TraceEvent(eventCache, source, eventType, id, format, args);
    }

    public override void TraceData(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, object? data)
    {
        Append(data?.ToString());
        base.TraceData(eventCache, source, eventType, id, data);
    }

    public override void TraceData(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, params object?[]? data)
    {
        Append(data is null ? null : string.Join(" | ", data));
        base.TraceData(eventCache, source, eventType, id, data);
    }

    private void Append(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        lock (_gate)
        {
            _errors.Add(message);
        }

#if DEBUG
        // Csak Debug BUILDBEN — a CrashDiagnostics soronkénti, pufferátütős
        // írása lassú, Release-ben nem éri meg az árát egy diagnosztikai
        // mellékhatásért. A `force: true` a normál verbose-kapcsolótól
        // függetlenül is kiírja: egy kötési hiba mindig érdekes.
        CrashDiagnostics.Write($"WPF kötési hiba: {message}", force: true);
#endif
    }

    /// <summary>
    /// Bekapcsolja a kötéshiba-nyomkövetést, és felszereli ezt a listenert.
    /// </summary>
    public static BindingErrorTraceListener Install()
    {
        var listener = new BindingErrorTraceListener();

        // SORREND SZÁMÍT: előbb a listener, utána a szint. Egy korábbi
        // próbálkozás a `PresentationTraceSources.Refresh()`-t is meghívta a
        // végén — ez viszont az app.config-alapú (itt üres) listákra állítja
        // vissza a forrást, és NÉMÁN letörli a most felvett listenert, mintha
        // sosem lett volna felszerelve. Enélkül működik.
        PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;

        return listener;
    }

    /// <summary>Leszerelés — a mérőkör végén, hogy ne maradjon bent élesben.</summary>
    public void Uninstall() => PresentationTraceSources.DataBindingSource.Listeners.Remove(this);
}
