using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Vanara.PInvoke;
using Vanara.Windows.Shell;
using static Vanara.PInvoke.Shell32;

namespace Pilaster.Shell.Menus;

/// <summary>
/// A valódi Windows shell jobbklikk-menü megjelenítése mappák HÁTTERÉN
/// (üres területére kattintva).
/// </summary>
/// <remarks>
/// <para>
/// SZÁNDÉKOSAN NEM használható fájl-/mappaELEMEKRE — lásd lent. Mappa
/// hátterére szó szerint ugyanaz a menü jelenik meg, amit az Intéző mutatna
/// (Nézet, Rendezés, Új ▸ stb.), mert ténylegesen a rendszer
/// <c>IShellFolder::CreateViewObject(IID_IContextMenu)</c> hívását adja
/// vissza (a projekt Vanara.Windows.Shell.Common csomagján keresztül) — ez a
/// hívási minta a heap-korrupciós bisectben SOHA nem volt vétkes.
/// </para>
/// <para>
/// A <see cref="ShellContextMenu.ShowContextMenu"/> belül a natív, BLOKKOLÓ
/// <c>TrackPopupMenuEx</c>-et hívja meg. Ha ezt közvetlenül a WPF UI szálról,
/// egy <c>PreviewMouseRightButtonDown</c> (alagcsöves) eseménykezelőből
/// hívnánk meg, az a WPF egér-capture/input-rendszerével ütközve
/// lefagyasztja az alkalmazást. Ezért a teljes natív hívás-láncot (COM
/// inicializálás, menü létrehozása és megjelenítése) egy külön, kifejezetten
/// erre indított STA szálon futtatjuk, és a WPF felől <see cref="Task"/>-ként,
/// aszinkron várjuk meg — így a Dispatcher a menü nyitva léte alatt is
/// pörög, nincs újrabelépés.
/// </para>
/// <para>
/// TÖRTÉNETI MEGJEGYZÉS (v1.0.3): ez az osztály korábban a FÁJL-elemek
/// menüjét is megjelenítette, a <c>ShellContextMenu.CreateFromItems</c>
/// (Vanara) hívásán keresztül. Az a metódus (<c>ShowAsync</c>/
/// <c>ShowItemsCore</c>) volt a dokumentált, öt körös 0xC0000374
/// heap-korrupció bizonyított okozója — a fájl-elemek menüje azóta a
/// <see cref="ShellMenuSession"/> NYERS P/Invoke útján épül. A módszert
/// SZÁNDÉKOSAN eltávolítottuk innen, nehogy egy jövőbeli módosítás
/// véletlenül visszahozza — a natív („Windows") jobbklikk-mód fájl-elemekre
/// a <see cref="NativeMenuPresenter"/>-en keresztül, ugyanazt a
/// <see cref="ShellMenuSession"/>-t használva jelenik meg.
/// </para>
/// </remarks>
public static class NativeContextMenuService
{
    private const uint CoInitApartmentThreaded = 0x2;

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(nint pvReserved, uint dwCoInit);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();

    /// <summary>
    /// Megjeleníti egy mappa VALÓDI Windows háttér-menüjét (Nézet, Rendezés,
    /// Frissítés, Beillesztés, Új &gt; stb.) — ugyanaz, mint amikor az
    /// Intézőben egy mappa üres területén jobb gombbal kattintasz. Dedikált,
    /// erre a hívásra indított STA szálon fut.
    /// </summary>
    /// <param name="folderPath">A mappa teljes elérési útja, aminek a háttér-menüjét meg kell jeleníteni.</param>
    /// <param name="screenX">A megjelenítés X koordinátája képernyő-térben.</param>
    /// <param name="screenY">A megjelenítés Y koordinátája képernyő-térben.</param>
    /// <param name="ownerWindowHandle">A tulajdonos ablak fogantyúja.</param>
    /// <returns>Hamis, ha a natív háttér-menü valamiért nem volt előállítható.</returns>
    public static Task<bool> ShowBackgroundAsync(string folderPath, int screenX, int screenY, nint ownerWindowHandle)
    {
        if (string.IsNullOrEmpty(folderPath))
        {
            return Task.FromResult(false);
        }

        return RunIsolatedAsync(() => ShowBackgroundCore(folderPath, screenX, screenY, ownerWindowHandle));
    }

    /// <summary>
    /// Elindít egy dedikált STA szálat, lefuttatja rajta <paramref name="work"/>-öt
    /// COM-inicializálás mellett, és a hívó szálat nem blokkolva, <see cref="Task"/>-ként
    /// adja vissza az eredményt.
    /// </summary>
    private static Task<bool> RunIsolatedAsync(Func<bool> work)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var staThread = new Thread(() =>
        {
            var comInitialized = CoInitializeEx(0, CoInitApartmentThreaded) >= 0;

            try
            {
                tcs.TrySetResult(work());
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
            finally
            {
                if (comInitialized)
                {
                    CoUninitialize();
                }
            }
        })
        {
            IsBackground = true,
            Name = "Pilaster.NativeContextMenu",
        };

        staThread.SetApartmentState(ApartmentState.STA);
        staThread.Start();

        return tcs.Task;
    }

    private static bool ShowBackgroundCore(string folderPath, int screenX, int screenY, nint ownerWindowHandle)
    {
        IShellFolder? folder = null;

        try
        {
            SHCreateItemHandlerFromParsingName(folderPath, out folder, BHID.BHID_SFObject).ThrowIfFailed();

            if (folder is null)
            {
                return false;
            }

            // IShellFolder::CreateViewObject(hwnd, IID_IContextMenu) adja
            // magának a mappának a háttér-menüjét — ez pontosan az, amit az
            // Intéző mutat, ha egy mappa ÜRES területén (nem egy elemen)
            // kattintasz jobb gombbal.
            var contextMenu = folder.CreateViewObject<IContextMenu>((HWND)ownerWindowHandle);

            if (contextMenu is null)
            {
                return false;
            }

            using var menu = new ShellContextMenu(contextMenu);

            menu.ShowContextMenu(new POINT(screenX, screenY), hWnd: (HWND)ownerWindowHandle);

            return true;
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or InvalidOperationException or Win32Exception)
        {
            return false;
        }
        finally
        {
            if (folder is not null)
            {
                Marshal.ReleaseComObject(folder);
            }
        }
    }
}
