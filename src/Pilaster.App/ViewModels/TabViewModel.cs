using System.Collections.ObjectModel;
using System.ComponentModel;

// A WPF projektek implicit using-készlete nem tartalmazza a System.IO-t,
// ezért itt kifejezetten be kell húzni.
using System.IO;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pilaster.App.Localization;
using Pilaster.App.Services;
using Pilaster.Core.Collections;
using Pilaster.Core.FileSystem;
using Pilaster.Core.Formatting;
using Pilaster.Core.Navigation;

namespace Pilaster.App.ViewModels;

/// <summary>
/// Egyetlen fül állapota: hol jár, mit mutat, és hogyan rendezi.
/// </summary>
public sealed partial class TabViewModel : ObservableObject
{
    /// <summary>
    /// Az első adag mérete. Szándékosan kicsi: a cél, hogy a felhasználó a
    /// lehető leghamarabb lásson tartalmat, még mielőtt a mappa végigolvasna.
    /// </summary>
    private const int FirstBatchSize = 200;

    /// <summary>
    /// A további adagok felső határa. Az adagméret adagonként négyszereződik,
    /// mert a betöltés elején a válaszkészség számít, a végén az átbocsátás.
    /// </summary>
    private const int MaxBatchSize = 20_000;

    /// <summary>
    /// A <see cref="CurrentPath"/> ezen az álnéven jelzi a virtuális
    /// Kezdőlap-nézetet — nincs mögötte valódi mappa, ezért a
    /// <see cref="LoadAsync"/> nem az <see cref="IFileSystemProvider"/>-t
    /// hívja meg rá, hanem <see cref="LoadHomeAsync"/>-et. Lásd
    /// <see cref="IsHome"/> és a nézet oldalán a Kezdőlap-panel.
    /// </summary>
    public const string HomeMarker = "pilaster:home";

    private readonly IFileSystemProvider _provider;
    private readonly FolderSizeService _folderSizes;
    private readonly FileMetadataService _metadata;
    private readonly EventHandler _metadataChangedHandler;
    private CancellationTokenSource? _loadCancellation;
    private bool _suppressResort;
    private int _pathCopiedGeneration;

    public TabViewModel(IFileSystemProvider provider, FolderSizeService folderSizes, FileMetadataService metadata)
    {
        _provider = provider;
        _folderSizes = folderSizes;
        _metadata = metadata;
        Items = [];
        Breadcrumbs = [];
        History = new NavigationHistory();
        Title = TranslationSource.Instance["Nav_Home"];

        // Címke/kedvenc bárhonnan változhat (más fül, oldalsáv, jobbklikk) —
        // az ITT látható elemeket ilyenkor frissen kell tartani. A kezelőt
        // névvel tároljuk, hogy Detach()-ben leiratkozhassunk — enélkül egy
        // bezárt fül vagy eldobott oszlop örökre bent ragadna a
        // FileMetadataService feliratkozói közt.
        _metadataChangedHandler = (_, _) => RefreshMetadataOnItems();
        _metadata.Changed += _metadataChangedHandler;
    }

    /// <summary>
    /// Leiratkozás a <see cref="FileMetadataService.Changed"/> eseményről —
    /// fül bezárásakor és oszlop eldobásakor kell hívni, különben a
    /// singleton szolgáltatás örökre él tartaná a már elhagyott példányt.
    /// </summary>
    public void Detach() => _metadata.Changed -= _metadataChangedHandler;

    /// <summary>Az aktuálisan betöltött elemek címkéinek/kedvenc-jelölésének frissítése — metaadat-változás után.</summary>
    private void RefreshMetadataOnItems()
    {
        foreach (var item in Items)
        {
            item.Tags = _metadata.GetTags(item.FullPath);
            item.IsFavorite = _metadata.IsFavorite(item.FullPath);
        }
    }

