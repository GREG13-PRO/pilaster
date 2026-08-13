using System.ComponentModel;
using System.Runtime.InteropServices;
using Vanara.PInvoke;
using Vanara.Windows.Shell;

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
/// </remarks>
public static class NativeContextMenuService
{
    /// <summary>
    /// Megjeleníti a menüt, és a kiválasztott parancsot automatikusan végre
    /// is hajtja. Hívja meg minden érintett <see cref="ShellItem"/>
    /// felszabadítását is.
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
    public static bool TryShow(IReadOnlyList<string> paths, int screenX, int screenY, nint ownerWindowHandle)
    {
        if (paths.Count == 0)
        {
            return false;
        }

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
}
