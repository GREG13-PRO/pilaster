using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Vanara.PInvoke;
using Vanara.Windows.Shell;
using static Vanara.PInvoke.Shell32;

namespace Pilaster.Shell.Menus;

/// <summary>
/// A valódi Windows shell jobbklikk-menü megjelenítése fájlokon/mappákon.
/// </summary>
/// <remarks>
/// Nem egy Fluent-stílusú másolat, hanem szó szerint ugyanaz a menü, amit az
/// Intéző mutatna — a telepített programok (7-Zip, Git, IDE-k stb.) saját
/// bejegyzéseivel együtt —, mert ténylegesen a rendszer
/// <c>IContextMenu</c>/<c>IShellFolder</c> gépezetét hívja meg
/// (<see cref="ShellContextMenu"/>, a projekt Vanara.Windows.Shell.Common
/// csomagján keresztül). A kiválasztott parancsot is a shell hajtja végre,
/// tehát a „Megnyitás ezzel", „Tulajdonságok" és minden third-party
/// bejegyzés natívan, változtatás nélkül működik.
///
/// A <see cref="ShellContextMenu.ShowContextMenu"/> belül a natív, BLOKKOLÓ
/// <c>TrackPopupMenuEx</c>-et hívja meg. Ha ezt közvetlenül a WPF UI szálról,
/// egy <c>PreviewMouseRightButtonDown</c> (alagcsöves) eseménykezelőből
/// hívnánk meg, az a WPF egér-capture/input-rendszerével ütközve
/// lefagyasztja az alkalmazást. Ezért a teljes natív hívás-láncot (COM
/// inicializálás, <see cref="ShellItem"/> megnyitás, menü létrehozása és
/// megjelenítése) egy külön, kifejezetten erre indított STA szálon futtatjuk,
/// és a WPF felől <see cref="Task"/>-ként, aszinkron várjuk meg — így a
/// Dispatcher a menü nyitva léte alatt is pörög, nincs újrabelépés.
/// </remarks>
public static class NativeContextMenuService
{
    private const uint CoInitApartmentThreaded = 0x2;

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(nint pvReserved, uint dwCoInit);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();

    /// <summary>
    /// Megjeleníti a menüt egy külön STA szálon, és a kiválasztott parancsot
    /// automatikusan végre is hajtja. A hívó szálat (jellemzően a WPF UI
    /// szálat) nem blokkolja — a visszaadott <see cref="Task"/> a menü
    /// bezárásakor fejeződik be.
    /// </summary>
    /// <param name="paths">A kijelölt elemek teljes útvonalai (legalább egy).</param>
    /// <param name="screenX">A megjelenítés X koordinátája képernyő-térben.</param>
    /// <param name="screenY">A megjelenítés Y koordinátája képernyő-térben.</param>
    /// <param name="ownerWindowHandle">A tulajdonos ablak fogantyúja — a shell párbeszédeinek (pl. törlés megerősítése) szülője.</param>
    /// <returns>
    /// Hamis, ha a natív menü valamiért nem volt előállítható (pl. egy
    /// hibás harmadik féltől származó shell-bővítmény) — ilyenkor a hívó
    /// eshet vissza saját, egyszerűbb menüre.
    /// </returns>
    public static Task<bool> ShowAsync(IReadOnlyList<string> paths, int screenX, int screenY, nint ownerWindowHandle)
    {
        if (paths.Count == 0)
        {
            return Task.FromResult(false);
        }

        return RunIsolatedAsync(() => ShowItemsCore(paths, screenX, screenY, ownerWindowHandle));
    }

    /// <summary>
    /// Megjeleníti egy mappa VALÓDI Windows háttér-menüjét (Nézet, Rendezés,
    /// Frissítés, Beillesztés, Új &gt; stb.) — ugyanaz, mint amikor az
    /// Intézőben egy mappa üres területén jobb gombbal kattintasz. Ugyanúgy
    /// külön STA szálon fut, mint <see cref="ShowAsync"/>.
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

    private static bool ShowItemsCore(IReadOnlyList<string> paths, int screenX, int screenY, nint ownerWindowHandle)
    {
        ShellItem[]? items = null;

        try
        {
            items = new ShellItem[paths.Count];

            for (var i = 0; i < paths.Count; i++)
            {
                items[i] = ShellItem.Open(paths[i]);
            }

            using var menu = ShellContextMenu.CreateFromItems(items, out var keepAlive);

            using (keepAlive)
            {
                // Ez a hívás blokkol, amíg a menü nyitva van (natív
                // TrackPopupMenuEx) — de ezen a külön szálon ez a szándék:
                // a WPF UI szálat nem érinti.
                menu.ShowContextMenu(new POINT(screenX, screenY), hWnd: (HWND)ownerWindowHandle);
            }

            return true;
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or InvalidOperationException or Win32Exception)
        {
            return false;
        }
        finally
        {
            if (items is not null)
            {
                foreach (var item in items)
                {
                    item?.Dispose();
                }
            }
        }
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
