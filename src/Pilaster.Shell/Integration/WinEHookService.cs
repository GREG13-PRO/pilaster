using Vanara.Extensions;
using Vanara.PInvoke;
using static Vanara.PInvoke.User32;

namespace Pilaster.Shell.Integration;

/// <summary>
/// Alacsony szintű billentyűzet-hook, ami elkapja a Win+E kombinációt, amíg a
/// folyamat fut.
/// </summary>
/// <remarks>
/// <para>
/// A Win+E rendszerszintű gyorsbillentyű, amit maga a Windows köt keményen az
/// Intézőhöz — ezt nem lehet regisztrykulccsal átirányítani, csak úgy, ha egy
/// <c>WH_KEYBOARD_LL</c> hook a lenyomás pillanatában elkapja és „lenyeli" a
/// billentyűt, mielőtt a rendszer feldolgozná. Ez KIZÁRÓLAG addig működik,
/// amíg ez a folyamat fut — ha a Pilaster nincs elindítva, a Win+E a normál
/// Intézőt nyitja meg, mert ekkor nincs, ami elfogja a billentyűt.
/// </para>
/// <para>
/// A <see cref="Vanara.PInvoke.User32.SetWindowsHookEx"/> egy <c>SafeHHOOK</c>-ot
/// ad vissza — ez a biztonságoshandle-alapú Vanara-csomagolás miatt magától
/// felszabadul <see cref="Stop"/>/<see cref="Dispose"/> hívásakor, kézzel írt
/// hook-eltávolítási hibalehetőség nélkül.
/// </para>
/// </remarks>
public sealed class WinEHookService : IDisposable
{
    private SafeHHOOK? _hook;
    private HookProc? _procDelegate;
    private bool _leftWindowsDown;
    private bool _rightWindowsDown;

    /// <summary>Akkor jelez, amikor a Win+E-t elkaptuk — a hívó fél dönt arról, mi történjen (pl. a főablak előtérbe hozása).</summary>
    public event EventHandler? WinEPressed;

    public bool IsRunning => _hook is { IsInvalid: false };

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        // A delegate-et mezőben tartjuk, különben a GC összeszedhetné, amíg a
        // natív oldal még hivatkozik rá — ez halk, időszakos összeomláshoz
        // vezetne, ami csak éles használat közben, véletlenszerűen jelentkezne.
        _procDelegate = HookProc;

        using var currentModule = Kernel32.GetModuleHandle();
        _hook = SetWindowsHookEx(HookType.WH_KEYBOARD_LL, _procDelegate, currentModule, 0);
    }

    public void Stop()
    {
        _hook?.Dispose();
        _hook = null;
        _procDelegate = null;
        _leftWindowsDown = false;
        _rightWindowsDown = false;
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
        {
            return CallNextHookEx(_hook!, nCode, wParam, lParam);
        }

        var info = lParam.ToStructure<KBDLLHOOKSTRUCT>();
        var message = (uint)wParam.ToInt64();
        var isKeyDown = message is 0x0100 or 0x0104; // WM_KEYDOWN / WM_SYSKEYDOWN
        var isKeyUp = message is 0x0101 or 0x0105; // WM_KEYUP / WM_SYSKEYUP

        switch (info.vkCode)
        {
            case VK.VK_LWIN:
                _leftWindowsDown = isKeyDown || (!isKeyUp && _leftWindowsDown);
                break;

            case VK.VK_RWIN:
                _rightWindowsDown = isKeyDown || (!isKeyUp && _rightWindowsDown);
                break;

            case VK.VK_E when isKeyDown && (_leftWindowsDown || _rightWindowsDown):
                WinEPressed?.Invoke(this, EventArgs.Empty);

                // Nem hívjuk a CallNextHookEx-et: ez "lenyeli" a billentyűt,
                // hogy a rendszer ne indítsa el mellette az Intézőt is.
                return (IntPtr)1;
        }

        return CallNextHookEx(_hook!, nCode, wParam, lParam);
    }

    public void Dispose() => Stop();
}
