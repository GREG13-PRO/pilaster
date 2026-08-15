using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Pilaster.App.Services;
using Pilaster.App.ViewModels;
using Pilaster.Core.FileSystem;

namespace Pilaster.App.Views;

/// <summary>
/// Egy önálló fájlpanel a kétpaneles nézethez — saját fülsáv, eszköztár,
/// útvonalsáv és fájllista, a <see cref="DataContext"/>-ként kapott
/// <see cref="PaneViewModel"/> vezérli.
/// </summary>
/// <remarks>
/// A v1.0-ban a panel már EGY TELJES PANELT képvisel (fülekkel együtt), nem
/// egyetlen fület — lásd <see cref="PaneViewModel"/>. A fülönkénti kijelölés
/// és görgetési pozíció itt, a nézetben mentődik és áll vissza, mert ezek
/// tisztán megjelenítési állapotok, amiket a modell nem tud kiszámolni.
/// </remarks>
public partial class FilePaneView : UserControl
{
    private Point? _dragStartPoint;

    /// <summary>A panel, amelynek eseményeit épp figyeljük — lásd <see cref="OnDataContextChanged"/>.</summary>
    private PaneViewModel? _trackedPane;

    /// <summary>Az aktuálisan megjelenített fül — a fülváltás ezen keresztül menti/állítja vissza a nézetállapotot.</summary>
    private TabViewModel? _trackedTab;

    /// <summary>
    /// Igaz, amíg a kijelölést PROGRAMBÓL állítjuk vissza — ilyenkor a
    /// <see cref="OnListSelectionChanged"/> nem írhatja vissza a modellbe,
    /// különben a részlegesen visszaállított állapot felülírná a mentettet.
    /// </summary>
    private bool _restoringSelection;

    public FilePaneView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>A panel bármelyik pontjára kattintva jelez — a szülő ezzel dönti el, melyik panel az „aktív".</summary>
    public event EventHandler? Activated;

    /// <summary>
    /// Fájlok a panelbe kerülnek (húzással a másik panelről/Intézőből, vagy
    /// beillesztéssel) — a tényleges másolást/áthelyezést a szülő végzi a
    /// <c>FileOperationEngine</c>-nel, ez a nézet maga nem ismeri a motort.
    /// </summary>
    public event EventHandler<(IReadOnlyList<string> Paths, string DestinationDir, PaneDropAction Action)>? FilesDropped;

    /// <summary>Törlés kérése a kijelölt elemekre — a szülő végzi a <c>FileOperationEngine</c>-nel.</summary>
    public event EventHandler<(IReadOnlyList<string> Paths, bool Permanent)>? DeleteRequested;

    /// <summary>A panel modellje.</summary>
    public PaneViewModel? Pane => DataContext as PaneViewModel;

    /// <summary>A panel aktív füle.</summary>
    public TabViewModel? Tab => Pane?.ActiveTab;

