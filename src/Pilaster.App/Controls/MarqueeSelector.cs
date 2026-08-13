using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Pilaster.App.Controls;

/// <summary>
/// Húzásos (marquee/rubber-band) kijelölés egy <see cref="ListBox"/>-hoz (a
/// <see cref="System.Windows.Controls.ListView"/> is ebből származik) — a
/// WPF ListView/ListBox natívan nem támogatja, az Explorer viszont igen.
/// </summary>
/// <remarks>
/// <para>
/// Csak akkor kezd húzást, ha az egérnyomás ÜRES területet talált el (nem
/// egy ListViewItem/ListBoxItem/ScrollBar fölött) — így elemen kezdett húzás
/// érintetlenül eljut a natív kijelölési logikához (Ctrl/Shift-klikk ott már
/// eleve helyesen működik az <c>Extended</c> módban), és a jövőbeli
/// drag-and-drop fájlmozgatással sem ütközik: az elemen indul, ez pedig
/// szándékosan figyelmen kívül hagyja az elemen induló húzásokat.
/// </para>
/// <para>
/// A virtualizált listákban csak a ténylegesen realizált (képernyőn lévő)
/// konténereket vizsgálja — az <c>ItemsHost</c> panel gyerekeit —, nem az
/// összes elemet, hogy nagy mappákban se legyen egérmozgásonként O(n) munka.
/// </para>
/// </remarks>
public sealed class MarqueeSelector
{
    private const double DragThresholdSquared = 16; // 4 px

    private readonly FrameworkElement _coordinateHost;
    private readonly Border _overlay;

    private ListBox? _activeSelector;
    private Point _startPoint;
    private bool _isDragging;
    private HashSet<object> _baseSelection = [];

    public MarqueeSelector(FrameworkElement coordinateHost, Border overlay)
    {
        _coordinateHost = coordinateHost;
        _overlay = overlay;
    }

    /// <summary>A viselkedés bekötése egy listára — DetailsView-nál és GridViewList-nél is hívva.</summary>
    public void Attach(ListBox selector)
    {
        selector.MouseLeftButtonDown += OnMouseDown;
        selector.MouseMove += OnMouseMove;
        selector.MouseLeftButtonUp += OnMouseUp;
        selector.LostMouseCapture += OnLostMouseCapture;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        var selector = (ListBox)sender;

        if (IsOverItemContainer(e.OriginalSource as DependencyObject))
        {
            return;
        }

        _activeSelector = selector;
        _startPoint = e.GetPosition(_coordinateHost);
        _isDragging = false;

        var additive = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        _baseSelection = additive ? [.. selector.SelectedItems.Cast<object>()] : [];

        // Sima kattintás (húzás nélkül) üres területen: kijelölés törlése —
        // ezt itt, azonnal tesszük meg, nem a felengedéskor, hogy a
        // visszajelzés ne késsen az Explorerhez képest.
        if (!additive)
        {
            selector.SelectedItems.Clear();
        }

        selector.CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_activeSelector is not { } selector || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(_coordinateHost);

        if (!_isDragging)
        {
            var delta = current - _startPoint;

            if (delta.LengthSquared < DragThresholdSquared)
            {
                return;
            }

            _isDragging = true;
            _overlay.Visibility = Visibility.Visible;
        }

        var rect = new Rect(_startPoint, current);

        _overlay.Margin = new Thickness(rect.X, rect.Y, 0, 0);
        _overlay.Width = rect.Width;
        _overlay.Height = rect.Height;

        ApplySelection(selector, rect);
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e) => Reset();

    private void OnLostMouseCapture(object sender, MouseEventArgs e) => Reset();

    private void Reset()
    {
        if (_activeSelector is { IsMouseCaptured: true } selector)
        {
            selector.ReleaseMouseCapture();
        }

        _activeSelector = null;
        _isDragging = false;
        _overlay.Visibility = Visibility.Collapsed;
    }

    private void ApplySelection(ListBox selector, Rect rect)
    {
        if (FindItemsHost(selector) is not { } panel)
        {
            return;
        }

        selector.SelectedItems.Clear();

        foreach (var item in _baseSelection)
        {
            selector.SelectedItems.Add(item);
        }

        foreach (UIElement child in panel.Children)
        {
            if (child is not FrameworkElement { DataContext: { } item } container)
            {
                continue;
            }

            Rect bounds;

            try
            {
                bounds = container.TransformToAncestor(_coordinateHost).TransformBounds(new Rect(container.RenderSize));
            }
            catch (InvalidOperationException)
            {
                // A konténer épp virtualizálás/újrahasznosítás közben leszakadt
                // a vizuális fáról — a következő egérmozgás úgyis frissíti.
                continue;
            }

            if (rect.IntersectsWith(bounds) && !selector.SelectedItems.Contains(item))
            {
                selector.SelectedItems.Add(item);
            }
        }
    }

    /// <summary>
    /// Igaz, ha a találat egy elemen, a görgetősávon vagy — a ListView
    /// esetében — az oszlopfejlécen belül van, tehát NEM üres terület.
    /// </summary>
    /// <remarks>
    /// A fejléc kihagyása azért kritikus, mert a fejléc is a lista vizuális
    /// fájának része: enélkül egy oszlopra kattintás (rendezéshez) üres
    /// területi kattintásnak tűnne, és váratlanul törölné a kijelölést.
    /// </remarks>
    private static bool IsOverItemContainer(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is ListViewItem or ListBoxItem or ScrollBar or GridViewColumnHeader)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private static Panel? FindItemsHost(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);

            if (child is Panel { IsItemsHost: true } panel)
            {
                return panel;
            }

            if (FindItemsHost(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
