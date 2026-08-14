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
/// Egy önálló fájlpanel a Kétablakos nézethez — saját eszköztár, útvonalsáv és
/// fájllista, a <see cref="DataContext"/>-ként kapott <c>TabViewModel</c>
/// vezérli. Lásd <c>MainWindowViewModel.LeftPaneTab</c>/<c>RightPaneTab</c>.
/// </summary>
public partial class FilePaneView : UserControl
{
    private Point? _dragStartPoint;

    /// <summary>A <see cref="Tab"/>, amelynek <c>RenameRequested</c> eseményét épp figyeljük — lásd <see cref="OnDataContextChanged"/>.</summary>
    private TabViewModel? _trackedTab;

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
    public event EventHandler<(IReadOnlyList<string> Paths, string DestinationDir, bool IsCopy)>? FilesDropped;

    /// <summary>Törlés kérése a kijelölt elemekre — a szülő végzi a <c>FileOperationEngine</c>-nel.</summary>
    public event EventHandler<(IReadOnlyList<string> Paths, bool Permanent)>? DeleteRequested;

    private TabViewModel? Tab => DataContext as TabViewModel;

    /// <summary>
    /// A belső fájllista — a Total Commander-billentyűkiosztás (lásd
    /// <c>MainWindow.OnMainPreviewKeyDown</c>) ezen keresztül olvassa/
    /// módosítja a kijelölést és a billentyűzet-fókuszt, panel-független
    /// (egy- vagy kétablakos) módon.
    /// </summary>
    public ListView SelectionList => List;

    /// <summary>
    /// A DataContext (lásd <c>MainWindow.xaml</c>: <c>LeftPaneTab</c>/
    /// <c>RightPaneTab</c>-hoz kötve) a panel egész élettartama alatt
    /// változatlan példány — ennek ellenére eseményalapúan iratkozunk fel,
    /// nem a konstruktorban, mert a kötés csak az <c>InitializeComponent</c>
    /// UTÁN érvényesül.
    /// </summary>
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_trackedTab is not null)
        {
            _trackedTab.RenameRequested -= OnTabRenameRequested;
        }

        _trackedTab = DataContext as TabViewModel;

        if (_trackedTab is not null)
        {
            _trackedTab.RenameRequested += OnTabRenameRequested;
        }
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
        DragDrop.DoDragDrop(List, data, DragDropEffects.Copy | DragDropEffects.Move);
    }

    private void OnListDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? DragDropEffects.Copy : DragDropEffects.Move
            : DragDropEffects.None;

        e.Handled = true;
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

        var isCopy = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        FilesDropped?.Invoke(this, (filtered, destinationDir, isCopy));
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
            FilesDropped?.Invoke(this, (filtered, destinationDir, !isCut));
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
