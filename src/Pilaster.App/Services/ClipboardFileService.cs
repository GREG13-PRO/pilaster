using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Pilaster.Core.Templates;

namespace Pilaster.App.Services;

public enum ClipboardPasteOutcome
{
    Succeeded,
    NoFilesOnClipboard,
    PartiallyFailed,
    TargetInvalid,
}

/// <param name="Outcome">Az eredmény.</param>
/// <param name="SucceededCount">Hány elem másolódott/mozgott sikeresen.</param>
/// <param name="FailedCount">Hány elemnél hiúsult meg a művelet.</param>
public readonly record struct ClipboardPasteResult(
    ClipboardPasteOutcome Outcome,
    int SucceededCount,
    int FailedCount);

/// <summary>
/// Fájlok beillesztése a Windows vágólapról.
/// </summary>
/// <remarks>
/// Ugyanazt a <c>CF_HDROP</c> + „Preferred DropEffect" formátumpárt olvassa,
/// amit az Intéző ír másoláskor/kivágáskor — ezért Explorerből másolt vagy
/// kivágott fájlok közvetlenül beilleszthetők a Pilasterbe, saját
/// vágólap-formátum bevezetése nélkül. A Pilaster egyelőre nem ír a
/// vágólapra saját másolás/kivágás parancsból (azok a jövőbeli másolómotor/
/// aktivitás-központ mérföldkőhöz tartoznak) — ez a szolgáltatás kizárólag a
/// beillesztést valósítja meg, ami önmagában is teljes értékű: bármi, amit a
/// felhasználó máshol (jellemzően az Intézőben) másolt vagy kivágott, itt
/// beilleszthető.
/// </remarks>
public static class ClipboardFileService
{
    /// <summary>DROPEFFECT_MOVE — lásd a Win32 <c>ole2.h</c>-t.</summary>
    private const int DropEffectMove = 2;

    public static ClipboardPasteResult Paste(string? targetDirectory)
    {
        if (string.IsNullOrWhiteSpace(targetDirectory) || !Directory.Exists(targetDirectory))
        {
            return new ClipboardPasteResult(ClipboardPasteOutcome.TargetInvalid, 0, 0);
        }

        System.Collections.Specialized.StringCollection? files;

        try
        {
            if (!Clipboard.ContainsFileDropList())
            {
                return new ClipboardPasteResult(ClipboardPasteOutcome.NoFilesOnClipboard, 0, 0);
            }

            files = Clipboard.GetFileDropList();
        }
        catch (COMException)
        {
            // A vágólapot időnként egy másik folyamat zárolja.
            return new ClipboardPasteResult(ClipboardPasteOutcome.NoFilesOnClipboard, 0, 0);
        }

        if (files is null || files.Count == 0)
        {
            return new ClipboardPasteResult(ClipboardPasteOutcome.NoFilesOnClipboard, 0, 0);
        }

        var move = IsCutOperation();
        var succeeded = 0;
        var failed = 0;

        foreach (var sourcePath in files)
        {
            if (sourcePath is not null && TryCopyOrMove(sourcePath, targetDirectory, move))
            {
                succeeded++;
            }
            else
            {
                failed++;
            }
        }

        var outcome = failed == 0 ? ClipboardPasteOutcome.Succeeded : ClipboardPasteOutcome.PartiallyFailed;

        return new ClipboardPasteResult(outcome, succeeded, failed);
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

    private static bool TryCopyOrMove(string sourcePath, string targetDirectory, bool move)
    {
        try
        {
            var isDirectory = Directory.Exists(sourcePath);
            var isFile = !isDirectory && File.Exists(sourcePath);

            if (!isDirectory && !isFile)
            {
                return false;
            }

            var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(sourcePath));
            var extension = isFile ? Path.GetExtension(name).TrimStart('.') : string.Empty;
            var baseName = isFile ? Path.GetFileNameWithoutExtension(name) : name;

            // Ütközésnél az Explorer szokása szerint " (2)", " (3)" — csak ha
            // a cél mappában már létezik ugyanaz a név, egyébként érintetlen.
            var destinationName = NameTemplate.ResolveUnique(targetDirectory, baseName, extension);
            var destinationPath = Path.Combine(targetDirectory, destinationName);

            if (isDirectory)
            {
                if (move)
                {
                    Directory.Move(sourcePath, destinationPath);
                }
                else
                {
                    CopyDirectory(sourcePath, destinationPath);
                }
            }
            else if (move)
            {
                File.Move(sourcePath, destinationPath);
            }
            else
            {
                File.Copy(sourcePath, destinationPath);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (var file in Directory.EnumerateFiles(sourceDirectory))
        {
            File.Copy(file, Path.Combine(destinationDirectory, Path.GetFileName(file)));
        }

        foreach (var subdirectory in Directory.EnumerateDirectories(sourceDirectory))
        {
            CopyDirectory(subdirectory, Path.Combine(destinationDirectory, Path.GetFileName(subdirectory)));
        }
    }
}