    /// <summary>Az aktuális mappa tartalma.</summary>
    public RangeObservableCollection<FileSystemItem> Items { get; }

    /// <summary>Az útvonalsáv szegmensei.</summary>
    public ObservableCollection<BreadcrumbSegment> Breadcrumbs { get; }

    /// <summary>Igaz, amíg a breadcrumb helyén a szerkeszthető útvonal-szövegmező látszik.</summary>
    [ObservableProperty]
    public partial bool IsEditingPath { get; set; }

    /// <summary>A szerkeszthető útvonal-szövegmező tartalma, amíg <see cref="IsEditingPath"/> igaz.</summary>
    [ObservableProperty]
    public partial string EditablePathText { get; set; } = string.Empty;

    /// <summary>Rövid ideig igaz az „Útvonal másolása" gomb után, „Másolva" visszajelzésként.</summary>
    [ObservableProperty]
    public partial bool PathCopied { get; set; }

    /// <summary>Breadcrumb-ra kattintva a jelenlegi útvonallal előtöltve szerkeszthetővé vált a sáv — mint az Intézőben.</summary>
    [RelayCommand]
    private void BeginEditPath()
    {
        EditablePathText = CurrentPath ?? string.Empty;
        IsEditingPath = true;
    }

    /// <summary>Esc vagy fókuszvesztés: vissza a breadcrumb nézetre, a beírt szöveg elvész.</summary>
    [RelayCommand]
    private void CancelEditPath() => IsEditingPath = false;

    /// <summary>
    /// Enter a szerkeszthető útvonal-mezőben: navigálás a beírt/beillesztett
    /// útra. Érvénytelen útnál a meglévő <see cref="LoadAsync"/> hibakezelése
    /// (lásd az ott bővített catch-ágakat) ad felhasználóbarát üzenetet —
    /// innen sosem száll ki kivétel.
    /// </summary>
    [RelayCommand]
    private async Task CommitEditPathAsync()
    {
        var target = EditablePathText.Trim();

        IsEditingPath = false;

        if (target.Length == 0)
        {
            return;
        }

        await NavigateAsync(target).ConfigureAwait(false);
    }

    /// <summary>Az aktuális útvonal vágólapra másolása, rövid „Másolva" visszajelzéssel.</summary>
    [RelayCommand]
    private void CopyPath()
    {
        if (CurrentPath is not { } path)
        {
            return;
        }

        try
        {
            Clipboard.SetText(path);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // A vágólapot időnként egy másik folyamat zárolja — nincs jobb
            // teendő, mint csendben kihagyni, mintsem hibaüzenettel zavarni.
            return;
        }

        var generation = ++_pathCopiedGeneration;
        PathCopied = true;
        _ = ClearPathCopiedAsync(generation);
    }

    private async Task ClearPathCopiedAsync(int generation)
    {
        await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);

