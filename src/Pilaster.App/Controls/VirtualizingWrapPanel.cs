using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Pilaster.App.Controls;

/// <summary>
/// Sortördelő elrendezés, ami csak a látható elemeket példányosítja.
/// </summary>
/// <remarks>
/// <para>
/// A WPF-ben nincs virtualizáló <c>WrapPanel</c>: a beépített
/// <c>VirtualizingStackPanel</c> nem tördel, a <c>WrapPanel</c> pedig minden
/// elemhez konténert gyárt. Egy 200 000 fájlos mappa rácsnézetben ez utóbbival
/// használhatatlan lenne.
/// </para>
/// <para>
/// Mivel a rácsnézet elemei egyforma méretű csempék, a panel fix
/// <see cref="ItemWidth"/>/<see cref="ItemHeight"/> értékkel dolgozik. Ez sokkal
/// egyszerűbb és gyorsabb, mint a változó méretű elrendezés: a pozíció és a
/// görgetési tartomány közvetlenül számolható, nem kell elemeket megmérni hozzá.
/// </para>
/// </remarks>
public class VirtualizingWrapPanel : VirtualizingPanel, IScrollInfo
{
    private Size _extent;
    private Size _viewport;
    private Point _offset;

    public static readonly DependencyProperty ItemWidthProperty =
        DependencyProperty.Register(
            nameof(ItemWidth),
            typeof(double),
            typeof(VirtualizingWrapPanel),
            new FrameworkPropertyMetadata(120.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ItemHeightProperty =
        DependencyProperty.Register(
            nameof(ItemHeight),
            typeof(double),
            typeof(VirtualizingWrapPanel),
            new FrameworkPropertyMetadata(120.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double ItemWidth
    {
        get => (double)GetValue(ItemWidthProperty);
        set => SetValue(ItemWidthProperty, value);
    }

    public double ItemHeight
    {
        get => (double)GetValue(ItemHeightProperty);
        set => SetValue(ItemHeightProperty, value);
    }

    private int ColumnCount => Math.Max(1, (int)Math.Floor(_viewport.Width / ItemWidth));

    protected override Size MeasureOverride(Size availableSize)
    {
        var owner = ItemsControl.GetItemsOwner(this);

        if (owner is null)
        {
            return default;
        }

        var itemCount = owner.Items.Count;

        // A gyerekek generálásához a generátornak léteznie kell; ezt a
        // hozzáférés váltja ki.
        _ = InternalChildren;

        var width = double.IsInfinity(availableSize.Width) ? ItemWidth : availableSize.Width;
        var height = double.IsInfinity(availableSize.Height) ? ItemHeight : availableSize.Height;

        var columns = Math.Max(1, (int)Math.Floor(width / ItemWidth));
        var rows = (int)Math.Ceiling(itemCount / (double)columns);

        UpdateScrollInfo(new Size(width, height), new Size(columns * ItemWidth, rows * ItemHeight));

        if (itemCount == 0)
        {
            CleanUpItems(0, -1);
            return new Size(width, height);
        }

        GetVisibleRange(columns, itemCount, out var firstIndex, out var lastIndex);

        var generator = ItemContainerGenerator;
        var startPosition = generator.GeneratorPositionFromIndex(firstIndex);

        // Ha a kezdőpozíció egy már létező konténerre esik, a beszúrás utána
        // következik — ezt jelzi az Offset.
        var childIndex = startPosition.Offset == 0
            ? startPosition.Index
            : startPosition.Index + 1;

        using (generator.StartAt(startPosition, GeneratorDirection.Forward, true))
        {
            for (var i = firstIndex; i <= lastIndex; i++, childIndex++)
            {
                var child = (UIElement)generator.GenerateNext(out var newlyRealized);

                if (newlyRealized)
                {
                    if (childIndex >= InternalChildren.Count)
                    {
                        AddInternalChild(child);
                    }
                    else
                    {
                        InsertInternalChild(childIndex, child);
                    }

                    generator.PrepareItemContainer(child);
                }

                child.Measure(new Size(ItemWidth, ItemHeight));
            }
        }

        CleanUpItems(firstIndex, lastIndex);

        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var generator = ItemContainerGenerator;
        var columns = Math.Max(1, (int)Math.Floor(finalSize.Width / ItemWidth));

        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            var itemIndex = generator.IndexFromGeneratorPosition(new GeneratorPosition(i, 0));

            if (itemIndex < 0)
            {
                continue;
            }

            var row = itemIndex / columns;
            var column = itemIndex % columns;

            child.Arrange(new Rect(
                column * ItemWidth - _offset.X,
                row * ItemHeight - _offset.Y,
                ItemWidth,
                ItemHeight));
        }

        return finalSize;
    }

    private void GetVisibleRange(int columns, int itemCount, out int firstIndex, out int lastIndex)
    {
        var firstRow = (int)Math.Floor(_offset.Y / ItemHeight);
        var lastRow = (int)Math.Ceiling((_offset.Y + _viewport.Height) / ItemHeight);

        firstIndex = Math.Max(0, firstRow * columns);
        lastIndex = Math.Min(itemCount - 1, ((lastRow + 1) * columns) - 1);

        if (lastIndex < firstIndex)
        {
            lastIndex = firstIndex;
        }
    }

    /// <summary>
    /// A látható tartományon kívülre került konténerek elengedése.
    /// </summary>
    /// <remarks>
    /// Visszafelé haladunk, mert az eltávolítás átindexeli a gyerekeket — előre
    /// haladva átugranánk elemeket.
    /// </remarks>
    private void CleanUpItems(int firstIndex, int lastIndex)
    {
        var generator = ItemContainerGenerator;

        for (var i = InternalChildren.Count - 1; i >= 0; i--)
        {
            var position = new GeneratorPosition(i, 0);
            var itemIndex = generator.IndexFromGeneratorPosition(position);

            if (itemIndex >= 0 && (itemIndex < firstIndex || itemIndex > lastIndex))
            {
                generator.Remove(position, 1);
                RemoveInternalChildRange(i, 1);
            }
        }
    }

    private void UpdateScrollInfo(Size viewport, Size extent)
    {
        var changed = false;

        if (extent != _extent)
        {
            _extent = extent;
            changed = true;
        }

        if (viewport != _viewport)
        {
            _viewport = viewport;
            changed = true;
        }

        // A görgetés nem lóghat túl a tartalmon (pl. ablak-átméretezés után).
        var maxOffsetY = Math.Max(0, _extent.Height - _viewport.Height);

        if (_offset.Y > maxOffsetY)
        {
            _offset.Y = maxOffsetY;
            changed = true;
        }

        if (changed)
        {
            ScrollOwner?.InvalidateScrollInfo();
        }
    }

    // ---- IScrollInfo ----

    public bool CanVerticallyScroll { get; set; } = true;

    public bool CanHorizontallyScroll { get; set; }

    public double ExtentWidth => _extent.Width;

    public double ExtentHeight => _extent.Height;

    public double ViewportWidth => _viewport.Width;

    public double ViewportHeight => _viewport.Height;

    public double HorizontalOffset => _offset.X;

    public double VerticalOffset => _offset.Y;

    public ScrollViewer? ScrollOwner { get; set; }

    public void SetVerticalOffset(double offset)
    {
        var clamped = Math.Clamp(offset, 0, Math.Max(0, _extent.Height - _viewport.Height));

        if (Math.Abs(clamped - _offset.Y) < 0.001)
        {
            return;
        }

        _offset.Y = clamped;
        ScrollOwner?.InvalidateScrollInfo();

        // Új sorok válnak láthatóvá, ezért újra kell mérni — nem elég elrendezni.
        InvalidateMeasure();
    }

    public void SetHorizontalOffset(double offset)
    {
        // A tördelés miatt vízszintesen soha nincs túlnyúlás.
    }

    public void LineUp() => SetVerticalOffset(_offset.Y - ItemHeight);

    public void LineDown() => SetVerticalOffset(_offset.Y + ItemHeight);

    public void PageUp() => SetVerticalOffset(_offset.Y - _viewport.Height);

    public void PageDown() => SetVerticalOffset(_offset.Y + _viewport.Height);

    /// <summary>Egérgörgő: három sornyi csempe, a Windows konvenciója szerint.</summary>
    public void MouseWheelUp() => SetVerticalOffset(_offset.Y - (ItemHeight * 3 / 2));

    public void MouseWheelDown() => SetVerticalOffset(_offset.Y + (ItemHeight * 3 / 2));

    public void LineLeft()
    {
    }

    public void LineRight()
    {
    }

    public void PageLeft()
    {
    }

    public void PageRight()
    {
    }

    public void MouseWheelLeft()
    {
    }

    public void MouseWheelRight()
    {
    }

    /// <summary>
    /// Billentyűzetes navigációnál a kijelölt elem legörgetése a nézetbe.
    /// </summary>
    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        var child = visual as UIElement;

        if (child is null)
        {
            return rectangle;
        }

        var index = InternalChildren.IndexOf(child);

        if (index < 0)
        {
            return rectangle;
        }

        var itemIndex = ItemContainerGenerator.IndexFromGeneratorPosition(new GeneratorPosition(index, 0));

        if (itemIndex < 0)
        {
            return rectangle;
        }

        var row = itemIndex / ColumnCount;
        var top = row * ItemHeight;
        var bottom = top + ItemHeight;

        if (top < _offset.Y)
        {
            SetVerticalOffset(top);
        }
        else if (bottom > _offset.Y + _viewport.Height)
        {
            SetVerticalOffset(bottom - _viewport.Height);
        }

        return rectangle;
    }
}
