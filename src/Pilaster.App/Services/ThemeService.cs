using System.Windows;
using System.Windows.Media.Animation;
using Pilaster.Core.Settings;
using Wpf.Ui.Appearance;

// A .NET 10-es WPF bevezetett egy saját System.Windows.ThemeMode típust a
// beépített Fluent témázáshoz. Mi a WPF-UI témakezelését használjuk, ezért az
// álnév egyértelműsíti, hogy végig a saját beállítás-enumunkról van szó.
using ThemeMode = Pilaster.Core.Settings.ThemeMode;

namespace Pilaster.App.Services;

/// <summary>A színséma alkalmazása és váltása.</summary>
public sealed class ThemeService(ISettingsService settings)
{
    /// <summary>
    /// Váltás közben eddig halványul a tartalom. Szándékosan nem nulláig:
    /// teljes eltűnésnél egy pillanatra átütne az ablak mögötti asztal, ami
    /// villanásnak látszana. Ennyi épp elfedi az átszíneződést.
    /// </summary>
    private const double DimOpacity = 0.35;

    private static readonly Duration FadeOut = TimeSpan.FromMilliseconds(90);
    private static readonly Duration FadeIn = TimeSpan.FromMilliseconds(150);

    /// <summary>Az aktuálisan beállított mód.</summary>
    public ThemeMode Current => settings.Current.Theme;

    /// <summary>
    /// Igaz, ha a felület jelenleg sötét — a nap/hold ikon ez alapján vált.
    /// </summary>
    public bool IsDark => ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;

    /// <summary>
    /// A mentett téma alkalmazása induláskor, animáció nélkül.
    /// </summary>
    public void ApplyInitial() => ApplyCore(settings.Current.Theme);

    /// <summary>
    /// A rendszertéma figyelése, hogy a <see cref="ThemeMode.System"/> mód
    /// menet közben is kövesse a Windows beállítását.
    /// </summary>
    public void WatchSystemTheme(Window window) => SystemThemeWatcher.Watch(window);

    /// <summary>
    /// Téma beállítása, mentéssel és — ha engedélyezett — átúsztatással.
    /// </summary>
    public async Task SetAsync(ThemeMode mode, Window? window)
    {
        settings.Current.Theme = mode;
        settings.NotifyChanged();

        var animate = settings.Current.Animations != AnimationLevel.Off && window?.Content is UIElement;

        if (!animate)
        {
            ApplyCore(mode);
            return;
        }

        var content = (UIElement)window!.Content;

        await FadeAsync(content, 1.0, DimOpacity, FadeOut);
        ApplyCore(mode);
        await FadeAsync(content, DimOpacity, 1.0, FadeIn);
    }

    /// <summary>
    /// Kapcsolás világos és sötét között.
    /// </summary>
    /// <remarks>
    /// Rendszerkövető módból az aktuálisan látható téma ellentettjére vált —
    /// így a gomb mindig azt csinálja, amit a felhasználó a nap/hold ikon
    /// alapján vár, ahelyett hogy előbb „rendszer" állapoton kellene átmennie.
    /// </remarks>
    public Task ToggleAsync(Window? window) =>
        SetAsync(IsDark ? ThemeMode.Light : ThemeMode.Dark, window);

    private static void ApplyCore(ThemeMode mode)
    {
        switch (mode)
        {
            case ThemeMode.Light:
                ApplicationThemeManager.Apply(ApplicationTheme.Light);
                break;

            case ThemeMode.Dark:
                ApplicationThemeManager.Apply(ApplicationTheme.Dark);
                break;

            default:
                ApplicationThemeManager.ApplySystemTheme();
                break;
        }
    }

    private static Task FadeAsync(UIElement element, double from, double to, Duration duration)
    {
        var completion = new TaskCompletionSource();

        var animation = new DoubleAnimation(from, to, duration)
        {
            // A gyorsulás-lassulás nélkül a rövid átmenet rángatósnak hat.
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut },
        };

        animation.Completed += (_, _) =>
        {
            // A animáció leválasztása után az Opacity újra szabadon állítható,
            // különben a rögzített érték „beragadna".
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = to;
            completion.TrySetResult();
        };

        element.BeginAnimation(UIElement.OpacityProperty, animation);
        return completion.Task;
    }
}
