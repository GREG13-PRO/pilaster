using System.Windows;
using System.Windows.Media;
using Pilaster.Core.Settings;
using Wpf.Ui.Appearance;

namespace Pilaster.App.Services;

/// <summary>
/// A felület téma-tokenjei — egyetlen, központi színkészlet, amiből MINDEN
/// saját (nem WPF-UI eredetű) szín származik.
/// </summary>
/// <remarks>
/// <para>
/// A tokenek szándékosan KÓDBAN élnek, nem egy XAML-szótárban. Ennek az oka a
/// WPF-UI témakezelése: az <see cref="ApplicationThemeManager.Apply"/> a teljes
/// összefűzött szótárat lecseréli, tehát egy XAML-ben definiált saját szótár
/// vagy kiesne a sorrendből, vagy — ami rosszabb — a benne
/// <c>StaticResource</c>-szal feloldott értékek az induláskori (sötét) témára
/// fagynának. Kódból, minden témaváltás után újraírva ez kizárt.
/// </para>
/// <para>
/// Minden vezérlő <c>DynamicResource</c>-szal hivatkozik ezekre, ezért a
/// témaváltás azonnal, újraindítás nélkül átfest minden nyitott ablakot és
/// fület — beleértve a felugró menüket és a Beállításokat is.
/// </para>
/// <para>
/// Kontraszt: minden szövegtoken legalább 4,5:1 arányt ad a hozzá tartozó
/// háttértokenen, mindkét témában (WCAG AA). A konkrét értékek a
/// <c>docs/THEME-CHECKLIST.md</c> táblázatában vannak kimérve.
/// </para>
/// </remarks>
public sealed class ThemeTokenService(ISettingsService settings)
{
    // --- Háttér ---
    public const string BgApp = "TokenBgApp";
    public const string BgSurface = "TokenBgSurface";
    public const string BgElevated = "TokenBgElevated";
    public const string BgInput = "TokenBgInput";

    // --- Szöveg ---
    public const string TextPrimary = "TokenTextPrimary";
    public const string TextSecondary = "TokenTextSecondary";
    public const string TextMuted = "TokenTextMuted";
    public const string TextInverse = "TokenTextInverse";

    // --- Keret ---
    public const string Border = "TokenBorder";
    public const string BorderStrong = "TokenBorderStrong";

    // --- Akcentus (az AccentColorService írja felül a tényleges színnel) ---
    public const string Accent = "TokenAccent";
    public const string AccentHover = "TokenAccentHover";
    public const string AccentText = "TokenAccentText";

    // --- Interakciós állapotok ---
    public const string Hover = "TokenHover";
    public const string Selected = "TokenSelected";
    public const string SelectedInactive = "TokenSelectedInactive";
    public const string FocusRing = "TokenFocusRing";

    // --- Jelzésszínek ---
    public const string Danger = "TokenDanger";
    public const string Warning = "TokenWarning";
    public const string Success = "TokenSuccess";

    // --- Egyéb ---
    public const string ScrollbarThumb = "TokenScrollbarThumb";
    public const string Overlay = "TokenOverlay";
    public const string Shadow = "TokenShadow";

    // --- Sűrűség (spec K1) ---

    /// <summary>Egy listasor magassága képpontban.</summary>
    public const string RowHeight = "TokenRowHeight";

    /// <summary>Egy listasor belső margója.</summary>
    public const string RowPadding = "TokenRowPadding";

    /// <summary>Egy listasor külső margója — ez adja a sorok közti rést.</summary>
    public const string RowMargin = "TokenRowMargin";

    /// <summary>Igaz, ha a jelenleg alkalmazott téma sötét.</summary>
    public static bool IsDark => ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;

    /// <summary>
    /// A tokenek első alkalmazása, és feliratkozás a témaváltásra.
    /// </summary>
    /// <remarks>
    /// A feliratkozás fedi a „Rendszer téma követése" esetet is: a WPF-UI
    /// <c>SystemThemeWatcher</c>-e a Windows világos/sötét váltásakor
    /// ugyanezt a <see cref="ApplicationThemeManager.Changed"/> eseményt
    /// tüzeli, tehát a tokenek valós időben követik.
    /// </remarks>
    public void ApplyInitial()
    {
        ApplicationThemeManager.Changed += (_, _) => Apply();
        Apply();

        // A sűrűség nem téma-függő, de ugyanez a szolgáltatás tartja karban,
        // mert a fájllista-stílusok egyetlen tokenkészletből olvasnak.
        ApplyDensity(settings.Current.Density);
        settings.Changed += (_, _) => ApplyDensity(settings.Current.Density);
    }

