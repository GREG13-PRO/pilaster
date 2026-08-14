using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Pilaster.App.ViewModels;
using Wpf.Ui.Controls;

namespace Pilaster.App.Views;

/// <summary>
/// A „Gyorselérés szerkesztése…" modális ablak — a gyorselérés fejlécének
/// jobbklikk-menüjéből nyílik.
/// </summary>
public partial class QuickAccessEditorWindow : FluentWindow
{
    private readonly QuickAccessEditorViewModel _viewModel;
    private Point? _dragStart;
    private QuickAccessRowViewModel? _dragged;

    public QuickAccessEditorWindow(QuickAccessEditorViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;

        InitializeComponent();

        viewModel.CloseRequested += (_, saved) =>
        {
            DialogResult = saved;
            Close();
        };
    }

    private void OnRowPreviewMouseDown(object sender, MouseButtonEventArgs e) => _dragStart = e.GetPosition(null);

    /// <summary>
    /// Húzás indítása a listán belüli átrendezéshez. Saját adatformátum
    /// helyett magát a sor-nézetmodellt visszük: a művelet az ablakon belül
    /// marad, nincs szükség szerializálható formátumra.
    /// </summary>
    private void OnRowPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not { } start || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(null);

        if (Math.Abs(current.X - start.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _dragStart = null;

        if (e.OriginalSource is not DependencyObject source
            || FindAncestor<ListBoxItem>(source)?.DataContext is not QuickAccessRowViewModel row)
        {
            return;
        }

        _dragged = row;
        DragDrop.DoDragDrop(RowList, row, DragDropEffects.Move);
        _dragged = null;
    }

    private void OnRowDrop(object sender, DragEventArgs e)
    {
        if (_dragged is not { } row)
        {
            return;
        }

        var target = FindRowAt(e.GetPosition(RowList));

        // A lista alsó, üres területére ejtve a sor a végére kerül — ez az
        // egyetlen módja annak, hogy egy elemet a legutolsó hely UTÁN lehessen
        // tenni, márpedig e nélkül a legalsó pozíció elérhetetlen volna.
        _viewModel.MoveTo(row, target is null ? _viewModel.Rows.Count - 1 : _viewModel.Rows.IndexOf(target));
    }

    private QuickAccessRowViewModel? FindRowAt(Point position)
    {
        if (RowList.InputHitTest(position) is not DependencyObject hit)
        {
            return null;
        }

        return FindAncestor<ListBoxItem>(hit)?.DataContext as QuickAccessRowViewModel;
    }

    private void OnColorSwatchClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string hex } && _viewModel.SelectedRow is { } row)
        {
            row.Color = hex;
        }
    }

    private void OnClearColorClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedRow is { } row)
        {
            row.Color = null;
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
