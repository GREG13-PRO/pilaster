using CommunityToolkit.Mvvm.ComponentModel;
using Pilaster.App.Localization;
using Pilaster.Core.Metadata;
using Pilaster.Providers.Local;
using Wpf.Ui.Controls;
using ImageSource = System.Windows.Media.ImageSource;

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

    /// <summary>
    /// Behelyezett optikai lemez saját ikonja (autorun.inf vagy gyökér-.ico
    /// alapján) — ha van, ez jelenik meg az <see cref="Icon"/> helyett.
    /// </summary>
    [ObservableProperty]
    public partial ImageSource? CustomIcon { get; set; }

    public bool HasCustomIcon => CustomIcon is not null;

    partial void OnCustomIconChanged(ImageSource? value) => OnPropertyChanged(nameof(HasCustomIcon));

    /// <summary>Meghajtóknál a kapacitás-adatok; egyébként <c>null</c>.</summary>
    public DriveEntry? Drive { get; init; }

    /// <summary>Igaz, ha meghajtó — ilyenkor a sor kihasználtság-sávot is rajzol.</summary>
    public bool IsDrive => Drive is not null;

    /// <summary>A meghajtó másodlagos sora, pl. „82,4 GB szabad / 476 GB".</summary>
    [ObservableProperty]
    public partial string? Detail { get; set; }

    [ObservableProperty]
    public partial double UsedFraction { get; set; }

    /// <summary>
    /// Igaz, ha ez a sor útvonala a jelenleg megnyitott mappa, vagy annak
    /// valódi őse (pl. „Dokumentumok" aktívnak számít akkor is, ha épp a
    /// Dokumentumok egy almappájában vagyunk).
    /// </summary>
    /// <remarks>
    /// Szándékosan nem a ListBox saját <c>IsSelected</c>/<c>SelectedItem</c>
    /// állapotára épül: az kattintás után azonnal nullázódik (hogy ugyanarra a
    /// sorra ismét rá lehessen kattintani), és nem követné a breadcrumb-bal,
    /// vissza/előre gombbal vagy fülváltással történő navigációt, sem a
    /// mélyebbre navigálást. Az útvonal-alapú jelzés mindegyikre helyesen
    /// reagál — lásd <c>MainWindowViewModel.IsPathOrAncestor</c>.
    /// </remarks>
    [ObservableProperty]
    public partial bool IsActive { get; set; }

    /// <summary>
    /// Igaz a Kedvencek szekció olyan sorára, amelynek célja már nem
    /// létezik a lemezen — a sor halványabban jelenik meg, és eltávolítást
    /// kínál fel navigáció helyett.
    /// </summary>
    [ObservableProperty]
    public partial bool IsMissing { get; set; }

    /// <summary>Igaz a Kedvencek szekció soraira — ekkor jelenik meg az eltávolító „x" gomb.</summary>
    public bool IsRemovable { get; init; }

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

/// <summary>
/// Egy címke sora az oldalsáv Címkék szekciójában — kattintva az aktuális
/// fül nézetét szűri az adott címkével ellátott elemekre (nem navigál,
/// szemben a többi oldalsáv-sorral).
/// </summary>
public sealed partial class TagFilterItemViewModel : ObservableObject
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required TagColor Color { get; init; }

    /// <summary>Igaz, ha ez a szűrő aktív az éppen kijelölt fülön.</summary>
    [ObservableProperty]
    public partial bool IsActive { get; set; }
}
