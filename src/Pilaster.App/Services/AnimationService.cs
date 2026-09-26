using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Pilaster.Core.Settings;
using Wpf.Ui.Controls;

namespace Pilaster.App.Services;

/// <summary>Az animációk szintjének alkalmazása, váltása és mentése.</summary>
/// <remarks>
/// <para>
/// A mappaváltáskori csúszó átmenet (<c>MainWindow.xaml</c>'s
/// <c>SlideInFileArea</c> storyboard) egy futásidőben cserélhető
/// <see cref="Duration"/> erőforrásra (<c>ContentSlideDuration</c>) hivatkozik
/// <c>DynamicResource</c>-ként — egyetlen alkalmazás-szintű csere itt azonnal
/// átállítja a sebességét. Ugyanaz a minta, mint az
/// <see cref="AccentColorService"/>-nél.
/// </para>
/// <para>
/// A sorok/csempék/oldalsáv-elemek finom hover-kiemelése (100–180 ms)
/// SZÁNDÉKOSAN NEM ezen a csatornán állítható: az a storyboard egy Style
/// <c>ControlTemplate.Triggers</c>-éből indul <c>BeginStoryboard</c>-dal, és
/// a Style első használatkor lezáródik (<c>Seal</c>), ami megpróbálja
/// lefagyasztani a teljes storyboard-fát — egy <c>DynamicResource</c>-öt
/// tartalmazó Freezable-t viszont nem lehet lefagyasztani, ez az ELSŐ
/// hoverre <c>"Cannot freeze this Storyboard timeline tree"</c> kivétellel
/// elszállt (self-teszttel elkapva). Ez a finom, alacsony amplitúdójú
/// opacitás-átmenet ezért minden szinten (Full/Reduced/Off) egyaránt fut —
/// az akadálymentesítési „csökkentett mozgás" iránymutatások is elsősorban a
/// NAGY, tájékozódást zavaró mozgásra (csúszás, skálázás) vonatkoznak, nem az
/// ilyen apró visszajelzésre.
/// </para>
/// </remarks>
public sealed class AnimationService(ISettingsService settings)
{
    private static readonly Duration FullContentSlide = new(TimeSpan.FromSeconds(0.22));
    private static readonly Duration ReducedContentSlide = new(TimeSpan.FromSeconds(0.11));
    private static readonly Duration Instant = new(TimeSpan.Zero);

    /// <summary>
    /// A mentett szint alkalmazása induláskor. Ha még sosem lett
    /// testreszabva, a rendszer „csökkentett mozgás" (Kisegítő lehetőségek ›
    /// Vizuális effektusok › Animációs effektusok) beállítása dönti el a
    /// kiinduló szintet, és ez a döntés explicit értékként el is mentődik —
    /// a rendszerbeállítás KÉSŐBBI váltása már nem írja felül némán.
    /// </summary>
    public void ApplyInitial()
    {
        if (settings.Current.Animations is null)
        {
            settings.Current.Animations = SystemParameters.ClientAreaAnimation
                ? AnimationLevel.Full
                : AnimationLevel.Reduced;
            settings.Save();
        }

        Apply(settings.Current.Animations.Value);
        RegisterGlobalAnimations();
        Controls.SmoothScroll.IsAnimationAllowed = () => AreAnimationsEnabled;
    }

    /// <summary>
    /// A <c>PilasterContextMenu</c> saját nyitóanimációval dolgozik — a
    /// globális menü-animáció ezt a nevet látva kihagyja, hogy ne fusson kétszer.
    /// </summary>
    public const string SelfAnimatedMenuName = "PilasterSelfAnimatedMenu";

    private static readonly IEasingFunction EntranceEase = new CubicEase { EasingMode = EasingMode.EaseOut };

    /// <summary>
    /// Alkalmazásszintű belépő-animációk, osztályszintű kezelőkkel — így minden
    /// (később megnyíló) ablak és helyi menü is megkapja, külön bekötés nélkül:
    /// <list type="bullet">
    /// <item>ablakok: a tartalom finoman beúszik és elhalványul-be;</item>
    /// <item>helyi menük és almenük (pl. a „Több" menü): rövid lecsúszás.</item>
    /// </list>
    /// „Ki" szinten egyik sem fut.
    /// </summary>
    private void RegisterGlobalAnimations()
    {
        EventManager.RegisterClassHandler(
            typeof(FluentWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) =>
            {
                if (sender is FluentWindow { Content: FrameworkElement content })
                {
                    PlayEntrance(content, offsetY: 10, milliseconds: 260);
                }
            }));