        if (generation == _pathCopiedGeneration)
        {
            await OnUiAsync(() => PathCopied = false).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Az aktív címkeszűrő azonosítója, vagy <c>null</c>, ha nincs szűrés —
    /// az oldalsáv Címkék szekciójában egy címkére kattintva állítódik.
    /// </summary>
    [ObservableProperty]
    public partial string? ActiveTagFilterId { get; set; }

    partial void OnActiveTagFilterIdChanged(string? value)
    {
        var view = CollectionViewSource.GetDefaultView(Items);

        view.Filter = value is null
            ? null
            : candidate => candidate is FileSystemItem item && item.Tags.Any(t => t.Id == value);
    }

    /// <summary>Kedvenc jelölés váltása — a szív ikon és a jobbklikk-menü közös belépési pontja.</summary>
    [RelayCommand]
    private void ToggleFavorite(FileSystemItem? item)
    {
        if (item is null)
        {
            return;
        }

        _metadata.ToggleFavorite(item.FullPath);
        item.IsFavorite = _metadata.IsFavorite(item.FullPath);
    }

    /// <summary>
    /// Akkor jelez, amikor egy elem szerkeszthető névmezőre vált — a nézet
    /// erre kijelöli a sort, láthatóvá görgeti, és fókuszba viszi a mezőt.
    /// Lásd <c>MainWindow.TrackTab</c>/<c>OnTrackedTabRenameRequested</c>.
    /// </summary>
    public event EventHandler<FileSystemItem>? RenameRequested;

    /// <summary>
    /// Átnevezés-mód indítása egy elemen — új elem létrehozása után azonnal,
    /// vagy kézi átnevezéskor. Egyszerre csak egy elem lehet szerkesztés alatt.
    /// </summary>
    public void BeginRename(FileSystemItem item)
    {
        foreach (var other in Items)
        {
            if (other.IsRenaming)
            {
                other.IsRenaming = false;
                other.RenameError = null;
            }
        }

        item.EditableName = item.Name;
        item.RenameError = null;
        item.IsRenaming = true;

        RenameRequested?.Invoke(this, item);
    }

    /// <summary>Esc: vissza a névre, a beírt szöveg elvész.</summary>
    [RelayCommand]
    private void CancelRename(FileSystemItem? item)
    {
        if (item is null)
        {
            return;
        }

        item.IsRenaming = false;
        item.RenameError = null;
    }

    /// <summary>
    /// Enter, vagy fókuszvesztés a névmezőn: átnevezés a beírt névre.
    /// Érvénytelen vagy ütköző névnél a mező NYITVA marad, hibaüzenettel —
    /// mint az Intézőben —, hogy a felhasználó rögtön javíthasson.
    /// </summary>
    [RelayCommand]
    private async Task CommitRenameAsync(FileSystemItem? item)
    {
        if (item is null)
        {
            return;
        }

        var newName = item.EditableName.Trim();

        if (newName.Length == 0 || newName == item.Name)
        {
            item.IsRenaming = false;
            item.RenameError = null;
            return;
        }

        if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            item.RenameError = TranslationSource.Instance["Rename_InvalidName"];
            return;
        }

        try
        {
            var newPath = await _provider.RenameAsync(item.FullPath, newName).ConfigureAwait(false);

            await OnUiAsync(() =>
            {
                item.FullPath = newPath;
                item.Name = newName;
                item.Extension = item.Kind == FileSystemItemKind.Directory
                    ? string.Empty
                    : GetExtensionLowerInvariant(newName);
                item.IsRenaming = false;
                item.RenameError = null;
            }).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await OnUiAsync(() =>
                item.RenameError = ex is UnauthorizedAccessException
                    ? TranslationSource.Instance["Error_AccessDenied"]
                    : TranslationSource.Instance["Rename_NameTaken"]).ConfigureAwait(false);
        }
    }

    private static string GetExtensionLowerInvariant(string name)
    {
        var dot = name.LastIndexOf('.');

        return dot <= 0 || dot == name.Length - 1
            ? string.Empty
            : name[(dot + 1)..].ToLowerInvariant();
    }

    /// <summary>A fül vissza/előre előzménye.</summary>
    public NavigationHistory History { get; }

    /// <summary>
    /// Az oszlopos nézet aktuálisan nyitott oszlopai — mindegyik egy-egy
    /// önálló <see cref="TabViewModel"/>, amely a saját mappájának
    /// tartalmát listázza. Csak <see cref="ViewMode.Columns"/> módban van
    /// tartalma; lásd <see cref="ResetColumns"/> és <see cref="SelectColumnItemAsync"/>.
    /// </summary>
    /// <remarks>
    /// Ugyanazt a <see cref="TabViewModel"/>-et használja oszloponként, mint
    /// amit a fülek — ugyanaz a betöltés/rendezés/mappaméret-logika kell
    /// mindkettőhöz, nincs értelme duplikálni. Az oszlop-<see cref="TabViewModel"/>-ek
    /// <see cref="ViewMode"/>-ja szándékosan marad <see cref="ViewMode.Details"/>
    /// (az alapérték): ha Columns lenne, az <see cref="OnCurrentPathChanged"/>
    /// végtelenül egymásba ágyazott oszlopfát próbálna építeni.
    /// </remarks>
    public ObservableCollection<TabViewModel> Columns { get; } = [];

