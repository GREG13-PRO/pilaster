using System.Windows;
using System.Windows.Media;
using Pilaster.Core.Settings;
using Wpf.Ui.Appearance;

namespace Pilaster.App.Services;

/// <summary>Az akcentus szín alkalmazása, váltása és mentése.</summary>
/// <remarks>
/// A WPF-UI <see cref="ApplicationAccentColorManager"/> egy alkalmazás-szintű
/// erőforrásszótárban tárolt szín-/ecset-kulcsokat frissít (pl.
/// <c>SystemAccentBrush</c>, <c>AccentFillColorDefaultBrush</c>) — minden
/// WPF-UI vezérlő (gomb, folyamatsáv stb.) ezekre <c>DynamicResource</c>-ként
/// hivatkozik, tehát egyetlen <see cref="ApplicationAccentColorManager.Apply"/>
/// hívás azonnal átfesti az egész felületet, változtatás nélkül a
/// vezérlők sablonjain. Ez tölti be a „CSS változó" szerepét ebben a WPF-alapú
/// felületben.
/// </remarks>
/// <remarks>
/// FONTOS: néhány WPF-UI kulcs (pl. <c>SystemAccentColorBrush</c>, a „Color"
/// szóval) STATIKUS <c>StaticResource</c>-szal hivatkozik a mögöttes színre a
/// gyári erőforrásszótárban — ez az induláskori értékre fagy, és SOHA nem
/// követi a később hívott <see cref="ApplicationAccentColorManager.Apply"/>-t.
/// Csak azok a kulcsok élők, amikre az <c>UpdateColorResources</c> (WPF-UI
/// forrás) közvetlenül, minden hívásnál új objektumot ír — ezt kipróbálás és a
/// WPF-UI forráskód ellenőrzése nélkül könnyű elvéteni.
/// </remarks>
public sealed class AccentColorService(ISettingsService settings)
{
    /// <summary>
    /// A kijelölés-hátterek (fájlsor, oldalsáv aktív mappa) áttetsző
    /// akcentus-árnyalatának erőforráskulcsa.
    /// </summary>
    /// <remarks>
    /// Szándékosan NEM a WPF-UI beépített, tömör <c>AccentFillColor*</c>
    /// ecsetei — azok gombokhoz készültek, egy teljes sor hátterén túl
    /// rikítóak lennének és eltakarnák a liquid glass hatást. Ehelyett saját,
    /// alacsony átlátszóságú változat ugyanabból a színből.
    /// </remarks>
    public const string SelectionBrushKey = "AccentSelectionBrush";

    /// <summary>Előre definiált paletta — a Beállítások panelen jelennek meg swatch-ként.</summary>
    public static IReadOnlyList<Color> Presets { get; } =
    [
        (Color)ColorConverter.ConvertFromString("#0078D4"), // kék
        (Color)ColorConverter.ConvertFromString("#8764B8"), // lila
        (Color)ColorConverter.ConvertFromString("#107C10"), // zöld
        (Color)ColorConverter.ConvertFromString("#C42B1C"), // piros
        (Color)ColorConverter.ConvertFromString("#CA5010"), // narancs
        (Color)ColorConverter.ConvertFromString("#E3008C"), // rózsaszín
        (Color)ColorConverter.ConvertFromString("#00B7C3"), // türkiz
        (Color)ColorConverter.ConvertFromString("#5A5A5A"), // grafit
    ];

    /// <summary>A mentett választás induláskori alkalmazása, és a rendszertéma-váltás követése.</summary>
    public void ApplyInitial()
    {
        ApplicationThemeManager.Changed += (_, _) => ApplyCurrent();
        ApplyCurrent();
    }

    /// <summary>Igaz, ha jelenleg a rendszer akcentus színét követjük.</summary>
    public bool IsSystemAccent => settings.Current.AccentColor == AccentColorMode.System;

    /// <summary>Az aktuálisan érvényes akcentus szín — palettakiemeléshez és előnézethez.</summary>
    public Color CurrentColor =>
        settings.Current.AccentColor == AccentColorMode.Custom
        && settings.Current.AccentColorHex is { } hex
        && TryParseHex(hex, out var custom)
            ? custom
            : ApplicationAccentColorManager.GetColorizationColor();

    /// <summary>Egyedi szín kiválasztása — paletta-swatch vagy hex-bevitel.</summary>
    public void SetCustom(Color color)
    {
        settings.Current.AccentColor = AccentColorMode.Custom;
        settings.Current.AccentColorHex = ToHex(color);
        settings.NotifyChanged();
        ApplyCurrent();
    }

    /// <summary>„Rendszer akcentus szín használata" — a Windows Személyre szabás színét követi.</summary>
    public void SetSystemAccent()
    {
        settings.Current.AccentColor = AccentColorMode.System;
        settings.Current.AccentColorHex = null;
        settings.NotifyChanged();
        ApplyCurrent();
    }

    /// <summary>
    /// Hex szöveg (pl. <c>"#1a2b3c"</c> vagy <c>"1a2b3c"</c>) érvényességének
    /// ellenőrzése — a Beállítások hex mezője ezzel dönti el, hogy az
    /// „Alkalmaz" gomb engedélyezett-e.
    /// </summary>
    public static bool TryParseHex(string text, out Color color)
    {
        var normalized = text.Trim();

        if (normalized.Length > 0 && normalized[0] != '#')
        {
            normalized = "#" + normalized;
        }

        try
        {
            if (normalized.Length is 7 or 9 && ColorConverter.ConvertFromString(normalized) is Color parsed)
            {
                color = parsed;
                return true;
            }
        }
        catch (FormatException)
        {
            // Érvénytelen hex — a hívó fél a false visszatéréssel jelzi a mezőn.
        }

        color = default;
        return false;
    }

    private void ApplyCurrent()
    {
        var theme = ApplicationThemeManager.GetAppTheme();

        if (settings.Current.AccentColor == AccentColorMode.Custom
            && settings.Current.AccentColorHex is { } hex
            && TryParseHex(hex, out var color))
        {
            ApplicationAccentColorManager.Apply(color, theme, systemGlassColor: false);
        }
        else
        {
            ApplicationAccentColorManager.ApplySystemAccent();
        }

        UpdateSelectionBrush(theme);
    }

    private static void UpdateSelectionBrush(ApplicationTheme theme)
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        var accent = ApplicationAccentColorManager.SystemAccent;

        // Sötét alapon egy telítettebb, világos módban egy visszafogottabb
        // áttetszőség kell, különben az egyik témában alig látszana a
        // kijelölés, a másikban túl rikító lenne a szöveg mögött.
        var alpha = theme == ApplicationTheme.Dark ? (byte)72 : (byte)46;

        app.Resources[SelectionBrushKey] =
            new SolidColorBrush(Color.FromArgb(alpha, accent.R, accent.G, accent.B));
    }

    private static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
