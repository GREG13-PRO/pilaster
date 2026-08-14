using System.IO;
using Vanara.Windows.Shell;

namespace Pilaster.Shell.Recycle;

/// <summary>
/// Egy elem a Lomtárban.
/// </summary>
/// <remarks>
/// A <see cref="ShellItem"/>-et (a Vanara csomag típusa) szándékosan nem
/// tesszük publikussá — a Pilaster.App réteg csak ezt a szűk, saját típust
/// lássa, ahogy a projekt többi Win32/COM-interop szolgáltatása is
/// (pl. <see cref="Pilaster.Shell.Devices.DriveEntry"/>) elrejti a natív
/// részleteket.
/// </remarks>
public sealed class RecycledItem
{
    internal RecycledItem(ShellItem shellItem)
    {
        ShellItem = shellItem;
        OriginalPath = shellItem.Name ?? string.Empty;
        IsDirectory = shellItem.IsFolder;
    }

    internal ShellItem ShellItem { get; }

    /// <summary>A törölt elem eredeti, teljes útvonala.</summary>
    public string OriginalPath { get; }

    public bool IsDirectory { get; }

    /// <summary>Az eredeti fájl-/mappanév.</summary>
    public string Name => Path.GetFileName(Path.TrimEndingDirectorySeparator(OriginalPath));

    /// <summary>Az eredeti szülőmappa, ahova visszaállításkor kerül.</summary>
    public string? OriginalFolder => Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(OriginalPath));
}

/// <summary>
/// A rendszer Lomtárának elérése — tartalom listázása, visszaállítás,
/// végleges törlés, ürítés.
/// </summary>
/// <remarks>
/// A Vanara <see cref="Vanara.Windows.Shell.RecycleBin"/> statikus
/// osztályára épül, ami a valódi Lomtár shell-névtérrel (nem a
/// <c>$Recycle.Bin</c> mappa nyers, rejtett fájljaival) dolgozik — így az
/// eredeti fájlnév és útvonal natívan elérhető, nem kell a Windows belső,
/// dokumentálatlan formátumát értelmezni.
/// </remarks>
public static class RecycleBinService
{
    /// <summary>Az elemek száma a Lomtárban. Nagy Lomtárnál ez a hívás időbe telhet.</summary>
    public static long Count => RecycleBin.Count;

    public static bool IsEmpty => Count == 0;

    /// <summary>A Lomtár tartalmának felsorolása.</summary>
    public static IReadOnlyList<RecycledItem> GetItems()
    {
        var items = new List<RecycledItem>();

        foreach (var shellItem in RecycleBin.GetItems())
        {
            items.Add(new RecycledItem(shellItem));
        }

        return items;
    }

    /// <summary>Egy elem visszaállítása az eredeti helyére.</summary>
    public static void Restore(RecycledItem item) => RecycleBin.Restore(item.ShellItem);

    /// <summary>
    /// Egy elem VÉGLEGES törlése — a Lomtárban lévő elem újbóli törlése nem
    /// kerül egy „második" lomtárba, ahogy az Intézőben sem.
    /// </summary>
    public static void Delete(RecycledItem item) =>
        ShellFileOperations.Delete(
            item.ShellItem,
            ShellFileOperations.OperationFlags.Silent
                | ShellFileOperations.OperationFlags.NoConfirmation
                | ShellFileOperations.OperationFlags.NoErrorUI);

    /// <summary>A teljes Lomtár ürítése.</summary>
    public static void Empty() => RecycleBin.Empty();

    /// <summary>
    /// Egy még nem törölt fájl/mappa Lomtárba küldése — ugyanaz, mint az
    /// Intéző Delete billentyűje. A <see cref="ShellFileOperations"/> alap
    /// beállítása (<c>AllowUndo</c>) már Lomtárba küld, tehát nincs szükség
    /// extra jelzőre.
    /// </summary>
    public static void SendToRecycleBin(string path) =>
        ShellFileOperations.Delete(
            path,
            ShellFileOperations.OperationFlags.AllowUndo
                | ShellFileOperations.OperationFlags.Silent
                | ShellFileOperations.OperationFlags.NoConfirmation
                | ShellFileOperations.OperationFlags.NoErrorUI);

    /// <summary>Egy még nem törölt fájl/mappa AZONNALI, végleges törlése (Shift+Delete).</summary>
    public static void DeletePermanently(string path) =>
        ShellFileOperations.Delete(
            path,
            ShellFileOperations.OperationFlags.Silent
                | ShellFileOperations.OperationFlags.NoConfirmation
                | ShellFileOperations.OperationFlags.NoErrorUI);
}
