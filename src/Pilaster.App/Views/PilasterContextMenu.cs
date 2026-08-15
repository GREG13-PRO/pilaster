using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Pilaster.App.Localization;
using Pilaster.App.Services;
using Pilaster.Shell.Menus;
using Wpf.Ui.Controls;

using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using Separator = System.Windows.Controls.Separator;
using TextBox = System.Windows.Controls.TextBox;

namespace Pilaster.App.Views;

/// <summary>Egy saját menüelem leírója — a fix sorrendű, beépített elemekhez.</summary>
/// <param name="LabelKey">A felirat fordítási kulcsa.</param>
/// <param name="Icon">A megjelenő ikon.</param>
/// <param name="Action">Kattintáskor futó művelet.</param>
/// <param name="IsEnabled">Engedélyezett-e.</param>
/// <param name="SubItems">Almenü elemei; üres, ha nincs almenü.</param>
public sealed record PilasterMenuEntry(
    string LabelKey,
    SymbolRegular Icon,
    Action? Action = null,
    bool IsEnabled = true,
    IReadOnlyList<PilasterMenuEntry>? SubItems = null)
{
    /// <summary>Elválasztó — a <see cref="LabelKey"/> üres.</summary>
    public static PilasterMenuEntry Separator { get; } = new(string.Empty, SymbolRegular.Empty);

    public bool IsSeparator => LabelKey.Length == 0;
}

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
    private readonly ContextMenu _menu = new();
    private readonly TextBox _searchBox;
    private ShellMenuSession? _session;

    private PilasterContextMenu(GlassEffectService glass)
    {
        _searchBox = new TextBox
        {
            Margin = new Thickness(8, 6, 8, 6),
            MinWidth = 200,
        };

        _searchBox.TextChanged += (_, _) => ApplyFilter(_searchBox.Text);

        // MÉRVE: az üvegeffektus alkalmazása 1–2 ms, tehát nem érdemes
        // halasztani vagy feltételhez kötni.
        _menu.Opened += (_, _) => glass.ApplyToContextMenu(_menu);

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
        string shellKind = "items")
    {
        var glass = (GlassEffectService)services.GetService(typeof(GlassEffectService))!;
        var guard = (ShellCrashGuard)services.GetService(typeof(ShellCrashGuard))!;
        var menu = new PilasterContextMenu(glass) { _guard = guard, _shellTarget = shellTarget, _shellKind = shellKind };

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
            menu.AddLoadingPlaceholder();
        }

        menu._menu.PlacementTarget = placementTarget;
        menu._menu.IsOpen = true;

        if (loadsShell)
        {
            _ = menu.LoadShellItemsAsync(shellQuery!, settings);
        }

        return menu;
    }

    /// <summary>Szeparátor + alacsony kontrasztú „Betöltés…" sor, amíg a shell elemek jönnek.</summary>
    private void AddLoadingPlaceholder()
    {
        _loadingSeparator = new Separator();

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

    private void BuildOwnItems(IReadOnlyList<PilasterMenuEntry> entries)
    {
        // A kereső csak akkor jelenik meg, ha van mit szűrni — egy három
        // elemű menü tetején felesleges és zavaró volna.
        if (entries.Count(e => !e.IsSeparator) >= 8)
        {
            _menu.Items.Add(_searchBox);
            _menu.Items.Add(new Separator());
        }

        foreach (var item in Convert(entries))
        {
            _menu.Items.Add(item);
        }
    }

    private IEnumerable<Control> Convert(IReadOnlyList<PilasterMenuEntry> entries)
    {
        foreach (var entry in entries)
        {
            if (entry.IsSeparator)
            {
                yield return new Separator();
                continue;
            }

            var item = new MenuItem
            {
                Header = TranslationSource.Instance[entry.LabelKey],
                IsEnabled = entry.IsEnabled,
                Icon = new SymbolIcon { Symbol = entry.Icon, FontSize = 15 },
            };

            if (entry.SubItems is { Count: > 0 } children)
            {
                foreach (var child in Convert(children))
                {
                    item.Items.Add(child);
                }
            }
            else if (entry.Action is { } action)
            {
                item.Click += (_, _) => action();
            }

            yield return item;
        }
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

        RemoveLoadingPlaceholder();

        if (session is null)
        {
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
            return;
        }

        _menu.Items.Add(new Separator());

        if (settings.ShellItemsInOwnSection)
        {
            _menu.Items.Add(new MenuItem
            {
                Header = TranslationSource.Instance["ContextMenu_OtherApps"],
                IsEnabled = false,
                FontSize = 11,
            });
        }

        foreach (var node in session.Items)
        {
            _menu.Items.Add(ConvertShellNode(node));
        }
    }

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
            Icon = node.Icon is null
                ? null
                : new System.Windows.Controls.Image { Source = node.Icon, Width = 16, Height = 16 },
        };

        // Az alapértelmezett parancsot (amit a dupla kattintás indít) az Intéző
        // félkövéren szedi — mi is.
        if (node.IsDefault)
        {
            item.FontWeight = FontWeights.SemiBold;
        }

        if (node.HasChildren)
        {
            foreach (var child in node.Children)
            {
                item.Items.Add(ConvertShellNode(child));
            }
        }
        else if (node.CommandId != 0)
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

        foreach (var element in _menu.Items.OfType<Control>())
        {
            if (element is Separator)
            {
                // Szűrés közben az elválasztók elrejtése, hogy ne maradjanak
                // magányos vonalak az eltűnt elemek helyén.
                element.Visibility = trimmed.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                continue;
            }

            if (element is not MenuItem menuItem)
            {
                continue;
            }

            var text = menuItem.Header?.ToString() ?? string.Empty;

            menuItem.Visibility = trimmed.Length == 0 || text.Contains(trimmed, StringComparison.CurrentCultureIgnoreCase)
                ? Visibility.Visible
                : Visibility.Collapsed;
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
}
