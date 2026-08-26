using CommunityToolkit.Mvvm.ComponentModel;
using Pilaster.Core.Metadata;

namespace Pilaster.Core.FileSystem;

/// <summary>
/// Egyetlen fájlrendszer-elem — fájl, mappa, meghajtó vagy virtuális elem.
/// </summary>
/// <remarks>
/// Ez a típus a listanézetek elemi egysége, ezért kritikus, hogy olcsó legyen
/// létrehozni: egy 200 000 fájlos mappa megnyitásakor ennyi példány készül.
/// Ezért a drága mezők (ikon, bélyegkép, mappaméret) NEM a konstruktorban
/// töltődnek, hanem később, háttérszálról íródnak be — innen az
/// <see cref="ObservableObject"/> ős, ami értesíti a köréjük épült bindingeket.
/// </remarks>
public sealed partial class FileSystemItem : ObservableObject
{
    /// <summary>
    /// Teljes útvonal. Virtuális elemeknél séma-előtagot is tartalmazhat.
    /// Sikeres átnevezés után frissül — lásd <c>TabViewModel.CommitRenameAsync</c>.
    /// </summary>
    [ObservableProperty]
    public required partial string FullPath { get; set; }

    /// <summary>
    /// A megjelenítendő név (fájlnév kiterjesztéssel, vagy meghajtó-címke).
    /// Sikeres átnevezés után frissül.
    /// </summary>
    [ObservableProperty]
    public required partial string Name { get; set; }

    /// <summary>Az elem alaptípusa.</summary>
    public required FileSystemItemKind Kind { get; init; }

    /// <summary>Kiterjesztés pont nélkül, kisbetűsítve. Mappáknál üres. Átnevezés után frissül.</summary>
    [ObservableProperty]
    public partial string Extension { get; set; } = string.Empty;

    /// <summary>
    /// A LISTÁBAN megjelenő név — a „Kiterjesztések megjelenítése" beállítástól
    /// függően a teljes név, vagy a kiterjesztés nélküli része.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Szándékosan külön, megfigyelhető tulajdonság, nem konverter: a beállítás
    /// globális és ritkán változik, viszont a nézetnek AZONNAL követnie kell —
    /// egy konverter a kötés forrásának változása nélkül nem futna le újra. Így
    /// a beállítás átbillentésekor elég egyszer végigmenni a betöltött
    /// elemeken (lásd <c>TabViewModel.RefreshDisplayNames</c>).
    /// </para>
    /// <para>
    /// FONTOS: az ÁTNEVEZÉS továbbra is a teljes <see cref="Name"/>-en dolgozik
    /// (lásd <c>EditableName</c>), különben a kiterjesztés elveszne, amint a
    /// felhasználó elmenti a rövidített nevet.
    /// </para>
    /// </remarks>
    [ObservableProperty]
    public partial string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// A <see cref="DisplayName"/> újraszámolása.
    /// </summary>
    /// <param name="showExtensions">A „Kiterjesztések megjelenítése" beállítás.</param>
    public void RefreshDisplayName(bool showExtensions)
    {
        // Mappánál és kiterjesztés nélküli fájlnál nincs mit levágni; a
        // ponttal KEZDŐDŐ nevek (.gitignore) is teljes egészében maradnak,
        // mert ott a „kiterjesztés" valójában maga a név.
        DisplayName = showExtensions
            || Kind != FileSystemItemKind.File
            || Extension.Length == 0
            || Name.LastIndexOf('.') <= 0
                ? Name
                : Name[..Name.LastIndexOf('.')];
    }

    /// <summary>Méret bájtban. Mappáknál -1, amíg ki nem számoltuk.</summary>
    public long SizeBytes { get; init; } = -1;

    public DateTime CreatedUtc { get; init; }
    public DateTime ModifiedUtc { get; init; }
    public DateTime AccessedUtc { get; init; }

    public FileAttributes Attributes { get; init; }

