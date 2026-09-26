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
/// Az ablakok natív DWM Acrylic hátteret kapnak (valódi, GPU-kompozit
/// elmosás az ablak mögötti tartalomról), a panelek és a tartalomterület
/// pedig a téma alapszínét kapják az erősség-csúszkának megfelelő
/// átlátszósággal (<c>GlassPanelBrush</c>, <c>GlassSurfaceBrush</c>).
/// Kikapcsolva minden átlátszatlan, háttér-effekt nélkül.
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

    /// <summary>Az üveghatás erőssége 0 és 1 között — lásd <c>AppSettings.GlassIntensity</c>.</summary>
    private double Intensity => Math.Clamp(settings.Current.GlassIntensity, 0, 100) / 100.0;

    /// <summary>
    /// Az ablakok <c>WindowBackdropType</c>-jának közös forrása — minden ablak
    /// XAML-je <c>{DynamicResource PilasterWindowBackdrop}</c>-ként kötődik rá.
    /// </summary>
    /// <remarks>
    /// A WPF-UI több belső úton (témaváltás, rendszertéma-figyelő, aktiválás)
    /// az ablak SAJÁT tulajdonsága szerint teszi vissza a hátteret — ha ez
    /// fixen „Mica" volt a XAML-ben, az üveghatás Acrylicja azonnal
    /// visszaváltott (MÉRVE: DWMWA_SYSTEMBACKDROP_TYPE = 2). Ezért a
    /// tulajdonságnak MÁR a létrehozáskor a helyes értéket kell kapnia.
    /// </remarks>
    public const string BackdropResourceKey = "PilasterWindowBackdrop";

    /// <summary>A jelenleg beállított ablakháttér — kódból létrehozott ablakokhoz.</summary>
    public static WindowBackdropType CurrentBackdrop =>
        Application.Current?.TryFindResource(BackdropResourceKey) is WindowBackdropType type ? type : WindowBackdropType.Mica;

    /// <summary>A mentett állapot alkalmazása induláskor, és feliratkozás a témaváltásra.</summary>
    /// <remarks>
    /// A témaváltás-feliratkozás nem elhagyható: az ecsetek a téma alapszínéből
    /// számolódnak, témaváltáskor ezért újra kell építeni őket (a v0.9-es
    /// „sötéten ragadt oldalsáv" hiba ugyanebből eredt). Az ablakokra egy
    /// OSZTÁLYSZINTŰ <c>Loaded</c>-kezelő teszi rá a hátteret, így minden,
    /// később megnyíló ablak (Beállítások, szerkesztő, párbeszédek) is
    /// ugyanúgy néz ki, külön bekötés nélkül.
    /// </remarks>
    public void ApplyInitial()
    {
        // MINDEN ablak létrehozása előtt: az ablakok ezt az értéket veszik fel
        // a WindowBackdropType tulajdonságukba. Futás közbeni váltáskor az
        // erőforrás SZÁNDÉKOSAN nem változik (a tulajdonság futásidejű váltása
        // a WPF-UI-ban kivételt dob) — a már nyitott ablakokra az
        // ApplyToWindow teszi rá közvetlenül a natív hátteret.
        if (Application.Current is { } app)
        {
            app.Resources[BackdropResourceKey] = IsEnabled ? WindowBackdropType.Acrylic : WindowBackdropType.None;
        }

        Wpf.Ui.Appearance.ApplicationThemeManager.Changed += (_, _) => ApplyAll();

        EventManager.RegisterClassHandler(
            typeof(FluentWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => ApplyToWindow((FluentWindow)sender)));

        ApplyBrushes();
    }

    /// <summary>Beállításokban történő váltás: mentés és azonnali alkalmazás.</summary>
    public void SetEnabled(bool enabled)
    {
        settings.Current.LiquidGlassEnabled = enabled;
        settings.NotifyChanged();
        ApplyAll();
    }

    /// <summary>Az erősség csúszka: mentés és azonnali alkalmazás (húzás közben is).</summary>
    public void SetIntensity(int intensity)
    {
        settings.Current.GlassIntensity = Math.Clamp(intensity, 0, 100);
        settings.Save();
        ApplyBrushes();
    }

    private void ApplyAll()
    {
        ApplyBrushes();

        if (Application.Current is { } app)
        {
            foreach (var window in app.Windows.OfType<FluentWindow>())
            {
                ApplyToWindow(window);
            }
        }
    }

    /// <summary>
    /// Az ablak háttere: bekapcsolva VALÓDI, natív Acrylic (a DWM az ablak
    /// MÖGÖTTI tartalmat mossa el — mint a Start menü), kikapcsolva teljesen
    /// átlátszatlan, egyszínű háttér.
    /// </summary>
    /// <remarks>
    /// Korábban a „liquid glass" csak egy alig áttetsző panelszín volt a
    /// Mica háttér fölött — a Mica viszont nem lát át semmin, csak a
    /// háttérkép színárnyalatát veszi fel, így a hatás gyakorlatilag
    /// láthatatlan volt (felhasználói visszajelzés: „az semmi").
    /// </remarks>
    private void ApplyToWindow(FluentWindow window)
    {
        // SZÁNDÉKOSAN nem a FluentWindow.WindowBackdropType tulajdonságon át:
        // annak futásidejű váltása a WPF-UI-ban újraépíti a WindowChrome-ot,
        // ami MÉRVE ArgumentException-nel („not a context for this Freezable")
        // elszállt. A natív hátteret közvetlenül a HWND-re tesszük, ugyanúgy,
        // mint a helyi menüknél.
        if (new WindowInteropHelper(window).Handle is var handle && handle == 0)
        {
            return;
        }

        if (IsEnabled)
        {
            // Sötét témában teljesen átlátszó; világosban fehéres tónus (lásd
            // ApplyBrushes) — a nyers Acrylic ott piszkosszürke volt.
            window.SetResourceReference(Control.BackgroundProperty, GlassWindowBrushKey);
            WindowBackdrop.ApplyBackdrop(handle, WindowBackdropType.Acrylic);
        }
        else
        {
            WindowBackdrop.RemoveBackdrop(window);
            // Erőforrás-hivatkozás, NEM a pillanatnyi ecset: egy bemásolt ecset
            // témaváltás után a régi téma színén ragadna.
            window.SetResourceReference(Control.BackgroundProperty, "ApplicationBackgroundBrush");
        }
    }

    /// <summary>
    /// A panelek (<c>GlassPanelBrush</c>: oldalsáv, felső sáv, Beállítások) és
    /// a tartalomterület (<c>GlassSurfaceBrush</c>: fájllista, panelek)
    /// ecsete a téma alapszínéből, az erősségnek megfelelő átlátszósággal.
    /// A tartalomterület szándékosan kevésbé áttetsző — a fájlnevek a
    /// legerősebb beállításnál is olvashatók maradjanak.
    /// </summary>
    private void ApplyBrushes()
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        if (!IsEnabled)
        {
            app.Resources["GlassPanelBrush"] = app.Resources["CardBackgroundFillColorDefaultBrush"];
            app.Resources["GlassSurfaceBrush"] = app.Resources["CardBackgroundFillColorDefaultBrush"];
            return;
        }

        // A téma saját alapszíne — ha valamiért nem elérhető, marad az
        // átlátszatlan kártyaháttér (nincs beégetett tartalék szín).
        if (app.TryFindResource("ApplicationBackgroundColor") is not Color baseColor)
        {
            app.Resources[GlassWindowBrushKey] = Brushes.Transparent;
            app.Resources["GlassPanelBrush"] = app.Resources["CardBackgroundFillColorDefaultBrush"];
            app.Resources["GlassSurfaceBrush"] = app.Resources["CardBackgroundFillColorDefaultBrush"];
            return;
        }

        var t = Intensity;

        // Világos témában a sötét asztalháttér az erős átlátszóságnál szürke,
        // foltos felületet adott (felhasználói hibajelentés: „szörnyű") — ott
        // a panelek a legerősebb fokozaton is jóval fedőbbek maradnak, és az
        // ablak alapja is fehéres tónust kap. Sötétben a tartomány változatlan.
        var light = Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme() == Wpf.Ui.Appearance.ApplicationTheme.Light;

        app.Resources["GlassPanelBrush"] = CreateBrush(baseColor, light ? Lerp(0.96, 0.72, t) : Lerp(0.94, 0.30, t));
        app.Resources["GlassSurfaceBrush"] = CreateBrush(baseColor, light ? Lerp(0.98, 0.84, t) : Lerp(0.97, 0.55, t));
        app.Resources[GlassWindowBrushKey] = light ? CreateBrush(baseColor, Lerp(0.75, 0.50, t)) : Brushes.Transparent;
    }

    /// <summary>Az ablak alapja bekapcsolt üveghatásnál — lásd <see cref="ApplyBrushes"/>.</summary>
    private const string GlassWindowBrushKey = "GlassWindowBrush";

    private static double Lerp(double from, double to, double t) => from + ((to - from) * t);

    private static SolidColorBrush CreateBrush(Color color, double opacity) =>
        ThemeTokenService.CreateTranslucentBrush(color, opacity);

    /// <summary>
    /// Natív Acrylic háttér és lekerekített sarok egy helyi menün — a menü
    /// <c>Opened</c> eseményéből kell hívni, mert csak akkor létezik a
    /// felugró ablak HWND-je, amit a <see cref="PresentationSource.FromVisual"/>
    /// megtalál.
    /// </summary>
    public void ApplyToContextMenu(ContextMenu menu)
    {
        if (PresentationSource.FromVisual(menu) is not HwndSource source)
        {
            return;
        }

        if (IsEnabled)
        {
            menu.Background = Brushes.Transparent;
        }
        else
        {
            menu.SetResourceReference(Control.BackgroundProperty, "ContextMenuBackground");
        }

        ApplyToPopupWindow(source.Handle);
    }

    /// <summary>
    /// Egy felugró (menü-) ablak natív megjelenése: a DWM kerekíti a sarkait
    /// (és ő rajzolja az árnyékot is), bekapcsolt üveghatásnál pedig Acrylic
    /// hátteret kap.
    /// </summary>
    /// <remarks>
    /// Sarok-beállítás nélkül az Acrylic a TELJES, téglalap alakú felugró
    /// ablakot kitölti — a lekerekített szegély mögött szürke, szögletes
    /// sarkok látszottak (felhasználói hibajelentés a „Több" menüről).
    /// </remarks>
    public void ApplyToPopupWindow(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var round = DwmCornerRound;
        _ = DwmSetWindowAttribute(handle, DwmWindowCornerPreference, ref round, sizeof(int));

        if (IsEnabled)
        {
            WindowBackdrop.ApplyBackdrop(handle, WindowBackdropType.Acrylic);
        }
    }

    // Az almenük (MenuItem felugrója) SZÁNDÉKOSAN nem kapnak DWM-sarkot és
    // Acrylicot: a WPF-UI almenü-sablonja saját lekerekített kerettel és
    // árnyékkal rajzol, a DWM-kerettel együtt DUPLA keret látszott körülöttük
    // (felhasználói hibajelentés).

    private const int DwmWindowCornerPreference = 33;
    private const int DwmCornerRound = 2;

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