    /// <summary>A teljes tokenkészlet (újra)írása az aktuális témához.</summary>
    public static void Apply()
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        var dark = IsDark;

        Set(app, BgApp, dark ? "#FF202020" : "#FFF3F3F3");
        Set(app, BgSurface, dark ? "#FF2B2B2B" : "#FFFFFFFF");
        Set(app, BgElevated, dark ? "#FF323232" : "#FFFFFFFF");
        Set(app, BgInput, dark ? "#FF2D2D2D" : "#FFFBFBFB");

        Set(app, TextPrimary, dark ? "#FFFFFFFF" : "#FF1A1A1A");
        Set(app, TextSecondary, dark ? "#FFC8C8C8" : "#FF5D5D5D");
        Set(app, TextMuted, dark ? "#FFA0A0A0" : "#FF6B6B6B");
        Set(app, TextInverse, dark ? "#FF1A1A1A" : "#FFFFFFFF");

        Set(app, Border, dark ? "#FF3D3D3D" : "#FFE5E5E5");
        Set(app, BorderStrong, dark ? "#FF767676" : "#FF9E9E9E");

        Set(app, Hover, dark ? "#14FFFFFF" : "#0A000000");
        Set(app, SelectedInactive, dark ? "#1AFFFFFF" : "#14000000");
        Set(app, FocusRing, dark ? "#FFFFFFFF" : "#FF1A1A1A");

        // A jelzésszínek sötét témában világosodnak: a világos módra tervezett
        // telített piros/zöld sötét háttéren 3:1 alá esne, ami olvashatatlan.
        Set(app, Danger, dark ? "#FFFF99A4" : "#FFC42B1C");
        Set(app, Warning, dark ? "#FFFFC83D" : "#FF9D5D00");
        Set(app, Success, dark ? "#FF6CCB5F" : "#FF0F7B32");

        Set(app, ScrollbarThumb, dark ? "#FF9A9A9A" : "#FF8A8A8A");
        Set(app, Overlay, dark ? "#99000000" : "#66000000");
        Set(app, Shadow, dark ? "#66000000" : "#33000000");

        // Az akcentus-eredetű tokeneket az AccentColorService tölti fel a
        // tényleges színnel; itt csak az induló (még akcentus előtti)
        // feloldásuk biztosított, hogy egy korai kötés se maradjon üresen.
        if (!app.Resources.Contains(Accent))
        {
            Set(app, Accent, "#FF0078D4");
            Set(app, AccentHover, "#FF1A86D9");
            Set(app, AccentText, "#FFFFFFFF");
            Set(app, Selected, "#2E0078D4");
        }
    }

    private static void Set(Application app, string key, string argb)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(argb)!);
        brush.Freeze();
        app.Resources[key] = brush;
    }

    /// <summary>
    /// A sűrűség-tokenek alkalmazása.
    /// </summary>
    /// <remarks>
    /// Szándékosan TOKENEK, nem szétszórt számok a stílusokban: a három érték
    /// (magasság, belső és külső margó) együtt mozog, és a fájllista, a
    /// gyorselérés-lista meg a Beállítások listái mind ugyanezt olvassák
    /// <c>DynamicResource</c>-ként — így a váltás azonnal, újraindítás nélkül
    /// érvényesül minden nyitott fülön és mindkét panelen.
    /// </remarks>
    /// <param name="density"><c>Compact</c>, <c>Comfortable</c> vagy <c>Relaxed</c>.</param>
    public static void ApplyDensity(string density)
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        var (height, padding, margin) = density switch
        {
            "Compact" => (24.0, new Thickness(6, 0, 6, 0), new Thickness(4, 0, 4, 0)),
            "Relaxed" => (40.0, new Thickness(10, 0, 10, 0), new Thickness(4, 3, 4, 3)),
            _ => (32.0, new Thickness(8, 0, 8, 0), new Thickness(4, 1, 4, 1)),
        };

        app.Resources[RowHeight] = height;
        app.Resources[RowPadding] = padding;
        app.Resources[RowMargin] = margin;
    }
}
