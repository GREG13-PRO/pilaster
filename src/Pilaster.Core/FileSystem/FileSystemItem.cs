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

    /// <summary>Méret bájtban. Mappáknál -1, amíg ki nem számoltuk.</summary>
    public long SizeBytes { get; init; } = -1;

    public DateTime CreatedUtc { get; init; }
    public DateTime ModifiedUtc { get; init; }
    public DateTime AccessedUtc { get; init; }

    public FileAttributes Attributes { get; init; }

    /// <summary>Igaz, ha rejtett vagy rendszerelem — a nézet halványabban rajzolja.</summary>
    public bool IsHidden =>
        Attributes.HasFlag(FileAttributes.Hidden) || Attributes.HasFlag(FileAttributes.System);

    /// <summary>Igaz, ha az elembe be lehet navigálni.</summary>
    public bool IsNavigable => Kind is FileSystemItemKind.Directory
        or FileSystemItemKind.Drive
        or FileSystemItemKind.Virtual;

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

    public override string ToString() => FullPath;
}
