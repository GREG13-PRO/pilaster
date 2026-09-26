using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Pilaster.App.Services;

/// <summary>
/// Az alkalmazás összes oldalra nyíló almenüjének egységes viselkedése
/// (osztályszintű kezelőkkel, így minden menüre hat):
/// <list type="bullet">
/// <item>rámutatásra gyorsan nyílik (a WPF a rendszer ~400 ms-os
/// <c>MenuShowDelay</c>-jét várta, ami lomhának és kiszámíthatatlannak hatott);</item>
/// <item>ha az egér elhagyja a sort ÉS az almenüt is, bezárul (a WPF nyitva
/// hagyta, amíg egy másik sorra nem mutattunk);</item>
/// <item>ugyanolyan háttérrel és üveghatással rajzolódik, mint a főmenü,
/// dupla keret nélkül.</item>
/// </list>
/// </summary>
public static class SubmenuBehavior
{
    private static readonly TimeSpan OpenDelay = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan CloseDelay = TimeSpan.FromMilliseconds(350);

    private static GlassEffectService? _glass;

    public static void Register(GlassEffectService glass)
    {
        _glass = glass;

        EventManager.RegisterClassHandler(typeof(MenuItem), UIElement.MouseEnterEvent, new MouseEventHandler(OnMouseEnter));
        EventManager.RegisterClassHandler(typeof(MenuItem), UIElement.MouseLeaveEvent, new MouseEventHandler(OnMouseLeave));
        EventManager.RegisterClassHandler(typeof(MenuItem), MenuItem.SubmenuOpenedEvent, new RoutedEventHandler(OnSubmenuOpened));
    }

    private static void OnMouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is not MenuItem item || !ReferenceEquals(e.OriginalSource, item) || !item.HasItems || !item.IsEnabled)
        {
            return;
        }

        After(OpenDelay, item, () =>
        {
            if (!item.IsMouseOver || item.IsSubmenuOpen)
            {
                return;
            }

            // A testvér-almenük bezárása — különben kettő lehetne nyitva egyszerre.
            if (ItemsControl.ItemsControlFromItemContainer(item) is { } parent)
            {
                foreach (var sibling in parent.Items.OfType<MenuItem>())
                {
                    if (!ReferenceEquals(sibling, item) && sibling.IsSubmenuOpen)
                    {
                        sibling.IsSubmenuOpen = false;
                    }
                }
            }

            item.IsSubmenuOpen = true;
        });
    }

    private static void OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is not MenuItem item || !ReferenceEquals(e.OriginalSource, item) || !item.HasItems)
        {
            return;
        }

        After(CloseDelay, item, () =>
        {
            if (item.IsSubmenuOpen && !IsPointerWithin(item))
            {
                item.IsSubmenuOpen = false;
            }
        });
    }

    /// <summary>Igaz, ha az egér a soron vagy annak (akár beágyazott) almenüjén van.</summary>
    private static bool IsPointerWithin(MenuItem item)
    {
        for (var node = Mouse.DirectlyOver as DependencyObject; node is not null; node = Parent(node))
        {
            if (ReferenceEquals(node, item))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Vizuális szülő, a felugró ablak gyökerénél a logikai szülőre (a Popupon át a menüsorra) lépve.</summary>
    private static DependencyObject? Parent(DependencyObject node) =>
        (node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : null)
        ?? LogicalTreeHelper.GetParent(node)
        ?? (node as FrameworkElement)?.TemplatedParent;

    /// <summary>
    /// Az almenü felugró ablaka a főmenüvel egyező kinézetet kap: a WPF-UI
    /// sablonjának árnyék-margója és effektje lekerül (ettől volt dupla a
    /// keret), a háttér a főmenüével azonos, a sarkokat és az üveghatást
    /// pedig — mint a főmenünél — a DWM adja.
    /// </summary>
    private static void OnSubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item || !ReferenceEquals(sender, e.OriginalSource))
        {
            return;
        }

        item.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            item.ApplyTemplate();

            if (item.Template?.FindName("SubmenuBorder", item) is not Border border
                || PresentationSource.FromVisual(border) is not HwndSource source)
            {
                return;
            }

            for (DependencyObject? node = border; node is not null && !ReferenceEquals(node, source.RootVisual); node = VisualTreeHelper.GetParent(node))
            {
                if (node is FrameworkElement element)
                {
                    element.Margin = new Thickness(0);
                    element.Effect = null;
                }
            }

            if (source.RootVisual is FrameworkElement root)
            {
                root.Margin = new Thickness(0);
                root.Effect = null;
            }

            border.CornerRadius = new CornerRadius(8);

            if (_glass is { IsEnabled: true })
            {
                border.Background = Brushes.Transparent;
            }
            else
            {
                border.SetResourceReference(Border.BackgroundProperty, "ContextMenuBackground");
            }

            border.SetResourceReference(Border.BorderBrushProperty, "ContextMenuBorderBrush");
            _glass?.ApplyToPopupWindow(source.Handle);
        });
    }

    private static void After(TimeSpan delay, DispatcherObject owner, Action action)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Input, owner.Dispatcher) { Interval = delay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            action();
        };
        timer.Start();
    }
}
