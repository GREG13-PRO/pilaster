using System.Windows;
using Pilaster.Core.FileSystem;

namespace Pilaster.App.Controls;

/// <summary>Az oszlopfejléces rendezés irányjelzője.</summary>
public enum SortIndicator
{
    /// <summary>Ez az oszlop nem rendez — nincs nyíl.</summary>
    None,

    Ascending,

    Descending,
}

/// <summary>
/// Csatolt tulajdonságok a <c>GridView</c> oszlopfejléces rendezéséhez.
/// </summary>
/// <remarks>
/// Azért csatolt tulajdonság, mert a <see cref="System.Windows.Controls.GridViewColumn"/>
/// nem <c>FrameworkElement</c>, tehát nincs <c>Tag</c>-je, amibe a rendezési
/// szempontot rejthetnénk. Így viszont a XAML-ben az oszlop mellett, olvashatóan
/// áll, hogy mi szerint rendez — nem egy törékeny oszlopindex-leképezésben.
/// </remarks>
public static class GridViewSort
{
    /// <summary>Melyik mező szerint rendez ez az oszlop.</summary>
    public static readonly DependencyProperty SortKeyProperty =
        DependencyProperty.RegisterAttached(
            "SortKey",
            typeof(SortKey),
            typeof(GridViewSort),
            new PropertyMetadata(SortKey.Name));

    public static SortKey GetSortKey(DependencyObject element) =>
        (SortKey)element.GetValue(SortKeyProperty);

    public static void SetSortKey(DependencyObject element, SortKey value) =>
        element.SetValue(SortKeyProperty, value);

    /// <summary>A fejlécen megjelenő nyíl állapota.</summary>
    public static readonly DependencyProperty IndicatorProperty =
        DependencyProperty.RegisterAttached(
            "Indicator",
            typeof(SortIndicator),
            typeof(GridViewSort),
            new PropertyMetadata(SortIndicator.None));

    public static SortIndicator GetIndicator(DependencyObject element) =>
        (SortIndicator)element.GetValue(IndicatorProperty);

    public static void SetIndicator(DependencyObject element, SortIndicator value) =>
        element.SetValue(IndicatorProperty, value);
}
