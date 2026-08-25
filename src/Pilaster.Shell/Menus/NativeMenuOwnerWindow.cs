using System.Runtime.InteropServices;
using Vanara.PInvoke;
using static Vanara.PInvoke.Shell32;

namespace Pilaster.Shell.Menus;

/// <summary>
/// Egy minimális, láthatatlan natív ablak — a <c>TrackPopupMenuEx</c>
/// tulajdonosa, ami a shell dinamikus almenü-feltöltéséhez (7-Zip, Küldés ▸
/// stb.) és az owner-draw elemekhez szükséges üzeneteket
/// (<c>WM_INITMENUPOPUP</c>, <c>WM_DRAWITEM</c>, <c>WM_MEASUREITEM</c>,
/// <c>WM_MENUCHAR</c>) az <c>IContextMenu3::HandleMenuMsg2</c>-nek
/// továbbítja.
/// </summary>
/// <remarks>
/// <para>
/// SZÁNDÉKOSAN NEM a WPF főablak: a <c>TrackPopupMenuEx</c>-et azon a
/// szálon KELL hívni, ahol az <c>IContextMenu</c> létrejött (a megosztott
/// STA szál) — ha a tulajdonos a WPF főablak volna (ami a UI szálhoz
/// tartozik), a fenti üzenetek a ROSSZ szál üzenetsorába kerülnének, és a
/// dinamikus almenük üresek maradnának. Ez az ablak ezért minden egyes
/// megjelenítéshez frissen jön létre, ugyanazon az STA szálon, ahol a
/// <c>TrackPopupMenuEx</c> is fut, és a menü bezárása után azonnal
/// megsemmisül.
/// </para>
/// <para>
/// A <c>WndProc</c> egy PÉLDÁNY-metódusra mutató delegate — a hívó felelős
/// azért, hogy az ablak élettartama alatt ez a példány (és vele a delegate)
/// életben maradjon, különben a natív kód egy már felszabadított
/// függvénymutatóra hívna vissza.
/// </para>
/// </remarks>
internal sealed class NativeMenuOwnerWindow : IDisposable
{
    private const string ClassName = "PilasterNativeMenuOwner";
    private static bool _classRegistered;

    private readonly NativeMenuInterop.WndProcDelegate _wndProc;
    private readonly IContextMenu? _contextMenu;
    private nint _hwnd;

    /// <param name="contextMenu">
    /// A menüt szolgáltató COM-objektum — <c>IContextMenu2</c>/<c>IContextMenu3</c>-ra
    /// castolva próbáljuk az üzeneteket továbbítani; ha egyik sem támogatott,
    /// az üzenetek egyszerűen a <c>DefWindowProc</c>-hoz esnek.
    /// </param>
    public NativeMenuOwnerWindow(IContextMenu? contextMenu)
    {
        _contextMenu = contextMenu;
        _wndProc = WndProc;
        EnsureClassRegistered();
        _hwnd = CreateWindow();
    }

    public nint Handle => _hwnd;

    /// <summary>
    /// Hányszor sikerült <c>WM_INITMENUPOPUP</c>-ot továbbítani — ebből a
    /// FELSŐ szintű menü nyitásakor mindig jár egy; egy DINAMIKUSAN
    /// feltöltődő almenü (7-Zip, Küldés ▸) élő hoverelésekor eggyel több.
    /// Kizárólag diagnosztikai/teszt-célra (lásd ShellMenuSession.ShowNativeAsync
    /// dokumentációját) — éles működést nem befolyásol.
    /// </summary>
    public int ForwardedInitMenuPopupCount { get; private set; }

    /// <summary>Diagnosztikai célra: ÖSSZES, erre az ablakra érkezett üzenet — annak eldöntéséhez, hogy a példány-WndProc egyáltalán megkapja-e a hívásokat.</summary>
    public int TotalMessagesReceived { get; private set; }

