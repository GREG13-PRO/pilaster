using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Pilaster.App.Localization;
using Wpf.Ui.Controls;

using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;
using Button = Wpf.Ui.Controls.Button;

namespace Pilaster.App.Views;

/// <summary>
/// Kis beviteli ablak a névmaszkhoz (Num+ / Num− kijelölés, pl. <c>*.jpg</c>)
/// — kódból épül, mert egyetlen mezőből és két gombból áll.
/// </summary>
public sealed class MaskInputWindow : FluentWindow
{
    private readonly TextBox _input;

    private MaskInputWindow(string title, string hint)
    {
        Title = title;
        Width = 380;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        ExtendsContentIntoTitleBar = true;
        WindowBackdropType = Services.GlassEffectService.CurrentBackdrop;

        var strings = TranslationSource.Instance;
        _input = new TextBox { Text = "*.*", Margin = new Thickness(0, 10, 0, 0) };
        _input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                DialogResult = true;
            }
        };

        var ok = new Button
        {
            Content = strings["Cmd_Ok"],
            Appearance = ControlAppearance.Primary,
            MinWidth = 90,
            IsDefault = true,
            Margin = new Thickness(0, 0, 8, 0),
        };
        ok.Click += (_, _) => DialogResult = true;

        var cancel = new Button { Content = strings["Cmd_Cancel"], MinWidth = 90, IsCancel = true };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var root = new StackPanel { Margin = new Thickness(20, 16, 20, 18) };
        root.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold });
        root.Children.Add(new TextBlock
        {
            Text = hint,
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
        });
        root.Children.Add(_input);
        root.Children.Add(buttons);

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.Children.Add(new TitleBar { ShowMaximize = false, ShowMinimize = false });
        Grid.SetRow(root, 1);
        layout.Children.Add(root);
        Content = layout;

        Loaded += (_, _) =>
        {
            _input.Focus();
            _input.SelectAll();
        };
    }

    /// <summary>A maszk bekérése; <c>null</c>, ha a felhasználó megszakította.</summary>
    public static string? Ask(Window owner, string title, string hint)
    {
        var window = new MaskInputWindow(title, hint) { Owner = owner };
        return window.ShowDialog() == true ? window._input.Text : null;
    }
}