    /// <summary>Igaz, ha rejtett vagy rendszerelem — a nézet halványabban rajzolja.</summary>
    public bool IsHidden =>
        Attributes.HasFlag(FileAttributes.Hidden) || Attributes.HasFlag(FileAttributes.System);

    /// <summary>
    /// Igaz, ha az elembe be lehet navigálni. Lomtár-elemeknél MINDIG hamis,
    /// akkor is, ha eredetileg mappa volt — a Lomtárban lévő mappa tartalma
    /// nem böngészhető, csak visszaállítható vagy véglegesen törölhető.
    /// </summary>
    public bool IsNavigable => !IsRecycled && Kind is FileSystemItemKind.Directory
        or FileSystemItemKind.Drive
        or FileSystemItemKind.Virtual;

    /// <summary>Igaz, ha ez az elem a Lomtárból jön — lásd <see cref="SourceTag"/>.</summary>
    public bool IsRecycled => SourceTag is not null;

    /// <summary>
    /// A shell ikon, illetve bélyegkép. Késleltetve töltjük, ezért megfigyelhető —
    /// a nézet előbb kirajzol egy helyőrzőt, majd frissül, amint megjön a kép.
    /// </summary>
    [ObservableProperty]
    public partial object? Icon { get; set; }

    /// <summary>
    /// Mappák háttérben kiszámolt mérete. -1 = még nem számoltuk.
    /// Megfigyelhető, mert a számítás hosszú és menet közben frissül.
    /// </summary>
    [ObservableProperty]
    public partial long ComputedFolderSize { get; set; } = -1;

    /// <summary>
    /// A ráhelyezett címkék — a mappabetöltés utáni feldúsítás tölti ki
    /// (lásd <c>TabViewModel.EnrichWithMetadata</c>), és a
    /// <c>FileMetadataService.Changed</c> eseményre frissül.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<TagDefinition> Tags { get; set; } = [];

    /// <summary>Igaz, ha az elem kedvencként meg van jelölve.</summary>
    [ObservableProperty]
    public partial bool IsFavorite { get; set; }

    /// <summary>
    /// Igaz, amíg a sor helyben szerkeszthető névmezőt mutat név helyett —
    /// új elem létrehozásakor azonnal, vagy kézi átnevezéskor. Lásd
    /// <c>TabViewModel.BeginRename</c>/<c>CommitRenameAsync</c>.
    /// </summary>
    [ObservableProperty]
    public partial bool IsRenaming { get; set; }

    /// <summary>A szerkeszthető névmező tartalma, amíg <see cref="IsRenaming"/> igaz.</summary>
    [ObservableProperty]
    public partial string EditableName { get; set; } = string.Empty;

    /// <summary>Sikertelen átnevezés oka, vagy <c>null</c>. Csak <see cref="IsRenaming"/> alatt jelenik meg.</summary>
    [ObservableProperty]
    public partial string? RenameError { get; set; }

    public bool HasRenameError => RenameError is not null;

    partial void OnRenameErrorChanged(string? value) => OnPropertyChanged(nameof(HasRenameError));

    /// <summary>
    /// Lomtár-elemeknél az eredeti szülőmappa (ahova visszaállításkor kerül)
    /// — máskülönben <c>null</c>. Lásd <see cref="SourceTag"/>.
    /// </summary>
    public string? OriginalFolder { get; init; }

    /// <summary>
    /// A mögöttes, réteg-specifikus modell type-erased hordozója — pl. egy
    /// Lomtár-sornál a <c>Pilaster.Shell.Recycle.RecycledItem</c>. A Core
    /// réteg szándékosan nem ismeri ezt a típust (a Shell ERRE a rétegre
    /// épül, nem fordítva), ezért csak <see cref="object"/>-ként utazik —
    /// az App réteg (ami mindkettőt látja) castolja vissza, amikor egy
    /// Lomtár-sor Visszaállítás/Végleges törlés parancsát végrehajtja.
    /// Lásd <c>TabViewModel.LoadRecycleBinAsync</c>.
    /// </summary>
    public object? SourceTag { get; init; }

    public override string ToString() => FullPath;
}
