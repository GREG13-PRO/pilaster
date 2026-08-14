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

    /// <summary>A Beállítások ablak, amíg nyitva van — hogy ne nyíljon kettő.</summary>
    private SettingsWindow? _settingsWindow;

    /// <summary>A Lomtár-ablak, amíg nyitva van — hogy ne nyíljon kettő.</summary>
    private RecycleBinWindow? _recycleBinWindow;

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

        var screenPoint = PointToScreen(e.GetPosition(this));
        var ownerHandle = new WindowInteropHelper(this).Handle;

        _isNativeContextMenuOpen = true;

        bool shown;

        try
        {
            shown = await NativeContextMenuService.ShowAsync(selectedPaths, (int)screenPoint.X, (int)screenPoint.Y, ownerHandle);
        }
        finally
        {
            _isNativeContextMenuOpen = false;
        }

        if (!shown && TryFindResource("FileItemContextMenu") is System.Windows.Controls.ContextMenu fallbackMenu)
        {
            fallbackMenu.PlacementTarget = container;
            fallbackMenu.IsOpen = true;
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
        if (((FrameworkElement)sender).DataContext is not FileSystemItem item)
        {
            return;
        }

        var metadata = _services.GetRequiredService<FileMetadataService>();
        var colorConverter = new TagColorConverter();
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
                    Icon = new System.Windows.Shapes.Ellipse
                    {
                        Width = 10,
                        Height = 10,
                        Fill = (System.Windows.Media.Brush)colorConverter.Convert(
                            tag.Color, typeof(System.Windows.Media.Brush), null, System.Globalization.CultureInfo.CurrentCulture),
                    },
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

        menu.PlacementTarget = (UIElement)sender;
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

    private void OnSidebarItemPreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
        _dragStartPoint = e.GetPosition(null);

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

        if (sender is not FrameworkElement { DataContext: SidebarItemViewModel { IsUnpinnable: true } item } container)
        {
            return;
        }

        var data = new System.Windows.DataObject(QuickAccessReorderFormat, item.Path);
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

        if (e.Data.GetData(QuickAccessReorderFormat) is string sourcePath)
        {
            if (FindSidebarItemAt(listBox, e.GetPosition(listBox)) is { } target)
            {
                _viewModel.ReorderQuickAccess(sourcePath, target.Path);
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