    /// <summary>
    /// Az oszlopos nézetben kijelölt fájl (nem navigálható elem) — ekkor a
    /// jobb oldali részletek panel ezt mutatja üres/új oszlop helyett.
    /// </summary>
    [ObservableProperty]
    public partial FileSystemItem? ColumnsSelectedFile { get; set; }

    [ObservableProperty]
    public partial string? CurrentPath { get; set; }

    /// <summary>
    /// Igaz, amíg a fül a virtuális Kezdőlap-nézetet mutatja („Ez a gép"
    /// stílusban: gyorselérés-mappák + meghajtók) a normál fájllista helyett.
    /// </summary>
    public bool IsHome => CurrentPath == HomeMarker;

    partial void OnCurrentPathChanged(string? value)
    {
        OnPropertyChanged(nameof(IsHome));

        // Csak akkor kell újraépíteni az oszlopokat, ha ez a fül maga az
        // oszlopos nézetet mutató "gyökér" — egy oszlop saját CurrentPath-
        // változása (amikor belé navigálunk) nem indíthat saját oszlopfát.
        // Kezdőlap-nézetben nincs értelme oszlopokat építeni.
        if (ViewMode == ViewMode.Columns && !IsHome)
        {
            ResetColumns();
        }
    }

    partial void OnViewModeChanged(ViewMode value)
    {
        OnPropertyChanged(nameof(ShowFlatEmptyMessage));
        OnPropertyChanged(nameof(ShowFlatLoading));

        if (value == ViewMode.Columns && !IsHome)
        {
            ResetColumns();
        }
    }

