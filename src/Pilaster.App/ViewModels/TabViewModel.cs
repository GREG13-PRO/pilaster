using System.Collections.ObjectModel;

// A WPF projektek implicit using-készlete nem tartalmazza a System.IO-t,
// ezért itt kifejezetten be kell húzni.
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pilaster.App.Localization;
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

    private readonly IFileSystemProvider _provider;
    private CancellationTokenSource? _loadCancellation;

    public TabViewModel(IFileSystemProvider provider)
    {
        _provider = provider;
        Items = [];
        Breadcrumbs = [];
        History = new NavigationHistory();
        Title = TranslationSource.Instance["Nav_Home"];
    }

    /// <summary>Az aktuális mappa tartalma.</summary>
    public RangeObservableCollection<FileSystemItem> Items { get; }

    /// <summary>Az útvonalsáv szegmensei.</summary>
    public ObservableCollection<BreadcrumbSegment> Breadcrumbs { get; }

    /// <summary>A fül vissza/előre előzménye.</summary>
    public NavigationHistory History { get; }

    [ObservableProperty]
    public partial string? CurrentPath { get; set; }

    /// <summary>A fülfeliraton megjelenő név.</summary>
    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial ViewMode ViewMode { get; set; } = ViewMode.Details;

    /// <summary>Hibaüzenet vagy üres-mappa jelzés; <c>null</c>, ha minden rendben.</summary>
    [ObservableProperty]
    public partial string? EmptyMessage { get; set; }

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
        }).ConfigureAwait(false);
    }

    private List<FileSystemItem> SortItems(List<FileSystemItem> items)
    {
        items.Sort(new FileSystemItemComparer(SortKey, SortDescending));
        return items;
    }

    private void ResortInPlace()
    {
        if (IsLoading || Items.Count == 0)
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
