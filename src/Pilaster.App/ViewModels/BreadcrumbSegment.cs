namespace Pilaster.App.ViewModels;

/// <summary>
/// Egy szegmens az útvonalsávban.
/// </summary>
/// <param name="Label">A megjelenített név (mappanév vagy meghajtó-címke).</param>
/// <param name="Path">A teljes útvonal eddig a szegmensig — erre navigál a kattintás.</param>
public sealed record BreadcrumbSegment(string Label, string Path);