    private static void EnsureClassRegistered()
    {
        if (_classRegistered)
        {
            return;
        }

        var wc = new NativeMenuInterop.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<NativeMenuInterop.WNDCLASSEX>(),
            style = 0,
            // A regisztrációhoz egy STATIKUS callback kell — a példány-szintű
            // továbbítás a CreateWindow-nál átadott GWLP_USERDATA helyett itt
            // egyszerűbb: az egyetlen célja, hogy legyen érvényes ablakosztály,
            // a TÉNYLEGES üzenetkezelést minden létrehozott ablak a SAJÁT
            // példány-WndProc-jával regisztrálja felül (lásd CreateWindow).
            lpfnWndProc = DefaultWndProc,
            hInstance = NativeMenuInterop.GetModuleHandle(null),
            lpszClassName = ClassName,
        };

        var atom = NativeMenuInterop.RegisterClassEx(ref wc);

        if (atom == 0)
        {
            var error = Marshal.GetLastWin32Error();

            // ERROR_CLASS_ALREADY_EXISTS (1410): egy korábbi hívás már
            // regisztrálta — ez nem hiba, csak azt jelzi, hogy a folyamat
            // már használta ezt az osztályt.
            if (error != 1410)
            {
                throw new InvalidOperationException($"RegisterClassEx sikertelen (hiba: {error})");
            }
        }

        _classRegistered = true;
    }

    private static nint DefaultWndProc(nint hWnd, uint msg, nint wParam, nint lParam) =>
        NativeMenuInterop.DefWindowProc(hWnd, msg, wParam, lParam);

    private nint CreateWindow()
    {
        var hwnd = NativeMenuInterop.CreateWindowEx(
            0, ClassName, string.Empty, 0,
            0, 0, 0, 0,
            NativeMenuInterop.HWND_MESSAGE, nint.Zero, NativeMenuInterop.GetModuleHandle(null), nint.Zero);

        if (hwnd == nint.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"CreateWindowEx sikertelen (hiba: {error})");
        }

        // A PÉLDÁNY-WndProc-ot itt, közvetlenül a natív SetWindowLongPtr-rel
        // állítjuk be — enélkül minden példány ugyanazt a statikus
        // DefaultWndProc-ot használná, és a HandleMenuMsg2-továbbítás sosem
        // futna le.
        NativeMenuInterop.SetWindowLongPtr(hwnd, NativeMenuInterop.GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_wndProc));

        return hwnd;
    }

    private nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        TotalMessagesReceived++;

        switch ((int)msg)
        {
            case NativeMenuInterop.WM_INITMENUPOPUP:
            case NativeMenuInterop.WM_DRAWITEM:
            case NativeMenuInterop.WM_MEASUREITEM:
            case NativeMenuInterop.WM_MENUCHAR:
                if (TryForward(msg, wParam, lParam, out var result))
                {
                    if ((int)msg == NativeMenuInterop.WM_INITMENUPOPUP)
                    {
                        ForwardedInitMenuPopupCount++;
                    }

                    return result;
                }

                break;
        }

        return NativeMenuInterop.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    /// <summary>
    /// Az üzenet továbbítása a shell <c>IContextMenu3::HandleMenuMsg2</c>
    /// (ha van), különben <c>IContextMenu2::HandleMenuMsg</c> hívásával — ez
    /// pontosan az a mechanizmus, ami a 7-Zip-hez és a Küldés ▸ almenühöz
    /// hasonló, DINAMIKUSAN feltöltődő almenüket élteti (lásd
    /// ShellMenuSession.ReadSubmenu, ami ugyanezt teszi, de a menüfa
    /// ELŐZETES beolvasásához, nem élő megjelenítéshez).
    /// </summary>
    private bool TryForward(uint msg, nint wParam, nint lParam, out nint result)
    {
        result = nint.Zero;

        try
        {
            if (_contextMenu is IContextMenu3 menu3)
            {
                var hr = menu3.HandleMenuMsg2(msg, wParam, lParam, out var lResult);
                result = lResult;
                return hr.Succeeded;
            }

            if (_contextMenu is IContextMenu2 menu2)
            {
                var hr = menu2.HandleMenuMsg(msg, wParam, lParam);
                return hr.Succeeded;
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or NotImplementedException)
        {
            // Egy hibás bővítmény üzenetkezelése nem viheti el a natív menüt
            // — enélkül a felhasználó egy egyszerű almenü-hovernél omlana el.
        }

        return false;
    }

    public void Dispose()
    {
        if (_hwnd != nint.Zero)
        {
            NativeMenuInterop.DestroyWindow(_hwnd);
            _hwnd = nint.Zero;
        }
    }
}
