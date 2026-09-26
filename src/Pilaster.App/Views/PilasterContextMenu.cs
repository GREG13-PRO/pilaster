using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Pilaster.App.Localization;
using Pilaster.App.Services;
using Pilaster.Shell.Menus;
using Wpf.Ui.Controls;

using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using Separator = System.Windows.Controls.Separator;

namespace Pilaster.App.Views;

/// <summary>Egy saját menüelem leírója — a fix sorrendű, beépített elemekhez.</summary>
/// <param name="LabelKey">A felirat fordítási kulcsa.</param>
/// <param name="Icon">A megjelenő ikon.</param>
/// <param name="Action">Kattintáskor futó művelet.</param>
/// <param name="IsEnabled">Engedélyezett-e.</param>
/// <param name="SubItems">Almenü elemei; üres, ha nincs almenü.</param>
/// <param name="IsVisible">
/// Hamis esetén az elem EGYÁLTALÁN NEM kerül a menübe (spec J4 v1.0.1):
/// egy olyan parancs, ami az adott elemtípuson sosem értelmezhető (pl.
/// "Megnyitás a másik panelen" fájlon), ne szürkén álljon ott — az azt
/// sugallná, hogy elromlott valami —, hanem tűnjön el. <see cref="IsEnabled"/>
/// arra való, amikor a parancs ELVILEG helyénvaló, csak épp most nem
/// hajtható végre.
/// </param>
public sealed record PilasterMenuEntry(
    string LabelKey,
    SymbolRegular Icon,
    Action? Action = null,
    bool IsEnabled = true,
    IReadOnlyList<PilasterMenuEntry>? SubItems = null,
    bool IsVisible = true,
    string? Gesture = null,
    bool IsDefault = false)
{
    /// <summary>Elválasztó — a <see cref="LabelKey"/> üres.</summary>
    public static PilasterMenuEntry Separator { get; } = new(string.Empty, SymbolRegular.Empty);

    public bool IsSeparator => LabelKey.Length == 0;
}

/// <summary>Egy gomb a menü tetején lévő ikonsorban (Kivágás, Másolás, Átnevezés …).</summary>
public sealed record PilasterQuickAction(string LabelKey, SymbolRegular Icon, Action Action, bool IsEnabled = true, bool IsActive = false);

/// <summary>Egy kattintható címke-chip: <see cref="IsOn"/>, ha minden kijelölt elemen rajta van.</summary>
public sealed record PilasterTagChip(Pilaster.Core.Metadata.TagDefinition Tag, bool IsOn, Action<bool> Toggle);

/// <summary>
/// A menü fejléce: a kijelölt elem(ek) adatai (ikon, név, méret, dátum —
/// több elemnél darabszám és összméret), az ikonsor és a címke-chipek.
/// </summary>
public sealed record PilasterMenuHeader(
    IReadOnlyList<Pilaster.Core.FileSystem.FileSystemItem> Items,
    string Title,
    string Detail,
    IReadOnlyList<PilasterQuickAction> Actions,
    IReadOnlyList<PilasterTagChip> Tags);

/// <summary>
/// A Pilaster saját jobbklikk-menüje: teljes egészében a mi designunk
/// (lekerekítés, téma, ikonok), de a telepített shell-bővítmények elemeit is
/// megjeleníti (spec F4).
/// </summary>
/// <remarks>
/// <para>
/// A saját elemek AZONNAL megjelennek — a shell-bővítmények lekérdezése
/// aszinkron, időkorláttal fut, és a válasz utólag csúszik be. Így egy lassú
/// bővítmény nem késlelteti a menü megnyílását, egy hibás pedig nem is
/// látszik.
/// </para>
/// <para>
/// Nincs „További lehetőségek megjelenítése" kétszintűség: a shell elemek
/// ugyanabban a menüben, egy szinten jelennek meg (a beállítástól függően
/// külön „Egyéb alkalmazások" szekcióban vagy inline).
/// </para>
/// </remarks>
public sealed class PilasterContextMenu
{
    private readonly ContextMenu _menu = new() { Name = AnimationService.SelfAnimatedMenuName };
    private readonly Wpf.Ui.Controls.TextBox _searchBox;
    private readonly AnimationService _animations;
    private ShellMenuSession? _session;

