using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Pilaster.Core.Settings;
using Wpf.Ui.Controls;

namespace Pilaster.App.Services;

/// <summary>
/// A „liquid glass" áttetsző felület be- és kikapcsolása.
/// </summary>
/// <remarks>
/// <para>
/// Két, egymástól független mechanizmust fog össze:
/// </para>
/// <list type="number">
/// <item>
/// Az ablakon belüli panelek (oldalsáv, felső sáv, Beállítások panel) egy
/// közös <c>GlassPanelBrush</c> erőforrást használnak háttérként — ez csak
/// egy félig-áttetsző szín (WPF-UI saját, a Mica hátterű felületekre szánt
/// <c>LayerOnMicaBaseAltFillColorDefaultBrush</c> tokenje), tehát pusztán
/// alfa-keveréssel jár, GPU-blur nélkül. Kikapcsolva ugyanaz az átlátszatlan
/// kártyaháttér jelenik meg, mint a v0.6.1-ig.
/// </item>
/// <item>
/// A helyi menük (<see cref="ContextMenu"/>) saját, önálló felugró ablakot
/// (<c>Popup</c>) nyitnak — ide a WPF-es alfa-keverés nem elég, mert a menü
/// MÖGÖTT nem az alkalmazás tartalma, hanem az asztal lenne. Ezért ott a
/// WPF-UI saját <see cref="WindowBackdrop"/> segítségével VALÓDI natív DWM
/// Acrylic hátteret kapcsolunk a menü HWND-jére — ugyanazt a mechanizmust,
/// amit a főablak Mica háttere is használ, csak a rövid életű felugró
/// ablakoknak szánt <see cref="WindowBackdropType.Acrylic"/> változatban.
/// Ez is GPU-kompozit, nem WPF-renderelés, ezért nincs görgetési/teljesítmény
/// hatása.
/// </item>
/// </list>
/// </remarks>
public sealed class GlassEffectService(ISettingsService settings)
{
    public bool IsEnabled => settings.Current.LiquidGlassEnabled;

    /// <summary>A mentett állapot alkalmazása induláskor, és feliratkozás a témaváltásra.</summary>
    /// <remarks>
    /// A feliratkozás nem elhagyható. A <see cref="ApplyPanelResource"/> a
    /// WPF-UI szótárából MÁSOL egy kész ecset-objektumot a
    /// <c>GlassPanelBrush</c> kulcsra — az pedig egy adott témához tartozó,
    /// befagyasztott szín. Témaváltáskor a WPF-UI lecseréli a saját
    /// szótárát, de a mi kulcsunkban a RÉGI objektum maradna, ezért az
    /// oldalsáv, a felső sáv és a Beállítások panel sötéten ragadt világos
    /// témára váltás után (v0.9-es hibajelentés). Itt minden témaváltás után
    /// újra kiolvassuk a friss ecsetet.
    /// </remarks>
    public void ApplyInitial()
    {
        Wpf.Ui.Appearance.ApplicationThemeManager.Changed += (_, _) => ApplyPanelResource(IsEnabled);
        ApplyPanelResource(IsEnabled);
    }

    /// <summary>Beállításokban történő váltás: mentés és azonnali alkalmazás.</summary>
    public void SetEnabled(bool enabled)
    {
        settings.Current.LiquidGlassEnabled = enabled;
        settings.NotifyChanged();
        ApplyPanelResource(enabled);
    }

    private static void ApplyPanelResource(bool enabled)
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        app.Resources["GlassPanelBrush"] = app.Resources[
            enabled ? "LayerOnMicaBaseAltFillColorDefaultBrush" : "CardBackgroundFillColorDefaultBrush"];
    }

    /// <summary>
    /// Natív Acrylic háttér egy helyi menün — a menü <c>Opened</c>
    /// eseményéből kell hívni, mert csak akkor létezik még a felugró ablak
    /// HWND-je, amit a <see cref="PresentationSource.FromVisual"/> megtalál.
    /// </summary>
    public void ApplyToContextMenu(ContextMenu menu)
    {
        if (!IsEnabled)
        {
            return;
        }

        if (PresentationSource.FromVisual(menu) is not HwndSource source)
        {
            return;
        }

        menu.Background = Brushes.Transparent;
        WindowBackdrop.ApplyBackdrop(source.Handle, WindowBackdropType.Acrylic);
    }
}
