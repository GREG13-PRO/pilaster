using System.Windows;
using Pilaster.Core.Settings;

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