    private PilasterContextMenu(GlassEffectService glass, AnimationService animations)
    {
        _animations = animations;

        // J1 (v1.0.1): a keresőmező korábban dísztelen, natív TextBox volt —
        // placeholder és ikon nélkül üres szürke dobozként ütött el a menü
        // tetején. A Wpf.Ui.Controls.TextBox PlaceholderText/Icon
        // tulajdonságaival ugyanaz a minta, mint a Beállítások keresőjén.
        _searchBox = new Wpf.Ui.Controls.TextBox
        {
            Margin = new Thickness(8, 6, 8, 6),
            MinWidth = 200,
            PlaceholderText = TranslationSource.Instance["ContextMenu_SearchPlaceholder"],
            Icon = new SymbolIcon { Symbol = SymbolRegular.Search24 },
        };

        _searchBox.TextChanged += (_, _) => ApplyFilter(_searchBox.Text);

        // MÉRVE: az üvegeffektus alkalmazása 1–2 ms, tehát nem érdemes
        // halasztani vagy feltételhez kötni.
        _menu.Opened += (_, _) => glass.ApplyToContextMenu(_menu);

        // J2 (v1.0.1): a menü korábban simán túlnyúlt a képernyő alján — a
        // Popup nem korlátozta magát a munkaterülethez. A MaxHeight
        // beállítása a natív ContextMenu-sablon BEÉPÍTETT ScrollViewerét
        // aktiválja (fel/le görgető nyilak, mint az Intézőben), tehát nem
        // kell saját görgetés-logikát írni — csak a felső korlátot kell
        // helyesen, a MEGFELELŐ monitoron és annak DPI-jével megadni.
        _menu.Opened += (_, _) => LimitHeightToWorkArea();

        // A1 (v1.0.2): a nyitóanimáció visszahozva, de NEM az eredeti hibát
        // okozó EffectThicknessDecorator+dinamikus Margin útján — ez a menü
        // MÁR KÉSZ, végleges méretű tartalmán fut, csak Opacity/RenderTransform
        // animációval, ezért nem módosítja utólag a Popup méretét.
        _menu.Opened += (_, _) => PlayOpenAnimation(_menu);

        // A munkamenetet a menü bezárásakor el KELL dobni, különben a
        // bővítmények COM-objektumai és a hozzájuk tartozó STA szál bent
        // ragadna a folyamat végéig.
        _menu.Closed += (_, _) =>
        {
            _session?.Dispose();
            _session = null;
        };

        _menu.PreviewKeyDown += OnPreviewKeyDown;
    }

    /// <summary>Az összes nyitóanimáció közös hossza/görbéje (spec A1, v1.0.2).</summary>
    private static readonly CubicEase OpenEase = new() { EasingMode = EasingMode.EaseOut };

    /// <summary>
    /// A menü (és az almenük) nyitóanimációja: halvány felúszás, a menü MÁR
    /// KÉSZ tartalmán, <c>Opacity</c>/<c>RenderTransform</c> animációval.
    /// </summary>
    /// <remarks>
    /// <para>
    /// SZÁNDÉKOSAN NEM <c>Style</c>/<c>ControlTemplate.Trigger</c>+
    /// <c>BeginStoryboard</c>-tal, és SZÁNDÉKOSAN NEM <c>DynamicResource</c>-
    /// hoz kötött <c>Duration</c>-nal — lásd <see cref="AnimationService"/>
    /// dokumentációját: egy megosztott <c>Style</c>-ban élő, DynamicResource-öt
    /// tartalmazó <c>Storyboard</c> az ELSŐ használatkor a <c>Style</c>
    /// lezárásakor (<c>Seal</c>) "Cannot freeze this Storyboard timeline tree"
    /// kivétellel elszállt egy korábbi kísérletnél (a sorok/csempék
    /// hover-kiemelésénél). Ehelyett ez a metódus kód-mögöttes
    /// <c>BeginAnimation</c>-t hív, KÖZVETLENÜL az adott (mindig ÚJ, sosem
    /// megosztott) menü/almenü példányán — ez sosem fagy le, mert nem
    /// <c>Freezable</c> erőforrásként él.
    /// </para>
    /// <para>
    /// A korábbi, fekete keretet okozó hiba (v1.0.1, 4. kör) az
    /// <c>EffectThicknessDecorator</c> MEGNYITÁS UTÁNI, dinamikus
    /// <c>Margin</c>-mutatásából jött — ez az animáció nem nyúl a Margin-hoz
    /// és nem méretezi át a Popupot, csak a MÁR VÉGLEGES MÉRETŰ tartalom
    /// átlátszóságát/pozícióját mozgatja a saját határain belül.
    /// </para>
    /// </remarks>
    private void PlayOpenAnimation(ContextMenu menu)
    {
        if (!_animations.AreAnimationsEnabled)
        {
            return;
        }

        menu.ApplyTemplate();

        if (menu.Template.FindName("Border", menu) is not Border border)
        {
            return;
        }

        AnimateEntrance(border);
    }

