using System.Runtime.InteropServices;

namespace Pilaster.Shell.Integration;

/// <summary>
/// Jelez, amikor a rendszerben egy másik ablak kerül előtérbe — a
/// fájlablak-panel (Megnyitás/Mentés mellé tapadó Pilaster-panel) ebből tudja
/// meg, mikor nyílt meg vagy került fókuszba egy fájlpárbeszéd.
/// </summary>
/// <remarks>
/// Egy folyamaton KÍVÜLI (<c>WINEVENT_OUTOFCONTEXT</c>) WinEvent-horog: a
/// képernyőolvasók is ezt az akadálymentesítési felületet használják. NEM tölt
/// be kódot más folyamatokba, és nem figyeli a billentyűzetet; a Windows csak
/// annyit küld, hogy melyik ablak lett az előtérben. A visszahívás a telepítő
/// szál üzenetsorán fut, ezért a WPF UI-szálról kell indítani.
/// </remarks>
public sealed class ForegroundWindowWatcher : IDisposable
{
    private const uint EventSystemForeground = 0x0003;
    private const uint WinEventOutOfContext = 0x0000;

    private nint _hook;
    private WinEventProc? _callback;

    /// <summary>Az új előtérablak natív azonosítója.</summary>
    public event EventHandler<nint>? ForegroundChanged;

    public bool IsRunning => _hook != 0;

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        // A delegate-et mezőben tartjuk: a natív oldal hivatkozik rá, a GC nem
        // szedheti össze, amíg a horog él.
        _callback = OnWinEvent;
        _hook = SetWinEventHook(EventSystemForeground, EventSystemForeground, 0, _callback, 0, 0, WinEventOutOfContext);
    }

    public void Stop()
    {
        if (_hook != 0)
        {
            UnhookWinEvent(_hook);
            _hook = 0;
        }

        _callback = null;
    }

    public void Dispose() => Stop();

    private void OnWinEvent(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint eventThread, uint eventTime)
    {
        if (hwnd != 0)
        {
            ForegroundChanged?.Invoke(this, hwnd);
        }
    }

    private delegate void WinEventProc(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint eventThread, uint eventTime);

    [DllImport("user32.dll")]
    private static extern nint SetWinEventHook(
        uint eventMin, uint eventMax, nint module, WinEventProc callback, uint processId, uint threadId, uint flags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(nint hook);
}
