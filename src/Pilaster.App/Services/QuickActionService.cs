using System.IO;
using Pilaster.Core.Settings;
using Pilaster.Core.Templates;

namespace Pilaster.App.Services;

/// <summary>Egy gyorsgomb végrehajtásának eredménye.</summary>
/// <param name="CreatedPath">A létrehozott elem teljes útvonala, siker esetén.</param>
/// <param name="ErrorMessage">A hiba oka, ha nem sikerült.</param>
public readonly record struct QuickActionResult(string? CreatedPath, string? ErrorMessage)
{
    public bool Succeeded => CreatedPath is not null;
}

/// <summary>A felső sáv testreszabható gyorsgombjainak végrehajtása.</summary>
public sealed class QuickActionService
{
    /// <summary>
    /// Új mappa vagy fájl létrehozása a gomb beállításai szerint.
    /// </summary>
    /// <param name="action">A gomb konfigurációja.</param>
    /// <param name="currentFolder">Az éppen megnyitott mappa; rögzített célnál figyelmen kívül marad.</param>
    public QuickActionResult Execute(QuickActionSettings action, string? currentFolder)
    {
        var directory = action.Target == QuickActionTarget.FixedPath
            ? action.FixedPath
            : currentFolder;

        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return new QuickActionResult(null, "Folder_NotFound");
        }

        var extension = action.Kind == QuickActionKind.File
            ? action.Extension.TrimStart('.')
            : string.Empty;

        var expanded = NameTemplate.Expand(action.NameTemplate);
        var fileName = NameTemplate.ResolveUnique(directory, expanded, extension);
        var fullPath = Path.Combine(directory, fileName);

        try
        {
            if (action.Kind == QuickActionKind.Folder)
            {
                Directory.CreateDirectory(fullPath);
            }
            else
            {
                // A CreateNew szándékos: ha a névütközés-feloldás után mégis
                // létezne a fájl (pl. közben más program hozta létre), inkább
                // hibázzunk, mint hogy csendben felülírjunk valamit.
                using var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write);
            }

            return new QuickActionResult(fullPath, null);
        }
        catch (UnauthorizedAccessException)
        {
            return new QuickActionResult(null, "Error_AccessDenied");
        }
        catch (IOException)
        {
            return new QuickActionResult(null, "Error_CreateFailed");
        }
    }
}