        EventManager.RegisterClassHandler(
            typeof(ContextMenu),
            ContextMenu.OpenedEvent,
            new RoutedEventHandler((sender, e) =>
            {
                if (sender is ContextMenu { Name: not SelfAnimatedMenuName } menu && ReferenceEquals(sender, e.OriginalSource))
                {
                    menu.ApplyTemplate();
                    PlayEntrance(menu.Template?.FindName("Border", menu) as FrameworkElement, offsetY: -6, milliseconds: 150);
                }
            }));

        EventManager.RegisterClassHandler(
            typeof(System.Windows.Controls.MenuItem),
            System.Windows.Controls.MenuItem.SubmenuOpenedEvent,
            new RoutedEventHandler((sender, e) =>
            {
                if (sender is not System.Windows.Controls.MenuItem item || !ReferenceEquals(sender, e.OriginalSource) || IsInSelfAnimatedMenu(item))
                {
                    return;
                }

                item.ApplyTemplate();
                PlayEntrance(item.Template?.FindName("SubmenuBorder", item) as FrameworkElement, offsetY: -4, milliseconds: 140);
            }));
    }

    private static bool IsInSelfAnimatedMenu(DependencyObject element)
    {
        for (var current = element; current is not null; current = LogicalTreeHelper.GetParent(current))
        {
            if (current is ContextMenu menu)
            {
                return menu.Name == SelfAnimatedMenuName;
            }
        }

        return false;
    }

    /// <summary>
    /// Belépő-animáció: elhalványulás-be és rövid csúszás a helyére. Csak
    /// Opacity/RenderTransform — a méretet és az elrendezést nem érinti.
    /// </summary>
    /// <param name="element">Az animálandó elem (<c>null</c>-ra nem csinál semmit).</param>
    /// <param name="offsetX">Vízszintes kiinduló eltolás (DIP).</param>
    /// <param name="offsetY">Függőleges kiinduló eltolás (DIP).</param>
    /// <param name="milliseconds">Teljes szinten ennyi ideig tart; csökkentett szinten a fele.</param>
    public void PlayEntrance(FrameworkElement? element, double offsetX = 0, double offsetY = 8, double milliseconds = 220)
    {
        if (element is null || !AreAnimationsEnabled)
        {
            return;
        }

        var duration = TimeSpan.FromMilliseconds(Current == AnimationLevel.Reduced ? milliseconds / 2 : milliseconds);

        // Minden alkalommal ÚJ transzformáció — sosem osztott vagy lefagyasztott
        // példány (lásd a „Cannot freeze" megjegyzést feljebb).
        var transform = new TranslateTransform(offsetX, offsetY);
        element.RenderTransform = transform;

        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = EntranceEase });
        transform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(offsetX, 0, duration) { EasingFunction = EntranceEase });
        transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(offsetY, 0, duration) { EasingFunction = EntranceEase });
    }

    public AnimationLevel Current => settings.Current.Animations ?? AnimationLevel.Full;

    /// <summary>
    /// Hamis, ha a szint <see cref="AnimationLevel.Off"/> — a kód-mögötti
    /// átmenetek (mappaváltás csúszása, témaváltás elhalványulása) ezzel
    /// döntik el, egyáltalán elinduljanak-e.
    /// </summary>
    public bool AreAnimationsEnabled => Current != AnimationLevel.Off;

    public void SetLevel(AnimationLevel level)
    {
        settings.Current.Animations = level;
        settings.NotifyChanged();
        Apply(level);
    }

    private static void Apply(AnimationLevel level)
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        app.Resources["ContentSlideDuration"] = level switch
        {
            AnimationLevel.Off => Instant,
            AnimationLevel.Reduced => ReducedContentSlide,
            _ => FullContentSlide,
        };
    }
}
