using CommunityToolkit.Mvvm.ComponentModel;
using Pilaster.App.Localization;
using Pilaster.Providers.Local;
using Wpf.Ui.Controls;

namespace Pilaster.App.ViewModels;

/// <summary>Egy sor az oldalsávban: gyorselérés vagy meghajtó.</summary>
/// <remarks>
/// A megjelenített <see cref="Label"/> megfigyelhető, nem pedig konverterrel
/// fordított kötés. Ennek az az oka, hogy a nyelvi kulcs itt futásidőben dől el
/// (a gyorselérés kulcsot használ, a meghajtó a kötet valódi nevét), és egy
/// konverter nem tudna újra lefutni nyelvváltáskor — a kötés forrása a
/// változatlan kulcs maradna.
/// </remarks>
public sealed partial class SidebarItemViewModel : ObservableObject
{
    /// <summary>
    /// A felirat erőforráskulcsa, ha fordítandó; <c>null</c>, ha a
    /// <see cref="Label"/> már kész szöveg (pl. meghajtónév).
    /// </summary>
    public string? LabelKey { get; init; }

    [ObservableProperty]
    public partial string Label { get; set; } = string.Empty;

    public required string Path { get; init; }

    public SymbolRegular Icon { get; init; } = SymbolRegular.Folder24;

    /// <summary>Meghajtóknál a kapacitás-adatok; egyébként <c>null</c>.</summary>
    public DriveEntry? Drive { get; init; }

    /// <summary>Igaz, ha meghajtó — ilyenkor a sor kihasználtság-sávot is rajzol.</summary>
    public bool IsDrive => Drive is not null;

    /// <summary>A meghajtó másodlagos sora, pl. „82,4 GB szabad / 476 GB".</summary>
    [ObservableProperty]
    public partial string? Detail { get; set; }

    [ObservableProperty]
    public partial double UsedFraction { get; set; }

    /// <summary>A lefordítható feliratok újraképzése nyelvváltás után.</summary>
    public void RefreshLabel()
    {
        if (LabelKey is { } key)
        {
            Label = TranslationSource.Instance[key];
        }
    }
}

/// <summary>Az oldalsáv egy csoportja fejléccel.</summary>
public sealed partial class SidebarSection : ObservableObject
{
    public required string HeaderKey { get; init; }

    [ObservableProperty]
    public partial string Header { get; set; } = string.Empty;

    public required IReadOnlyList<SidebarItemViewModel> Items { get; init; }

    public void RefreshLabels()
    {
        Header = TranslationSource.Instance[HeaderKey];

        foreach (var item in Items)
        {
            item.RefreshLabel();
        }
    }
}
