using System.Windows;
using System.Windows.Controls;
using Pilaster.App.Converters;
using Pilaster.App.Services;
using Pilaster.Core.Metadata;

namespace Pilaster.App.Controls;

/// <summary>
/// Egy címke színmintája: lekerekített négyzet a címke SAJÁT színével
/// kitöltve, mindig látható szegéllyel.
/// </summary>
/// <remarks>
/// <para>
/// Szándékosan CLR-típus, nem <c>DataTemplate</c> vagy <c>Style</c>
/// erőforrás: a fő ablak, a kétablakos nézet panelje és a Beállítások
/// külön XAML-hatókörben élnek, egy erőforrást mindháromban duplikálni
/// kellene (lásd a <c>FilePaneView.xaml</c> tetején lévő megjegyzést). Így
/// egyetlen definíció szolgál ki minden megjelenési helyet — a
/// Beállításokat, a fájllistát és a szűrő legördülőt egyaránt.
/// </para>
/// <para>
/// A szegély nem díszítés: enélkül egy fehér vagy nagyon világos címke
/// világos témában láthatatlan lenne a szintén világos háttéren — ez volt a
/// B2 hiba egyik eleme.
/// </para>
/// </remarks>
public sealed class TagSwatch : Border
{
    public static readonly DependencyProperty TagColorProperty = DependencyProperty.Register(
        nameof(TagColor),
        typeof(TagColor),
        typeof(TagSwatch),
        new PropertyMetadata(Core.Metadata.TagColor.Gray, OnColorChanged));

    public static readonly DependencyProperty ColorHexProperty = DependencyProperty.Register(
        nameof(ColorHex),
        typeof(string),
        typeof(TagSwatch),
        new PropertyMetadata(null, OnColorChanged));

    public TagSwatch()
    {
        Width = 14;
        Height = 14;
        CornerRadius = new CornerRadius(4);
        BorderThickness = new Thickness(1);
        SnapsToDevicePixels = true;

        SetResourceReference(BorderBrushProperty, ThemeTokenService.BorderStrong);
        UpdateFill();
    }

    /// <summary>A címke paletta-színe.</summary>
    public TagColor TagColor
    {
        get => (TagColor)GetValue(TagColorProperty);
        set => SetValue(TagColorProperty, value);
    }

    /// <summary>Egyedi <c>#RRGGBB</c> szín, ha a felhasználó ilyet adott meg.</summary>
    public string? ColorHex
    {
        get => (string?)GetValue(ColorHexProperty);
        set => SetValue(ColorHexProperty, value);
    }

    private static void OnColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((TagSwatch)d).UpdateFill();

    private void UpdateFill() => Background = TagPalette.Resolve(TagColor, ColorHex);
}
