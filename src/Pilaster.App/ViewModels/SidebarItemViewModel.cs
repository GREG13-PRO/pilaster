using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pilaster.App.Localization;
using Pilaster.Core.Metadata;
using Pilaster.Providers.Local;
using Pilaster.Shell.Devices;
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

    /// <summary>A mögöttes gyorselérés-bejegyzés azonosítója; más szekciókban <c>null</c>.</summary>
    public string? EntryId { get; init; }

    /// <summary>Igaz az elválasztó sorokra — ekkor csak egy vonal rajzolódik, felirat nélkül.</summary>
    public bool IsSeparator { get; init; }

    /// <summary>Opcionális csoportfejléc, ami e sor FÖLÖTT jelenik meg.</summary>
    public string? GroupHeader { get; init; }

    /// <summary>Igaz, ha van csoportfejléce.</summary>
    public bool HasGroupHeader => !string.IsNullOrWhiteSpace(GroupHeader);

    public SymbolRegular Icon { get; init; } = SymbolRegular.Folder24;

    /// <summary>Egyedi ikonszín <c>#RRGGBB</c> alakban, vagy <c>null</c> az örökölt szöveg-színhez.</summary>
    public string? IconColorHex { get; init; }

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

    /// <summary>
    /// Igaz, ha ez a sor egy cserélhető/optikai meghajtó, ami biztonságosan
    /// kiadható — ilyenkor jelenik meg a sor végén a Kiadás ikon.
    /// Rendszermeghajtónál (fix lemeznél) sosem igaz.
    /// </summary>
    public bool CanEject => Drive is { } drive && RemovableDriveService.IsEjectable(drive.DriveType);

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

    /// <summary>Igaz a Gyorselérés rögzített mappáira — ekkor jelenik meg a leoldó „x" gomb.</summary>
    public bool IsUnpinnable { get; init; }

    /// <summary>
    /// Igaz a Lomtár sorára — kattintáskor ilyenkor nem navigálás történik
    /// (nincs is valódi <see cref="Path"/>-ja), hanem a Lomtár-ablak nyílik
    /// meg. Lásd <c>MainWindow.OnSidebarSelectionChanged</c>.
    /// </summary>
    public bool IsRecycleBin { get; init; }

    /// <summary>Igaz a Kezdőlap sorára — a Kezdőlap-panel nem mutat magára mutató csempét.</summary>
    public bool IsHomeEntry { get; init; }

    /// <summary>
    /// Igaz a Felhő meghajtók szekció soraira — ekkor az <see cref="EntryId"/>
    /// a <c>CloudDriveService</c> bejegyzés-azonosítója, és a jobbklikk-menü
    /// eltávolítást (nem gyorselérés-szerkesztést) kínál.
    /// </summary>
    public bool IsCloudDrive { get; init; }

    /// <summary>Igaz, ha ez a sor csempeként megjeleníthető a Kezdőlap-panelen (nem a Lomtár és nem maga a Kezdőlap).</summary>
    public bool IsDashboardTile => !IsRecycleBin && !IsHomeEntry;

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

    /// <summary>
    /// Igaz, ha a szekció akkor is megjelenik, ha üres — pl. a Felhő
    /// meghajtóknál, ahol a fejléc jobbklikkje az EGYETLEN belépési pont az
    /// első bejegyzés hozzáadásához (nincs másik felület, ami elrejtve
    /// tartaná ezt a lehetőséget elérhetetlenné, ha a lista üres).
    /// </summary>
    public bool AlwaysVisible { get; init; }

    /// <summary>Igaz, ha a szekciónak van tartalma, VAGY mindig látszania kell — ez vezérli a fejléc és a lista láthatóságát.</summary>
    public bool IsVisible => AlwaysVisible || Items.Count > 0;

    /// <summary>Igaz, ha a szekció sorai láthatók — a fejlécre kattintva csukható/nyitható.</summary>
    [ObservableProperty]
    public partial bool IsExpanded { get; set; } = true;

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;

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

    /// <summary>A címke egyedi színe, ha van — lásd <see cref="TagDefinition.ColorHex"/>.</summary>
    public string? ColorHex { get; init; }

    /// <summary>Igaz, ha ez a szűrő aktív az éppen kijelölt fülön.</summary>
    [ObservableProperty]
    public partial bool IsActive { get; set; }
}
