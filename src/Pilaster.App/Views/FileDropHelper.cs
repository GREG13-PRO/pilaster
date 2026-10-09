using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Pilaster.Core.FileSystem;

namespace Pilaster.App.Views;

/// <summary>
/// Fájlok ejtése egy fájllistára, mint az Intézőben: egy mappa-sor fölött
/// abba a mappába, máshol a lista saját mappájába. A főablak listái és a
/// kétpaneles nézet panelei közösen használják.
/// </summary>
internal sealed class FileDropHelper
{
    /// <summary>A sablonok ezt a <c>Tag</c>-értéket figyelik a célsor kiemeléséhez.</summary>
    public const string DropTargetTag = "DropTarget";

    private ListBoxItem? _highlighted;

    /// <summary>
    /// Az ejtés célmappája és a kiemelendő sor (csak ha egy mappa-sor fölött
    /// vagyunk), vagy <c>null</c>, ha ide nem lehet ejteni — például a mappa
    /// önmagába húzásakor, vagy ha az elemek már ott vannak.
    /// </summary>
    public static (string Folder, ListBoxItem? Row)? ResolveTarget(DependencyObject? hit, string? listFolder, IReadOnlyList<string> sources)
    {
        var row = FindAncestor<ListBoxItem>(hit);

        if (row?.DataContext is FileSystemItem { IsNavigable: true, Kind: not FileSystemItemKind.Virtual } item
            && !sources.Any(source => string.Equals(
                Path.TrimEndingDirectorySeparator(source),
                Path.TrimEndingDirectorySeparator(item.FullPath),
                StringComparison.OrdinalIgnoreCase))
            && IsValidDropInto(sources, item.FullPath))
        {
            return (item.FullPath, row);
        }

        return listFolder is { Length: > 0 } && IsValidDropInto(sources, listFolder)
            ? (listFolder, null)
            : null;
    }

    /// <summary>Egy mappa önmagába vagy a saját almappájába nem húzható, és a már ott lévő elem sem.</summary>
    public static bool IsValidDropInto(IEnumerable<string> paths, string folder)
    {
        try
        {
            var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));

            foreach (var path in paths)
            {
                var source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

                if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase)
                    || target.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(Path.GetDirectoryName(source), target, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return false;
        }
    }

    public static DragDropEffects ToEffect(PaneDropAction action, DragDropEffects allowed)
    {
        var wanted = action switch
        {
            PaneDropAction.Move => DragDropEffects.Move,
            PaneDropAction.Shortcut => DragDropEffects.Link,
            _ => DragDropEffects.Copy,
        };

        return (allowed & wanted) != 0 ? wanted : allowed & DragDropEffects.Copy;
    }

    /// <summary>A célsor kiemelése (vagy <c>null</c>-lal a kiemelés megszüntetése).</summary>
    public void Highlight(ListBoxItem? row)
    {
        if (ReferenceEquals(row, _highlighted))
        {
            return;
        }

        if (_highlighted is not null)
        {
            _highlighted.Tag = null;
        }

        _highlighted = row;

        if (row is not null)
        {
            row.Tag = DropTargetTag;
        }
    }

    public static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null and not T)
        {
            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return current as T;
    }
}