    /// <summary>
    /// A virtuális Kezdőlap-nézet betöltése — nincs valódi fájlrendszer-
    /// elérés, csak a fül állapotát állítja „Kezdőlap" módra. Maga a tartalom
    /// (gyorselérés-mappák, meghajtók) a <c>MainWindowViewModel.Sections</c>-ből
    /// jön a nézet oldalán, hogy ne kelljen duplikálni azt a logikát.
    /// </summary>
    private async Task LoadHomeAsync()
    {
        var previous = _loadCancellation;
        _loadCancellation = null;

        if (previous is not null)
        {
            await previous.CancelAsync().ConfigureAwait(false);
            previous.Dispose();
        }

        await OnUiAsync(() =>
        {
            CurrentPath = HomeMarker;
            Title = TranslationSource.Instance["Nav_Home"];
            IsLoading = false;
            EmptyMessage = null;
            Items.Clear();
            Breadcrumbs.Clear();
            Breadcrumbs.Add(new BreadcrumbSegment(TranslationSource.Instance["Nav_Home"], HomeMarker));
            RaiseNavigationState();
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Az oszlopok visszaállítása egyetlen, a fül aktuális mappáját mutató
    /// oszlopra — nézetváltáskor, vagy ha a fül más úton (breadcrumb,
    /// oldalsáv, vissza/előre) navigál, amíg oszlopos nézetben van.
    /// </summary>
    private void ResetColumns()
    {
        foreach (var column in Columns)
        {
            column.Detach();
        }

        Columns.Clear();
        ColumnsSelectedFile = null;

        if (CurrentPath is not { } path)
        {
            return;
        }

        var root = new TabViewModel(_provider, _folderSizes, _metadata);
        Columns.Add(root);
        _ = root.NavigateAsync(path);
    }

    /// <summary>
    /// Egy elem kijelölése egy oszlopban: navigálható elemnél új oszlop
    /// nyílik jobbra a tartalmával, fájlnál a részletek panel jelenik meg.
    /// Az adott oszlop utáni, korábban nyitott oszlopok bezáródnak — ahogy
    /// a Finderben is, ha egy korábbi oszlopban más elemre kattintasz.
    /// </summary>
    public async Task SelectColumnItemAsync(TabViewModel column, FileSystemItem item)
    {
        var columnIndex = Columns.IndexOf(column);

        if (columnIndex < 0)
        {
            return;
        }

        while (Columns.Count > columnIndex + 1)
        {
            var discarded = Columns[^1];
            Columns.RemoveAt(Columns.Count - 1);
            discarded.Detach();
        }

        if (item.IsNavigable)
        {
            ColumnsSelectedFile = null;

            var next = new TabViewModel(_provider, _folderSizes, _metadata);
            Columns.Add(next);
            await next.NavigateAsync(item.FullPath).ConfigureAwait(false);
        }
        else
        {
            ColumnsSelectedFile = item;
        }
    }

    /// <summary>A fülfeliraton megjelenő név.</summary>
    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    /// <summary>Igaz, ha a betöltésjelzőt a lapos nézetben kell mutatni — lásd <see cref="ShowFlatEmptyMessage"/>.</summary>
    public bool ShowFlatLoading => IsLoading && ViewMode != ViewMode.Columns;

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(ShowFlatLoading));

    [ObservableProperty]
    public partial ViewMode ViewMode { get; set; } = ViewMode.Details;

    /// <summary>Hibaüzenet vagy üres-mappa jelzés; <c>null</c>, ha minden rendben.</summary>
    [ObservableProperty]
    public partial string? EmptyMessage { get; set; }

    /// <summary>
    /// Igaz, ha az EmptyMessage-et a lapos (Részletek/Rács) nézet szövegként
    /// meg is jelenítendő üzenetként kell mutatni.
    /// </summary>
    /// <remarks>
    /// Oszlopos nézetben ez a fül maga is lefuttatja a saját betöltését (a
    /// gyökérmappa tartalmát <see cref="Columns"/>[0] mutatja), tehát az
    /// EmptyMessage itt is beállítódna — de a felületen már az adott oszlop
    /// SAJÁT üres/hiba-üzenete jelenik meg. E nélkül a megkülönböztetés
    /// nélkül a lapos nézet felirata átfedésben, „lebegve" jelenne meg az
    /// oszlopok fölött.
    /// </remarks>
    public bool ShowFlatEmptyMessage => EmptyMessage is not null && ViewMode != ViewMode.Columns;

    partial void OnEmptyMessageChanged(string? value) => OnPropertyChanged(nameof(ShowFlatEmptyMessage));

    /// <summary>Az állapotsor bal oldali szövege.</summary>
    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool ShowHiddenItems { get; set; }

    [ObservableProperty]
    public partial SortKey SortKey { get; set; } = SortKey.Name;

    [ObservableProperty]
    public partial bool SortDescending { get; set; }

    public bool CanGoBack => History.CanGoBack;

    public bool CanGoForward => History.CanGoForward;

    public bool CanGoUp => CurrentPath is not null && _provider.GetParentPath(CurrentPath) is not null;

    [RelayCommand]
    public async Task NavigateAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        History.Navigate(path);
        await LoadAsync(path).ConfigureAwait(false);
    }

    [RelayCommand]
    private async Task GoBackAsync()
    {
        if (History.GoBack() is { } path)
        {
            await LoadAsync(path).ConfigureAwait(false);
        }
    }

    [RelayCommand]
    private async Task GoForwardAsync()
    {
        if (History.GoForward() is { } path)
        {
            await LoadAsync(path).ConfigureAwait(false);
        }
    }

    [RelayCommand]
    private async Task GoUpAsync()
    {
        if (CurrentPath is null)
        {
            return;
        }

        if (_provider.GetParentPath(CurrentPath) is { } parent && parent.Length > 0)
        {
            await NavigateAsync(parent).ConfigureAwait(false);
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (CurrentPath is { } path)
        {
            await LoadAsync(path).ConfigureAwait(false);
        }
    }

    partial void OnShowHiddenItemsChanged(bool value) => _ = RefreshAsync();

    partial void OnSortKeyChanged(SortKey value) => ResortInPlace();

    partial void OnSortDescendingChanged(bool value) => ResortInPlace();

    /// <summary>
    /// Rendezési szempont és irány beállítása egy lépésben.
    /// </summary>
    /// <remarks>
    /// A két tulajdonság külön-külön is kiváltaná az újrarendezést, ami
    /// oszlopfejléc-kattintásnál azt jelentené, hogy a lista kétszer rendeződik
    /// át — egyszer még a régi iránnyal. A zárolás ezt fogja össze egyetlen
    /// rendezéssé.
    /// </remarks>
    public void ApplySort(SortKey key, bool descending)
    {
        _suppressResort = true;

        try
        {
            SortKey = key;
            SortDescending = descending;
        }
        finally
        {
            _suppressResort = false;
        }

        ResortInPlace();
    }

    /// <summary>
    /// Egy mappa betöltése.
    /// </summary>
    /// <remarks>
    /// A megjelenítés adagokban történik, és a rendezés csak a végén fut le.
    /// Ennek az az oka, hogy az NTFS a könyvtárbejegyzéseket név szerinti
    /// B-fában tárolja, tehát a beérkező sorrend már majdnem ábécérendes —
    /// a felhasználó gyakorlatilag kész listát lát az első pillanattól, a
    /// záró rendezés pedig csak finomít rajta.
    /// </remarks>
    private async Task LoadAsync(string path)
    {
        if (path == HomeMarker)
        {
            await LoadHomeAsync().ConfigureAwait(false);
            return;
        }

        // Gyors mappaváltogatásnál a korábbi betöltés feleslegessé válik.
        var previous = _loadCancellation;
        var cancellation = new CancellationTokenSource();
        _loadCancellation = cancellation;

        if (previous is not null)
        {
            await previous.CancelAsync().ConfigureAwait(false);
            previous.Dispose();
        }

        var token = cancellation.Token;

        await OnUiAsync(() =>
        {
            CurrentPath = path;
            Title = BuildTitle(path);
            IsLoading = true;
            EmptyMessage = null;
            StatusText = TranslationSource.Instance["Status_Loading"];
            Items.Clear();
            RebuildBreadcrumbs(path);
            RaiseNavigationState();
        }).ConfigureAwait(false);

        var collected = new List<FileSystemItem>();
        var buffer = new List<FileSystemItem>(FirstBatchSize);
        var batchSize = FirstBatchSize;
        string? failure = null;

        try
        {
            var options = new ListingOptions(ShowHiddenItems, ShowHiddenItems);

            await foreach (var item in _provider
                .EnumerateAsync(path, options, token)
                .ConfigureAwait(false))
            {
                collected.Add(item);
                buffer.Add(item);

                if (buffer.Count < batchSize)
                {
                    continue;
                }

                var flush = buffer;
                buffer = new List<FileSystemItem>(batchSize);
                await OnUiAsync(() => Items.AddRange(flush)).ConfigureAwait(false);

                batchSize = Math.Min(batchSize * 4, MaxBatchSize);
            }
        }
        catch (OperationCanceledException)
        {
            // Új navigáció előzte meg — nincs teendő, a friss betöltés átveszi.
            return;
        }
        catch (UnauthorizedAccessException)
        {
            failure = TranslationSource.Instance["Folder_AccessDenied"];
        }
        catch (DirectoryNotFoundException)
        {
            failure = TranslationSource.Instance["Folder_NotFound"];
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            // Eddig minden útvonal már ellenőrzött forrásból jött (breadcrumb,
            // oldalsáv, Vissza/Előre) — a szerkeszthető útvonalsáv (lásd
            // CommitEditPathAsync) viszont szabad szöveget enged be, ami
            // ilyen kivételeket dobhat egy érvénytelen elérési útnál.
            failure = TranslationSource.Instance["Folder_InvalidPath"];
        }
        catch (IOException ex)
        {
            failure = ex.Message;
        }

        if (token.IsCancellationRequested)
        {
            return;
        }

        // A rendezés 200 000 elemnél is milliszekundumos nagyságrendű, de a
        // UI-szálon még ez is látható akadás lenne.
        var sorted = await Task
            .Run(() => SortItems(collected), CancellationToken.None)
            .ConfigureAwait(false);

        if (token.IsCancellationRequested)
        {
            return;
        }

        await OnUiAsync(() =>
        {
            Items.Reset(sorted);
            IsLoading = false;
            EmptyMessage = failure ?? (sorted.Count == 0
                ? TranslationSource.Instance["Folder_Empty"]
                : null);
            UpdateStatus(0, 0);
            RefreshMetadataOnItems();
        }).ConfigureAwait(false);

        // Mappánként háttérben induló, korlátozott párhuzamosságú számítás —
        // lásd FolderSizeService. A token elnavigáláskor megszakítja a még
        // futó, immár érdektelen számításokat.
        foreach (var item in sorted)
        {
            _folderSizes.EnsureComputed(item, token);
        }
    }

    private List<FileSystemItem> SortItems(List<FileSystemItem> items)
    {
        items.Sort(new FileSystemItemComparer(SortKey, SortDescending));
        return items;
    }

    private void ResortInPlace()
    {
        if (_suppressResort || IsLoading || Items.Count == 0)
        {
            return;
        }

        var snapshot = Items.ToList();
        snapshot.Sort(new FileSystemItemComparer(SortKey, SortDescending));
        Items.Reset(snapshot);
    }

    /// <summary>Az állapotsor frissítése a kijelölés alapján.</summary>
    public void UpdateStatus(int selectedCount, long selectedBytes)
    {
        var strings = TranslationSource.Instance;

        StatusText = selectedCount switch
        {
            0 => string.Format(strings["Status_Items"], Items.Count),
            _ => string.Format(
                strings["Status_SelectedSize"],
                selectedCount,
                ByteSize.Format(selectedBytes)),
        };
    }

    private void RebuildBreadcrumbs(string path)
    {
        Breadcrumbs.Clear();

        var root = Path.GetPathRoot(path);

        if (string.IsNullOrEmpty(root))
        {
            Breadcrumbs.Add(new BreadcrumbSegment(path, path));
            return;
        }

        Breadcrumbs.Add(new BreadcrumbSegment(root.TrimEnd(Path.DirectorySeparatorChar), root));

        var remainder = path[root.Length..].Trim(Path.DirectorySeparatorChar);

        if (remainder.Length == 0)
        {
            return;
        }

        var accumulated = root;

        foreach (var segment in remainder.Split(
            Path.DirectorySeparatorChar,
            StringSplitOptions.RemoveEmptyEntries))
        {
            accumulated = Path.Combine(accumulated, segment);
            Breadcrumbs.Add(new BreadcrumbSegment(segment, accumulated));
        }
    }

    private static string BuildTitle(string path)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        return string.IsNullOrEmpty(name) ? path : name;
    }

    private void RaiseNavigationState()
    {
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        OnPropertyChanged(nameof(CanGoUp));
    }

    /// <summary>
    /// Művelet futtatása a UI-szálon. A betöltés háttérszálon fut, de a
    /// megfigyelt gyűjtemények módosítása csak a UI-szálról biztonságos.
    /// </summary>
    private static async Task OnUiAsync(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        await dispatcher.InvokeAsync(action);
    }
}