    /// <summary>
    /// A belső fájllista — a Pilaster Classic billentyűkiosztás (lásd
    /// <c>MainWindow.OnMainPreviewKeyDown</c>) ezen keresztül olvassa/
    /// módosítja a kijelölést és a billentyűzet-fókuszt.
    /// </summary>
    public ListView SelectionList => List;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_trackedPane is not null)
        {
            _trackedPane.PropertyChanged -= OnPanePropertyChanged;
        }

        _trackedPane = DataContext as PaneViewModel;

        if (_trackedPane is not null)
        {
            _trackedPane.PropertyChanged += OnPanePropertyChanged;
        }

        TrackTab(_trackedPane?.ActiveTab);
    }

    private void OnPanePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PaneViewModel.ActiveTab))
        {
            TrackTab(_trackedPane?.ActiveTab);
        }
    }

    /// <summary>
    /// Fülváltás: az elhagyott fül nézetállapota (kijelölés, kurzor,
    /// görgetés) mentődik, az új fülé pedig visszaáll.
    /// </summary>
    private void TrackTab(TabViewModel? tab)
    {
        if (_trackedTab is not null)
        {
            CaptureViewState(_trackedTab);
            _trackedTab.RenameRequested -= OnTabRenameRequested;
        }

        _trackedTab = tab;

        if (_trackedTab is not null)
        {
            _trackedTab.RenameRequested += OnTabRenameRequested;

            // Egy Background prioritású visszahívásra van szükség: a
            // fülváltás pillanatában az ItemsSource még a régi listát mutatja,
            // a konténerek pedig még nem generálódtak újra.
            _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, RestoreViewState);
        }
    }

    private void CaptureViewState(TabViewModel tab)
    {
        tab.SelectedPaths = [.. List.SelectedItems.Cast<FileSystemItem>().Select(i => i.FullPath)];
        tab.FocusedPath = (List.SelectedItem as FileSystemItem)?.FullPath;

        if (FindScrollViewer(List) is { } scroll)
        {
            tab.ScrollOffset = scroll.VerticalOffset;
        }
    }

    private void RestoreViewState()
    {
        if (_trackedTab is not { } tab)
        {
            return;
        }

        _restoringSelection = true;

        try
        {
            List.SelectedItems.Clear();

            if (tab.SelectedPaths.Count > 0)
            {
                var wanted = tab.SelectedPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var item in List.Items.OfType<FileSystemItem>().Where(i => wanted.Contains(i.FullPath)))
                {
                    List.SelectedItems.Add(item);
                }
            }
        }
        finally
        {
            _restoringSelection = false;
        }

        if (FindScrollViewer(List) is { } scroll && tab.ScrollOffset > 0)
        {
            scroll.ScrollToVerticalOffset(tab.ScrollOffset);
        }
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer viewer)
        {
            return viewer;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            if (FindScrollViewer(VisualTreeHelper.GetChild(root, i)) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private void OnListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_restoringSelection || _trackedTab is not { } tab)
        {
            return;
        }

        var selected = List.SelectedItems.Cast<FileSystemItem>().ToList();

        tab.SelectedPaths = [.. selected.Select(i => i.FullPath)];

        // Fájloknál SizeBytes, mappáknál a háttérben számolt
        // ComputedFolderSize — mindkettő negatív, amíg nincs ismert érték.
        var totalBytes = selected
            .Select(item => item.Kind == FileSystemItemKind.Directory ? item.ComputedFolderSize : item.SizeBytes)
            .Where(size => size > 0)
            .Sum();

        tab.UpdateStatus(selected.Count, totalBytes);
    }

    /// <summary>
    /// Ugyanaz a minta, mint a fő ablak <c>OnTrackedTabRenameRequested</c>-je:
    /// F7 (új mappa) vagy F2 (átnevezés) után kijelöli és láthatóvá görgeti a
    /// szerkesztés alá kerülő sort.
    /// </summary>
    private void OnTabRenameRequested(object? sender, FileSystemItem item)
    {
        List.SelectedItem = item;

        _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () => List.ScrollIntoView(item));
    }

    private void OnRootPreviewMouseDown(object sender, MouseButtonEventArgs e) => Activated?.Invoke(this, EventArgs.Empty);

    private List<string> GetSelectedPaths() =>
        [.. List.SelectedItems.Cast<FileSystemItem>().Select(i => i.FullPath)];

    private async void OnItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (List.SelectedItem is FileSystemItem item)
        {
            await OpenItemAsync(item);
        }
    }

    private async void OnOpenItemClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is FileSystemItem item)
        {
            await OpenItemAsync(item);
        }
    }

    private async Task OpenItemAsync(FileSystemItem item)
    {
        if (item.IsNavigable)
        {
            if (Tab is { } tab)
            {
                await tab.NavigateAsync(item.FullPath);
            }

            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(item.FullPath) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            // Nincs társított program, vagy a felhasználó elvetette a "Megnyitás ezzel" párbeszédet.
        }
    }

    private void OnItemPreviewRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        Activated?.Invoke(this, EventArgs.Empty);

        if (e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        var container = FindAncestor<ListViewItem>(source);

        if (container?.DataContext is not FileSystemItem item)
        {
            return;
        }

        if (!List.SelectedItems.Contains(item))
        {
            List.SelectedItem = item;
        }
    }

    private void OnItemPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        Activated?.Invoke(this, EventArgs.Empty);
        _dragStartPoint = e.GetPosition(null);
    }

    private void OnItemPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStartPoint is not { } start || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(null);

        if (Math.Abs(current.X - start.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _dragStartPoint = null;

        var paths = GetSelectedPaths();

        if (paths.Count == 0)
        {
            return;
        }

        var data = new DataObject(DataFormats.FileDrop, paths.ToArray());
        DragDrop.DoDragDrop(List, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
    }

    /// <summary>
    /// A húzás alapértelmezett hatása a Windows szabálya szerint: azonos
    /// meghajtón belül ÁTHELYEZÉS, eltérő meghajtóra MÁSOLÁS. A módosítók
    /// felülírják: <c>Shift</c> = áthelyezés, <c>Ctrl</c> = másolás,
    /// <c>Alt</c> = parancsikon (spec F7).
    /// </summary>
    private void OnListDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop) || Tab is not { CurrentPath: { } destination })
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var sourcePaths = e.Data.GetData(DataFormats.FileDrop) as string[] ?? [];

        e.Effects = ResolveDropEffect(sourcePaths, destination, Keyboard.Modifiers) switch
        {
            PaneDropAction.Copy => DragDropEffects.Copy,
            PaneDropAction.Shortcut => DragDropEffects.Link,
            _ => DragDropEffects.Move,
        };

        e.Handled = true;
    }

    /// <summary>
    /// A húzás közben lenyomott módosítóból és a kötet-egyezésből dönti el a
    /// hatást.
    /// </summary>
    /// <remarks>
    /// <c>internal</c>, és a módosítót PARAMÉTERKÉNT kapja (nem az élő
    /// <see cref="Keyboard.Modifiers"/>-ból olvassa): a <c>Pilaster.Tests</c>
    /// ezt fedi le közvetlenül (spec A2) — az egérrel húzás maga marad kézi
    /// ellenőrzés, de a döntési mátrix (melyik módosító melyik hatást adja)
    /// nem, és a valódi billentyűállapot lekérdezése nélkül tesztelhető.
    /// </remarks>
    internal static PaneDropAction ResolveDropEffect(IReadOnlyList<string> sourcePaths, string destination, ModifierKeys modifiers)
    {
        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            return PaneDropAction.Shortcut;
        }

        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            return PaneDropAction.Copy;
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            return PaneDropAction.Move;
        }

        return IsSameVolume(sourcePaths, destination) ? PaneDropAction.Move : PaneDropAction.Copy;
    }

    /// <summary>
    /// Igaz, ha MINDEN forrás ugyanazon a köteten van, mint a cél. Vegyes
    /// forrásnál a másolás a biztonságosabb alapértelmezés, mert az sosem
    /// szüntet meg semmit a forrásoldalon.
    /// </summary>
    /// <remarks><c>internal</c>: lásd <see cref="ResolveDropEffect"/>.</remarks>
    internal static bool IsSameVolume(IReadOnlyList<string> sourcePaths, string destination)
    {
        if (sourcePaths.Count == 0)
        {
            return false;
        }

        try
        {
            var destinationRoot = Path.GetPathRoot(Path.GetFullPath(destination));

            return !string.IsNullOrEmpty(destinationRoot)
                && sourcePaths.All(p =>
                    string.Equals(Path.GetPathRoot(Path.GetFullPath(p)), destinationRoot, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return false;
        }
    }

    private void OnListDrop(object sender, DragEventArgs e)
    {
        if (Tab is not { CurrentPath: { } destinationDir } || e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
        {
            return;
        }

        // Nincs értelme egy elemet önmagába ejteni.
        var filtered = paths.Where(p => !string.Equals(Path.GetDirectoryName(p), destinationDir, StringComparison.OrdinalIgnoreCase)).ToList();

        if (filtered.Count == 0)
        {
            return;
        }

        Activated?.Invoke(this, EventArgs.Empty);
        FilesDropped?.Invoke(this, (filtered, destinationDir, ResolveDropEffect(filtered, destinationDir, Keyboard.Modifiers)));
    }

    private void OnBreadcrumbClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is string path && Tab is { } tab)
        {
            _ = tab.NavigateAsync(path);
        }
    }

    private void OnCopyItemClick(object sender, RoutedEventArgs e) =>
        ClipboardFileService.SetClipboard(GetSelectedPaths(), isCut: false);

    private void OnCutItemClick(object sender, RoutedEventArgs e) =>
        ClipboardFileService.SetClipboard(GetSelectedPaths(), isCut: true);

    private void OnPasteClick(object sender, RoutedEventArgs e)
    {
        if (Tab is not { CurrentPath: { } destinationDir })
        {
            return;
        }

        if (!ClipboardFileService.TryGetClipboardFiles(out var paths, out var isCut))
        {
            return;
        }

        var filtered = paths.Where(p => !string.Equals(Path.GetDirectoryName(p), destinationDir, StringComparison.OrdinalIgnoreCase)).ToList();

        if (filtered.Count > 0)
        {
            FilesDropped?.Invoke(this, (filtered, destinationDir, isCut ? PaneDropAction.Move : PaneDropAction.Copy));
        }
    }

    private void OnDeleteItemClick(object sender, RoutedEventArgs e)
    {
        var paths = GetSelectedPaths();

        if (paths.Count > 0)
        {
            DeleteRequested?.Invoke(this, (paths, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)));
        }
    }

    private void OnShowPropertiesClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not FileSystemItem item)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(item.FullPath) { UseShellExecute = true, Verb = "properties" });
        }
        catch (Win32Exception)
        {
        }
    }

    private void OnRenameBoxIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true || sender is not TextBox textBox)
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(() =>
        {
            textBox.Focus();
            textBox.SelectAll();
        });
    }

    private void OnRenameBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: FileSystemItem item } || Tab is not { } tab)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                tab.CommitRenameCommand.Execute(item);
                break;
            case Key.Escape:
                e.Handled = true;
                tab.CancelRenameCommand.Execute(item);
                break;
        }
    }

    private void OnRenameBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: FileSystemItem item } && Tab is { } tab)
        {
            tab.CancelRenameCommand.Execute(item);
        }
    }

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null and not T)
        {
            source = VisualTreeHelper.GetParent(source);
        }

        return source as T;
    }
}

/// <summary>Mit tegyen egy panelbe ejtett fájlkészlettel a szülő.</summary>
public enum PaneDropAction
{
    Copy,

    Move,

    /// <summary>Parancsikon készítése a célmappában (<c>Alt</c> + húzás).</summary>
    Shortcut,
}
