using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Extensions.DependencyInjection;
using Pilaster.App.Controls;
using Pilaster.App.Converters;
using Pilaster.App.Diagnostics;
using Pilaster.App.Localization;
using Pilaster.App.Services;
using Pilaster.App.ViewModels;
using Pilaster.Core.FileSystem;
using Pilaster.Core.Settings;
using Pilaster.Shell.Devices;
using Pilaster.Shell.Menus;
using Wpf.Ui.Controls;

// A WPF-UI saját ListView/ListBox/Panel típusokat is szállít ugyanezekkel a
// nevekkel. A XAML a WPF beépített vezérlőit példányosítja, ezért a kódban is
// azokra hivatkozunk — az álnév egyértelműsíti, melyikről van szó.
using GridViewColumnHeader = System.Windows.Controls.GridViewColumnHeader;
using ItemsControl = System.Windows.Controls.ItemsControl;
using ListBox = System.Windows.Controls.ListBox;
using ListView = System.Windows.Controls.ListView;
using Panel = System.Windows.Controls.Panel;
using SelectionChangedEventArgs = System.Windows.Controls.SelectionChangedEventArgs;

namespace Pilaster.App.Views;

public partial class MainWindow : FluentWindow
{
    private readonly MainWindowViewModel _viewModel;
    private readonly IServiceProvider _services;
    private readonly ThemeService _theme;
    private readonly ISettingsService _settings;

    /// <summary>A Beállítások ablak, amíg nyitva van — hogy ne nyíljon kettő.</summary>
    private SettingsWindow? _settingsWindow;

    /// <summary>A Lomtár-ablak, amíg nyitva van — hogy ne nyíljon kettő.</summary>
    private RecycleBinWindow? _recycleBinWindow;

    /// <summary>Az F3 (Megtekintés) előnézeti ablaka, amíg nyitva van — hogy ne nyíljon kettő.</summary>
    private FilePreviewWindow? _previewWindow;

    /// <summary>
    /// Igaz, amíg a natív jobbklikk-menü (<see cref="NativeContextMenuService"/>)
    /// nyitva van a külön STA szálon — ne induljon el egy második egymásra,
    /// ha a felhasználó a menü bezáródása előtt újra jobb gombot nyom.
    /// </summary>
    private bool _isNativeContextMenuOpen;

    /// <summary>
    /// A gomb lenyomásának képernyőpontja — a húzás-indítás küszöbének
    /// (<see cref="SystemParameters.MinimumHorizontalDragDistance"/>)
    /// méréséhez. Közös a fájlsorok és az oldalsáv sorai közt, mert
    /// egyszerre csak az egyik húzás-fajta lehet folyamatban.
    /// </summary>
    private System.Windows.Point? _dragStartPoint;

    /// <summary>Egyedi vágólap-formátum a gyorselérés-sorok áthúzásos átrendezéséhez.</summary>
    private const string QuickAccessReorderFormat = "Pilaster.QuickAccessReorder";

    /// <summary>
    /// A fül, amelynek <c>CurrentPath</c> változását épp figyeljük — a csúszó
    /// átmenet ehhez van feliratkozva. Fülváltáskor át kell iratkozni.
    /// </summary>
    private TabViewModel? _trackedTab;

    public MainWindow(MainWindowViewModel viewModel, IServiceProvider services, ThemeService theme)
    {
        _viewModel = viewModel;
        _services = services;
        _theme = theme;
        _settings = services.GetRequiredService<ISettingsService>();
        DataContext = viewModel;

        viewModel.SettingsRequested += OnSettingsRequested;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        viewModel.Updates.RestartRequested += OnUpdateRestartRequested;
        viewModel.EjectCompleted += OnEjectCompleted;

        InitializeComponent();

        // A rendszertéma figyelése: „rendszerkövető" módban a Windows
        // világos/sötét váltása menet közben is átszínezi a felületet.
        Loaded += (_, _) => _theme.WatchSystemTheme(this);

        // Húzásos kijelölés: a WPF ListView/ListBox natívan nem támogatja,
        // lásd Controls/MarqueeSelector.cs. Mindkét nézet (részletes + rács)
        // ugyanazt a viselkedést és átfedő téglalapot osztja meg, mivel
        // egyszerre csak az egyik látható.
        var marquee = new MarqueeSelector(FileListHost, MarqueeRectangle);
        marquee.Attach(DetailsView);
        marquee.Attach(GridViewList);

        TrackTab(_viewModel.SelectedTab);

        // A nézetmód (lista/rács/oszlopok) fülenként eltérhet és mentődik
        // (lásd MainWindowViewModel.AddTab) — induláskor az induló fülhöz
        // tartozó nézetet kell megjeleníteni, nem a XAML-ben alapértelmezett
        // Részleteset.
        SyncViewModeVisuals(_viewModel.SelectedTab);
        ApplyDualPaneOrientation(_viewModel.DualPaneVertical);

        // A munkamenet mentése kilépéskor: a késleltetett beállítás-mentés
        // (JsonSettingsService) még sorban állhat, ezért itt kifejezetten
        // rögzítjük az utolsó állapotot is.
        Closing += (_, _) => _viewModel.SaveSession();
    }

