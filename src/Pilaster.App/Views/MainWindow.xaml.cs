using System.ComponentModel;
using System.Diagnostics;
using System.IO;
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

    /// <summary>A beépített szerkesztő ablaka, amíg nyitva van — lásd <see cref="OpenInEditorAsync"/>.</summary>
    private EditorWindow? _editorWindow;

    /// <summary>Az F3 (Megtekintés) előnézeti ablaka, amíg nyitva van — hogy ne nyíljon kettő.</summary>
    private FilePreviewWindow? _previewWindow;

    /// <summary>
    /// Igaz, amíg BÁRMELYIK natív jobbklikk-menü nyitva van — a mappa-háttéré
    /// (<see cref="NativeContextMenuService"/>, saját STA szálon) VAGY a
    /// fájl-elemeké (<see cref="Pilaster.Shell.Menus.NativeMenuPresenter"/>,
    /// a megosztott STA száron, spec v1.0.3) —, hogy ne induljon el egy
    /// második egymásra, ha a felhasználó a menü bezáródása előtt újra jobb
    /// gombot nyom.
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
        viewModel.ToastRequested += (_, toast) => ShowToast(toast.Message, toast.Icon);
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        viewModel.Updates.RestartRequested += OnUpdateRestartRequested;
        viewModel.EjectCompleted += OnEjectCompleted;

        InitializeComponent();
        ContentRendered += (_, _) =>
        {
            _firstFrameRendered = true;
            Diagnostics.StartupTiming.LogFirstFrame();
        };

        // Kisebb felbontású vagy erősen felskálázott (DPI) kijelzőn a XAML-ben
        // megadott 1280×820-as alapméret nagyobb lehet, mint a képernyő tényleges
        // munkaterülete — a WindowStartupLocation="CenterScreen" ilyenkor a
        // képernyő fölé/alá lógatná az ablakot, és a felső sáv (címsor,
        // eszköztár) a látható területen kívülre kerülne (felhasználói
        // visszajelzés). Az induló méretet ezért a munkaterületre korlátozzuk,
        // hogy CenterScreen mindig teljesen látható ablakot pozicionáljon.
        var workArea = SystemParameters.WorkArea;
        Width = Math.Min(Width, workArea.Width);
        Height = Math.Min(Height, workArea.Height);
        ApplyOptionalColumns();
        _settings.Changed += (_, _) => Dispatcher.Invoke(ApplyOptionalColumns);

        foreach (var job in viewModel.FileOperationJobs)
        {
            job.PropertyChanged += OnActivityJobPropertyChanged;
        }

        viewModel.FileOperationJobs.CollectionChanged += (_, e) =>
        {
            foreach (var job in e.NewItems?.OfType<Services.FileOperations.FileOperationJob>() ?? [])
            {
                job.PropertyChanged += OnActivityJobPropertyChanged;
            }

            // Az első művelettel megjelenő panel alulról beúszik.
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add && viewModel.FileOperationJobs.Count == 1)
            {
                _services.GetRequiredService<AnimationService>().PlayEntrance(ActivityPanel, offsetY: 24, milliseconds: 280);
            }

            foreach (var job in e.OldItems?.OfType<Services.FileOperations.FileOperationJob>() ?? [])
            {
                job.PropertyChanged -= OnActivityJobPropertyChanged;
            }
        };

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

        PreviewMouseDown += OnWindowPreviewMouseDown;

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

            // Fülváltáskor ugyanaz a csúszó átmenet, mint mappaváltáskor —
            // az induláskori (első) fülbeállításnál nem, az csak az első
            // képkockát késleltetné.
            if (!_viewModel.DualPaneEnabled && _firstFrameRendered)
            {
                PlayContentTransition();
            }
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.DualPaneVertical))
        {
            ApplyDualPaneOrientation(_viewModel.DualPaneVertical);
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.DualPaneEnabled))
        {
            // Nézetváltás: az újonnan megjelenő elrendezés beúszik. Az
            // egypaneles terület saját TranslateTransformját a mappaváltás
            // csúszása használja, ezért ott csak az átlátszóság animál.
            var animations = _services.GetRequiredService<AnimationService>();

            if (_viewModel.DualPaneEnabled)
            {
                animations.PlayEntrance(DualPaneHost, offsetX: 0, offsetY: 12, milliseconds: 240);
            }
            else if (animations.AreAnimationsEnabled)
            {
                FileAreaBorder.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
            }
        }
    }

    /// <summary>Címsorbeli fül „x" gombja.</summary>
    private void OnTitleTabCloseClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TabViewModel tab })
        {
            e.Handled = true;
            _viewModel.CloseTabCommand.Execute(tab);
        }
    }

    /// <summary>Középső kattintás a fülön: bezárás — mint a böngészőkben és az Intézőben.</summary>
    private void OnTitleTabMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton == System.Windows.Input.MouseButton.Middle && sender is FrameworkElement { DataContext: TabViewModel tab })
        {
            e.Handled = true;
            _viewModel.CloseTabCommand.Execute(tab);
        }
    }

    /// <summary>Új fül megjelenésekor rövid beúszás.</summary>
    private void OnTitleTabLoaded(object sender, RoutedEventArgs e)
    {
        // Az induláskor visszaállított fülek NEM úsznak be — csak a később nyitottak.
        if (_firstFrameRendered)
        {
            _services.GetRequiredService<AnimationService>().PlayEntrance(sender as FrameworkElement, offsetX: -10, offsetY: 0, milliseconds: 200);
        }
    }

    /// <summary>Igaz az első képkocka után — addig az átmeneti animációk kimaradnak.</summary>
    private bool _firstFrameRendered;

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
    /// Aktivitás-központ: egy befejezett művelet rövid idő múlva magától,
    /// animációval eltűnik (korábban a Pilaster bezárásáig ott maradt).
    /// Sikeres műveletnél 1,5 mp, hibásnál 6 mp — azt legyen idő elolvasni.
    /// </summary>
    private void OnActivityJobPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(Services.FileOperations.FileOperationJob.State)
            || sender is not Services.FileOperations.FileOperationJob { IsActive: false } job)
        {
            return;
        }

        var delay = job.State == Services.FileOperations.FileOperationState.CompletedWithErrors
            ? TimeSpan.FromSeconds(6)
            : TimeSpan.FromSeconds(1.5);

        var timer = new System.Windows.Threading.DispatcherTimer { Interval = delay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            DismissActivityJob(job);
        };
        timer.Start();
    }

    private void DismissActivityJob(Services.FileOperations.FileOperationJob job)
    {
        var jobs = _viewModel.FileOperationJobs;

        if (!jobs.Contains(job) || job.IsActive)
        {
            return;
        }

        var animate = _services.GetRequiredService<AnimationService>().AreAnimationsEnabled;
        var duration = TimeSpan.FromMilliseconds(320);
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };

        // Az utolsó elemnél az EGÉSZ panel úszik ki és halványul el; egyébként
        // csak az adott sor, a magasságát is összecsukva.
        FrameworkElement? target = jobs.Count == 1
            ? ActivityPanel
            : ActivityJobsList.ItemContainerGenerator.ContainerFromItem(job) as FrameworkElement;

        if (!animate || target is null)
        {
            jobs.Remove(job);
            return;
        }

        var slide = new TranslateTransform();
        target.RenderTransform = slide;
        var fade = new DoubleAnimation(1, 0, duration) { EasingFunction = ease };
        fade.Completed += (_, _) =>
        {
            jobs.Remove(job);

            // A panel visszaáll alapállapotba a következő művelethez.
            target.BeginAnimation(OpacityProperty, null);
            target.RenderTransform = null;
            ActivityPanel.Opacity = 1;
        };

        slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, 40, duration) { EasingFunction = ease });

        if (!ReferenceEquals(target, ActivityPanel) && target.ActualHeight > 0)
        {
            target.BeginAnimation(HeightProperty, new DoubleAnimation(target.ActualHeight, 0, duration) { EasingFunction = ease, BeginTime = TimeSpan.FromMilliseconds(120) });
        }

        target.BeginAnimation(OpacityProperty, fade);
    }

    private System.Windows.Threading.DispatcherTimer? _toastTimer;

    /// <summary>
    /// Rövid visszajelző buborék a fájlterület alján (másolás, kivágás,
    /// beillesztés…): beúszik, kb. 2 mp-ig látszik, majd elhalványul. Egy
    /// újabb üzenet a régit azonnal lecseréli.
    /// </summary>
    private void ShowToast(string message, SymbolRegular icon)
    {
        ToastText.Text = message;
        ToastIcon.Symbol = icon;
        Toast.Visibility = Visibility.Visible;

        var animate = _services.GetRequiredService<AnimationService>().AreAnimationsEnabled;
        Toast.BeginAnimation(OpacityProperty, null);
        Toast.Opacity = 1;

        if (animate)
        {
            _services.GetRequiredService<AnimationService>().PlayEntrance(Toast, offsetY: 14, milliseconds: 220);
        }

        _toastTimer?.Stop();
        _toastTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2.2) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer?.Stop();

            if (!animate)
            {
                Toast.Visibility = Visibility.Collapsed;
                return;
            }

            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(260));
            fade.Completed += (_, _) =>
            {
                if (Toast.Opacity == 0)
                {
                    Toast.Visibility = Visibility.Collapsed;
                }
            };
            Toast.BeginAnimation(OpacityProperty, fade);
        };
        _toastTimer.Start();
    }

    /// <summary>
    /// Csúszó-elhalványuló átmenet lejátszása a fájlterületen, valahányszor a
    /// megnyitott mappa változik — gyorselérésre kattintás, breadcrumb,
    /// vissza/előre, vagy dupla kattintás egy mappára.
    /// </summary>
    private void PlayContentTransition()
    {
        if (!_viewModel.AnimationsEnabled || !_firstFrameRendered)
        {
            return;
        }

        if (Resources["SlideInFileArea"] is Storyboard storyboard)
        {
            storyboard.Begin(FileAreaBorder);
        }
    }

    /// <summary>
    /// A Részletes nézet opcionális oszlopai (Létrehozva, Utolsó hozzáférés):
    /// a GridView-nak nincs oszlop-láthatósága, ezért a beállítás szerint
    /// kivesszük/visszatesszük őket — a végén, a sorrendjüket megtartva.
    /// </summary>
    private void ApplyOptionalColumns()
    {
        var current = _settings.Current;

        foreach (var (column, visible) in new[] { (CreatedColumn, current.ShowCreatedColumn), (AccessedColumn, current.ShowAccessedColumn) })
        {
            var present = DetailsGridView.Columns.Contains(column);

            if (visible && !present)
            {
                DetailsGridView.Columns.Add(column);
            }
            else if (!visible && present)
            {
                DetailsGridView.Columns.Remove(column);
            }
        }
    }

    /// <summary>
    /// Legördülő menü egy eszköztár-gomb alatt, a gomb JOBB széléhez igazítva
    /// (a sima Bottom elhelyezés a bal széléhez igazított, és a menü kilógott
    /// az ablakból). Minden megnyitáskor frissen épül, így a pipák mindig az
    /// aktuális állapotot mutatják.
    /// </summary>
    private void OpenToolbarMenu(FrameworkElement anchor, System.Windows.Controls.ContextMenu menu)
    {
        menu.PlacementTarget = anchor;
        menu.Placement = PlacementMode.Custom;
        menu.CustomPopupPlacementCallback = (popupSize, targetSize, _) =>
        [
            new CustomPopupPlacement(new Point(targetSize.Width - popupSize.Width, targetSize.Height + 4), PopupPrimaryAxis.Horizontal),
            new CustomPopupPlacement(new Point(targetSize.Width - popupSize.Width, -popupSize.Height - 4), PopupPrimaryAxis.Horizontal),
        ];
        menu.Opened += OnGlassContextMenuOpened;
        menu.IsOpen = true;
    }

    private static System.Windows.Controls.MenuItem MenuEntry(string header, SymbolRegular? icon, bool isChecked, Action onClick)
    {
        var item = new System.Windows.Controls.MenuItem
        {
            Header = header,
            IsChecked = isChecked,
            Icon = icon is { } symbol ? new SymbolIcon { Symbol = symbol, FontSize = 15 } : null,
        };
        item.Click += (_, _) => onClick();
        return item;
    }

    /// <summary>Rendezés ▾ — szempont és irány egy lapos menüben (almenü nélkül).</summary>
    private void OnSortMenuClick(object sender, RoutedEventArgs e)
    {
        var tab = _viewModel.SelectedTab;
        var strings = TranslationSource.Instance;
        var menu = new System.Windows.Controls.ContextMenu();

        foreach (var (key, label) in SortMenuEntries())
        {
            menu.Items.Add(MenuEntry(strings[label], null, tab?.SortKey == key, () =>
            {
                if (_viewModel.SelectedTab is { } current)
                {
                    current.ApplySort(key, current.SortKey == key ? current.SortDescending : false);
                    SyncColumnHeaderIndicators();
                }
            }));
        }

        menu.Items.Add(new System.Windows.Controls.Separator());

        foreach (var descending in new[] { false, true })
        {
            menu.Items.Add(MenuEntry(
                strings[descending ? "Sort_Descending" : "Sort_Ascending"],
                descending ? SymbolRegular.ArrowDown24 : SymbolRegular.ArrowUp24,
                tab is not null && tab.SortDescending == descending,
                () =>
                {
                    if (_viewModel.SelectedTab is { } current)
                    {
                        current.ApplySort(current.SortKey, descending);
                        SyncColumnHeaderIndicators();
                    }
                }));
        }

        OpenToolbarMenu((FrameworkElement)sender, menu);
    }

    /// <summary>
    /// Nézet ▾ — nézetmód (csak egypaneles nézetben; a panelek mindig
    /// Részletes nézetűek) és a rejtett elemek kapcsolója, egy lapos menüben.
    /// </summary>
    private void OnViewMenuClick(object sender, RoutedEventArgs e)
    {
        var tab = _viewModel.SelectedTab;
        var strings = TranslationSource.Instance;
        var menu = new System.Windows.Controls.ContextMenu();

        if (!_viewModel.DualPaneEnabled)
        {
            menu.Items.Add(MenuEntry(strings["View_Details"], SymbolRegular.TextBulletListLtr24, tab?.ViewMode == ViewMode.Details, () => ApplyViewMode(ViewMode.Details)));
            menu.Items.Add(MenuEntry(strings["View_Grid"], SymbolRegular.GridDots24, tab?.ViewMode == ViewMode.Grid, () => ApplyViewMode(ViewMode.Grid)));
            menu.Items.Add(MenuEntry(strings["View_Columns"], SymbolRegular.ColumnTriple24, tab?.ViewMode == ViewMode.Columns, () => ApplyViewMode(ViewMode.Columns)));
            menu.Items.Add(new System.Windows.Controls.Separator());
        }

        menu.Items.Add(MenuEntry(strings["Cmd_ToggleHidden"], SymbolRegular.Eye24, tab?.ShowHiddenItems == true, () =>
        {
            if (_viewModel.SelectedTab is { } current)
            {
                current.ShowHiddenItems = !current.ShowHiddenItems;
            }
        }));

        menu.Items.Add(MenuEntry(strings["Settings_ShowExtensions"], SymbolRegular.DocumentText24, _settings.Current.ShowExtensions, () =>
        {
            _settings.Current.ShowExtensions = !_settings.Current.ShowExtensions;
            _settings.NotifyChanged();
        }));

        OpenToolbarMenu((FrameworkElement)sender, menu);
    }

    private void OnDualNewFolderClick(object sender, RoutedEventArgs e) => CreateFolderInActivePane();

    private void OnDualNewFileClick(object sender, RoutedEventArgs e)
    {
        if (GetActiveTab() is { } tab)
        {
            _ = _viewModel.CreateNewFileInTabAsync(tab);
        }
    }

    private void OnDualCopyClick(object sender, RoutedEventArgs e) => _ = StartTcTransferAsync(isMove: false);

    private void OnDualMoveClick(object sender, RoutedEventArgs e) => _ = StartTcTransferAsync(isMove: true);

    private void OnDualDeleteClick(object sender, RoutedEventArgs e) => DeleteActiveSelection(permanent: false);

    private void OnSwapPanesClick(object sender, RoutedEventArgs e) => SwapPanesAnimated();

    /// <summary>
    /// Panelcsere animációval: a csere UTÁN a két panel a másik oldalról
    /// úszik be, így látszik, hogy helyet cseréltek.
    /// </summary>
    private void SwapPanesAnimated()
    {
        _viewModel.SwapPanesCommand.Execute(null);

        var animations = _services.GetRequiredService<AnimationService>();
        var distance = _viewModel.DualPaneVertical ? 0 : 60;
        var distanceY = _viewModel.DualPaneVertical ? 40 : 0;
        animations.PlayEntrance(LeftPaneView, offsetX: distance, offsetY: distanceY, milliseconds: 280);
        animations.PlayEntrance(RightPaneView, offsetX: -distance, offsetY: -distanceY, milliseconds: 280);
    }

    /// <summary>A rendezési szempontok a menükhöz — a beállításokban bekapcsolt extra oszlopokkal együtt.</summary>
    private IEnumerable<(SortKey Key, string Label)> SortMenuEntries()
    {
        yield return (SortKey.Name, "Col_Name");
        yield return (SortKey.Modified, "Col_Modified");
        yield return (SortKey.Type, "Col_Type");
        yield return (SortKey.Size, "Col_Size");
        yield return (SortKey.Created, "Col_Created");
        yield return (SortKey.Accessed, "Col_Accessed");
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
    /// <summary>Kétpaneles nézet: a panel-sorokon ugyanaz a jobbklikk-menü, mint az egypaneles listán.</summary>
    private void OnPaneItemContextMenuRequested(object? sender, (FrameworkElement Container, System.Windows.Input.MouseButtonEventArgs Args) e) =>
        OnItemPreviewRightButtonDown(e.Container, e.Args);

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

        // Lomtár-sor: a valódi shell-menü (Megnyitás, Kivágás, Tulajdonságok
        // stb.) itt értelmetlen lenne — a FullPath szintetikus, nem egy
        // valódi shell-elemre mutat. Saját, szűk menü: Visszaállítás /
        // Végleges törlés.
        if (item.IsRecycled)
        {
            var selectedRecycled = selector switch
            {
                ListView view => view.SelectedItems.Cast<FileSystemItem>().ToList(),
                ListBox box => box.SelectedItems.Cast<FileSystemItem>().ToList(),
                _ => [item],
            };
            ShowRecycleBinItemMenu(container, selectedRecycled);
            return;
        }

        var selectedPaths = selector switch
        {
            ListView view => view.SelectedItems.Cast<FileSystemItem>().Select(i => i.FullPath).ToList(),
            ListBox box => box.SelectedItems.Cast<FileSystemItem>().Select(i => i.FullPath).ToList(),
            _ => [item.FullPath],
        };

        var extendedVerbs = System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift);

        // A2 (v1.0.2): ha PONTOSAN erre a kijelölésre van kész (vagy még
        // folyamatban lévő) előretöltés, azt használjuk friss lekérdezés
        // helyett — ha már kész, a menü rögtön a teljes tartalommal nyílik.
        // Ha a kijelölés nem egyezik (pl. a jobbklikk egy nem kijelölt elemre
        // esett), nincs találat, és a szokásos friss lekérdezés indul. EZ
        // MINDKÉT megjelenítési módra érvényes (spec v1.0.3) — az
        // előretöltés a lekérdezést gyorsítja, nem a megjelenítést dönti el.
        var preloaded = _services.GetService(typeof(ShellMenuPreloadCoordinator)) is ShellMenuPreloadCoordinator preload
            ? preload.TakeIfMatches(selectedPaths)
            : null;

        // v1.0.3: Beállítások → Jobbklikk menü — melyik VALÓDI menü jelenjen
        // meg. Alapértelmezett a Windows natívja (lásd ContextMenuMode
        // dokumentációja); a Pilaster saját designja opcionális marad, amíg
        // a bővítmény-megjelenítése nincs tökéletesre csiszolva.
        if (_settings.Current.ContextMenuMode == ContextMenuMode.Windows)
        {
            var screenPoint = PointToScreen(e.GetPosition(this));
            await ShowNativeFileMenuAsync(item, selectedPaths, extendedVerbs, screenPoint, preloaded);
            return;
        }

        // A SAJÁT menü (spec F4): a mi designunk, de a telepített
        // shell-bővítmények elemeivel együtt. A saját elemek azonnal
        // megjelennek, a shell-elemek aszinkron, időkorláttal csúsznak be.
        PilasterContextMenu.Show(
            _services,
            container,
            BuildFileMenuEntries(item, selectedPaths, extendedVerbs),
            (timeout, blacklist) => preloaded ?? ShellMenuSession.QueryItemsAsync(selectedPaths, extendedVerbs, timeout, blacklist),
            _settings.Current,
            item.FullPath,
            header: BuildFileMenuHeader(item, SelectedItemsFor(container, item), selectedPaths, extendedVerbs),
            footer: BuildFileMenuFooter(item));

        await Task.CompletedTask;
    }

    /// <summary>
    /// Egy Lomtár-sor saját, szűk jobbklikk-menüje: Visszaállítás és Végleges
    /// törlés — csak a jobbklikkelt elemre vonatkozik, nem a teljes
    /// kijelölésre (ugyanaz a korlátozás, mint a korábbi RecycleBinWindow
    /// soronkénti gombjainál volt).
    /// </summary>
    private void ShowRecycleBinItemMenu(FrameworkElement placementTarget, IReadOnlyList<FileSystemItem> items)
    {
        if (_viewModel.SelectedTab is not { } tab || items.Count == 0)
        {
            return;
        }

        var strings = TranslationSource.Instance;
        var menu = new System.Windows.Controls.ContextMenu();

        var restore = new System.Windows.Controls.MenuItem { Header = strings["Cmd_Restore"] };
        restore.Click += (_, _) =>
        {
            foreach (var item in items)
            {
                tab.RestoreRecycledItemCommand.Execute(item);
            }

            _viewModel.RefreshQuickAccess();
        };
        menu.Items.Add(restore);

        var delete = new System.Windows.Controls.MenuItem { Header = strings["Cmd_DeletePermanently"] };
        delete.Click += async (_, _) => await DeleteRecycledItemsWithConfirmationAsync(tab, items);
        menu.Items.Add(delete);

        menu.PlacementTarget = placementTarget;
        menu.IsOpen = true;
    }

    /// <summary>
    /// Egy Igen/Mégse megerősítő párbeszéd a WPF-UI Fluent design-ú
    /// <see cref="Wpf.Ui.Controls.MessageBox"/>-szal — NEM a natív
    /// <see cref="System.Windows.MessageBox"/>, ami a többi felület mellett
    /// stílustörésnek hat (felhasználói visszajelzés).
    /// </summary>
    private async Task<bool> ShowConfirmDialogAsync(string title, string message, string primaryButtonText)
    {
        var dialog = new Wpf.Ui.Controls.MessageBox
        {
            Title = title,
            Content = message,
            PrimaryButtonText = primaryButtonText,
            CloseButtonText = TranslationSource.Instance["Cmd_Cancel"],
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        var result = await dialog.ShowDialogAsync();
        return result == Wpf.Ui.Controls.MessageBoxResult.Primary;
    }

    /// <summary>
    /// Törlés indítása. Végleges törlésnél (Shift+Delete), és ha a hely nem
    /// támogatja a Lomtárt (hálózati megosztás, pendrive), előbb rákérdez,
    /// mint az Intéző — korábban mindkettő szó nélkül, visszavonhatatlanul
    /// törölt.
    /// </summary>
    private async void RequestDelete(IReadOnlyList<string> paths, bool permanent)
    {
        if (paths.Count == 0)
        {
            return;
        }

        var noRecycleBin = !permanent && paths.Any(p => !Pilaster.Shell.Recycle.RecycleBinService.IsSupported(p));

        if (permanent || noRecycleBin)
        {
            var strings = TranslationSource.Instance;
            var first = Path.GetFileName(Path.TrimEndingDirectorySeparator(paths[0])) is { Length: > 0 } name ? name : paths[0];
            var message = (noRecycleBin, paths.Count == 1) switch
            {
                (true, true) => string.Format(strings["Delete_ConfirmNoRecycle"], first),
                (true, false) => string.Format(strings["Delete_ConfirmNoRecycleMultiple"], paths.Count),
                (false, true) => string.Format(strings["RecycleBin_ConfirmDelete"], first),
                (false, false) => string.Format(strings["RecycleBin_ConfirmDeleteMultiple"], paths.Count),
            };

            if (!await ShowConfirmDialogAsync(strings["Cmd_DeletePermanently"], message, strings["Cmd_DeletePermanently"]))
            {
                return;
            }
        }

        _viewModel.StartPaneDelete(paths, permanent);
    }

    /// <summary>
    /// Lomtár-elemek végleges törlése megerősítés után — a jobbklikk-menüből
    /// (<see cref="ShowRecycleBinItemMenu"/>) ÉS a Delete billentyűből
    /// (<see cref="OnFileListHostPreviewKeyDown"/>) egyaránt ide fut ki.
    /// </summary>
    private async Task DeleteRecycledItemsWithConfirmationAsync(TabViewModel tab, IReadOnlyList<FileSystemItem> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        var strings = TranslationSource.Instance;
        var message = items.Count == 1
            ? string.Format(strings["RecycleBin_ConfirmDelete"], items[0].Name)
            : string.Format(strings["RecycleBin_ConfirmDeleteMultiple"], items.Count);

        if (!await ShowConfirmDialogAsync(strings["RecycleBin_Title"], message, strings["Cmd_DeletePermanently"]))
        {
            return;
        }

        foreach (var item in items)
        {
            tab.DeleteRecycledItemPermanentlyCommand.Execute(item);
        }

        _viewModel.RefreshQuickAccess();
    }

    /// <summary>
    /// A VALÓDI Windows natív jobbklikk-menü megjelenítése fájl-elemeken
    /// (spec v1.0.3) — a shell elemei elé a Pilaster nyolc saját, natív
    /// megfelelő NÉLKÜLI parancsa (Megnyitás új fülön, Megnyitás a másik
    /// panelen, Szerkesztés Pilaster Editorral, Útvonal/Név másolása,
    /// Terminál megnyitása itt, Rögzítés a gyorseléréshez, Címkék) kerül be
    /// — a többi (Megnyitás, Kivágás, Másolás, Beillesztés, Törlés,
    /// Átnevezés stb.) MÁR benne van a valódi shell menüben, azokat nem
    /// duplikáljuk.
    /// </summary>
    /// <remarks>
    /// A <c>ShellMenuSession.ShowNativeAsync</c> a megosztott STA száron fut
    /// — ugyanazon a szálon, ahol az <c>IContextMenu</c> létrejött —, és a
    /// natív <c>TrackPopupMenuEx</c> hívás ideje alatt BLOKKOLJA azt a szálat
    /// (ez szándékos, lásd a metódus dokumentációját), de a WPF UI szálat
    /// nem: ez a hívó itt <c>await</c>-tal, nem blokkolva várja meg.
    /// </remarks>
    private async Task ShowNativeFileMenuAsync(
        FileSystemItem item,
        IReadOnlyList<string> selectedPaths,
        bool extendedVerbs,
        Point screenPoint,
        Task<ShellMenuSession?>? preloaded)
    {
        if (_isNativeContextMenuOpen)
        {
            return;
        }

        _isNativeContextMenuOpen = true;

        try
        {
            var session = preloaded is not null
                ? await preloaded
                : await ShellMenuSession.QueryItemsAsync(
                    selectedPaths,
                    extendedVerbs,
                    TimeSpan.FromMilliseconds(_settings.Current.ShellMenuTimeoutMs),
                    _settings.Current.ShellHandlerBlacklist);

            var ownerHandle = new WindowInteropHelper(this).Handle;

            if (session is null)
            {
                // A shell-lekérdezés teljesen sikertelen — a bevált, saját
                // menüre esünk vissza, NEM egy ritkán használt, kevésbé
                // karbantartott statikus erőforrásra, hogy a felhasználó
                // legalább a Pilaster-designú menüt lássa üres kéz helyett.
                PilasterContextMenu.Show(
                    _services,
                    this,
                    BuildFileMenuEntries(item, selectedPaths, extendedVerbs),
                    (timeout, blacklist) => ShellMenuSession.QueryItemsAsync(selectedPaths, extendedVerbs, timeout, blacklist),
                    _settings.Current,
                    item.FullPath,
                    header: BuildFileMenuHeader(item, [item], selectedPaths, extendedVerbs),
                    footer: BuildFileMenuFooter(item));
                return;
            }

            var ownCommands = BuildNativeOwnCommands(item, selectedPaths);

            var result = await session.ShowNativeAsync(
                ownCommands.Select(c => c.Command).ToList(),
                (int)screenPoint.X,
                (int)screenPoint.Y,
                ownerHandle);

            if (result.Outcome == NativeMenuOutcome.OwnCommand)
            {
                var match = ownCommands.FirstOrDefault(c => c.Command.CommandId == result.CommandId);
                match.Action?.Invoke();
            }

            session.Dispose();
        }
        finally
        {
            _isNativeContextMenuOpen = false;
        }
    }

    /// <summary>
    /// A nyolc, natív megfelelő nélküli saját parancs a natív menühöz — lásd
    /// <see cref="ShowNativeFileMenuAsync"/>. Ugyanaz a nyolc parancs és
    /// ugyanaz a láthatósági logika, mint a <see cref="BuildFileMenuEntries"/>
    /// megfelelő sorai — SZÁNDÉKOSAN nem ugyanabból a listából származtatva,
    /// mert azok WPF <see cref="PilasterMenuEntry"/>-k (a Pilaster-menühöz),
    /// itt viszont natív <c>HMENU</c>-be beszúrható parancsazonosítót és
    /// HBITMAP-ikont kell rendelni hozzájuk.
    /// </summary>
    private List<(NativeOwnCommand Command, Action Action)> BuildNativeOwnCommands(
        FileSystemItem item, IReadOnlyList<string> selectedPaths)
    {
        var dual = _viewModel.DualPaneEnabled;
        var nextId = ShellMenuSession.NativeOwnCommandIdBase;

        var candidates = new (string LabelKey, SymbolRegular Icon, Action Action, bool Visible)[]
        {
            ("Cmd_OpenNewTab", SymbolRegular.TabAdd24,
                () => _viewModel.ActivePane.AddTab(item.FullPath), item.IsNavigable),
            ("QuickAccess_OpenOther", SymbolRegular.DualScreen24,
                () => _ = _viewModel.InactivePane.NavigateAsync(item.FullPath), item.IsNavigable && dual),
            ("Cmd_EditWithPilaster", SymbolRegular.Code24,
                () => OpenInEditor(item.FullPath), !item.IsNavigable),
            ("Cmd_CopyPath", SymbolRegular.Copy24,
                () => CopyTextToClipboard(item.FullPath), true),
            ("Cmd_CopyName", SymbolRegular.Copy24,
                () => CopyTextToClipboard(item.Name), true),
            ("Cmd_OpenTerminal", SymbolRegular.WindowConsole20,
                () => OpenTerminalAt(item), true),
            ("Cmd_PinToQuickAccess", SymbolRegular.Pin24,
                () => _viewModel.PinToQuickAccessCommand.Execute(item.FullPath), item.IsNavigable),
            ("Cmd_Tags", SymbolRegular.Tag24,
                () => ShowTagPickerFor(item, null), true),
        };

        var result = new List<(NativeOwnCommand, Action)>();

        foreach (var (labelKey, icon, action, visible) in candidates)
        {
            if (!visible)
            {
                continue;
            }

            var command = new NativeOwnCommand(
                nextId++,
                TranslationSource.Instance[labelKey],
                NativeMenuIconRenderer.GetOrRender(icon));

            result.Add((command, action));
        }

        return result;
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

        // A+C terv: a Kivágás/Másolás/Átnevezés/Törlés a fejléc ikonsorában
        // van, a címkék chipként — a lista így rövid marad. A ritkábban
        // használt parancsok a „Továbbiak" almenübe kerültek, a Tulajdonságok
        // pedig a menü aljára, a telepített programok alá (lásd BuildFileMenuFooter).
        return
        [
            new("Cmd_Open", SymbolRegular.Open24, () => _ = OpenItemAsync(item), Gesture: "Enter", IsDefault: true),
            // Mappán/fájlon értelmetlen parancsok EL SEM jelennek meg (J4).
            new("Cmd_OpenNewTab", SymbolRegular.TabAdd24, () => _viewModel.ActivePane.AddTab(item.FullPath),
                IsVisible: item.IsNavigable),
            new("QuickAccess_OpenOther", SymbolRegular.SplitVertical24,
                () => _ = _viewModel.InactivePane.NavigateAsync(item.FullPath),
                IsVisible: item.IsNavigable && dual),
            new("Cmd_OpenWith", SymbolRegular.AppGeneric24, () => OpenWithDialog(item.FullPath),
                IsVisible: !item.IsNavigable),
            new("Cmd_EditWithPilaster", SymbolRegular.Code24, () => OpenInEditor(item.FullPath),
                IsVisible: !item.IsNavigable, Gesture: "F4"),

            PilasterMenuEntry.Separator,

            new("Cmd_CopyPath", SymbolRegular.Link24, () => CopyTextToClipboard(string.Join(Environment.NewLine, selectedPaths))),
            new("Cmd_PinToQuickAccess", SymbolRegular.Pin24,
                () => _viewModel.PinToQuickAccessCommand.Execute(item.FullPath), IsVisible: item.IsNavigable),
            new("ContextMenu_MoreOptions", SymbolRegular.MoreHorizontal24, SubItems:
            [
                new("Cmd_CopyName", SymbolRegular.Copy24, () => CopyTextToClipboard(item.Name)),
                new("Cmd_CreateShortcut", SymbolRegular.Link24, () => CreateShortcutsHere(selectedPaths)),
                new("Cmd_OpenTerminal", SymbolRegular.WindowConsole20, () => OpenTerminalAt(item)),
                new("Cmd_ShowInExplorer", SymbolRegular.Folder24, () => ShowInExplorer(item.FullPath)),
                new("Cmd_Tags", SymbolRegular.Tag24, () => ShowTagPickerFor(item, null)),
            ]),
        ];
    }

    /// <summary>A jobbklikkelt elemet tartalmazó lista teljes kijelölése (elemekként) — a menü fejlécéhez.</summary>
    private static IReadOnlyList<FileSystemItem> SelectedItemsFor(FrameworkElement container, FileSystemItem item) =>
        ItemsControl.ItemsControlFromItemContainer(container) is System.Windows.Controls.ListBox list && list.SelectedItems.Contains(item)
            ? [.. list.SelectedItems.OfType<FileSystemItem>()]
            : [item];

    /// <summary>A menü alja — a telepített programok („Egyéb alkalmazások") ALATT.</summary>
    private IReadOnlyList<PilasterMenuEntry> BuildFileMenuFooter(FileSystemItem item) =>
    [
        new("Cmd_Properties", SymbolRegular.Info24, () => ShowProperties(item.FullPath), Gesture: "Alt+Enter"),
    ];

    /// <summary>
    /// A menü fejléce (A+C terv): a kijelölés adatai, ikonsor a leggyakoribb
    /// műveletekkel, és a címkék kattintható chipként.
    /// </summary>
    private PilasterMenuHeader BuildFileMenuHeader(FileSystemItem item, IReadOnlyList<FileSystemItem> selected, IReadOnlyList<string> selectedPaths, bool shiftHeld)
    {
        var metadata = _services.GetRequiredService<FileMetadataService>();
        var strings = TranslationSource.Instance;
        var single = selected.Count <= 1;

        string title;
        string detail;

        if (single)
        {
            title = item.Name;
            var date = item.ModifiedUtc.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture);
            var type = new FileTypeConverter().Convert(item, typeof(string), null, System.Globalization.CultureInfo.CurrentCulture) as string ?? string.Empty;
            var size = item.Kind == FileSystemItemKind.File
                ? Pilaster.Core.Formatting.ByteSize.Format(item.SizeBytes)
                : item.ComputedFolderSize > 0 ? Pilaster.Core.Formatting.ByteSize.Format(item.ComputedFolderSize) : null;
            detail = string.Join(" · ", new[] { size, type, date }.Where(p => !string.IsNullOrEmpty(p)));
        }
        else
        {
            title = string.Format(strings["ContextMenu_ItemsSelected"], selected.Count);
            var bytes = selected.Sum(i => i.Kind == FileSystemItemKind.File ? Math.Max(0, i.SizeBytes) : Math.Max(0, i.ComputedFolderSize));
            detail = Pilaster.Core.Formatting.ByteSize.Format(bytes);
        }

        var allFavorite = selectedPaths.Count > 0 && selectedPaths.All(metadata.IsFavorite);

        List<PilasterQuickAction> actions =
        [
            new("Cmd_Cut", SymbolRegular.Cut24, () => _viewModel.CutSelectionCommand.Execute(selectedPaths)),
            new("Cmd_Copy", SymbolRegular.Copy24, () => _viewModel.CopySelectionCommand.Execute(selectedPaths)),
            new("Keymap_Rename", SymbolRegular.Rename24, () => GetActiveTab()?.BeginRename(item), IsEnabled: single),
            new(allFavorite ? "Cmd_RemoveFavorite" : "Cmd_AddFavorite", SymbolRegular.Heart24, () =>
            {
                foreach (var path in selectedPaths)
                {
                    metadata.SetFavorite(path, !allFavorite);
                }
            }, IsActive: allFavorite),
            new(shiftHeld ? "Cmd_DeletePermanently" : "Cmd_Delete", SymbolRegular.Delete24,
                () => RequestDelete(selectedPaths, shiftHeld)),
        ];

        var tags = metadata.Tags
            .Select(tag => new PilasterTagChip(
                tag,
                selectedPaths.Count > 0 && selectedPaths.All(path => metadata.GetTags(path).Any(t => t.Id == tag.Id)),
                on =>
                {
                    foreach (var path in selectedPaths)
                    {
                        if (on)
                        {
                            metadata.AddTag(path, tag.Id);
                        }
                        else
                        {
                            metadata.RemoveTag(path, tag.Id);
                        }
                    }
                }))
            .ToList();

        return new PilasterMenuHeader(selected.Count == 0 ? [item] : selected, title, detail, actions, tags);
    }

    private void CopyTextToClipboard(string text)
    {
        try
        {
            Clipboard.SetText(text);
            ShowToast(TranslationSource.Instance["Toast_TextCopied"], SymbolRegular.Copy24);
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
        // Egyszerre egy szerkesztőablak. A fülei (EditorViewModel, singleton)
        // túlélik a bezárást; maga az ablak viszont újra létrejön, mert egy
        // bezárt WPF-ablakot nem lehet újra megjeleníteni — korábban a
        // második megnyitás csendben elbukott.
        if (_editorWindow is { } open)
        {
            if (open.WindowState == WindowState.Minimized)
            {
                open.WindowState = WindowState.Normal;
            }

            open.Activate();
        }
        else
        {
            var editor = _services.GetRequiredService<EditorWindow>();
            editor.Owner = this;
            editor.Closed += (_, _) => _editorWindow = null;
            _editorWindow = editor;
            editor.Show();
        }

        var editorViewModel = _services.GetRequiredService<EditorViewModel>();

        if (!await editorViewModel.OpenAsync(path))
        {
            // Bináris tartalom: a szerkesztő nem nyitja meg — az F3 előnézet
            // hexdumpja viszont igen (spec F2). Ha a szerkesztőben nincs más
            // fül, az üres ablakát nem hagyjuk az előnézet mögött.
            if (editorViewModel.Documents.Count == 0)
            {
                _editorWindow?.Close();
            }

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

        if (_viewModel.SelectedTab is not { CurrentPath: { } currentPath } tab)
        {
            return;
        }

        // Lomtár háttér: nincs valódi mappa a currentPath mögött, a szokásos
        // Új mappa/Beillesztés/shell-háttérmenü itt értelmetlen lenne — csak
        // Frissítés és Lomtár ürítése.
        if (tab.IsRecycleBin)
        {
            ShowRecycleBinBackgroundMenu(itemsControl, tab);
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

    /// <summary>
    /// A Lomtár háttér-menüje: Frissítés + Lomtár ürítése — ugyanaz a
    /// megerősítés, mint korábban a RecycleBinWindow „Ürítés" gombjánál.
    /// </summary>
    private void ShowRecycleBinBackgroundMenu(FrameworkElement placementTarget, TabViewModel tab)
    {
        var strings = TranslationSource.Instance;
        var menu = new System.Windows.Controls.ContextMenu();

        var refresh = new System.Windows.Controls.MenuItem { Header = strings["Cmd_Refresh"] };
        refresh.Click += (_, _) => tab.RefreshCommand.Execute(null);
        menu.Items.Add(refresh);

        menu.Items.Add(new System.Windows.Controls.Separator());

        var empty = new System.Windows.Controls.MenuItem { Header = strings["Cmd_EmptyRecycleBin"] };
        empty.Click += async (_, _) => await EmptyRecycleBinWithConfirmationAsync(tab);
        menu.Items.Add(empty);

        menu.PlacementTarget = placementTarget;
        menu.IsOpen = true;
    }

    /// <summary>
    /// A Lomtár ürítése megerősítés után — a háttér-menüből
    /// (<see cref="ShowRecycleBinBackgroundMenu"/>) ÉS az eszköztár „Lomtár
    /// ürítése" gombjából (<see cref="OnEmptyRecycleBinToolbarClick"/>)
    /// egyaránt ide fut ki.
    /// </summary>
    private async Task EmptyRecycleBinWithConfirmationAsync(TabViewModel tab)
    {
        var strings = TranslationSource.Instance;

        if (!await ShowConfirmDialogAsync(strings["RecycleBin_Title"], strings["RecycleBin_ConfirmEmpty"], strings["Cmd_EmptyRecycleBin"]))
        {
            return;
        }

        tab.EmptyRecycleBinCommand.Execute(null);
        _viewModel.RefreshQuickAccess();
    }

    /// <summary>Az eszköztár „Lomtár ürítése" gombja — csak Lomtár-nézetben látszik, lásd MainWindow.xaml.</summary>
    private async void OnEmptyRecycleBinToolbarClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedTab is { IsRecycleBin: true } tab)
        {
            await EmptyRecycleBinWithConfirmationAsync(tab);
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

    /// <summary>Ugyanaz, mint <see cref="GetSelectedFilePaths"/>, de a teljes elemekkel — a Lomtár-parancsoknak ez kell.</summary>
    private List<FileSystemItem> GetSelectedFileSystemItems()
    {
        if (DetailsView.Visibility == Visibility.Visible)
        {
            return [.. DetailsView.SelectedItems.Cast<FileSystemItem>()];
        }

        if (GridViewList.Visibility == Visibility.Visible)
        {
            return [.. GridViewList.SelectedItems.Cast<FileSystemItem>()];
        }

        if (_viewModel.SelectedTab?.ColumnsSelectedFile is { } columnsFile)
        {
            return [columnsFile];
        }

        return [];
    }

    private void OnCopyItemClick(object sender, RoutedEventArgs e) =>
        _viewModel.CopySelectionCommand.Execute(GetSelectedFilePaths());

    private void OnCutItemClick(object sender, RoutedEventArgs e) =>
        _viewModel.CutSelectionCommand.Execute(GetSelectedFilePaths());

    private void OnDeleteItemClick(object sender, RoutedEventArgs e) =>
        RequestDelete(GetSelectedFilePaths(), permanent: false);

    /// <summary>
    /// Ctrl+C/Ctrl+X/Ctrl+V/Delete/Shift+Delete — a fájllista területén
    /// bárhol működik, a jelenlegi kijelölésen. Szándékosan a fájlterület
    /// Gridjén (nem az egész ablakon), hogy szövegmezőkben (keresés,
    /// átnevezés, útvonalszerkesztő) a Ctrl+C/V a normál szövegműveletet
    /// végezze, ne fájlműveletet indítson.
    /// </summary>
    private void OnFileListHostPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_viewModel.SelectedTab is not { IsHome: false } tab)
        {
            return;
        }

        // Lomtár-nézetben Copy/Cut/Paste nem értelmezhető (szintetikus
        // útvonalak) — a Delete viszont, ugyanúgy, mint az Intézőben,
        // VÉGLEGES törlést jelent (a Lomtárban lévő elem újbóli törlése nem
        // kerül egy „második" lomtárba).
        if (tab.IsRecycleBin)
        {
            if (e.Key == System.Windows.Input.Key.Delete)
            {
                e.Handled = true;
                _ = DeleteRecycledItemsWithConfirmationAsync(tab, GetSelectedFileSystemItems());
            }

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
                RequestDelete(GetSelectedFilePaths(), shift);
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

        // F9 — kétpaneles nézet be/ki, mindkét kiosztásban.
        if (e.Key == System.Windows.Input.Key.F9 && !ctrl && !alt)
        {
            e.Handled = true;
            _viewModel.DualPaneEnabled = !_viewModel.DualPaneEnabled;
            return;
        }

        if (HandleNavigationKey(e, ctrl, shift, alt))
        {
            e.Handled = true;
            return;
        }

        if (_viewModel.DualPaneEnabled && HandleDualPaneKey(e, ctrl, shift, alt))
        {
            e.Handled = true;
            return;
        }

        // Kétpaneles nézetben a Total Commander-billentyűk (F3–F8, Tab,
        // Insert, Ctrl+U …) a Modern kiosztás mellett is élnek — a
        // kétpaneles nézetet pont ezekért kapcsolja be az ember. Egypaneles
        // nézetben a Modern kiosztás marad az Intéző-konvenció.
        var modern = _settings.Current.Keymap != KeymapPreset.PilasterClassic;

        if (modern && !_viewModel.DualPaneEnabled)
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

            case System.Windows.Input.Key.F4 when !shift:
                e.Handled = true;
                EditActiveSelection();
                break;


            case System.Windows.Input.Key.F5:
                e.Handled = true;
                _ = StartTcTransferAsync(isMove: false);
                break;

            case System.Windows.Input.Key.F6 when !shift:
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

            // Csak a fájllistán — egy gombon/kapcsolón a Space a saját dolgát végzi.
            case System.Windows.Input.Key.Space when System.Windows.Input.Keyboard.FocusedElement is System.Windows.Controls.ListBoxItem or System.Windows.Controls.ListBox:
                e.Handled = true;
                ToggleCurrentSelection();
                break;

            case System.Windows.Input.Key.F4 when shift:
                e.Handled = true;

                if (GetActiveTab() is { } newFileTab)
                {
                    _ = _viewModel.CreateNewFileInTabAsync(newFileTab);
                }

                break;

            case System.Windows.Input.Key.F6 when shift:
                e.Handled = true;
                RenameActiveSelection();
                break;

            case System.Windows.Input.Key.Add:
                e.Handled = true;
                SelectByMask(select: true);
                break;

            case System.Windows.Input.Key.A when ctrl:
                e.Handled = true;
                GetActiveList()?.SelectAll();
                break;

            case System.Windows.Input.Key.D when ctrl:
                e.Handled = true;
                GetActiveList()?.UnselectAll();
                break;

            case System.Windows.Input.Key.Subtract:
                e.Handled = true;
                SelectByMask(select: false);
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
                SwapPanesAnimated();
                break;

            case System.Windows.Input.Key.L when ctrl:
                e.Handled = true;
                _ = _viewModel.CopyLeftPathToRightCommand.ExecuteAsync(null);
                break;

            case System.Windows.Input.Key.R when ctrl && shift:
                e.Handled = true;
                RefreshActiveTab();
                break;

            case System.Windows.Input.Key.R when ctrl && modern:
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
    /// Az Intézőben megszokott navigációs billentyűk, mindkét kiosztásban:
    /// Alt+←/→/↑ (vissza/előre/fel), Ctrl+F (keresés), és egypaneles
    /// nézetben Enter (megnyitás), Backspace (fel), F2 (átnevezés),
    /// Alt+D / Ctrl+L (útvonalsáv), Ctrl+Shift+N (új mappa). A Beállítások
    /// billentyűlistája korábban is ezeket ígérte, de egypaneles nézetben
    /// egyik sem működött. Igaz, ha kezelte.
    /// </summary>
    private bool HandleNavigationKey(System.Windows.Input.KeyEventArgs e, bool ctrl, bool shift, bool alt)
    {
        var tab = _viewModel.DualPaneEnabled ? _viewModel.ActivePaneTab : _viewModel.SelectedTab;

        if (tab is null)
        {
            return false;
        }

        if (alt && !ctrl && !shift)
        {
            switch (e.SystemKey)
            {
                case System.Windows.Input.Key.Left:
                    _ = tab.GoBackCommand.ExecuteAsync(null);
                    return true;

                case System.Windows.Input.Key.Right:
                    _ = tab.GoForwardCommand.ExecuteAsync(null);
                    return true;

                case System.Windows.Input.Key.Up:
                    _ = tab.GoUpCommand.ExecuteAsync(null);
                    return true;

                case System.Windows.Input.Key.D when !_viewModel.DualPaneEnabled:
                    tab.BeginEditPathCommand.Execute(null);
                    return true;
            }

            return false;
        }

        if (ctrl && !alt && e.Key == System.Windows.Input.Key.F)
        {
            QuickFilterBox.Focus();
            QuickFilterBox.SelectAll();
            return true;
        }

        // A többi csak az egypaneles listán: a panelek saját kezelője
        // (FilePaneView) intézi az Entert és a Backspace-t, és egy fókuszban
        // lévő gombtól sem szabad elvenni az Entert.
        if (_viewModel.DualPaneEnabled || !FileListHost.IsKeyboardFocusWithin)
        {
            if (ctrl && !shift && !alt && e.Key == System.Windows.Input.Key.L
                && !_viewModel.DualPaneEnabled && _settings.Current.Keymap != KeymapPreset.PilasterClassic)
            {
                tab.BeginEditPathCommand.Execute(null);
                return true;
            }

            return false;
        }

        switch (e.Key)
        {
            case System.Windows.Input.Key.Enter when !ctrl && !shift && !alt:
                OpenActiveSelection();
                return true;

            case System.Windows.Input.Key.Back when !ctrl && !shift && !alt:
            case System.Windows.Input.Key.PageUp when ctrl:
                _ = tab.GoUpCommand.ExecuteAsync(null);
                return true;

            case System.Windows.Input.Key.F2 when !ctrl && !shift && !alt:
                RenameActiveSelection();
                return true;

            case System.Windows.Input.Key.L when ctrl && !shift && !alt && _settings.Current.Keymap != KeymapPreset.PilasterClassic:
                tab.BeginEditPathCommand.Execute(null);
                return true;

            case System.Windows.Input.Key.N when ctrl && shift && !alt:
                CreateFolderInActivePane();
                return true;
        }

        return false;
    }

    /// <summary>
    /// Enter: mappánál belép (a kurzor alatti elembe), fájloknál a
    /// kijelölteket megnyitja a társított programmal — mint az Intézőben.
    /// </summary>
    private void OpenActiveSelection()
    {
        if (GetActiveList() is not { } list || GetFocusedItem(list) is not { } focused)
        {
            return;
        }

        if (focused.IsNavigable)
        {
            _ = OpenItemAsync(focused);
            return;
        }

        // Egy elcsúszott Ctrl+A + Enter ne indítson el száz programot.
        foreach (var item in list.SelectedItems.Cast<FileSystemItem>().Where(i => !i.IsNavigable && !i.IsRecycled).Take(15))
        {
            OpenWithShell(item.FullPath);
        }
    }

    /// <summary>Az egér oldalsó (vissza/előre) gombjai, mint az Intézőben és a böngészőkben.</summary>
    private void OnWindowPreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton is not (System.Windows.Input.MouseButton.XButton1 or System.Windows.Input.MouseButton.XButton2)
            || (_viewModel.DualPaneEnabled ? _viewModel.ActivePaneTab : _viewModel.SelectedTab) is not { } tab)
        {
            return;
        }

        e.Handled = true;
        _ = e.ChangedButton == System.Windows.Input.MouseButton.XButton1
            ? tab.GoBackCommand.ExecuteAsync(null)
            : tab.GoForwardCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// Kétpaneles nézet billentyűi, kiosztástól függetlenül: vágólap
    /// (Ctrl+C/X/V — korábban csak az egypaneles listán éltek), Alt+F1/F2
    /// meghajtóválasztó, Ctrl+←/→ a kurzor alatti mappa megnyitása a
    /// bal/jobb panelben, Ctrl+PgUp szülőmappa. Igaz, ha kezelte.
    /// </summary>
    private bool HandleDualPaneKey(System.Windows.Input.KeyEventArgs e, bool ctrl, bool shift, bool alt)
    {
        if (alt && !ctrl)
        {
            switch (e.SystemKey)
            {
                case System.Windows.Input.Key.F1:
                    ShowDriveMenu(left: true);
                    return true;

                case System.Windows.Input.Key.F2:
                    ShowDriveMenu(left: false);
                    return true;
            }

            return false;
        }

        if (!ctrl || GetActiveTab() is not { IsHome: false, IsRecycleBin: false } tab)
        {
            return false;
        }

        switch (e.Key)
        {
            case System.Windows.Input.Key.C:
                _viewModel.CopySelectionCommand.Execute(GetActivePaneSelectedPaths());
                return true;

            case System.Windows.Input.Key.X:
                _viewModel.CutSelectionCommand.Execute(GetActivePaneSelectedPaths());
                return true;

            case System.Windows.Input.Key.V:
                _viewModel.PasteCommand.Execute(null);
                return true;

            case System.Windows.Input.Key.PageUp:
                _ = tab.GoUpCommand.ExecuteAsync(null);
                return true;

            case System.Windows.Input.Key.Left or System.Windows.Input.Key.Right when !shift:
                if (GetActiveList() is { } list && GetFocusedItem(list) is { IsNavigable: true } folder)
                {
                    var target = e.Key == System.Windows.Input.Key.Left ? _viewModel.LeftPane : _viewModel.RightPane;
                    _ = target.NavigateAsync(folder.FullPath);
                }

                return true;
        }

        return false;
    }

    private List<string> GetActivePaneSelectedPaths() =>
        GetActiveList() is { } list ? [.. list.SelectedItems.Cast<FileSystemItem>().Select(i => i.FullPath)] : [];

    /// <summary>Alt+F1 / Alt+F2 — meghajtóválasztó menü a bal/jobb panel tetején (mint a Total Commanderben).</summary>
    private void ShowDriveMenu(bool left)
    {
        var paneView = left ? LeftPaneView : RightPaneView;
        var pane = left ? _viewModel.LeftPane : _viewModel.RightPane;
        var menu = new System.Windows.Controls.ContextMenu
        {
            PlacementTarget = paneView,
            Placement = PlacementMode.Relative,
            HorizontalOffset = 12,
            VerticalOffset = 44,
        };

        foreach (var drive in _viewModel.HomeDriveItems)
        {
            var item = new System.Windows.Controls.MenuItem
            {
                Header = drive.Label,
                InputGestureText = drive.Detail ?? string.Empty,
                Icon = new SymbolIcon { Symbol = drive.Icon, FontSize = 15 },
            };
            var path = drive.Path;
            item.Click += (_, _) =>
            {
                _viewModel.IsLeftPaneActive = left;
                _ = pane.NavigateAsync(path);
                FocusActivePaneList();
            };
            menu.Items.Add(item);
        }

        menu.Opened += OnGlassContextMenuOpened;
        menu.IsOpen = true;
    }

    /// <summary>
    /// Num+ / Num− — kijelölés (vagy a kijelölés megszüntetése) névmaszkkal
    /// (pl. <c>*.jpg</c>), mint a Total Commanderben.
    /// </summary>
    private void SelectByMask(bool select)
    {
        if (GetActiveList() is not { } list)
        {
            return;
        }

        var strings = TranslationSource.Instance;
        var mask = MaskInputWindow.Ask(this, strings[select ? "Mask_SelectTitle" : "Mask_UnselectTitle"], strings["Mask_Hint"]);

        if (string.IsNullOrWhiteSpace(mask))
        {
            return;
        }

        var patterns = mask.Split([';', ' '], StringSplitOptions.RemoveEmptyEntries);

        foreach (var item in list.Items.OfType<FileSystemItem>())
        {
            if (!patterns.Any(p => System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(p, item.Name)))
            {
                continue;
            }

            if (select && !list.SelectedItems.Contains(item))
            {
                list.SelectedItems.Add(item);
            }
            else if (!select)
            {
                list.SelectedItems.Remove(item);
            }
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

        RequestDelete(paths, permanent);
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
        // Lomtár-elem: nincs mögötte valódi, megnyitható útvonal — a
        // Windows Intéző is csak visszaállítás után engedi megnyitni.
        // Duplaklikk itt szándékosan nem csinál semmit (lásd a jobbklikk
        // menü Visszaállítás/Végleges törlés parancsait).
        if (item.IsRecycled)
        {
            return;
        }

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

        // A kétpaneles nézet ebből állítja vissza a kijelölést — enélkül
        // átváltáskor az állapotsor „1 kijelölve" maradt, a panelben pedig
        // semmi sem volt kijelölve.
        tab.SelectedPaths = [.. selected.Select(item => item.FullPath)];

        // A2 (v1.0.2): a kijelölésre indított, debounce-olt shell-előretöltés
        // — lásd ShellMenuPreloadCoordinator. Csak fájl-kijelölésre indul (a
        // mappa-háttér menüje nem kijelöléstől függ), és a felhasználó a
        // Beállítások → Jobbklikk menü alatt kikapcsolhatja.
        if (_services.GetService(typeof(ShellMenuPreloadCoordinator)) is ShellMenuPreloadCoordinator preload)
        {
            var extendedVerbs = System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift);
            preload.NotifySelectionChanged(selected.Select(i => i.FullPath).ToList(), extendedVerbs);
        }
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

        // A Lomtár (item.Path == TabViewModel.RecycleBinMarker) ugyanezen az
        // úton navigál, mint bármelyik valódi mappa — lásd
        // TabViewModel.LoadRecycleBinAsync. Nincs külön ág rá.
        if (_viewModel.SelectedTab is { } tab)
        {
            await tab.NavigateAsync(item.Path);
        }
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

        if (sender is not FrameworkElement { DataContext: FileSystemItem item } container)
        {
            return;
        }

        // Fájl és mappa egyaránt húzható — a kijelölés részeként megfogva az
        // EGÉSZ kijelölés. Korábban csak egyetlen mappa, és csak „hivatkozás"
        // effekttel: így az oldalsáv mappáira (pl. Dokumentumok) semmit nem
        // lehetett behúzni.
        var paths = ItemsControl.ItemsControlFromItemContainer(container) is System.Windows.Controls.ListBox { SelectedItems.Count: > 1 } list
            && list.SelectedItems.Contains(item)
                ? list.SelectedItems.OfType<FileSystemItem>().Select(i => i.FullPath).ToArray()
                : [item.FullPath];

        var data = new System.Windows.DataObject(System.Windows.DataFormats.FileDrop, paths);
        DragDrop.DoDragDrop(container, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
    }

    /// <summary>Rámutatásos jobbklikk-előtöltés — lásd <see cref="ShellMenuPreloadCoordinator.NotifyHover"/>.</summary>
    private void OnFileItemMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed
            || sender is not FrameworkElement { DataContext: FileSystemItem { IsRecycled: false } item } container
            || _services.GetService(typeof(ShellMenuPreloadCoordinator)) is not ShellMenuPreloadCoordinator preload)
        {
            return;
        }

        var selection = ItemsControl.ItemsControlFromItemContainer(container) is System.Windows.Controls.ListBox list
            ? list.SelectedItems.OfType<FileSystemItem>().Select(i => i.FullPath).ToList()
            : [];

        preload.NotifyHover(item.FullPath, selection);
    }

    private void OnSidebarItemPreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);

        // A jobb gomb nem indít húzást, viszont a soron menüt nyit — a
        // PreviewMouseLeftButtonDown csak a bal gombra fut, ezért a
        // jobbklikk-menü külön kezelőben él (OnSidebarItemRightClick).
    }

    /// <summary>
    /// Jobbklikk a „Gyorselérés" fejlécen → a szerkesztő megnyitása (spec F5),
    /// vagy a „Felhő meghajtók" fejlécen → új felhő meghajtó hozzáadása
    /// (spec: NextCloud-támogatás). Más szekciók fejlécén nincs menü.
    /// </summary>
    private void OnSidebarHeaderRightClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: "Nav_QuickAccess" or "Nav_CloudDrives" } header)
        {
            return;
        }

        e.Handled = true;

        var strings = TranslationSource.Instance;
        var menu = new System.Windows.Controls.ContextMenu { PlacementTarget = header };

        if ((string)header.Tag == "Nav_QuickAccess")
        {
            var edit = new System.Windows.Controls.MenuItem { Header = strings["QuickAccess_Edit"] };
            edit.Click += (_, _) => OpenQuickAccessEditor();
            menu.Items.Add(edit);
        }
        else
        {
            var add = new System.Windows.Controls.MenuItem { Header = strings["CloudDrive_Add"] };
            add.Click += (_, _) => OpenAddCloudDriveWindow();
            menu.Items.Add(add);
        }

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

        if (item.IsCloudDrive)
        {
            menu.Items.Add(new System.Windows.Controls.Separator());
            Add(strings["CloudDrive_Remove"], () => _viewModel.RemoveCloudDriveCommand.Execute(item));
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
            WindowBackdropType = Services.GlassEffectService.CurrentBackdrop,
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

    /// <summary>Az „Felhő meghajtó hozzáadása" ablak megnyitása (spec: NextCloud-támogatás).</summary>
    private void OpenAddCloudDriveWindow()
    {
        var dialog = _services.GetRequiredService<AddCloudDriveWindow>();
        dialog.Owner = this;
        dialog.ShowDialog();
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
        e.Handled = true;

        if (e.Data.GetDataPresent(QuickAccessReorderFormat))
        {
            SetSidebarDropHighlight(null);
            e.Effects = DragDropEffects.Move;
            return;
        }

        if (sender is not ListBox listBox || e.Data.GetData(System.Windows.DataFormats.FileDrop) is not string[] { Length: > 0 } paths)
        {
            SetSidebarDropHighlight(null);
            e.Effects = DragDropEffects.None;
            return;
        }

        var container = FindSidebarContainerAt(listBox, e.GetPosition(listBox));
        var (effect, highlight) = ResolveSidebarDrop(container?.DataContext as SidebarItemViewModel, paths, e.KeyStates, e.AllowedEffects);
        SetSidebarDropHighlight(highlight ? container : null);
        e.Effects = effect;
    }

    private void OnSidebarDragLeave(object sender, DragEventArgs e) => SetSidebarDropHighlight(null);

    /// <summary>
    /// Ejtés az oldalsávon:
    /// <list type="bullet">
    /// <item>gyorselérés-sor átrendezése (belső húzás);</item>
    /// <item>fájlok/mappák egy oldalsáv-MAPPÁRA (Dokumentumok, meghajtó, felhő):
    /// másolás vagy áthelyezés oda — ugyanaz a szabály, mint a kétpaneles
    /// nézetben (azonos kötet: áthelyezés, különben másolás; Ctrl/Shift/Alt
    /// felülírja);</item>
    /// <item>a Lomtárra: törlés a Lomtárba;</item>
    /// <item>mappa a sorok közé / üres helyre: rögzítés a gyorselérésbe.</item>
    /// </list>
    /// </summary>
    private void OnSidebarDrop(object sender, DragEventArgs e)
    {
        SetSidebarDropHighlight(null);

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

        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is not string[] { Length: > 0 } paths)
        {
            return;
        }

        e.Handled = true;
        var target = FindSidebarItemAt(listBox, e.GetPosition(listBox));

        if (target is { IsRecycleBin: true })
        {
            RequestDelete(paths, permanent: false);
            return;
        }

        if (GetSidebarDropFolder(target) is { } folder)
        {
            if (!IsValidDropInto(paths, folder))
            {
                return;
            }

            var action = FilePaneView.ResolveDropEffect(paths, folder, ToModifierKeys(e.KeyStates));
            OnPaneFilesDropped(this, (paths, folder, action));
            return;
        }

        foreach (var path in paths.Where(Directory.Exists))
        {
            _viewModel.PinToQuickAccessCommand.Execute(path);
        }
    }

    /// <summary>A húzás effektje és az, hogy a célsor kiemelést kap-e — lásd <see cref="OnSidebarDrop"/>.</summary>
    private static (DragDropEffects Effect, bool Highlight) ResolveSidebarDrop(
        SidebarItemViewModel? target, string[] paths, DragDropKeyStates keys, DragDropEffects allowed)
    {
        if (target is { IsRecycleBin: true })
        {
            return ((allowed & DragDropEffects.Move) != 0 ? DragDropEffects.Move : DragDropEffects.None, true);
        }

        if (GetSidebarDropFolder(target) is { } folder)
        {
            if (!IsValidDropInto(paths, folder))
            {
                return (DragDropEffects.None, false);
            }

            var wanted = FilePaneView.ResolveDropEffect(paths, folder, ToModifierKeys(keys)) switch
            {
                PaneDropAction.Move => DragDropEffects.Move,
                PaneDropAction.Shortcut => DragDropEffects.Link,
                _ => DragDropEffects.Copy,
            };

            return ((allowed & wanted) != 0 ? wanted : allowed & DragDropEffects.Copy, true);
        }

        // Nem mappa-sor fölött: mappák rögzítése a gyorselérésbe.
        return (paths.All(Directory.Exists) && (allowed & DragDropEffects.Link) != 0 ? DragDropEffects.Link : DragDropEffects.None, false);
    }

    /// <summary>Az oldalsáv-sor célmappája, ha oda lehet fájlt ejteni (létező mappa, nem a Kezdőlap/Lomtár).</summary>
    private static string? GetSidebarDropFolder(SidebarItemViewModel? item) =>
        item is { IsRecycleBin: false, IsHomeEntry: false, IsMissing: false, Path: { Length: > 0 } path } && Directory.Exists(path)
            ? path
            : null;

    private static bool IsValidDropInto(IEnumerable<string> paths, string folder) => FileDropHelper.IsValidDropInto(paths, folder);

    /// <summary>A fájllista húzás alatti célsor-kiemelése — lásd <see cref="ResolveFileListDrop"/>.</summary>
    private readonly FileDropHelper _fileListDrop = new();

    private void OnFileListDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;

        if (ResolveFileListDrop(e) is not { } drop)
        {
            _fileListDrop.Highlight(null);
            e.Effects = DragDropEffects.None;
            return;
        }

        _fileListDrop.Highlight(drop.Row);
        e.Effects = FileDropHelper.ToEffect(
            FilePaneView.ResolveDropEffect(drop.Paths, drop.Folder, ToModifierKeys(e.KeyStates)),
            e.AllowedEffects);
    }

    private void OnFileListDragLeave(object sender, DragEventArgs e) => _fileListDrop.Highlight(null);

    /// <summary>
    /// Fájlok ejtése a fájllistára — az Asztalról, az Intézőből vagy a lista
    /// saját soraiból. Korábban az egypaneles listára egyáltalán nem lehetett
    /// ejteni, csak a kétpaneles nézet paneljeire és az oldalsávra.
    /// </summary>
    private void OnFileListDrop(object sender, DragEventArgs e)
    {
        _fileListDrop.Highlight(null);

        if (ResolveFileListDrop(e) is not { } drop)
        {
            return;
        }

        e.Handled = true;
        OnPaneFilesDropped(this, (drop.Paths, drop.Folder, FilePaneView.ResolveDropEffect(drop.Paths, drop.Folder, ToModifierKeys(e.KeyStates))));
    }

    /// <summary>
    /// Az ejtés célja: egy mappa-sor fölött az a mappa, máshol a lista mappája —
    /// oszlopos nézetben az adott oszlopé, egyébként az aktív fülé. A
    /// Kezdőlapra és a Lomtárba nem lehet ejteni.
    /// </summary>
    private (string[] Paths, string Folder, System.Windows.Controls.ListBoxItem? Row)? ResolveFileListDrop(DragEventArgs e)
    {
        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is not string[] { Length: > 0 } paths)
        {
            return null;
        }

        var hit = e.OriginalSource as DependencyObject;
        var list = FileDropHelper.FindAncestor<ItemsControl>(hit);
        var tab = list?.DataContext as TabViewModel ?? _viewModel.SelectedTab;

        if (tab is not { IsHome: false, IsRecycleBin: false, CurrentPath: { } folder }
            || FileDropHelper.ResolveTarget(hit, folder, paths) is not { } target)
        {
            return null;
        }

        return (paths, target.Folder, target.Row);
    }

    private static System.Windows.Input.ModifierKeys ToModifierKeys(DragDropKeyStates keys)
    {
        var modifiers = System.Windows.Input.ModifierKeys.None;

        if (keys.HasFlag(DragDropKeyStates.ControlKey))
        {
            modifiers |= System.Windows.Input.ModifierKeys.Control;
        }

        if (keys.HasFlag(DragDropKeyStates.ShiftKey))
        {
            modifiers |= System.Windows.Input.ModifierKeys.Shift;
        }

        if (keys.HasFlag(DragDropKeyStates.AltKey))
        {
            modifiers |= System.Windows.Input.ModifierKeys.Alt;
        }

        return modifiers;
    }

    /// <summary>Az a sor, amelyik fölött épp húzunk — a sablonja a <c>Tag="DropTarget"</c> értékre kiemelést kap.</summary>
    private System.Windows.Controls.ListBoxItem? _sidebarDropHighlight;

    private void SetSidebarDropHighlight(System.Windows.Controls.ListBoxItem? item)
    {
        if (ReferenceEquals(item, _sidebarDropHighlight))
        {
            return;
        }

        if (_sidebarDropHighlight is not null)
        {
            _sidebarDropHighlight.Tag = null;
        }

        _sidebarDropHighlight = item;

        if (item is not null)
        {
            item.Tag = SidebarDropTargetTag;
        }
    }

    private const string SidebarDropTargetTag = "DropTarget";

    private static SidebarItemViewModel? FindSidebarItemAt(ListBox listBox, System.Windows.Point position) =>
        FindSidebarContainerAt(listBox, position)?.DataContext as SidebarItemViewModel;

    private static System.Windows.Controls.ListBoxItem? FindSidebarContainerAt(ListBox listBox, System.Windows.Point position)
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

        return current as System.Windows.Controls.ListBoxItem;
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
    private async void OnUpdateRestartRequested(object? sender, EventArgs e)
    {
        var strings = TranslationSource.Instance;

        if (!await ModernDialog.ConfirmAsync(
                this,
                "Pilaster",
                string.Format(strings["Update_ConfirmRestartMessage"], _viewModel.Updates.PendingVersion),
                strings["Cmd_RestartNow"]))
        {
            return;
        }

        _viewModel.Updates.BeginInstallAndExit();
        System.Windows.Application.Current.Shutdown();
    }

    /// <summary>
    /// A kiadás eredménye: sikernél csak egy magától eltűnő buborék (nincs mit
    /// „leokézni"), hibánál modern párbeszédablak a teendővel.
    /// </summary>
    private async void OnEjectCompleted(object? sender, EjectOutcome outcome)
    {
        var strings = TranslationSource.Instance;

        if (outcome == EjectOutcome.Succeeded)
        {
            ShowToast(strings["Eject_Success"], SymbolRegular.CheckmarkCircle24);
            return;
        }

        await ModernDialog.InformAsync(
            this,
            strings["Cmd_Eject"],
            outcome == EjectOutcome.InUse ? strings["Eject_InUse"] : strings["Eject_Error"]);
    }

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
