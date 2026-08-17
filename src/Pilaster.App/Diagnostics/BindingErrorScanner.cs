using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Pilaster.App.Diagnostics;

/// <summary>
/// A vizuális fa bejárásával keres meghibásodott WPF kötéseket.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="BindingErrorTraceListener"/> a WPF hivatalos, dokumentált
/// mechanizmusa — de ebben a .NET-verzióban (net10.0-windows) MÉRVE nem
/// jelent semmit: sem egy egyszerű, egyszintű, sem egy többszintű útvonalú
/// hibás kötésnél nem hív semmilyen <see cref="System.Diagnostics.TraceListener"/>
/// metódust, a `SourceLevels.All`-lal és a felszerelés legkorábbi lehetséges
/// időpontjával (az <see cref="App"/> KONSTRUKTORÁBAN, még
/// <c>InitializeComponent</c> előtt) próbálva is — miközben egy kézzel
/// kiváltott <c>TraceEvent</c>-hívás bizonyítottan eljut a listenerig, tehát
/// maga a bekötés helyes, csak a WPF belső kötés-motorja nem hívja meg.
/// </para>
/// <para>
/// Ezért ez az osztály egy MÁSIK, publikus és dokumentált API-ra épül: minden
/// <see cref="DependencyObject"/> <see cref="DependencyObject.GetLocalValueEnumerator"/>
/// metódusa felsorolja a helyben beállított értékeket, EZEK KÖZÖTT a még fel
/// nem oldott <see cref="BindingExpression"/>/<see cref="MultiBindingExpression"/>
/// objektumokat is — ezeknek van <see cref="BindingExpressionBase.Status"/>
/// tulajdonságuk, ami <see cref="BindingStatus.PathError"/>-ra áll, ha a forrás
/// nem találja a kötött tulajdonságot. Ez a mechanizmus a vizuális fa
/// bejárásakor MEGBÍZHATÓAN működik, nem függ semmilyen globális
/// nyomkövetési kapcsolótól.
/// </para>
/// </remarks>
public static class BindingErrorScanner
{
    /// <summary>
    /// Bejárja a megadott gyökér teljes vizuális (és logikai, a vizuális fába
    /// még be nem kötött részekhez) alfáját, és minden PathError/
    /// UpdateSourceError állapotú kötést jelent.
    /// </summary>
    public static IReadOnlyList<string> Scan(DependencyObject root)
    {
        var results = new List<string>();
        var visited = new HashSet<DependencyObject>();
        Walk(root, results, visited);
        return results;
    }

    private static void Walk(DependencyObject node, List<string> results, HashSet<DependencyObject> visited)
    {
        if (!visited.Add(node))
        {
            return;
        }

        var enumerator = node.GetLocalValueEnumerator();

        while (enumerator.MoveNext())
        {
            var entry = enumerator.Current;

            switch (entry.Value)
            {
                // A `be.DataItem is null` eset NEM valódi hiba: olyan
                // panelekben fordul elő, amik szándékosan `null` forrásra
                // (pl. „nincs kijelölt fájl", „nincs kijelölt gyorselérés-
                // sor") vannak kötve, és emiatt Collapsed is a felületük —
                // a WPF ilyenkor is PathError-t jelent, holott a kötés maga
                // helyes, csak épp nincs mit olvasnia belőle.
                case BindingExpression be when IsError(be.Status) && be.DataItem is not null:
                    results.Add(Describe(node, entry.Property.Name, be.ParentBinding?.Path?.Path, be.Status));
                    break;

                case MultiBindingExpression mbe when IsError(mbe.Status):
                    results.Add(Describe(node, entry.Property.Name, "(MultiBinding)", mbe.Status));
                    break;
            }
        }

        // Vizuális ÉS logikai gyerekek is kellenek: egy Collapsed vagy még
        // nem renderelt tartalom logikailag már a fában lehet, mielőtt
        // vizuálisan is megjelenne.
        foreach (var child in VisualChildren(node))
        {
            Walk(child, results, visited);
        }

        foreach (var child in LogicalChildren(node))
        {
            Walk(child, results, visited);
        }
    }

    private static bool IsError(BindingStatus status) =>
        status is BindingStatus.PathError or BindingStatus.UpdateSourceError or BindingStatus.UpdateTargetError;

    private static string Describe(DependencyObject node, string property, string? path, BindingStatus status) =>
        $"{node.GetType().Name}.{property} ← \"{path}\" ({status})";

    private static IEnumerable<DependencyObject> VisualChildren(DependencyObject node)
    {
        if (node is not Visual)
        {
            yield break;
        }

        var count = VisualTreeHelper.GetChildrenCount(node);

        for (var i = 0; i < count; i++)
        {
            yield return VisualTreeHelper.GetChild(node, i);
        }
    }

    private static IEnumerable<DependencyObject> LogicalChildren(DependencyObject node)
    {
        foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(node))
        {
            if (child is DependencyObject dependencyObject)
            {
                yield return dependencyObject;
            }
        }
    }
}