    /// <summary>
    /// Meghajtó-csatlakoztatás/eltávolítás vagy lemezváltás figyelése, hogy
    /// az oldalsáv Meghajtók szekciója (kötetcímke, egyedi lemezikon)
    /// automatikusan frissüljön — nem csak a jobbklikk/Frissítés gombra.
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            source.AddHook(OnWindowMessage);
        }
    }

    private nint OnWindowMessage(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        const int WmDeviceChange = 0x0219;
        const int DbtDeviceArrival = 0x8000;
        const int DbtDeviceRemoveComplete = 0x8004;

        if (msg == WmDeviceChange && (int)wParam is DbtDeviceArrival or DbtDeviceRemoveComplete)
        {
            _viewModel.RefreshDrives();
        }

        return nint.Zero;
    }

    private void OnSettingsRequested(object? sender, EventArgs e)
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = _services.GetRequiredService<SettingsWindow>();
        _settingsWindow.Owner = this;
        _settingsWindow.Closed += OnSettingsWindowClosed;
        _settingsWindow.Show();
    }

    /// <summary>
    /// Egy Mica hátterű, <c>ExtendsContentIntoTitleBar</c> tulajdonságú owned
    /// ablak (itt: Beállítások) bezárásakor a Windows/DWM időnként hibásan a
    /// TULAJDONOS ablakot (ez, a főablak) is leminimalizálja — ismert
    /// owner/owned ablak jelenség, nem az alkalmazás saját hibája. Itt
    /// visszaállítjuk és fókuszba hozzuk, hogy a Beállítások bezárása után a
    /// főablak biztosan nyitva és aktív maradjon.
    /// </summary>
    private void OnSettingsWindowClosed(object? sender, EventArgs e)
    {
        _settingsWindow = null;

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    /// <summary>
    /// Kattintás a breadcrumb sáv ÜRES területén (nem egy szegmens-gombon):
    /// szerkeszthető útvonal-szövegmezőre vált, mint az Intézőben. Egy
    /// szegmensre kattintva a gomb saját <c>OpenBreadcrumbCommand</c>-ja
    /// navigál — ezt itt nem szabad felülírni, ezért a bealagcsövezésnél meg
    /// kell nézni, hogy a kattintás egy gombon (vagy már a szerkesztőmezőn)
    /// történt-e.
    /// </summary>
    private void OnBreadcrumbAreaPreviewLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_viewModel.SelectedTab is not { IsEditingPath: false } tab)
        {
            return;
        }

        if (sender is DependencyObject boundary
            && e.OriginalSource is DependencyObject originalSource
            && HasVisualAncestor<ButtonBase>(boundary, originalSource))
        {
            return;
        }

        tab.BeginEditPathCommand.Execute(null);
    }

    private static bool HasVisualAncestor<T>(DependencyObject boundary, DependencyObject start) where T : DependencyObject
    {
        var current = start;

        while (current is not null && !ReferenceEquals(current, boundary))
        {
            if (current is T)
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    /// <summary>A szerkeszthető útvonalmező automatikusan fókuszba kerül és kijelölődik, amint láthatóvá válik.</summary>
    private void OnPathEditBoxIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true || sender is not TextBox textBox)
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(() =>
        {
            textBox.Focus();
            textBox.SelectAll();
        });
    }

    private void OnPathEditBoxKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_viewModel.SelectedTab is not { } tab)
        {
            return;
        }

        switch (e.Key)
        {
            case System.Windows.Input.Key.Enter:
                e.Handled = true;
                tab.CommitEditPathCommand.Execute(null);
                break;
            case System.Windows.Input.Key.Escape:
                e.Handled = true;
                tab.CancelEditPathCommand.Execute(null);
                break;
        }
    }

    private void OnPathEditBoxLostFocus(object sender, RoutedEventArgs e) =>
        _viewModel.SelectedTab?.CancelEditPathCommand.Execute(null);

    /// <summary>
    /// A névmező fókuszba kerülésekor: mint az Intézőben, csak a NÉV RÉSZ
    /// jelölődik ki (a kiterjesztés nem) — így egy gondatlan Enter nem
    /// veszíti el véletlenül a fájl típusát. Mappáknál/kiterjesztés nélküli
    /// fájloknál a teljes név kijelölődik.
    /// </summary>
    private void OnRenameBoxIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true || sender is not TextBox textBox || textBox.DataContext is not FileSystemItem item)
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(() =>
        {
            textBox.Focus();

            var baseLength = item.EditableName.Length - item.Extension.Length - 1;

            if (item.Kind != FileSystemItemKind.Directory && item.Extension.Length > 0 && baseLength > 0)
            {
                textBox.Select(0, baseLength);
            }
            else
            {
                textBox.SelectAll();
            }
        });
    }

    private void OnRenameBoxKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: FileSystemItem item } || _viewModel.SelectedTab is not { } tab)
        {
            return;
        }

        switch (e.Key)
        {
            case System.Windows.Input.Key.Enter:
                e.Handled = true;
                tab.CommitRenameCommand.Execute(item);
                break;
            case System.Windows.Input.Key.Escape:
                e.Handled = true;
                tab.CancelRenameCommand.Execute(item);
                break;
        }
    }

    /// <summary>
    /// Fókuszvesztés (kattintás máshova): mint az Intézőben, ez ELFOGADJA a
    /// beírt nevet, nem elveti. Ha a mező már nincs szerkesztés alatt (mert
    /// az Enter/Esc épp most zárta le, és ez csak annak visszhangja, hiszen
    /// az elrejtett mező is fókuszt veszít), nincs teendő — enélkül egy Esc
    /// utáni visszhang tévesen újra elmentené a nevet.
    /// </summary>
    private void OnRenameBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox { DataContext: FileSystemItem { IsRenaming: true } item })
        {
            return;
        }

        _viewModel.SelectedTab?.CommitRenameCommand.Execute(item);
    }

    /// <summary>
    /// „Liquid glass" — natív Acrylic háttér a helyi menükön, ha a
    /// Beállításokban be van kapcsolva. Lásd <see cref="GlassEffectService"/>.
    /// </summary>
    private void OnGlassContextMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.ContextMenu menu)
        {
            _services.GetRequiredService<GlassEffectService>().ApplyToContextMenu(menu);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.SelectedTab))
        {
            TrackTab(_viewModel.SelectedTab);
            SyncViewModeVisuals(_viewModel.SelectedTab);
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.DualPaneVertical))
        {
            ApplyDualPaneOrientation(_viewModel.DualPaneVertical);
        }
    }

    private void OnToggleDualPaneClick(object sender, RoutedEventArgs e) =>
        _viewModel.DualPaneEnabled = !_viewModel.DualPaneEnabled;

    /// <summary>
    /// Egymás mellett (vízszintes) vagy egymás alatt (függőleges) — a
    /// panelek/elválasztó Grid.Row/Column-ját közvetlenül állítjuk át,
    /// mert a XAML-nek nincs deklaratív módja "vagy oszlopok, vagy sorok"
    /// elrendezés-váltásra ugyanazon rács belül.
    /// </summary>
    private void ApplyDualPaneOrientation(bool vertical)
    {
        if (vertical)
        {
            DualPaneLeftColumn.Width = new GridLength(1, GridUnitType.Star);
            DualPaneRightColumn.Width = new GridLength(0);
            DualPaneSplitterColumn.Width = new GridLength(0);
            DualPaneSplitterRow.Height = GridLength.Auto;

            System.Windows.Controls.Grid.SetColumn(LeftPaneView, 0);
            System.Windows.Controls.Grid.SetRow(LeftPaneView, 0);
            System.Windows.Controls.Grid.SetColumn(RightPaneView, 0);
            System.Windows.Controls.Grid.SetRow(RightPaneView, 2);

            System.Windows.Controls.Grid.SetColumn(DualPaneSplitter, 0);
            System.Windows.Controls.Grid.SetRow(DualPaneSplitter, 1);
            System.Windows.Controls.Grid.SetColumnSpan(DualPaneSplitter, 1);
            System.Windows.Controls.Grid.SetRowSpan(DualPaneSplitter, 1);
            DualPaneSplitter.Width = double.NaN;
            DualPaneSplitter.Height = 6;
            DualPaneSplitter.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
            DualPaneSplitter.VerticalAlignment = VerticalAlignment.Center;
            DualPaneSplitter.ResizeDirection = System.Windows.Controls.GridResizeDirection.Rows;
        }
        else
        {
            DualPaneSplitterColumn.Width = GridLength.Auto;
            DualPaneTopRow.Height = new GridLength(1, GridUnitType.Star);
            DualPaneBottomRow.Height = new GridLength(0);
            DualPaneSplitterRow.Height = new GridLength(0);

            System.Windows.Controls.Grid.SetColumn(LeftPaneView, 0);
            System.Windows.Controls.Grid.SetRow(LeftPaneView, 0);
            System.Windows.Controls.Grid.SetColumn(RightPaneView, 2);
            System.Windows.Controls.Grid.SetRow(RightPaneView, 0);

            System.Windows.Controls.Grid.SetColumn(DualPaneSplitter, 1);
            System.Windows.Controls.Grid.SetRow(DualPaneSplitter, 0);
            System.Windows.Controls.Grid.SetColumnSpan(DualPaneSplitter, 1);
            System.Windows.Controls.Grid.SetRowSpan(DualPaneSplitter, 3);
            DualPaneSplitter.Width = 6;
            DualPaneSplitter.Height = double.NaN;
            DualPaneSplitter.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
            DualPaneSplitter.VerticalAlignment = VerticalAlignment.Stretch;
            DualPaneSplitter.ResizeDirection = System.Windows.Controls.GridResizeDirection.Columns;
        }

        // Az elrendezés váltása után a mentett arányt a MÁSIK tengelyre kell
        // alkalmazni — enélkül a váltás mindig 50/50-re ugrana vissza.
        ApplySplitRatio();
    }

    /// <summary>Dupla kattintás az elválasztóra: 50/50 arány visszaállítása, mentéssel.</summary>
    private void OnDualPaneSplitterDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _settings.Current.DualPaneSplitRatio = 0.5;
        _settings.Save();
        ApplySplitRatio();
    }

    private void OnLeftPaneActivated(object? sender, EventArgs e) => _viewModel.IsLeftPaneActive = true;

    private void OnRightPaneActivated(object? sender, EventArgs e) => _viewModel.IsLeftPaneActive = false;

    private void OnPaneFilesDropped(object? sender, (IReadOnlyList<string> Paths, string DestinationDir, PaneDropAction Action) e)
    {
        switch (e.Action)
        {
            case PaneDropAction.Copy:
                _viewModel.StartPaneCopy(e.Paths, e.DestinationDir);
                break;

            case PaneDropAction.Move:
                _viewModel.StartPaneMove(e.Paths, e.DestinationDir);
                break;

            case PaneDropAction.Shortcut:
                _viewModel.CreateShortcuts(e.Paths, e.DestinationDir);
                break;
        }
    }

    /// <summary>
    /// Az elválasztó helyzetének mentése.
    /// </summary>
    /// <remarks>
    /// A <c>GridSplitter</c> nem ad „húzás vége" eseményt, a
    /// <c>DragCompleted</c> pedig csak a belső <c>Thumb</c>-on létezik. A
    /// rács saját <c>LayoutUpdated</c>-jére kötni túl gyakori lenne; a
    /// <c>SizeChanged</c> az érintett oszlopokon pontosan akkor tüzel,
    /// amikor a felhasználó elengedi (vagy húzás közben lép), és a
    /// beállítás-mentés amúgy is késleltetett.
    /// </remarks>
    private void OnDualPaneSizeChanged(object sender, SizeChangedEventArgs e) => SaveSplitRatio();

    private void SaveSplitRatio()
    {
        if (!_viewModel.DualPaneEnabled || _isApplyingSplitRatio)
        {
            return;
        }

        var (first, second) = _viewModel.DualPaneVertical
            ? (DualPaneTopRow.Height.Value, DualPaneBottomRow.Height.Value)
            : (DualPaneLeftColumn.Width.Value, DualPaneRightColumn.Width.Value);

        var total = first + second;

        if (total <= 0)
        {
            return;
        }

        var ratio = Math.Clamp(first / total, 0.05, 0.95);

        if (Math.Abs(ratio - _settings.Current.DualPaneSplitRatio) < 0.005)
        {
            return;
        }

        _settings.Current.DualPaneSplitRatio = ratio;
        _settings.Save();
    }

    /// <summary>Igaz, amíg a mentett arányt ÁLLÍTJUK be — enélkül a saját beállítás visszamentése zajt keltene.</summary>
    private bool _isApplyingSplitRatio;

    /// <summary>Az elválasztó programozott mozgatása — az F7 önteszt használja.</summary>
    internal void SetSplitRatioForTest(double ratio)
    {
        if (_viewModel.DualPaneVertical)
        {
            DualPaneTopRow.Height = new GridLength(ratio, GridUnitType.Star);
            DualPaneBottomRow.Height = new GridLength(1 - ratio, GridUnitType.Star);
        }
        else
        {
            DualPaneLeftColumn.Width = new GridLength(ratio, GridUnitType.Star);
            DualPaneRightColumn.Width = new GridLength(1 - ratio, GridUnitType.Star);
        }

        SaveSplitRatio();
    }

    private void ApplySplitRatio()
    {
        var ratio = Math.Clamp(_settings.Current.DualPaneSplitRatio, 0.05, 0.95);

        _isApplyingSplitRatio = true;

        try
        {
            if (_viewModel.DualPaneVertical)
            {
                DualPaneTopRow.Height = new GridLength(ratio, GridUnitType.Star);
                DualPaneBottomRow.Height = new GridLength(1 - ratio, GridUnitType.Star);
            }
            else
            {
                DualPaneLeftColumn.Width = new GridLength(ratio, GridUnitType.Star);
                DualPaneRightColumn.Width = new GridLength(1 - ratio, GridUnitType.Star);
            }
        }
        finally
        {
            _isApplyingSplitRatio = false;
        }
    }

    private void OnPaneDeleteRequested(object? sender, (IReadOnlyList<string> Paths, bool Permanent) e) =>
        _viewModel.StartPaneDelete(e.Paths, e.Permanent);

    /// <summary>
    /// A csúszó átmenet forrását a mindenkori aktív fülre állítja át.
    /// </summary>
    private void TrackTab(TabViewModel? tab)
    {
        if (_trackedTab is not null)
        {
            _trackedTab.PropertyChanged -= OnTrackedTabPropertyChanged;
            _trackedTab.RenameRequested -= OnTrackedTabRenameRequested;
        }

        _trackedTab = tab;

        if (_trackedTab is not null)
        {
            _trackedTab.PropertyChanged += OnTrackedTabPropertyChanged;
            _trackedTab.RenameRequested += OnTrackedTabRenameRequested;
        }
    }

    private void OnTrackedTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TabViewModel.CurrentPath))
        {
            // Kezdőlapra navigálva (vagy onnan elnavigálva) a fül IsHome
            // állapota is változik ugyanekkor — a látható panelt is
            // szinkronizálni kell, nem csak az átmenetet lejátszani.
            SyncViewModeVisuals(_trackedTab);
            PlayContentTransition();
        }
    }

    /// <summary>
    /// Egy elem átnevezés-módba vált (új létrehozás után azonnal, vagy kézi
    /// átnevezéskor) — kijelöli és láthatóvá görgeti a sort. A tényleges
    /// fókuszt/kijelölést a szerkesztőmezőn az OnRenameBoxIsVisibleChanged
    /// adja, amint a virtualizált konténer ténylegesen megjelenik.
    /// </summary>
    /// <remarks>
    /// Oszlopos nézetben egyelőre nincs helyben-szerkesztő UI (lásd
    /// FileNameEditTemplate — csak a Részletes/Rács nézet sablonjaiba van
    /// bekötve), ezért ott ez a metódus nem csinál semmit; az új elem a
    /// normál nevével jelenik meg.
    /// </remarks>
    private void OnTrackedTabRenameRequested(object? sender, FileSystemItem item)
    {
        Selector? selector = _viewModel.SelectedTab?.ViewMode switch
        {
            ViewMode.Grid => GridViewList,
            ViewMode.Details => DetailsView,
            _ => null,
        };

        if (selector is null)
        {
            return;
        }

        selector.SelectedItem = item;

        _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
        {
            switch (selector)
            {
                case ListView listView:
                    listView.ScrollIntoView(item);
                    break;
                case ListBox listBox:
                    listBox.ScrollIntoView(item);
                    break;
            }
        });
    }

    /// <summary>
    /// Csúszó-elhalványuló átmenet lejátszása a fájlterületen, valahányszor a
    /// megnyitott mappa változik — gyorselérésre kattintás, breadcrumb,
    /// vissza/előre, vagy dupla kattintás egy mappára.
    /// </summary>
    private void PlayContentTransition()
    {
        if (!_viewModel.AnimationsEnabled)
        {
            return;
        }

        if (Resources["SlideInFileArea"] is Storyboard storyboard)
        {
            storyboard.Begin(FileAreaBorder);
        }
    }

    /// <summary>
    /// Az overflow („több") gomb bal kattintásra nyissa a menüjét.
    /// </summary>
    /// <remarks>
    /// A <c>ContextMenu</c> alapból csak jobb gombra nyílik, itt viszont a
    /// gomb egyetlen funkciója a menü megnyitása — a felhasználó bal kattintást
    /// várna, és jobb kattintással sosem próbálkozna.
    /// </remarks>
    private void OnOverflowMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { ContextMenu: { } menu } element)
        {
            menu.PlacementTarget = element;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    /// <summary>
    /// Dupla kattintás: mappába lépés, fájlnál megnyitás a társított programmal.
    /// </summary>
    private async void OnItemDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not Selector { SelectedItem: FileSystemItem item })
        {
            return;
        }

        await OpenItemAsync(item);
    }

    /// <summary>
    /// Jobb kattintás fájlelemen: ha a kattintott sor még nincs kijelölve, a
    /// helyi menü kizárólag arra vonatkozzon — ez az Explorer és a legtöbb
    /// fájlkezelő megszokott viselkedése. Ha már a kijelölés része, a meglévő
    /// (esetleg többelemes) kijelölés érintetlen marad.
    /// </summary>
    /// <remarks>
    /// A menü maga a VALÓDI Windows shell jobbklikk-menü — lásd
    /// <see cref="NativeContextMenuService"/> —, a telepített programok
    /// (7-Zip, Git stb.) bejegyzéseivel együtt. Csak akkor esik vissza a
    /// saját, egyszerűbb <c>FileItemContextMenu</c> erőforrásra, ha a natív
    /// hívás valamiért (pl. egy hibás shell-bővítmény miatt) sikertelen.
    ///
    /// A natív hívás egy külön STA szálon fut (lásd <see cref="NativeContextMenuService.ShowAsync"/>),
    /// itt csak <c>await</c>-olva várjuk meg — a WPF Dispatcher emiatt a menü
    /// nyitva léte alatt is fut, nem fagy le az alkalmazás.
    /// </remarks>
    private async void OnItemPreviewRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: FileSystemItem item } container)
        {
            return;
        }

        if (ItemsControl.ItemsControlFromItemContainer(container) is not Selector selector)
        {
            return;
        }

        e.Handled = true;

        if (_isNativeContextMenuOpen)
        {
            return;
        }

        var alreadySelected = selector switch
        {
            ListView view => view.SelectedItems.Contains(item),
            ListBox box => box.SelectedItems.Contains(item),
            _ => false,
        };

        if (!alreadySelected)
        {
            selector.SelectedItem = item;
        }

        var selectedPaths = selector switch
        {
            ListView view => view.SelectedItems.Cast<FileSystemItem>().Select(i => i.FullPath).ToList(),
            ListBox box => box.SelectedItems.Cast<FileSystemItem>().Select(i => i.FullPath).ToList(),
            _ => [item.FullPath],
        };

        // A SAJÁT menü (spec F4): a mi designunk, de a telepített
        // shell-bővítmények elemeivel együtt. A saját elemek azonnal
        // megjelennek, a shell-elemek aszinkron, időkorláttal csúsznak be.
        var extendedVerbs = System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift);

        PilasterContextMenu.Show(
            _services,
            container,
            BuildFileMenuEntries(item, selectedPaths, extendedVerbs),
            (timeout, blacklist) => ShellMenuSession.QueryItemsAsync(selectedPaths, extendedVerbs, timeout, blacklist),
            _settings.Current,
            item.FullPath);

        await Task.CompletedTask;
    }

    /// <summary>
    /// A saját menüelemek fájlokon/mappákon — MINDIG felül, a specifikált fix
    /// sorrendben (spec F4).
    /// </summary>
    private IReadOnlyList<PilasterMenuEntry> BuildFileMenuEntries(
        FileSystemItem item,
        IReadOnlyList<string> selectedPaths,
        bool shiftHeld)
    {
        var dual = _viewModel.DualPaneEnabled;
        var single = selectedPaths.Count == 1;

        return
        [
            new("Cmd_Open", SymbolRegular.Open24, () => _ = OpenItemAsync(item)),
            new("Cmd_OpenNewTab", SymbolRegular.TabAdd24, () => _viewModel.ActivePane.AddTab(item.FullPath), item.IsNavigable),
            new("QuickAccess_OpenOther", SymbolRegular.DualScreen24,
                () => _ = _viewModel.InactivePane.NavigateAsync(item.FullPath), item.IsNavigable && dual),
            new("Cmd_OpenWith", SymbolRegular.AppGeneric24, () => OpenWithDialog(item.FullPath), !item.IsNavigable),

            new("Cmd_EditWithPilaster", SymbolRegular.Code24, () => OpenInEditor(item.FullPath), !item.IsNavigable),

            PilasterMenuEntry.Separator,

            new("Cmd_Cut", SymbolRegular.Cut24, () => _viewModel.CutSelectionCommand.Execute(selectedPaths)),
            new("Cmd_Copy", SymbolRegular.DocumentCopy24, () => _viewModel.CopySelectionCommand.Execute(selectedPaths)),
            new("Cmd_Paste", SymbolRegular.ClipboardPaste24, () => _viewModel.PasteCommand.Execute(null)),
            new("Cmd_CreateShortcut", SymbolRegular.Link24, () => CreateShortcutsHere(selectedPaths)),

            PilasterMenuEntry.Separator,

            new("Keymap_Rename", SymbolRegular.Rename24, () => GetActiveTab()?.BeginRename(item), single),
            new(shiftHeld ? "Cmd_DeletePermanently" : "Cmd_Delete", SymbolRegular.Delete24,
                () => _viewModel.DeleteSelectionCommand.Execute((selectedPaths, shiftHeld))),

            PilasterMenuEntry.Separator,

            new("Cmd_CopyPath", SymbolRegular.Copy24, () => CopyTextToClipboard(item.FullPath)),
            new("Cmd_CopyName", SymbolRegular.Copy24, () => CopyTextToClipboard(item.Name)),
            new("Cmd_OpenTerminal", SymbolRegular.WindowConsole20, () => OpenTerminalAt(item)),
            new("Cmd_PinToQuickAccess", SymbolRegular.Pin24,
                () => _viewModel.PinToQuickAccessCommand.Execute(item.FullPath), item.IsNavigable),
            new("Cmd_Tags", SymbolRegular.Tag24, () => ShowTagPickerFor(item, null)),

            PilasterMenuEntry.Separator,

            new("Cmd_ShowInExplorer", SymbolRegular.Folder24, () => ShowInExplorer(item.FullPath)),
            new("Cmd_Properties", SymbolRegular.Info24, () => ShowProperties(item.FullPath)),
        ];
    }

    private void CopyTextToClipboard(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // A vágólapot időnként egy másik folyamat zárolja — csendben kihagyjuk.
        }
    }

    private void OpenWithDialog(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "openas" });
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }

    /// <summary>
    /// Megnyitás a BEÉPÍTETT szerkesztővel (F4 a Pilaster Classic
    /// kiosztásban, Ctrl+E mindkettőben, és a jobbklikk-menü „Szerkesztés
    /// Pilaster Editorral" pontja).
    /// </summary>
    private async Task OpenInEditorAsync(string path)
    {
        var editor = _services.GetRequiredService<EditorWindow>();

        // Egyetlen példány: a fülei túlélik az ablak bezárását-újranyitását.
        if (!editor.IsLoaded)
        {
            editor.Owner = this;
            editor.Show();
        }
        else
        {
            editor.Activate();
        }

        if (!await _services.GetRequiredService<EditorViewModel>().OpenAsync(path))
        {
            // Bináris tartalom: a szerkesztő nem nyitja meg — az F3 előnézet
            // hexdumpja viszont igen (spec F2).
            await ViewFileAsync(path);
        }
    }

    private void OpenInEditor(string path) => _ = OpenInEditorAsync(path);

    private void CreateShortcutsHere(IReadOnlyList<string> paths)
    {
        if (GetActiveTab()?.CurrentPath is { } directory)
        {
            _viewModel.CreateShortcuts(paths, directory);
        }
    }

    /// <summary>„Terminál megnyitása itt" — mappánál abban, fájlnál a tartalmazó mappában.</summary>
    private void OpenTerminalAt(FileSystemItem item)
    {
        var directory = item.IsNavigable ? item.FullPath : System.IO.Path.GetDirectoryName(item.FullPath);

        if (string.IsNullOrEmpty(directory))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(_settings.Current.ExternalTerminalPath)
            {
                UseShellExecute = true,
                WorkingDirectory = directory,
            });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // A beállított terminál nem található — a Beállításokban javítható.
        }
    }

    private void ShowInExplorer(string path)
    {
        try
        {
            Process.Start("explorer.exe", $"/select,\"{path}\"");
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }

    private void ShowProperties(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "properties" });
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }

    /// <summary>
    /// A fájlsoron megjelenő címke-ikon kattintása: kipipálható listát mutat
    /// a létrehozott címkékből, ki-/bejelölésre azonnal hozzáadja/eltávolítja
    /// az adott elemen. Új címke létrehozása a Beállításokban történik, nem
    /// itt — lásd a v0.7 feladatlista 5. pontját.
    /// </summary>
    /// <remarks>
    /// Ez SAJÁT, WPF-es menü (nem a natív shell menü), ezért itt szabadon
    /// bővíthető egyedi tartalommal — a natív <see cref="NativeContextMenuService"/>
    /// menüje ezt nem tenné lehetővé.
    /// </remarks>
    private void OnTagPickerClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is FileSystemItem item)
        {
            ShowTagPickerFor(item, (UIElement)sender);
        }
    }

    private void ShowTagPickerFor(FileSystemItem item, UIElement? placementTarget)
    {
        var metadata = _services.GetRequiredService<FileMetadataService>();
        var menu = new System.Windows.Controls.ContextMenu();

        if (metadata.Tags.Count == 0)
        {
            menu.Items.Add(new System.Windows.Controls.MenuItem
            {
                Header = TranslationSource.Instance["Tags_None"],
                IsEnabled = false,
            });
        }
        else
        {
            var currentTagIds = item.Tags.Select(t => t.Id).ToHashSet();

            foreach (var tag in metadata.Tags)
            {
                var menuItem = new System.Windows.Controls.MenuItem
                {
                    Header = tag.Name,
                    IsCheckable = true,
                    IsChecked = currentTagIds.Contains(tag.Id),
                    Icon = new TagSwatch { TagColor = tag.Color, ColorHex = tag.ColorHex },
                };

                menuItem.Click += (_, _) =>
                {
                    if (menuItem.IsChecked)
                    {
                        metadata.AddTag(item.FullPath, tag.Id);
                    }
                    else
                    {
                        metadata.RemoveTag(item.FullPath, tag.Id);
                    }
                };

                menu.Items.Add(menuItem);
            }
        }

        menu.PlacementTarget = placementTarget ?? this;
        menu.IsOpen = true;
    }

    /// <summary>
    /// Jobb kattintás a lista/rács ÜRES területén (nem egy elemen): a mappa
    /// VALÓDI Windows háttér-menüjét jeleníti meg (Nézet, Rendezés,
    /// Frissítés, Beillesztés, Új &gt; stb.) — ugyanaz, mint az Intézőben.
    /// </summary>
    /// <remarks>
    /// Ez a kezelő a <see cref="ListView"/>/<see cref="ListBox"/> konténeren
    /// van feliratkozva, tehát a bealagcsövezésnél (tunneling) KORÁBBAN fut
    /// le, mint a soron/csempén lévő <see cref="OnItemPreviewRightButtonDown"/>.
    /// Ezért itt meg kell nézni, hogy a kattintás egy elemen történt-e — ha
    /// igen, nem szabad kezelni, hogy az esemény továbbjuthasson lefelé az
    /// elem saját kezelőjéhez.
    /// </remarks>
    private async void OnEmptyAreaPreviewRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not ItemsControl itemsControl)
        {
            return;
        }

        if (e.OriginalSource is DependencyObject originalSource
            && ItemsControl.ContainerFromElement(itemsControl, originalSource) is not null)
        {
            return;
        }

        e.Handled = true;

        if (_isNativeContextMenuOpen)
        {
            return;
        }

        if (_viewModel.SelectedTab?.CurrentPath is not { } currentPath)
        {
            return;
        }

        var screenPoint = PointToScreen(e.GetPosition(this));
        var ownerHandle = new WindowInteropHelper(this).Handle;

        _isNativeContextMenuOpen = true;

        bool shown;

        try
        {
            shown = await NativeContextMenuService.ShowBackgroundAsync(currentPath, (int)screenPoint.X, (int)screenPoint.Y, ownerHandle);
        }
        finally
        {
            _isNativeContextMenuOpen = false;
        }

        if (!shown && TryFindResource("EmptyAreaContextMenu") is System.Windows.Controls.ContextMenu fallbackMenu)
        {
            fallbackMenu.PlacementTarget = itemsControl;
            fallbackMenu.IsOpen = true;
        }
    }

    private async void OnOpenItemClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is FileSystemItem item)
        {
            await OpenItemAsync(item);
        }
    }

    /// <summary>
    /// A jelenleg látható nézet (Részletes/Rács) teljes kijelölése — a
    /// Másolás/Kivágás/Törlés a teljes kijelölésen dolgozik, nem csak azon az
    /// elemen, amire jobbklikkeltek (ahogy az Intézőben is).
    /// </summary>
    private List<string> GetSelectedFilePaths()
    {
        if (DetailsView.Visibility == Visibility.Visible)
        {
            return [.. DetailsView.SelectedItems.Cast<FileSystemItem>().Select(i => i.FullPath)];
        }

        if (GridViewList.Visibility == Visibility.Visible)
        {
            return [.. GridViewList.SelectedItems.Cast<FileSystemItem>().Select(i => i.FullPath)];
        }

        if (_viewModel.SelectedTab?.ColumnsSelectedFile is { } columnsFile)
        {
            return [columnsFile.FullPath];
        }

        return [];
    }

    private void OnCopyItemClick(object sender, RoutedEventArgs e) =>
        _viewModel.CopySelectionCommand.Execute(GetSelectedFilePaths());

    private void OnCutItemClick(object sender, RoutedEventArgs e) =>
        _viewModel.CutSelectionCommand.Execute(GetSelectedFilePaths());

    private void OnDeleteItemClick(object sender, RoutedEventArgs e) =>
        _viewModel.DeleteSelectionCommand.Execute((GetSelectedFilePaths(), false));

    /// <summary>
    /// Ctrl+C/Ctrl+X/Ctrl+V/Delete/Shift+Delete — a fájllista területén
    /// bárhol működik, a jelenlegi kijelölésen. Szándékosan a fájlterület
    /// Gridjén (nem az egész ablakon), hogy szövegmezőkben (keresés,
    /// átnevezés, útvonalszerkesztő) a Ctrl+C/V a normál szövegműveletet
    /// végezze, ne fájlműveletet indítson.
    /// </summary>
    private void OnFileListHostPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_viewModel.SelectedTab is not { IsHome: false })
        {
            return;
        }

        var ctrl = System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control);
        var shift = System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift);

        switch (e.Key)
        {
            case System.Windows.Input.Key.C when ctrl:
                e.Handled = true;
                _viewModel.CopySelectionCommand.Execute(GetSelectedFilePaths());
                break;

            case System.Windows.Input.Key.X when ctrl:
                e.Handled = true;
                _viewModel.CutSelectionCommand.Execute(GetSelectedFilePaths());
                break;

            case System.Windows.Input.Key.V when ctrl:
                e.Handled = true;
                _viewModel.PasteCommand.Execute(null);
                break;

            case System.Windows.Input.Key.Delete:
                e.Handled = true;
                _viewModel.DeleteSelectionCommand.Execute((GetSelectedFilePaths(), shift));
                break;
        }
    }

    /// <summary>
    /// Pilaster Classic billentyűkiosztás — csak akkor avatkozik be,
    /// ha a felhasználó a Beállításokban bekapcsolta (lásd
    /// <see cref="AppSettings.Keymap"/>). Kikapcsolva
    /// a hagyományos, Intéző-szerű gyorsbillentyűk (lásd
    /// <see cref="OnFileListHostPreviewKeyDown"/> és a többi meglévő kezelő)
    /// változatlanul működnek, ez a metódus el sem éri a switch-et.
    /// </summary>
    /// <remarks>
    /// Ablakszintű, bealagcsövező (Preview) esemény: a fájllista/szövegmezők
    /// saját kezelőinél KORÁBBAN fut le. Ezért itt a legelső lépés kizárni a
    /// szövegszerkesztés alatt álló mezőket (átnevezés, útvonalszerkesztő,
    /// gyorsszűrő) — különben pl. egy F2 közben begépelt szöveg helyett a
    /// billentyűkiosztás próbálná értelmezni a lenyomott billentyűt.
    /// </remarks>
    private void OnMainPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (System.Windows.Input.Keyboard.FocusedElement is System.Windows.Controls.TextBox)
        {
            return;
        }

        var ctrl = System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control);
        var shift = System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift);
        var alt = System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Alt);

        // Ctrl+E MINDKÉT kiosztásban megnyitja a beépített szerkesztőt — csak
        // az F4 az, ami a Pilaster Classic kiosztás sajátja (spec F2).
        if (e.Key == System.Windows.Input.Key.E && ctrl)
        {
            e.Handled = true;
            EditActiveSelection();
            return;
        }

        // Alt+F5 (mindkét panel frissítése) MINDKÉT presetben él — lásd az
        // alábbi Alt-ágat, ami a preset-ellenőrzés ELŐTT fut.
        if (alt && e.SystemKey == System.Windows.Input.Key.F5)
        {
            e.Handled = true;
            _ = _viewModel.RefreshBothPanesCommand.ExecuteAsync(null);
            return;
        }

        if (_settings.Current.Keymap != KeymapPreset.PilasterClassic)
        {
            // Pilaster Modern: az Explorer/böngésző konvenció. A Ctrl+R és az
            // F5 is FRISSÍT — a Classic ág panel-műveletei (F5 másolás,
            // Ctrl+R útvonal-átadás) itt nem foglalják le ezeket (spec K2).
            switch (e.Key)
            {
                case System.Windows.Input.Key.R when ctrl:
                case System.Windows.Input.Key.F5:
                    e.Handled = true;
                    RefreshActiveTab();
                    break;
            }

            return;
        }

        // Alt+F7 / Alt+F5 — az Alt-tal lenyomott billentyűt a rendszer
        // e.SystemKey-ben adja át, e.Key ilyenkor System marad, ezért külön ág.
        if (alt)
        {
            switch (e.SystemKey)
            {
                case System.Windows.Input.Key.F7:
                    e.Handled = true;
                    QuickFilterBox.Focus();
                    QuickFilterBox.SelectAll();
                    return;

            }
        }

        switch (e.Key)
        {
            case System.Windows.Input.Key.Tab when _viewModel.DualPaneEnabled && !ctrl && !alt:
                e.Handled = true;
                _viewModel.IsLeftPaneActive = !_viewModel.IsLeftPaneActive;
                FocusActivePaneList();
                break;

            case System.Windows.Input.Key.F3:
                e.Handled = true;
                _ = ViewActiveSelectionAsync();
                break;

            case System.Windows.Input.Key.F4:
                e.Handled = true;
                EditActiveSelection();
                break;


            case System.Windows.Input.Key.F5:
                e.Handled = true;
                _ = StartTcTransferAsync(isMove: false);
                break;

            case System.Windows.Input.Key.F6:
                e.Handled = true;
                _ = StartTcTransferAsync(isMove: true);
                break;

            case System.Windows.Input.Key.F7:
                e.Handled = true;
                CreateFolderInActivePane();
                break;

            case System.Windows.Input.Key.F8:
                e.Handled = true;
                DeleteActiveSelection(permanent: false);
                break;

            case System.Windows.Input.Key.Delete:
                e.Handled = true;
                DeleteActiveSelection(permanent: shift);
                break;

            case System.Windows.Input.Key.F2:
                e.Handled = true;
                RenameActiveSelection();
                break;

            case System.Windows.Input.Key.Insert:
                e.Handled = true;
                MarkCurrentAndAdvance();
                break;

            case System.Windows.Input.Key.Space:
                e.Handled = true;
                ToggleCurrentSelection();
                break;

            case System.Windows.Input.Key.A when ctrl:
                e.Handled = true;
                GetActiveList()?.SelectAll();
                break;

            case System.Windows.Input.Key.D when ctrl:
            case System.Windows.Input.Key.Subtract:
                e.Handled = true;
                GetActiveList()?.UnselectAll();
                break;

            case System.Windows.Input.Key.Multiply:
                e.Handled = true;
                InvertActiveSelection();
                break;

            // Panelműveletek — CSAK a Pilaster Classic presetben. A Ctrl+R itt
            // breaking változás a v0.9-hez képest (addig frissítés volt), de a
            // klasszikus kétpaneles konvenciót követi; a frissítés
            // Ctrl+Shift+R-re és Alt+F5-re került. A Pilaster Modern presetben
            // a Ctrl+R változatlanul frissít (lásd fentebb, spec K2).
            case System.Windows.Input.Key.U when ctrl:
                e.Handled = true;
                _viewModel.SwapPanesCommand.Execute(null);
                break;

            case System.Windows.Input.Key.L when ctrl:
                e.Handled = true;
                _ = _viewModel.CopyLeftPathToRightCommand.ExecuteAsync(null);
                break;

            case System.Windows.Input.Key.R when ctrl && shift:
                e.Handled = true;
                RefreshActiveTab();
                break;

            case System.Windows.Input.Key.R when ctrl:
                e.Handled = true;
                _ = _viewModel.CopyRightPathToLeftCommand.ExecuteAsync(null);
                break;

            // Panelenkénti fülkezelés — mindig az AKTÍV panelre hat.
            case System.Windows.Input.Key.T when ctrl:
                e.Handled = true;
                _viewModel.NewTabCommand.Execute(null);
                break;

            case System.Windows.Input.Key.W when ctrl:
                e.Handled = true;
                _viewModel.CloseTabCommand.Execute(_viewModel.SelectedTab);
                break;

            case System.Windows.Input.Key.Tab when ctrl && shift:
                e.Handled = true;
                _viewModel.PreviousTabCommand.Execute(null);
                break;

            case System.Windows.Input.Key.Tab when ctrl:
                e.Handled = true;
                _viewModel.NextTabCommand.Execute(null);
                break;
        }
    }

    /// <summary>
    /// Az „aktív" fájllista — egyablakos nézetben a látható Részletes/Rács
    /// nézet, kétablakos nézetben az aktív panel belső listája. Oszlopos
    /// nézetben (Columns) szándékosan <c>null</c>-t ad: a Pilaster Classic
    /// billentyűk ott nem értelmezettek.
    /// </summary>
    private ListBox? GetActiveList()
    {
        if (_viewModel.DualPaneEnabled)
        {
            return _viewModel.IsLeftPaneActive ? LeftPaneView.SelectionList : RightPaneView.SelectionList;
        }

        if (DetailsView.Visibility == Visibility.Visible)
        {
            return DetailsView;
        }

        if (GridViewList.Visibility == Visibility.Visible)
        {
            return GridViewList;
        }

        return null;
    }

    private TabViewModel? GetActiveTab() =>
        _viewModel.DualPaneEnabled ? _viewModel.ActivePaneTab : _viewModel.SelectedTab is { IsHome: false } tab ? tab : null;

    /// <summary>
    /// A billentyűzet-fókusz alatt álló („kurzor alatti") elem — Total
    /// kétpaneles kezelőkben ez a keret, ami függetlenül mozog a tényleges (be- vagy
    /// kijelölt) kijelöléstől. Ha semmi nincs fókuszban (pl. a lista most
    /// kapta a fókuszt), a jelenlegi kijelölésre esik vissza.
    /// </summary>
    private static FileSystemItem? GetFocusedItem(ListBox list)
    {
        if (System.Windows.Input.Keyboard.FocusedElement is DependencyObject focused
            && FindVisualAncestor<System.Windows.Controls.ListBoxItem>(focused) is { DataContext: FileSystemItem item })
        {
            return item;
        }

        return list.SelectedItem as FileSystemItem;
    }

    private static T? FindVisualAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null and not T)
        {
            source = VisualTreeHelper.GetParent(source);
        }

        return source as T;
    }

    private void FocusActivePaneList()
    {
        var paneView = _viewModel.IsLeftPaneActive ? LeftPaneView : RightPaneView;
        paneView.SelectionList.Focus();
    }

    /// <summary>F3 — csak olvasható előnézet a fókuszban lévő fájlról, lásd <see cref="FilePreviewWindow"/>.</summary>
    private async Task ViewActiveSelectionAsync()
    {
        if (GetActiveList() is not { } list || GetFocusedItem(list) is not { Kind: FileSystemItemKind.File } item)
        {
            return;
        }

        if (_previewWindow is not { IsLoaded: true })
        {
            _previewWindow = _services.GetRequiredService<FilePreviewWindow>();
            _previewWindow.Owner = this;
            _previewWindow.Closed += (_, _) => _previewWindow = null;
            _previewWindow.Show();
        }
        else
        {
            _previewWindow.Activate();
        }

        await _previewWindow.LoadAsync(item);
    }

    /// <summary>F4 / Ctrl+E — a fókuszban lévő fájl megnyitása a beépített Pilaster Editorral.</summary>
    private void EditActiveSelection()
    {
        if (GetActiveList() is { } list && GetFocusedItem(list) is { Kind: FileSystemItemKind.File } item)
        {
            _ = OpenInEditorAsync(item.FullPath);
        }
    }

    /// <summary>Egy fájl megnyitása az F3 előnézetben — útvonal alapján.</summary>
    private async Task ViewFileAsync(string path)
    {
        if (GetActiveTab()?.Items.FirstOrDefault(i =>
                string.Equals(i.FullPath, path, StringComparison.OrdinalIgnoreCase)) is not { } item)
        {
            return;
        }

        if (_previewWindow is not { IsLoaded: true })
        {
            _previewWindow = _services.GetRequiredService<FilePreviewWindow>();
            _previewWindow.Owner = this;
            _previewWindow.Closed += (_, _) => _previewWindow = null;
            _previewWindow.Show();
        }

        await _previewWindow.LoadAsync(item);
    }

    /// <summary>F5/F6 — megerősítő párbeszéd a célmappáról, majd a tényleges átvitel indítása, lásd <see cref="MainWindowViewModel.BeginTransfer"/>.</summary>
    private async Task StartTcTransferAsync(bool isMove)
    {
        if (GetActiveTab() is not { } tab || GetActiveList() is not { } list)
        {
            return;
        }

        var paths = list.SelectedItems.Cast<FileSystemItem>().Select(i => i.FullPath).ToList();

        if (paths.Count == 0)
        {
            return;
        }

        // A párbeszéd a MÁSIK panel útvonalát ajánlja fel célnak (spec F7) —
        // egypaneles nézetben, vagy ha a másik panel még nem navigált, a saját
        // mappára esik vissza, amiből a BeginTransfer átnevezést csinál.
        var initialTarget = _viewModel.DualPaneEnabled
            ? _viewModel.InactivePaneTab?.CurrentPath ?? tab.CurrentPath
            : tab.CurrentPath;

        if (initialTarget is null)
        {
            return;
        }

        var dialog = _services.GetRequiredService<TransferConfirmWindow>();
        dialog.Owner = this;
        dialog.Initialize(isMove, paths.Count, initialTarget);

        if (dialog.ShowDialog() == true && dialog.ConfirmedTarget is { } target)
        {
            _viewModel.BeginTransfer(tab, paths, target, isMove);
        }
    }

    /// <summary>F7 — új mappa az aktív panelben/fülben, a v0.8-as azonnali átnevezéssel.</summary>
    private void CreateFolderInActivePane()
    {
        if (GetActiveTab() is { } tab)
        {
            _ = _viewModel.CreateNewFolderInTabAsync(tab);
        }
    }

    /// <summary>F8 (Lomtárba)/Delete/Shift+Delete (véglegesen) — a fókuszban lévő panel/fül teljes kijelölésén.</summary>
    private void DeleteActiveSelection(bool permanent)
    {
        if (GetActiveList() is not { } list)
        {
            return;
        }

        var paths = list.SelectedItems.Cast<FileSystemItem>().Select(i => i.FullPath).ToList();

        if (paths.Count > 0)
        {
            _viewModel.StartPaneDelete(paths, permanent);
        }
    }

    /// <summary>F2 — a fókuszban lévő elem helyben-átnevezése.</summary>
    private void RenameActiveSelection()
    {
        if (GetActiveTab() is not { } tab || GetActiveList() is not { } list || GetFocusedItem(list) is not { } item)
        {
            return;
        }

        tab.BeginRename(item);
    }

    /// <summary>Insert — a fókuszban lévő elem kijelölése (ha még nem az), majd a fókusz a következőre lép.</summary>
    private void MarkCurrentAndAdvance()
    {
        if (GetActiveList() is not { } list || GetFocusedItem(list) is not { } item)
        {
            return;
        }

        if (!list.SelectedItems.Contains(item))
        {
            list.SelectedItems.Add(item);
        }

        var index = list.Items.IndexOf(item);

        if (index < 0 || index + 1 >= list.Items.Count)
        {
            return;
        }

        var next = list.Items[index + 1];
        list.ScrollIntoView(next);

        _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
        {
            if (list.ItemContainerGenerator.ContainerFromItem(next) is System.Windows.Controls.ListBoxItem container)
            {
                container.Focus();
            }
        });
    }

    /// <summary>Space — a fókuszban lévő elem kijelölésének átbillentése, a fókusz mozgatása nélkül.</summary>
    private void ToggleCurrentSelection()
    {
        if (GetActiveList() is not { } list || GetFocusedItem(list) is not { } item)
        {
            return;
        }

        if (list.SelectedItems.Contains(item))
        {
            list.SelectedItems.Remove(item);
        }
        else
        {
            list.SelectedItems.Add(item);
        }
    }

    /// <summary>Num* — a kijelölés megfordítása: minden kijelölt kijelöletlenné válik, és fordítva.</summary>
    private void InvertActiveSelection()
    {
        if (GetActiveList() is not { } list)
        {
            return;
        }

        var currentlySelected = list.SelectedItems.Cast<object>().ToList();
        var toSelect = list.Items.Cast<object>().Where(i => !currentlySelected.Contains(i)).ToList();

        list.SelectedItems.Clear();

        foreach (var item in toSelect)
        {
            list.SelectedItems.Add(item);
        }
    }

    /// <summary>Ctrl+R — kifejezett frissítés, szándékosan külön az F5-től (ami a billentyűkiosztásban Másolás).</summary>
    private void RefreshActiveTab()
    {
        if (GetActiveTab() is { } tab)
        {
            _ = tab.RefreshCommand.ExecuteAsync(null);
        }
    }

    // A kétablakos nézet alján megjelenő funkcióbillentyű-sáv gombjai —
    // ugyanazokat a metódusokat hívják, mint a billentyűzet-lenyomás, lásd
    // OnMainPreviewKeyDown/ShowFunctionKeyBar.
    private void OnFKeyViewClick(object sender, RoutedEventArgs e) => _ = ViewActiveSelectionAsync();

    private void OnFKeyEditClick(object sender, RoutedEventArgs e) => EditActiveSelection();

    private void OnFKeyCopyClick(object sender, RoutedEventArgs e) => _ = StartTcTransferAsync(isMove: false);

    private void OnFKeyMoveClick(object sender, RoutedEventArgs e) => _ = StartTcTransferAsync(isMove: true);

    private void OnFKeyNewFolderClick(object sender, RoutedEventArgs e) => CreateFolderInActivePane();

    private void OnFKeyDeleteClick(object sender, RoutedEventArgs e) => DeleteActiveSelection(permanent: false);

    private void OnCopyPathClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not FileSystemItem item)
        {
            return;
        }

        try
        {
            Clipboard.SetText(item.FullPath);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // A vágólapot időnként egy másik folyamat zárolja — nincs jobb
            // teendő, mint csendben kihagyni, mintsem hibaüzenettel zavarni.
        }
    }

    private void OnPinToQuickAccessClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is FileSystemItem item)
        {
            _viewModel.PinToQuickAccessCommand.Execute(item.FullPath);
        }
    }

    private void OnShowInExplorerClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not FileSystemItem item)
        {
            return;
        }

        try
        {
            Process.Start("explorer.exe", $"/select,\"{item.FullPath}\"");
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }

    private void OnShowPropertiesClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not FileSystemItem item)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(item.FullPath) { UseShellExecute = true, Verb = "properties" });
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }

    /// <summary>
    /// Egy elem megnyitása: mappánál/meghajtónál navigáció, fájlnál a
    /// társított programmal indítás. Ezt hívja a dupla kattintás és a helyi
    /// menü „Megnyitás" pontja is, hogy a viselkedés egy helyen éljen.
    /// </summary>
    private async Task OpenItemAsync(FileSystemItem item)
    {
        if (item.IsNavigable)
        {
            if (_viewModel.SelectedTab is { } tab)
            {
                await tab.NavigateAsync(item.FullPath);
            }

            return;
        }

        OpenWithShell(item.FullPath);
    }

    /// <summary>
    /// Fájl megnyitása az alapértelmezett társított alkalmazással.
    /// </summary>
    /// <remarks>
    /// A <c>UseShellExecute</c> szándékos: enélkül a .NET közvetlenül próbálná
    /// futtatni a fájlt, ami csak végrehajtható állományoknál működne.
    /// </remarks>
    private static void OpenWithShell(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Nincs társított program, vagy a felhasználó elvetette a
            // „Megnyitás ezzel" párbeszédet — mindkettő normális eset.
        }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel.SelectedTab is not { } tab || sender is not Selector selector)
        {
            return;
        }

        var selected = selector switch
        {
            ListView view => view.SelectedItems.Cast<FileSystemItem>().ToList(),
            ListBox box => box.SelectedItems.Cast<FileSystemItem>().ToList(),
            _ => [],
        };

        // Fájloknál SizeBytes, mappáknál a háttérben számolt
        // ComputedFolderSize — mindkettő -1, amíg nincs (még) ismert érték,
        // azt nem szabad beleszámolni az összegbe.
        var totalBytes = selected
            .Select(item => item.Kind == FileSystemItemKind.Directory ? item.ComputedFolderSize : item.SizeBytes)
            .Where(size => size > 0)
            .Sum();

        tab.UpdateStatus(selected.Count, totalBytes);
    }

    private async void OnSidebarSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox { SelectedItem: SidebarItemViewModel item })
        {
            return;
        }

        // A kijelölés elengedése, hogy ugyanarra a helyre ismét lehessen lépni,
        // és hogy ne maradjon két szekcióban egyszerre kiemelt sor.
        ((ListBox)sender).SelectedItem = null;

        // Hiányzó kedvenc (a célja már nem létezik): navigáció helyett a sor
        // saját eltávolító gombja ajánlja fel a törlést — lásd IsMissing.
        if (item.IsMissing)
        {
            return;
        }

        if (item.IsRecycleBin)
        {
            OpenRecycleBinWindow();
            return;
        }

        if (_viewModel.SelectedTab is { } tab)
        {
            await tab.NavigateAsync(item.Path);
        }
    }

    /// <summary>
    /// A Lomtár-ablak megnyitása — nem modális, és nem nyílik meg kettő
    /// egyszerre (ugyanaz a minta, mint a Beállításoknál).
    /// </summary>
    private void OpenRecycleBinWindow()
    {
        if (_recycleBinWindow is { IsLoaded: true })
        {
            _recycleBinWindow.Activate();
            return;
        }

        _recycleBinWindow = _services.GetRequiredService<RecycleBinWindow>();
        _recycleBinWindow.Owner = this;

        _recycleBinWindow.Closed += (_, _) =>
        {
            _recycleBinWindow = null;

            // Ürítés/visszaállítás/végleges törlés után a Gyors elérés
            // „üres" jelzése elavulttá válhatott.
            _viewModel.RefreshQuickAccess();
        };

        _recycleBinWindow.Show();
    }

    private void OnFileRowPreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
        _dragStartPoint = e.GetPosition(null);

    /// <summary>
    /// Mappa húzásának indítása a fájllistából — a gyorselérés panelre
    /// ejtve rögzíti (lásd <see cref="OnSidebarDrop"/>). A szabványos
    /// <see cref="System.Windows.DataFormats.FileDrop"/> formátumot
    /// használja, tehát mellékesen valódi Explorer-ablakra húzva is működne.
    /// </summary>
    private void OnFileRowPreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed || _dragStartPoint is not { } start)
        {
            return;
        }

        var current = e.GetPosition(null);

        if (Math.Abs(current.X - start.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _dragStartPoint = null;

        if (sender is not FrameworkElement { DataContext: FileSystemItem { IsNavigable: true } item } container)
        {
            return;
        }

        var data = new System.Windows.DataObject(System.Windows.DataFormats.FileDrop, new[] { item.FullPath });
        DragDrop.DoDragDrop(container, data, DragDropEffects.Link);
    }

    private void OnSidebarItemPreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);

        // A jobb gomb nem indít húzást, viszont a soron menüt nyit — a
        // PreviewMouseLeftButtonDown csak a bal gombra fut, ezért a
        // jobbklikk-menü külön kezelőben él (OnSidebarItemRightClick).
    }

    /// <summary>
    /// Jobbklikk a „Gyorselérés" fejlécen → a szerkesztő megnyitása (spec F5).
    /// Más szekciók fejlécén nincs menü.
    /// </summary>
    private void OnSidebarHeaderRightClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: "Nav_QuickAccess" } header)
        {
            return;
        }

        e.Handled = true;

        var strings = TranslationSource.Instance;
        var menu = new System.Windows.Controls.ContextMenu { PlacementTarget = header };
        var edit = new System.Windows.Controls.MenuItem { Header = strings["QuickAccess_Edit"] };

        edit.Click += (_, _) => OpenQuickAccessEditor();
        menu.Items.Add(edit);
        menu.IsOpen = true;
    }

    /// <summary>
    /// Jobbklikk egy gyorselérés-soron: megnyitás (új fülön / másik panelen),
    /// átnevezés, ikon módosítása, mozgatás, eltávolítás, szerkesztő (spec F5).
    /// </summary>
    private void OnSidebarItemRightClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SidebarItemViewModel item } row)
        {
            return;
        }

        e.Handled = true;

        var strings = TranslationSource.Instance;
        var menu = new System.Windows.Controls.ContextMenu { PlacementTarget = row };

        void Add(string header, Action action, bool enabled = true)
        {
            var entry = new System.Windows.Controls.MenuItem { Header = header, IsEnabled = enabled };
            entry.Click += (_, _) => action();
            menu.Items.Add(entry);
        }

        if (!item.IsRecycleBin && !item.IsSeparator)
        {
            Add(strings["Cmd_Open"], () => _ = _viewModel.ActivePane.NavigateAsync(item.Path), !item.IsMissing);
            Add(strings["Cmd_OpenNewTab"], () => _viewModel.ActivePane.AddTab(item.Path), !item.IsMissing);
            Add(strings["QuickAccess_OpenOther"], () => _ = _viewModel.InactivePane.NavigateAsync(item.Path),
                !item.IsMissing && _viewModel.DualPaneEnabled);
        }

        if (item.EntryId is { } entryId && item.IsUnpinnable)
        {
            menu.Items.Add(new System.Windows.Controls.Separator());
            Add(strings["QuickAccess_Rename"], () => PromptRenameQuickAccess(entryId, item.Label));
            Add(strings["QuickAccess_ChangeIcon"], () => ShowQuickAccessIconPicker(row, entryId));

            if (item.IsMissing)
            {
                Add(strings["QuickAccess_Path"], () => PromptFixQuickAccessPath(entryId));
            }

            menu.Items.Add(new System.Windows.Controls.Separator());
            Add(strings["QuickAccess_MoveUp"], () => _viewModel.NudgeQuickAccessEntry(entryId, -1));
            Add(strings["QuickAccess_MoveDown"], () => _viewModel.NudgeQuickAccessEntry(entryId, +1));
            menu.Items.Add(new System.Windows.Controls.Separator());
            Add(strings["Cmd_UnpinQuickAccess"], () => _viewModel.UnpinQuickAccessCommand.Execute(item));
        }

        if (menu.Items.Count > 0)
        {
            menu.Items.Add(new System.Windows.Controls.Separator());
        }

        Add(strings["QuickAccess_Edit"], OpenQuickAccessEditor);

        menu.IsOpen = true;
    }

    /// <summary>Egyszerű, egymezős bekérő — átnevezéshez és útvonal-javításhoz.</summary>
    private void PromptRenameQuickAccess(string entryId, string current)
    {
        if (PromptForText(TranslationSource.Instance["QuickAccess_Rename"], current) is { } label)
        {
            _viewModel.RenameQuickAccessEntry(entryId, label);
        }
    }

    private void PromptFixQuickAccessPath(string entryId)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = TranslationSource.Instance["QuickAccess_Path"] };

        if (dialog.ShowDialog() == true)
        {
            _viewModel.FixQuickAccessPath(entryId, dialog.FolderName);
        }
    }

    private void ShowQuickAccessIconPicker(FrameworkElement target, string entryId)
    {
        string[] icons =
        [
            "Folder24", "FolderOpen24", "Home24", "Desktop24", "Document24", "ArrowDownload24",
            "Image24", "MusicNote124", "Video24", "Code24", "Briefcase24", "Star24",
            "Heart24", "Archive24", "Cloud24", "Storage24", "Pin24", "Bookmark24",
        ];

        var menu = new System.Windows.Controls.ContextMenu { PlacementTarget = target };

        foreach (var icon in icons)
        {
            var entry = new System.Windows.Controls.MenuItem
            {
                Header = icon,
                Icon = new SymbolIcon { Symbol = ViewModels.QuickAccessEditorViewModel.ParseIcon(icon) },
            };

            entry.Click += (_, _) => _viewModel.SetQuickAccessIcon(entryId, icon);
            menu.Items.Add(entry);
        }

        menu.IsOpen = true;
    }

    /// <summary>
    /// Kis, modális szövegbekérő. Szándékosan kódból épül, nem külön XAML
    /// ablakból: egyetlen mező és két gomb, amihez egy önálló nézet és
    /// nézetmodell aránytalan lenne.
    /// </summary>
    private string? PromptForText(string title, string initial)
    {
        var box = new TextBox { Text = initial, Margin = new Thickness(0, 0, 0, 12) };
        var ok = new Wpf.Ui.Controls.Button { Content = TranslationSource.Instance["Cmd_Ok"], Appearance = ControlAppearance.Primary, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Wpf.Ui.Controls.Button { Content = TranslationSource.Instance["Cmd_Cancel"] };

        var buttons = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(box);
        panel.Children.Add(buttons);

        var window = new FluentWindow
        {
            Title = title,
            Width = 380,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowBackdropType = WindowBackdropType.Mica,
            Content = panel,
        };

        ok.Click += (_, _) => { window.DialogResult = true; window.Close(); };
        cancel.Click += (_, _) => { window.DialogResult = false; window.Close(); };

        window.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };

        return window.ShowDialog() == true ? box.Text : null;
    }

    private void OpenQuickAccessEditor()
    {
        var editor = _services.GetRequiredService<QuickAccessEditorWindow>();
        editor.Owner = this;
        editor.ShowDialog();
    }

    /// <summary>
    /// Gyorselérés-sor húzásának indítása az átrendezéshez — saját
    /// vágólap-formátummal (<see cref="QuickAccessReorderFormat"/>), hogy a
    /// leejtő oldal megkülönböztethesse egy fájllistából húzott mappa
    /// rögzítésétől.
    /// </summary>
    private void OnSidebarItemPreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed || _dragStartPoint is not { } start)
        {
            return;
        }

        var current = e.GetPosition(null);

        if (Math.Abs(current.X - start.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _dragStartPoint = null;

        if (sender is not FrameworkElement { DataContext: SidebarItemViewModel { IsUnpinnable: true, EntryId: { } entryId } } container)
        {
            return;
        }

        // Az AZONOSÍTÓ utazik, nem az útvonal: két bejegyzés ugyanarra a
        // mappára is mutathat (eltérő névvel/ikonnal), és az útvonal
        // menet közben szerkeszthető is.
        var data = new System.Windows.DataObject(QuickAccessReorderFormat, entryId);
        DragDrop.DoDragDrop(container, data, DragDropEffects.Move);
    }

    private void OnSidebarDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(QuickAccessReorderFormat) || e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)
            ? DragDropEffects.Move
            : DragDropEffects.None;

        e.Handled = true;
    }

    /// <summary>
    /// Ejtés az oldalsávon: mappa(k) rögzítése a gyorselérésbe (külső húzás
    /// a fájllistából), vagy egy meglévő gyorselérés-sor átrendezése (belső
    /// húzás). A cél sort a leejtés pontjának vizuálisfa-bejárásával
    /// találjuk meg — virtualizált listánál is működik, mert a húzás alatt
    /// a látható konténerek már realizálva vannak.
    /// </summary>
    private void OnSidebarDrop(object sender, DragEventArgs e)
    {
        if (sender is not ListBox listBox)
        {
            return;
        }

        if (e.Data.GetData(QuickAccessReorderFormat) is string sourceEntryId)
        {
            if (FindSidebarItemAt(listBox, e.GetPosition(listBox))?.EntryId is { } targetEntryId)
            {
                _viewModel.ReorderQuickAccess(sourceEntryId, targetEntryId);
            }

            e.Handled = true;
            return;
        }

        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] paths)
        {
            foreach (var path in paths)
            {
                _viewModel.PinToQuickAccessCommand.Execute(path);
            }

            e.Handled = true;
        }
    }

    private static SidebarItemViewModel? FindSidebarItemAt(ListBox listBox, System.Windows.Point position)
    {
        if (listBox.InputHitTest(position) is not DependencyObject hit)
        {
            return null;
        }

        var current = hit;

        while (current is not null && current is not System.Windows.Controls.ListBoxItem)
        {
            current = VisualTreeHelper.GetParent(current);
        }

        return (current as System.Windows.Controls.ListBoxItem)?.DataContext as SidebarItemViewModel;
    }

    /// <summary>
    /// Oszlopfejléc-kattintás: rendezés az oszlop szempontja szerint.
    /// </summary>
    /// <remarks>
    /// Ugyanarra az oszlopra kattintva az irány fordul, más oszlopra kattintva
    /// növekvővel indul — ez a Windows és a macOS közös viselkedése, és a
    /// felhasználó ezt várja anélkül, hogy meg kellene tanulnia.
    /// </remarks>
    private void OnColumnHeaderClick(object sender, RoutedEventArgs e)
    {
        // A GridView jobb szélén ül egy „töltelék" fejléc, aminek nincs oszlopa.
        if (e.OriginalSource is not GridViewColumnHeader { Column: { } column })
        {
            return;
        }

        if (_viewModel.SelectedTab is not { } tab)
        {
            return;
        }

        var key = GridViewSort.GetSortKey(column);
        ApplySort(tab, key);
    }

    /// <summary>Az üres terület helyi menüjének „Rendezés" almenüje.</summary>
    private void OnSortByClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not SortKey key || _viewModel.SelectedTab is not { } tab)
        {
            return;
        }

        ApplySort(tab, key);
    }

    private void ApplySort(TabViewModel tab, SortKey key)
    {
        var descending = key == tab.SortKey && !tab.SortDescending;
        tab.ApplySort(key, descending);
        SyncColumnHeaderIndicators();
    }

    /// <summary>
    /// Az oszlopfejléc-nyilak összhangba hozása az aktuális rendezéssel.
    /// </summary>
    /// <remarks>
    /// A rendezés nem csak oszlopfejléc-kattintásra változhat — a „Rendezés"
    /// almenü is beállíthatja —, ezért a nyíl a jelenlegi <c>SortKey</c>/
    /// <c>SortDescending</c> tiszta függvénye, nem egy külön kézzel
    /// karbantartott mezőé.
    /// </remarks>
    private void SyncColumnHeaderIndicators()
    {
        if (_viewModel.SelectedTab is not { } tab)
        {
            return;
        }

        foreach (var header in FindVisualChildren<GridViewColumnHeader>(DetailsView))
        {
            if (header.Column is not { } column)
            {
                continue;
            }

            var key = GridViewSort.GetSortKey(column);

            GridViewSort.SetIndicator(
                header,
                key != tab.SortKey
                    ? SortIndicator.None
                    : tab.SortDescending ? SortIndicator.Descending : SortIndicator.Ascending);
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);

            if (child is T typed)
            {
                yield return typed;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    /// <summary>
    /// A frissítés letöltve, telepítésre kész — megerősítés kérése az
    /// újraindításhoz. A tényleges fájlcsere csak azután történhet, hogy a
    /// Pilaster.exe kilépett és elengedte a saját fájljainak zárolását, ezért
    /// előbb ezt kell megerősíteni, nem lehet csendben, azonnal újraindítani.
    /// </summary>
    private void OnUpdateRestartRequested(object? sender, EventArgs e)
    {
        var strings = TranslationSource.Instance;

        // Fajlagosan a System.Windows verzió: a Wpf.Ui.Controls névtérnek
        // (ami ebben a fájlban is be van húzva) saját MessageBox-hoz tartozó
        // azonos nevű típusai vannak, amik enélkül ütköznének.
        var result = System.Windows.MessageBox.Show(
            string.Format(strings["Update_ConfirmRestartMessage"], _viewModel.Updates.PendingVersion),
            "Pilaster",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);

        if (result != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        _viewModel.Updates.BeginInstallAndExit();
        System.Windows.Application.Current.Shutdown();
    }

    private void OnEjectCompleted(object? sender, EjectOutcome outcome)
    {
        var strings = TranslationSource.Instance;

        var (message, icon) = outcome switch
        {
            EjectOutcome.Succeeded => (strings["Eject_Success"], System.Windows.MessageBoxImage.Information),
            EjectOutcome.InUse => (strings["Eject_InUse"], System.Windows.MessageBoxImage.Warning),
            _ => (strings["Eject_Error"], System.Windows.MessageBoxImage.Error),
        };

        System.Windows.MessageBox.Show(message, "Pilaster", System.Windows.MessageBoxButton.OK, icon);
    }

    private void OnSetViewDetails(object sender, RoutedEventArgs e) => ApplyViewMode(ViewMode.Details);

    private void OnSetViewGrid(object sender, RoutedEventArgs e) => ApplyViewMode(ViewMode.Grid);

    private void OnSetViewColumns(object sender, RoutedEventArgs e) => ApplyViewMode(ViewMode.Columns);

    private void ApplyViewMode(ViewMode mode)
    {
        if (_viewModel.SelectedTab is { } tab)
        {
            tab.ViewMode = mode;
        }

        SyncViewModeVisuals(_viewModel.SelectedTab);
    }

    /// <summary>
    /// A négy nézet (Részletes/Rács/Oszlopok/Kezdőlap) közül csak az adott
    /// fülnek megfelelő gyökérelem látszik. Külön a <see cref="ApplyViewMode"/>-tól,
    /// mert fülváltáskor, induláskor és Kezdőlap-navigáláskor (vagy onnan
    /// elnavigáláskor) is szinkronizálni kell a vizuális állapotot, anélkül,
    /// hogy a <see cref="TabViewModel.ViewMode"/>-ot újra beállítanánk (ami
    /// felesleges mentést váltana ki).
    /// </summary>
    private void SyncViewModeVisuals(TabViewModel? tab)
    {
        var isHome = tab?.IsHome ?? false;
        var mode = tab?.ViewMode ?? ViewMode.Details;

        HomeDashboard.Visibility = isHome ? Visibility.Visible : Visibility.Collapsed;
        DetailsView.Visibility = !isHome && mode == ViewMode.Details ? Visibility.Visible : Visibility.Collapsed;
        GridViewList.Visibility = !isHome && mode == ViewMode.Grid ? Visibility.Visible : Visibility.Collapsed;
        ColumnsHost.Visibility = !isHome && mode == ViewMode.Columns ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Elem kijelölése egy oszlopban az oszlopos nézetben: navigálható
    /// elemnél új oszlop nyílik, fájlnál a részletek panel jelenik meg —
    /// lásd <see cref="TabViewModel.SelectColumnItemAsync"/>.
    /// </summary>
    private async void OnColumnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox { DataContext: TabViewModel column, SelectedItem: FileSystemItem item })
        {
            return;
        }

        if (_viewModel.SelectedTab is not { } tab)
        {
            return;
        }

        await tab.SelectColumnItemAsync(column, item);
    }
}
