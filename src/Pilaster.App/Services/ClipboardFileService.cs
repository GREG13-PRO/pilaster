using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace Pilaster.App.Services;

/// <summary>
/// Fájlok vágólapra írása/olvasása a Windows Intéző saját formátumában.
/// </summary>
/// <remarks>
/// Ugyanazt a <c>CF_HDROP</c> + „Preferred DropEffect" formátumpárt írja/
/// olvassa, amit az Intéző használ másoláskor/kivágáskor — ezért Explorerből
/// másolt/kivágott fájlok közvetlenül beilleszthetők a Pilasterbe, és
/// fordítva, a Pilasterben másolt/kivágott fájlok is beilleszthetők az
/// Intézőbe. A tényleges fájlműveletet (másolás/áthelyezés) NEM ez a
/// szolgáltatás végzi — az a <see cref="FileOperations.FileOperationEngine"/>
/// feladata, ez csak a vágólap-interopot adja hozzá.
/// </remarks>
public static class ClipboardFileService
{
    /// <summary>DROPEFFECT_MOVE — lásd a Win32 <c>ole2.h</c>-t.</summary>
    private const int DropEffectMove = 2;

    /// <summary>DROPEFFECT_COPY — lásd a Win32 <c>ole2.h</c>-t.</summary>
    private const int DropEffectCopy = 1;

    /// <summary>Fájlok vágólapra írása — Ctrl+C/Ctrl+X esetén hívva.</summary>
    public static void SetClipboard(IReadOnlyList<string> paths, bool isCut)
    {
        if (paths.Count == 0)
        {
            return;
        }

        var files = new System.Collections.Specialized.StringCollection();
        files.AddRange([.. paths]);

        var data = new DataObject();
        data.SetFileDropList(files);
        data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(isCut ? DropEffectMove : DropEffectCopy)));

        try
        {
            Clipboard.SetDataObject(data, copy: true);
        }
        catch (COMException)
        {
            // A vágólapot időnként egy másik folyamat zárolja — nincs jobb teendő, mint csendben eldobni a kísérletet.
        }
    }

    /// <summary>A vágólapon lévő fájlok kiolvasása, ha vannak — a beillesztés parancs hívja.</summary>
    public static bool TryGetClipboardFiles(out IReadOnlyList<string> paths, out bool isCut)
    {
        paths = [];
        isCut = false;

        System.Collections.Specialized.StringCollection? files;

        try
        {
            if (!Clipboard.ContainsFileDropList())
            {
                return false;
            }

            files = Clipboard.GetFileDropList();
        }
        catch (COMException)
        {
            // A vágólapot időnként egy másik folyamat zárolja.
            return false;
        }

        if (files is null || files.Count == 0)
        {
            return false;
        }

        var list = new List<string>(files.Count);

        foreach (var path in files)
        {
            if (path is not null && (File.Exists(path) || Directory.Exists(path)))
            {
                list.Add(path);
            }
        }

        if (list.Count == 0)
        {
            return false;
        }

        paths = list;
        isCut = IsCutOperation();
        return true;
    }

    /// <summary>
    /// Igaz, ha a vágólap tartalma kivágásból származik (nem másolásból) — az
    /// Intéző ezt egy „Preferred DropEffect" nevű, DROPEFFECT-értéket hordozó
    /// adatformátummal jelzi.
    /// </summary>
    private static bool IsCutOperation()
    {
        try
        {
            if (Clipboard.GetData("Preferred DropEffect") is not MemoryStream stream)
            {
                return false;
            }

            var buffer = new byte[4];
            var read = stream.Read(buffer, 0, buffer.Length);

            return read >= 4 && BitConverter.ToInt32(buffer, 0) == DropEffectMove;
        }
        catch (COMException)
        {
            return false;
        }
    }
}