    private void AnimateEntrance(Border border)
    {
        var duration = TimeSpan.FromMilliseconds(
            _animations.Current == Pilaster.Core.Settings.AnimationLevel.Reduced ? 70 : 130);

        // ÚJ TranslateTransform minden nyitáskor — sosem osztott, sosem
        // Freezable-ként lefagyasztott példány, tehát a fenti "Cannot freeze"
        // hiba osztálya itt nem fordulhat elő.
        var transform = new TranslateTransform();
        border.RenderTransform = transform;
        border.Opacity = 0;

        border.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = OpenEase });
        transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-6, 0, duration) { EasingFunction = OpenEase });
    }

    /// <summary>
    /// Menü összeállítása és megnyitása.
    /// </summary>
    /// <param name="services">A szolgáltatásokhoz (téma-effekt, beállítások).</param>
    /// <param name="placementTarget">A vezérlő, amihez a menü igazodik.</param>
    /// <param name="ownItems">A saját elemek, fix sorrendben — mindig felül.</param>
    /// <param name="shellQuery">
    /// A shell-elemek lekérdezése; <c>null</c>, ha ebben a helyzetben nincs
    /// értelme (pl. egy fül vagy egy gyorselérés-sor menüjében).
    /// </param>
    /// <param name="settings">A jobbklikk-menü beállításai (engedélyezés, időkorlát, feketelista).</param>
    public static PilasterContextMenu Show(
        IServiceProvider services,
        UIElement placementTarget,
        IReadOnlyList<PilasterMenuEntry> ownItems,
        Func<TimeSpan, IReadOnlyCollection<string>, Task<ShellMenuSession?>>? shellQuery,
        Core.Settings.AppSettings settings,
        string? shellTarget = null,
        string shellKind = "items",
        PilasterMenuHeader? header = null,
        IReadOnlyList<PilasterMenuEntry>? footer = null)
    {
        var glass = (GlassEffectService)services.GetService(typeof(GlassEffectService))!;
        var animations = (AnimationService)services.GetService(typeof(AnimationService))!;
        var guard = (ShellCrashGuard)services.GetService(typeof(ShellCrashGuard))!;
        var menu = new PilasterContextMenu(glass, animations) { _guard = guard, _shellTarget = shellTarget, _shellKind = shellKind };

        if (header is not null)
        {
            menu.BuildHeader(header);
        }

        menu.BuildOwnItems(ownItems);

        // Ha az előző futás egy shell-lekérdezés közben halt meg, a
        // bővítmények kimaradnak — és ezt MEGMONDJUK, a bűnös útvonalával
        // együtt, hogy a felhasználó ki tudja feketelistázni (spec P3).
        if (guard.CrashDetected)
        {
            menu.AddCrashNotice(guard.LastCrash);
        }

        var loadsShell = shellQuery is not null && settings.ShellExtensionsEnabled && !guard.CrashDetected;

        if (loadsShell)
        {
            // Helyfoglaló, MIELŐTT a menü megnyílik: enélkül a menü
            // átméreteződne, amikor a shell elemek beérkeznek, és a kurzor
            // alatt elmozdulnának a saját elemek (spec K4).
            menu.AddLoadingPlaceholder(settings.ShellItemsInOwnSection);
        }

        if (footer is { Count: > 0 })
        {
            // A lábléc (pl. Tulajdonságok) a bővítmények ALATT, a menü végén.
            menu._footerSeparator = new Separator();
            menu._menu.Items.Add(menu._footerSeparator);

            foreach (var item in menu.Convert(footer))
            {
                menu._menu.Items.Add(item);
            }
        }

        menu._menu.PlacementTarget = placementTarget;
        menu._menu.IsOpen = true;

        if (loadsShell)
        {
            _ = menu.LoadShellItemsAsync(shellQuery!, settings);
        }

        return menu;
    }

    /// <summary>
    /// Helyfoglaló, amíg a shell elemek jönnek. Almenüs módban (alapértelmezés)
    /// maga a VÉGLEGES „Egyéb alkalmazások ›" sor kerül be rögtön, egy
    /// „Betöltés…" gyerekelemmel — a lekérdezés végén csak az almenü tartalma
    /// cserélődik, a főmenü mérete és elrendezése NEM változik (korábban a
    /// „Betöltés…" sor helyére került az almenü sora, és a menü ugrált).
    /// </summary>
    private void AddLoadingPlaceholder(bool ownSection)
    {
        _loadingSeparator = new Separator();

        if (ownSection)
        {
            _shellGroup = CreateGroup(
                TranslationSource.Instance["ContextMenu_OtherApps"],
                new SymbolIcon { Symbol = SymbolRegular.Apps24, FontSize = 15 },
                ShellGroupTag);
            _shellPlaceholder = new MenuItem
            {
                Header = TranslationSource.Instance["ContextMenu_LoadingShell"],
                IsEnabled = false,
                Icon = new SymbolIcon { Symbol = SymbolRegular.Empty },
            };

            _menu.Items.Add(_loadingSeparator);
            _menu.Items.Add(_shellGroup);
            _menu.Items.Add(_shellPlaceholder);
            RegisterChild(_shellGroup, _shellPlaceholder, 1);
            return;
        }

        _loadingItem = new MenuItem
        {
            Header = TranslationSource.Instance["ContextMenu_LoadingShell"],
            IsEnabled = false,
            FontSize = 11,
        };

        _menu.Items.Add(_loadingSeparator);
        _menu.Items.Add(_loadingItem);
    }

    private Separator? _loadingSeparator;
    private MenuItem? _loadingItem;

    /// <summary>Az „Egyéb alkalmazások ›" sor, ha almenüs módban már a betöltés előtt bekerült.</summary>
    private MenuItem? _shellGroup;

    /// <summary>A lábléc elválasztója — inline módban a shell-elemek ELÉ kerülnek.</summary>
    private Separator? _footerSeparator;

    /// <summary>„Betöltés…" sor az „Egyéb alkalmazások" csoport alatt, amíg a bővítmények jönnek.</summary>
    private MenuItem? _shellPlaceholder;

    // ---- Helyben lenyíló csoportok ----
    // Almenük (külön felugró ablakok) helyett a csoportok („További
    // lehetőségek", „Egyéb alkalmazások", 7-Zip …) a menün BELÜL nyílnak le
    // kattintásra. A WPF almenüi rámutatásra késve vagy egyáltalán nem
    // nyíltak, más színűek voltak, és elvett egérrel is nyitva maradtak
    // (felhasználói hibajelentés) — a lenyíló sorokkal ez az egész
    // hibaosztály megszűnik.
    private readonly Dictionary<MenuItem, List<Control>> _groupChildren = [];
    private readonly Dictionary<Control, MenuItem> _parentGroup = [];
    private readonly HashSet<MenuItem> _expandedGroups = [];

    private const string ChevronClosed = "▾";
    private const string ChevronOpen = "▴";

    private MenuItem CreateGroup(object header, object? icon, object? tag = null)
    {
        var group = new MenuItem
        {
            Header = header,
            Icon = icon,
            Tag = tag,
            StaysOpenOnClick = true,
            InputGestureText = ChevronClosed,
        };

        _groupChildren[group] = [];
        group.Click += (_, _) => ToggleGroup(group);
        return group;
    }

    private void RegisterChild(MenuItem group, Control child, int depth)
    {
        _groupChildren[group].Add(child);
        _parentGroup[child] = group;
        child.Margin = new Thickness(14 * depth, 0, 0, 0);
        child.Visibility = IsOpenChain(group) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UnregisterChild(Control child)
    {
        if (_parentGroup.Remove(child, out var group))
        {
            _groupChildren[group].Remove(child);
        }

        _menu.Items.Remove(child);
    }

    /// <summary>Igaz, ha a csoport és minden szülőcsoportja le van nyitva.</summary>
    private bool IsOpenChain(MenuItem group) =>
        _expandedGroups.Contains(group) && (!_parentGroup.TryGetValue(group, out var parent) || IsOpenChain(parent));

    private void ToggleGroup(MenuItem group)
    {
        if (!_expandedGroups.Remove(group))
        {
            _expandedGroups.Add(group);
        }

        group.InputGestureText = _expandedGroups.Contains(group) ? ChevronOpen : ChevronClosed;
        RefreshGroup(group, animate: true);
    }

    private void RefreshGroup(MenuItem group, bool animate)
    {
        var show = IsOpenChain(group);

        foreach (var child in _groupChildren[group])
        {
            var wasVisible = child.Visibility == Visibility.Visible;
            child.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

            if (show && !wasVisible && animate)
            {
                _animations.PlayEntrance(child, offsetY: -4, milliseconds: 170);
            }

            if (child is MenuItem nested && _groupChildren.ContainsKey(nested))
            {
                RefreshGroup(nested, animate);
            }
        }
    }

    /// <summary>A csoport utolsó (akár beágyazott) elemének indexe a menüben — ez után kerülnek az új elemei.</summary>
    private int LastDescendantIndex(MenuItem group)
    {
        var index = _menu.Items.IndexOf(group);

        foreach (var child in _groupChildren[group])
        {
            index = Math.Max(index, child is MenuItem nested && _groupChildren.ContainsKey(nested)
                ? LastDescendantIndex(nested)
                : _menu.Items.IndexOf(child));
        }

        return index;
    }

    /// <summary>A fejléc (fájladatok, ikonsor, címkék) tartója — a kereső nem rejti el.</summary>
    private const string HeaderTag = "pilaster:header";
    private ShellCrashGuard? _guard;
    private string? _shellTarget;
    private string? _shellKind;

    /// <summary>
    /// Egysoros figyelmeztetés a menü tetején, ha a shell-bővítmények egy
    /// korábbi összeomlás miatt ki vannak kapcsolva.
    /// </summary>
    private void AddCrashNotice(ShellInflightRecord? crash)
    {
        var text = crash is null
            ? TranslationSource.Instance["ContextMenu_ShellDisabledAfterCrash"]
            : string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                TranslationSource.Instance["ContextMenu_ShellDisabledAfterCrashAt"],
                crash.Path);

        _menu.Items.Add(new Separator());
        _menu.Items.Add(new MenuItem
        {
            Header = text,
            IsEnabled = false,
            FontSize = 11,
        });
    }

    /// <summary>A helyfoglaló eltávolítása — a shell elemek helyére.</summary>
    private void RemoveLoadingPlaceholder()
    {
        // Almenüs módban a sor MARAD (lásd AddLoadingPlaceholder) — ha nem jött
        // semmi, szürkén jelzi, hogy nincs további alkalmazás.
        if (_shellGroup is not null)
        {
            if (_shellPlaceholder is not null)
            {
                UnregisterChild(_shellPlaceholder);
                _shellPlaceholder = null;
            }

            if (_groupChildren[_shellGroup].Count == 0)
            {
                _shellGroup.Header = TranslationSource.Instance["ContextMenu_NoOtherApps"];
                _shellGroup.InputGestureText = string.Empty;
                _shellGroup.IsEnabled = false;
            }

            return;
        }

        if (_loadingItem is not null)
        {
            _menu.Items.Remove(_loadingItem);
            _loadingItem = null;
        }

        if (_loadingSeparator is not null)
        {
            _menu.Items.Remove(_loadingSeparator);
            _loadingSeparator = null;
        }
    }

    /// <summary>
    /// Fejléc (A+C terv): a kijelölés adatai, az ikonsor és a címke-chipek.
    /// Egy saját sablonú, nem kiemelhető <see cref="MenuItem"/> hordozza —
    /// a menü minden nem-MenuItem elemét magától MenuItem-be csomagolná,
    /// ami rámutatáskor kiemelné, kattintásra pedig bezárná a menüt.
    /// </summary>
    private void BuildHeader(PilasterMenuHeader header)
    {
        var root = new StackPanel { Margin = new Thickness(4, 4, 4, 2) };

        // --- a kijelölés adatai ---
        var info = new Grid { Margin = new Thickness(8, 6, 8, 8) };
        info.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        info.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Képnél a kis előnézet sarkai lekerekítve — a szögletes bélyegkép
        // idegenül hatott a lekerekített menüben.
        FrameworkElement icon = header.Items.Count == 1
            ? new Controls.ShellIconImage
            {
                IconSize = 36,
                Item = header.Items[0],
                Width = 36,
                Height = 36,
                Stretch = System.Windows.Media.Stretch.UniformToFill,
                Clip = new System.Windows.Media.RectangleGeometry(new Rect(0, 0, 36, 36), 7, 7),
            }
            : new SymbolIcon { Symbol = SymbolRegular.DocumentMultiple24, FontSize = 30 };
        icon.Margin = new Thickness(0, 0, 12, 0);
        icon.VerticalAlignment = VerticalAlignment.Center;
        info.Children.Add(icon);

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, MaxWidth = 240 };
        texts.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = header.Title,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        var detail = new System.Windows.Controls.TextBlock
        {
            Text = header.Detail,
            FontSize = 11.5,
            Margin = new Thickness(0, 2, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        detail.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        texts.Children.Add(detail);
        Grid.SetColumn(texts, 1);
        info.Children.Add(texts);
        root.Children.Add(info);

        // --- ikonsor ---
        if (header.Actions.Count > 0)
        {
            var bar = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, Margin = new Thickness(2, 0, 2, 4) };

            foreach (var action in header.Actions)
            {
                // Saját rajzolású gomb, NEM Wpf.Ui Button: az a menü üveghátterén
                // fekete kitöltéssel jelent meg (felhasználói hibajelentés).
                var glyph = new SymbolIcon
                {
                    Symbol = action.Icon,
                    FontSize = 18,
                    Filled = action.IsActive,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                glyph.SetResourceReference(Control.ForegroundProperty, action.IsActive ? "SystemAccentBrush" : "TextFillColorPrimaryBrush");

                var button = new Border
                {
                    Height = 36,
                    Margin = new Thickness(1, 0, 1, 0),
                    CornerRadius = new CornerRadius(6),
                    Background = System.Windows.Media.Brushes.Transparent,
                    Cursor = action.IsEnabled ? System.Windows.Input.Cursors.Hand : null,
                    Opacity = action.IsEnabled ? 1 : 0.35,
                    ToolTip = TranslationSource.Instance[action.LabelKey],
                    Child = glyph,
                };

                if (action.IsEnabled)
                {
                    button.MouseEnter += (_, _) => button.SetResourceReference(Border.BackgroundProperty, "SubtleFillColorSecondaryBrush");
                    button.MouseLeave += (_, _) => button.Background = System.Windows.Media.Brushes.Transparent;
                    button.MouseLeftButtonDown += (_, e) =>
                    {
                        e.Handled = true;
                        button.SetResourceReference(Border.BackgroundProperty, "SubtleFillColorTertiaryBrush");
                    };
                    button.MouseLeftButtonUp += (_, e) =>
                    {
                        e.Handled = true;
                        _menu.IsOpen = false;
                        action.Action();
                    };
                }

                bar.Children.Add(button);
            }

            root.Children.Add(bar);
        }

        // --- címke-chipek: kattintásra rá/le, a menü nyitva marad ---
        if (header.Tags.Count > 0)
        {
            var chips = new WrapPanel { Margin = new Thickness(6, 2, 6, 6), MaxWidth = 300 };

            foreach (var chip in header.Tags)
            {
                var isOn = chip.IsOn;
                var content = new StackPanel { Orientation = Orientation.Horizontal };
                content.Children.Add(new Controls.TagSwatch { TagColor = chip.Tag.Color, ColorHex = chip.Tag.ColorHex, Width = 10, Height = 10, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
                content.Children.Add(new System.Windows.Controls.TextBlock { Text = chip.Tag.Name, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });

                var border = new Border
                {
                    Child = content,
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(9, 3, 10, 3),
                    Margin = new Thickness(0, 0, 6, 6),
                    BorderThickness = new Thickness(1),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = TranslationSource.Instance["ContextMenu_TagChipHint"],
                };

                void Paint()
                {
                    border.SetResourceReference(Border.BorderBrushProperty, isOn ? "SystemAccentBrush" : "ControlStrokeColorDefaultBrush");
                    border.SetResourceReference(Border.BackgroundProperty, isOn ? "SubtleFillColorTertiaryBrush" : "SubtleFillColorTransparentBrush");
                }

                Paint();
                border.MouseLeftButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    isOn = !isOn;
                    chip.Toggle(isOn);
                    Paint();
                };
                chips.Children.Add(border);
            }

            root.Children.Add(chips);
        }

        var host = new MenuItem
        {
            Header = root,
            Tag = HeaderTag,
            StaysOpenOnClick = true,
            Focusable = false,
            Template = HeaderTemplate,
        };

        _menu.Items.Add(host);
        _menu.Items.Add(new Separator());
    }

    /// <summary>Csak a tartalmat rajzoló sablon a fejléc-tartóhoz — nincs kiemelés, nincs ikonoszlop.</summary>
    private static readonly ControlTemplate HeaderTemplate = CreateHeaderTemplate();

    private static ControlTemplate CreateHeaderTemplate()
    {
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.ContentSourceProperty, "Header");
        var template = new ControlTemplate(typeof(MenuItem)) { VisualTree = presenter };
        template.Seal();
        return template;
    }

    private void BuildOwnItems(IReadOnlyList<PilasterMenuEntry> entries)
    {
        // A kereső csak akkor jelenik meg, ha van mit szűrni — egy három
        // elemű menü tetején felesleges és zavaró volna. A rejtett (nem
        // IsVisible) elemek nem számítanak bele — azok úgysem kerülnek a
        // menübe.
        if (entries.Count(e => !e.IsSeparator && e.IsVisible) >= 8)
        {
            _menu.Items.Add(_searchBox);
            _menu.Items.Add(new Separator());
        }

        foreach (var item in Convert(entries))
        {
            _menu.Items.Add(item);
        }
    }

    /// <summary>
    /// A <see cref="PilasterMenuEntry"/>-fa WPF vezérlőkké alakítása. A nem
    /// <see cref="PilasterMenuEntry.IsVisible"/> elemek EGYÁLTALÁN NEM
    /// kerülnek bele (spec J4 v1.0.1), és az emiatt egymás mellé kerülő
    /// elválasztók összevonódnak — lásd az osztályszintű megjegyzést a
    /// <see cref="ShellMenuSession"/>-nél ugyanerről a mintáról.
    /// </summary>
    private List<Control> Convert(IReadOnlyList<PilasterMenuEntry> entries, MenuItem? parent = null, int depth = 0)
    {
        var result = new List<Control>();
        var lastWasSeparator = true;

        void Add(Control control)
        {
            result.Add(control);

            if (parent is not null)
            {
                RegisterChild(parent, control, depth);
            }
        }

        foreach (var entry in entries)
        {
            if (entry.IsSeparator)
            {
                if (!lastWasSeparator)
                {
                    lastWasSeparator = true;
                    Add(new Separator());
                }

                continue;
            }

            if (!entry.IsVisible)
            {
                continue;
            }

            lastWasSeparator = false;
            var icon = new SymbolIcon { Symbol = entry.Icon, FontSize = 15 };

            if (entry.SubItems is { Count: > 0 } children)
            {
                var group = CreateGroup(TranslationSource.Instance[entry.LabelKey], icon);
                group.IsEnabled = entry.IsEnabled;
                Add(group);
                result.AddRange(Convert(children, group, depth + 1));
                continue;
            }

            var item = new MenuItem
            {
                Header = TranslationSource.Instance[entry.LabelKey],
                IsEnabled = entry.IsEnabled,
                Icon = icon,
                InputGestureText = entry.Gesture ?? string.Empty,
                FontWeight = entry.IsDefault ? FontWeights.SemiBold : FontWeights.Normal,
            };

            if (entry.Action is { } action)
            {
                item.Click += (_, _) => action();
            }

            Add(item);
        }

        return result;
    }

    /// <summary>
    /// A shell-elemek betöltése és beszúrása — a menü MÁR NYITVA van, amikor
    /// ez lefut.
    /// </summary>
    private async Task LoadShellItemsAsync(
        Func<TimeSpan, IReadOnlyCollection<string>, Task<ShellMenuSession?>> shellQuery,
        Core.Settings.AppSettings settings)
    {
        // CATCH-ALL. Ezt a metódust eldobott taskként indítjuk
        // (`_ = LoadShellItemsAsync(...)`), tehát ami innen kiszökik, azt senki
        // nem figyeli meg: a legjobb esetben némán elvész, a legrosszabban a
        // folyamatot viszi. A shell-elemek hiánya bosszantó, az összeomlás nem
        // elfogadható — ezért itt MINDEN kivétel megáll.
        try
        {
            await LoadShellItemsCoreAsync(shellQuery, settings);
        }
        catch (Exception ex)
        {
            Diagnostics.CrashDiagnostics.Write($"A shell-elemek betöltése elszállt: {ex}", force: true);
            Serilog.Log.Error(ex, "A shell-elemek betöltése nem sikerült");
            RemoveLoadingPlaceholder();
        }
    }

    private async Task LoadShellItemsCoreAsync(
        Func<TimeSpan, IReadOnlyCollection<string>, Task<ShellMenuSession?>> shellQuery,
        Core.Settings.AppSettings settings)
    {
        // A jelző a lekérdezés ELŐTT íródik ki és utána törlődik: ha a
        // folyamat közben hal meg (natív AV egy bővítményben), a következő
        // indulás ebből tudja, hogy shell nélkül kell indulnia (spec P3).
        _guard?.MarkInflight(_shellTarget ?? string.Empty, _shellKind ?? "items");

        ShellMenuSession? session;

        try
        {
            session = await shellQuery(
                TimeSpan.FromMilliseconds(settings.ShellMenuTimeoutMs),
                settings.ShellHandlerBlacklist);
        }
        finally
        {
            _guard?.Clear();
        }

        if (session is null)
        {
            RemoveLoadingPlaceholder();
            return;
        }

        // A menü közben be is zárulhatott — ilyenkor a munkamenetet azonnal
        // el kell dobni, különben a COM-objektumok bent ragadnának.
        if (!_menu.IsOpen)
        {
            session.Dispose();
            return;
        }

        _session = session;

        if (session.Items.Count == 0)
        {
            RemoveLoadingPlaceholder();
            return;
        }

        // Ami a saját menüben már szerepel (Megnyitás, Kivágás, Másolás,
        // Törlés, Tulajdonságok, Rögzítés a gyorseléréshez, Terminál …), az a
        // shell-részből kimarad — különben minden ilyen parancs kétszer
        // szerepelne, eltérő felirattal.
        var nodes = TrimSeparators(session.Items.Where(node => !IsDuplicateOfOwnCommand(node)).ToList());

        if (_shellGroup is not null)
        {
            if (_shellPlaceholder is not null)
            {
                UnregisterChild(_shellPlaceholder);
                _shellPlaceholder = null;
            }

            var at = LastDescendantIndex(_shellGroup) + 1;

            foreach (var control in ConvertShellNodes(nodes, _shellGroup, 1))
            {
                _menu.Items.Insert(at++, control);
            }

            RemoveLoadingPlaceholder();
            return;
        }

        RemoveLoadingPlaceholder();

        if (nodes.Count == 0)
        {
            return;
        }

        var insertAt = _footerSeparator is null ? _menu.Items.Count : _menu.Items.IndexOf(_footerSeparator);
        _menu.Items.Insert(insertAt++, new Separator());

        foreach (var control in ConvertShellNodes(nodes, null, 0))
        {
            _menu.Items.Insert(insertAt++, control);
        }
    }

    /// <summary>Az „Egyéb alkalmazások" almenü jelölése — a kereső ebbe is belenéz.</summary>
    private const string ShellGroupTag = "pilaster:shellgroup";

    /// <summary>A szűrés után a lista elején/végén maradt, és az egymás utáni elválasztók eldobása.</summary>
    private static List<ShellMenuNode> TrimSeparators(List<ShellMenuNode> nodes)
    {
        var result = new List<ShellMenuNode>(nodes.Count);

        foreach (var node in nodes)
        {
            if (node.IsSeparator && (result.Count == 0 || result[^1].IsSeparator))
            {
                continue;
            }

            result.Add(node);
        }

        while (result.Count > 0 && result[^1].IsSeparator)
        {
            result.RemoveAt(result.Count - 1);
        }

        return result;
    }

    /// <summary>
    /// A shell NYELVFÜGGETLEN parancsnevei (verb), amelyeknek van saját
    /// megfelelője a Pilaster-menüben. MÉRVE (a fejlesztői gépen megjelenő
    /// shell-elemek naplójából): <c>pintohome</c> = „Rögzítés a Gyors
    /// elérésbe", <c>PilasterOpen</c> = a Pilaster saját Intéző-bejegyzése (a
    /// Pilasteren BELÜL értelmetlen), a GUID a Windows Terminal „Megnyitás a
    /// terminálban" parancsa.
    /// </summary>
    private static readonly HashSet<string> OwnCommandVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "open", "openas", "cut", "copy", "paste", "delete", "rename", "link", "properties",
        "copyaspath", "pintohome", "PilasterOpen",
        "{9F156763-7844-4DC4-B2B1-901F640F5155}",
    };

    /// <summary>
    /// Igaz, ha a felső szintű shell-elemnek van saját megfelelője a menüben.
    /// Verb híján az MFS_DEFAULT állapot a jelző: az a „Megnyitás" (a natív
    /// menüben egyszerre csak EGY elem lehet alapértelmezett).
    /// </summary>
    private static bool IsDuplicateOfOwnCommand(ShellMenuNode node)
    {
        if (node.IsSeparator || node.HasChildren)
        {
            return false;
        }

        var duplicate = node.Verb is { Length: > 0 } verb
            ? OwnCommandVerbs.Contains(verb)
            : node.IsDefault;

        if (duplicate)
        {
            Serilog.Log.Debug("Jobbklikk-menü: saját paranccsal egyező shell-elem kiszűrve (verb={Verb}): {Text}", node.Verb, node.Text);
        }

        return duplicate;
    }

    /// <summary>
    /// Shell-elemek laposított listája: az almenüs bővítmények (pl. 7-Zip)
    /// helyben lenyíló csoportként, beljebb húzott elemekkel.
    /// </summary>
    private List<Control> ConvertShellNodes(IEnumerable<ShellMenuNode> nodes, MenuItem? parent, int depth)
    {
        var result = new List<Control>();

        foreach (var node in nodes)
        {
            Control control;

            if (!node.IsSeparator && node.HasChildren)
            {
                control = CreateGroup(node.Text, ShellIcon(node));
                control.IsEnabled = node.IsEnabled;
            }
            else
            {
                control = ConvertShellNode(node);
            }

            result.Add(control);

            if (parent is not null)
            {
                RegisterChild(parent, control, depth);
            }

            if (control is MenuItem group && _groupChildren.ContainsKey(group))
            {
                result.AddRange(ConvertShellNodes(node.Children, group, depth + 1));
            }
        }

        return result;
    }

    private static object ShellIcon(ShellMenuNode node) =>
        node.Icon is null
            ? new SymbolIcon { Symbol = SymbolRegular.Empty }
            : new System.Windows.Controls.Image { Source = node.Icon, Width = 16, Height = 16 };

    private Control ConvertShellNode(ShellMenuNode node)
    {
        if (node.IsSeparator)
        {
            return new Separator();
        }

        var item = new MenuItem
        {
            Header = node.Text,
            IsEnabled = node.IsEnabled,
            IsChecked = node.IsChecked,
            // Ikon nélküli elemnél is le kell foglalni az ikon-oszlopot — különben
            // a felirat balra csúszik a többi sorhoz képest (ez volt a
            // „elcsúszott szöveg" a menüben).
            Icon = node.Icon is null
                ? new SymbolIcon { Symbol = SymbolRegular.Empty }
                : new System.Windows.Controls.Image { Source = node.Icon, Width = 16, Height = 16 },
        };

        // Az alapértelmezett parancsot (amit a dupla kattintás indít) az Intéző
        // félkövéren szedi — mi is.
        if (node.IsDefault)
        {
            item.FontWeight = FontWeights.SemiBold;
        }

        if (node.CommandId != 0)
        {
            var commandId = node.CommandId;

            item.Click += (_, _) =>
            {
                var owner = Window.GetWindow(_menu.PlacementTarget) is { } window
                    ? new System.Windows.Interop.WindowInteropHelper(window).Handle
                    : nint.Zero;

                // A menü bezárul, de a munkamenetet a Closed kezelője csak a
                // parancs elindítása UTÁN dobja el — a helyi másolat így
                // biztosan él a hívás pillanatában.
                //
                // CATCH-ALL: ez is eldobott task, tehát ami innen kiszökik, azt
                // senki nem figyeli meg. Egy hibás bővítmény parancsa nem
                // viheti a folyamatot.
                _ = InvokeShellCommandAsync(commandId, owner);
            };
        }

        return item;
    }

    /// <summary>Egy shell-parancs végrehajtása, minden hibát elnyelve.</summary>
    private async Task InvokeShellCommandAsync(uint commandId, nint owner)
    {
        try
        {
            if (_session is { } session)
            {
                await session.InvokeAsync(commandId, owner);
            }
        }
        catch (Exception ex)
        {
            Diagnostics.CrashDiagnostics.Write($"A shell-parancs végrehajtása elszállt: {ex}", force: true);
            Serilog.Log.Error(ex, "A shell-parancs végrehajtása nem sikerült ({CommandId})", commandId);
        }
    }

    /// <summary>
    /// Gépelés a nyitott menüben: szűkíti az elemeket (spec F4 UX).
    /// </summary>
    private void ApplyFilter(string query)
    {
        var trimmed = query.Trim();

        bool Matches(MenuItem item) =>
            (item.Header?.ToString() ?? string.Empty).Contains(trimmed, StringComparison.CurrentCultureIgnoreCase)
            || (_groupChildren.TryGetValue(item, out var children) && children.OfType<MenuItem>().Any(Matches));

        foreach (var element in _menu.Items.OfType<Control>())
        {
            if (element is MenuItem { Tag: HeaderTag })
            {
                continue;
            }

            if (trimmed.Length == 0)
            {
                // Vissza az alapállapotba: a lenyíló csoportok elemei csak
                // akkor látszanak, ha a csoport le van nyitva.
                element.Visibility = _parentGroup.TryGetValue(element, out var group) && !IsOpenChain(group)
                    ? Visibility.Collapsed
                    : Visibility.Visible;
                continue;
            }

            element.Visibility = element switch
            {
                // Szűrés közben az elválasztók eltűnnek, hogy ne maradjanak magányos vonalak.
                Separator => Visibility.Collapsed,
                MenuItem item => Matches(item) ? Visibility.Visible : Visibility.Collapsed,
                _ => element.Visibility,
            };
        }
    }

    /// <summary>
    /// Billentyűzetes navigáció. A nyilak, az Enter és az Esc a WPF menüjének
    /// beépített viselkedése; itt csak azt kell megoldani, hogy a KERESŐMEZŐ
    /// jelenléte ne törje meg a betű szerinti ugrást — gépelésre a fókusz a
    /// mezőbe kerül, és onnan a lefelé nyíl visszaadja a listának.
    /// </summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_menu.Items.Contains(_searchBox))
        {
            return;
        }

        if (e.Key == Key.Down && _searchBox.IsKeyboardFocusWithin)
        {
            e.Handled = true;
            _menu.Items.OfType<MenuItem>().FirstOrDefault(i => i.Visibility == Visibility.Visible)?.Focus();
            return;
        }

        if (e.Key is Key.Escape && _searchBox.Text.Length > 0)
        {
            e.Handled = true;
            _searchBox.Clear();
        }
    }

    /// <summary>
    /// A menü magasságának korlátozása arra a munkaterületre, amelyiken
    /// megnyílik (spec J2 v1.0.1) — a natív <c>MonitorFromWindow</c>/
    /// <c>GetMonitorInfo</c> adja a FIZIKAI pixelekben mért munkaterületet,
    /// amit a placement-ablak DPI-jével kell WPF-egységekre váltani, mert a
    /// <see cref="SystemParameters.WorkArea"/> csak az ELSŐDLEGES monitorra
    /// adna helyes értéket — több monitornál rossz korlátot szabna.
    /// </summary>
    private void LimitHeightToWorkArea()
    {
        if (Window.GetWindow(_menu.PlacementTarget) is not { } window)
        {
            return;
        }

        var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;

        if (hwnd == nint.Zero)
        {
            return;
        }

        var monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new NativeMethods.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>() };

        if (monitor == nint.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(window);
        var workAreaHeightPx = info.rcWork.Bottom - info.rcWork.Top;

        // 24 px levegő fent-lent — enélkül a menü pontosan a munkaterület
        // szélére tapadna, ami ugyanolyan "levágott" hatást keltene.
        _menu.MaxHeight = Math.Max(100, workAreaHeightPx / dpi.DpiScaleY - 24);
    }

    private static class NativeMethods
    {
        public const uint MONITOR_DEFAULTTONEAREST = 2;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        public static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        public struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }
    }
}
