using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Pilaster.App.Controls;

/// <summary>
/// Sima egérgörgős görgetés bármely <see cref="ScrollViewer"/>-en (vagy
/// olyan vezérlőn, aminek a sablonjában van egy — pl. ListBox, ListView):
/// <c>controls:SmoothScroll.IsEnabled="True"</c>.
/// </summary>
/// <remarks>
/// A WPF alapból lépcsőben, egyszerre ~48 DIP-et ugorva görget. Itt a görgő a
/// CÉLT tolja, a tényleges eltolás pedig képkockánként exponenciálisan
/// közelít hozzá (a képkocka-időből számolva, így gyors és lassú gépen is
/// ugyanolyan tempójú) — ugyanaz a mozgás, mint a Beállítások oldalán.
/// Virtualizált listán a <c>VirtualizingPanel.ScrollUnit="Pixel"</c> kell
/// hozzá (a fájllisták már így vannak beállítva).
/// </remarks>
public static class SmoothScroll
{
    /// <summary>Kikapcsolt animációknál (lásd <c>AnimationService</c>) a görgetés azonnali marad.</summary>
    public static Func<bool> IsAnimationAllowed { get; set; } = () => true;

    /// <summary>Egy görgőrovátka (120 egység) ennyi DIP-et görget.</summary>
    private const double WheelStep = 110;

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(SmoothScroll),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static readonly ConditionalWeakTable<ScrollViewer, State> States = new();

    private sealed class State
    {
        public double Target;
        public bool Animating;
        public TimeSpan LastFrame;
        public EventHandler? Frame;
    }

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            element.PreviewMouseWheel += OnPreviewMouseWheel;
        }
        else
        {
            element.PreviewMouseWheel -= OnPreviewMouseWheel;
        }
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || !IsAnimationAllowed() || sender is not DependencyObject host)
        {
            return;
        }

        var viewer = host as ScrollViewer ?? FindScrollViewer(host);

        if (viewer is null || viewer.ScrollableHeight <= 0 || IsInsideNestedScroller(e.OriginalSource as DependencyObject, viewer, e.Delta))
        {
            return;
        }

        e.Handled = true;

        var state = States.GetValue(viewer, _ => new State());
        var from = state.Animating ? state.Target : viewer.VerticalOffset;
        state.Target = Math.Clamp(from - (e.Delta / 120.0 * WheelStep), 0, viewer.ScrollableHeight);

        if (state.Animating)
        {
            return;
        }

        state.Animating = true;
        state.LastFrame = TimeSpan.Zero;
        state.Frame = (_, args) => OnFrame(viewer, state, args);
        CompositionTarget.Rendering += state.Frame;
    }

    private static void OnFrame(ScrollViewer viewer, State state, EventArgs args)
    {
        var now = args is RenderingEventArgs rendering ? rendering.RenderingTime : TimeSpan.Zero;
        var dt = state.LastFrame == TimeSpan.Zero || now <= state.LastFrame
            ? 1.0 / 60
            : Math.Min((now - state.LastFrame).TotalSeconds, 0.05);
        state.LastFrame = now;

        var current = viewer.VerticalOffset;
        var remaining = state.Target - current;

        if (Math.Abs(remaining) < 0.5 || !viewer.IsLoaded)
        {
            viewer.ScrollToVerticalOffset(state.Target);
            CompositionTarget.Rendering -= state.Frame;
            state.Animating = false;
            return;
        }

        viewer.ScrollToVerticalOffset(current + (remaining * (1 - Math.Exp(-dt * 16.0))));
    }

    /// <summary>Egy belső, a görgetés irányában még görgethető területtől nem vesszük el a görgőt.</summary>
    private static bool IsInsideNestedScroller(DependencyObject? source, ScrollViewer outer, int delta)
    {
        for (var node = source;
             node is not null && !ReferenceEquals(node, outer);
             node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
        {
            if (node is ScrollViewer { ScrollableHeight: > 0 } inner)
            {
                return delta > 0 ? inner.VerticalOffset > 0 : inner.VerticalOffset < inner.ScrollableHeight;
            }
        }

        return false;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            if (child is ScrollViewer viewer)
            {
                return viewer;
            }

            if (FindScrollViewer(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }
}
