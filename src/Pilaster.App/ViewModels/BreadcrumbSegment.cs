namespace Pilaster.App.ViewModels;

/// <summary>
/// Egy szegmens az útvonalsávban.
/// </summary>
/// <param name="Label">A megjelenített név (mappanév vagy meghajtó-címke).</param>
/// <param name="Path">A teljes útvonal eddig a szegmensig — erre navigál a kattintás.</param>
public sealed record BreadcrumbSegment(string Label, string Path)
{
    /// <summary>
    /// A Kezdőlap szegmense — szöveg helyett csak házikó-ikonként jelenik meg
    /// (mint az Intézőben), különben a fül címével együtt kétszer olvasható a
    /// „Kezdőlap" felirat.
    /// </summary>
    public bool IsHome => Path == TabViewModel.HomeMarker;
}
