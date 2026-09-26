using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Pilaster.Core.Settings;
using System.Windows.Navigation;
using Pilaster.App.Diagnostics;
using Pilaster.App.Localization;
using Pilaster.App.ViewModels;
using Wpf.Ui.Controls;

namespace Pilaster.App.Views;

public partial class SettingsWindow : FluentWindow
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        DataContext = viewModel;

        // A témaváltás átúsztatása ezen az ablakon fusson, hogy a felhasználó
        // ott lássa a hatást, ahol épp állítja.
        viewModel.AnimationHost = this;

        viewModel.NavigateToSettingRequested += OnNavigateToSettingRequested;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;

        InitializeComponent();

        Loaded += OnLoaded;
        Closed += (_, _) =>
        {
            StopScrollAnimation();
            viewModel.NavigateToSettingRequested -= OnNavigateToSettingRequested;
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        };
    }

    // ================= Egyoldalas elrendezés: tartalomjegyzék ↔ görgetés =================

    private const string CategoryTagPrefix = "category:";

    /// <summary>A kategória címe fölött ennyi DIP hely marad odaugráskor.</summary>
    private const double CategoryTopGap = 8;

    /// <summary>Egy egérgörgő-lépés (120 delta) ennyi DIP-et görget — a böngészők nagyságrendje.</summary>
    private const double WheelStep = 90;

    /// <summary>A kategóriák tartalomblokkjai azonosító szerint — egyszer gyűjtjük be (lásd <see cref="OnLoaded"/>).</summary>
    private readonly Dictionary<string, FrameworkElement> _sections = new(StringComparer.Ordinal);

    /// <summary>
    /// Igaz, amíg a kijelölést a GÖRGETÉS állítja (lásd <see cref="SyncSelectionToScroll"/>)
    /// — ilyenkor a kijelölés-változás nem indíthat újabb odagörgetést, különben
    /// a kettő egymást rángatná.
    /// </summary>
    private bool _selectionFromScroll;

    /// <summary>A sima görgetés célpozíciója — az animáció ehhez közelít képkockánként.</summary>
    private double _scrollTarget;

    private bool _scrollAnimating;

    /// <summary>
    /// Igaz, amíg egy kattintásra indított odagörgetés tart — közben a
    /// görgetés-követés nem írja felül a kiválasztott kategóriát az átsuhanó
    /// köztes kategóriákkal.
    /// </summary>
    private bool _suppressScrollSpy;

    private TimeSpan _lastFrameTime;

    private AnimationLevel CurrentAnimationLevel =>
        DataContext is SettingsViewModel viewModel ? viewModel.SelectedAnimationLevel : AnimationLevel.Full;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel)
        {
            foreach (var category in viewModel.Categories)
            {
                if (FindByTag(ContentScroll, CategoryTagPrefix + category.Id) is { } section)
                {
                    _sections[category.Id] = section;
                }
            }
        }

        ContentScroll.PreviewMouseWheel += OnContentPreviewMouseWheel;
        CategoryList.SizeChanged += (_, _) => MoveSelectionIndicator(animate: false);

        MoveSelectionIndicator(animate: false);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Keresés közben a lista szűkül (csak a találatos kategóriák látszanak),
        // így a sorok helye is változik — a jelölő a következő elrendezés után
        // igazodik hozzájuk.
        if (e.PropertyName == nameof(SettingsViewModel.IsSearching))
        {
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => MoveSelectionIndicator(animate: false));
        }
    }

    private void OnCategorySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Az InitializeComponent közben a SelectedItem-kötés már kijelöl, de a
        // tartalomterület ekkor még nem létezik — az első megnyitáskor úgyis a
        // lap tetején (az első kategóriánál) állunk, lásd OnLoaded.
        if (!IsLoaded)
        {
            return;
        }

        MoveSelectionIndicator(animate: true);

        if (_selectionFromScroll
            || DataContext is not SettingsViewModel { IsSearching: false }
            || CategoryList.SelectedItem is not SettingsCategoryViewModel category)
        {
            return;
        }

        ScrollToCategory(category.Id);
    }

    /// <summary>
    /// A csúszó kijelölés-jelölő a kijelölt sorhoz igazítása — animálva
    /// (lassuló csúszás), vagy azonnal (megnyitáskor, átméretezéskor).
    /// </summary>
    private void MoveSelectionIndicator(bool animate)
    {
        if (CategoryList.SelectedItem is null
            || CategoryList.ItemContainerGenerator.ContainerFromItem(CategoryList.SelectedItem) is not ListBoxItem { IsVisible: true, ActualHeight: > 0 } container)
        {
            SelectionIndicator.Opacity = 0;
            return;
        }

        var position = container.TranslatePoint(new Point(0, 0), IndicatorLayer);

        SelectionIndicator.Width = container.ActualWidth;
        SelectionIndicator.Height = container.ActualHeight;
        Canvas.SetLeft(SelectionIndicator, position.X);

        var wasHidden = SelectionIndicator.Opacity == 0;
        SelectionIndicator.Opacity = 1;

        var level = CurrentAnimationLevel;

        if (!animate || wasHidden || level == AnimationLevel.Off)
        {
            SelectionIndicatorOffset.BeginAnimation(TranslateTransform.YProperty, null);
            SelectionIndicatorOffset.Y = position.Y;
            return;
        }

        var slide = new DoubleAnimation(position.Y, TimeSpan.FromMilliseconds(level == AnimationLevel.Reduced ? 120 : 240))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };

        // A „From" nélküli animáció az aktuális (akár épp csúszó) pozícióból
        // indul — egy gyors görgetés közbeni több váltás így folyamatos marad.
        SelectionIndicatorOffset.BeginAnimation(TranslateTransform.YProperty, slide, HandoffBehavior.SnapshotAndReplace);
    }

    private void ScrollToCategory(string categoryId)
    {
        if (!_sections.TryGetValue(categoryId, out var section) || ContentScroll.Content is not Visual content)
        {
            return;
        }

        // A kategória CÍMÉHEZ igazítunk (a blokk első eleme), nem a blokk
        // felső széléhez — a címsor feletti térköz különben üresen maradna.
        var anchor = section is Panel { Children.Count: > 0 } panel && panel.Children[0] is FrameworkElement header
            ? header
            : section;
        var target = anchor.TransformToAncestor(content).Transform(new Point(0, 0)).Y - CategoryTopGap;

        ScrollSmoothlyTo(target, suppressScrollSpy: true);
    }

    /// <summary>
    /// Egérgörgő: a WPF alapértelmezett, lépcsős (egyszerre 48 DIP-et ugró)
    /// görgetése helyett sima, lassuló csúszás. Több gyors görgetés a célt
    /// tolja tovább, a mozgás közben nem áll meg.
    /// </summary>
    private void OnContentPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (CurrentAnimationLevel == AnimationLevel.Off || IsInsideNestedScroller(e.OriginalSource as DependencyObject, e.Delta))
        {
            return;
        }

        e.Handled = true;

        var from = _scrollAnimating ? _scrollTarget : ContentScroll.VerticalOffset;
        ScrollSmoothlyTo(from - (e.Delta / 120.0 * WheelStep), suppressScrollSpy: false);
    }

    /// <summary>
    /// Igaz, ha a görgetés egy BELSŐ, a görgetés irányában még görgethető
    /// területen (pl. a billentyűkiosztás táblázata) történik — azt nem
    /// vehetjük el tőle.
    /// </summary>
    private bool IsInsideNestedScroller(DependencyObject? source, int delta)
    {
        for (var node = source;
             node is not null && !ReferenceEquals(node, ContentScroll);
             node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
        {
            if (node is ScrollViewer { ScrollableHeight: > 0 } inner)
            {
                var canScroll = delta > 0 ? inner.VerticalOffset > 0 : inner.VerticalOffset < inner.ScrollableHeight;

                if (canScroll)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Sima görgetés a célpontig. Képkockánként exponenciálisan közelít
    /// (a képkocka-időből számolva, így gyors és lassú gépen is ugyanolyan
    /// tempójú) — egy új cél menet közben egyszerűen átveszi a régit.
    /// </summary>
    private void ScrollSmoothlyTo(double target, bool suppressScrollSpy)
    {
        _scrollTarget = Math.Clamp(target, 0, ContentScroll.ScrollableHeight);
        _suppressScrollSpy = suppressScrollSpy;

        if (CurrentAnimationLevel == AnimationLevel.Off)
        {
            ContentScroll.ScrollToVerticalOffset(_scrollTarget);
            _suppressScrollSpy = false;
            return;
        }

        if (!_scrollAnimating)
        {
            _scrollAnimating = true;
            _lastFrameTime = TimeSpan.Zero;
            CompositionTarget.Rendering += OnScrollFrame;
        }
    }

    private void OnScrollFrame(object? sender, EventArgs e)
    {
        var now = e is RenderingEventArgs rendering ? rendering.RenderingTime : TimeSpan.Zero;

        // Az első képkockánál még nincs előző időpont — egy átlagos (60 Hz-es)
        // képkockával számolunk.
        var dt = _lastFrameTime == TimeSpan.Zero || now <= _lastFrameTime
            ? 1.0 / 60
            : Math.Min((now - _lastFrameTime).TotalSeconds, 0.05);
        _lastFrameTime = now;

        var current = ContentScroll.VerticalOffset;
        var remaining = _scrollTarget - current;

        if (Math.Abs(remaining) < 0.5)
        {
            ContentScroll.ScrollToVerticalOffset(_scrollTarget);
            StopScrollAnimation();
            return;
        }

        // Kb. 0,25 mp alatt teszi meg a táv 98%-át — lendületes, de nem ugrik.
        var speed = CurrentAnimationLevel == AnimationLevel.Reduced ? 28.0 : 16.0;
        ContentScroll.ScrollToVerticalOffset(current + (remaining * (1 - Math.Exp(-dt * speed))));
    }

    /// <summary>A futó sima görgetés leállítása.</summary>
    private void StopScrollAnimation()
    {
        if (_scrollAnimating)
        {
            CompositionTarget.Rendering -= OnScrollFrame;
            _scrollAnimating = false;
        }

        _suppressScrollSpy = false;
    }

    private void OnContentScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange != 0 || e.ExtentHeightChange != 0)
        {
            SyncSelectionToScroll();
        }
    }

    /// <summary>
    /// Görgetés-követés: az a kategória a kijelölt, amelyiknek a címe utoljára
    /// haladt át a látható terület tetején. Az oldal legaljára érve az utolsó
    /// kategória — a rövid utolsó kategória (Névjegy) címe különben sosem érné
    /// el a tetejét. Sima görgetés közben képkockánként fut, így a bal oldali
    /// jelölő a görgetéssel együtt csúszik át a következő kategóriára.
    /// </summary>
    private void SyncSelectionToScroll()
    {
        if (_suppressScrollSpy
            || _sections.Count == 0
            || DataContext is not SettingsViewModel { IsSearching: false } viewModel
            || ContentScroll.Content is not Visual content)
        {
            return;
        }

        var atBottom = ContentScroll.ScrollableHeight > 0
            && ContentScroll.VerticalOffset >= ContentScroll.ScrollableHeight - 1;
        SettingsCategoryViewModel? current = null;

        foreach (var category in viewModel.Categories)
        {
            if (!_sections.TryGetValue(category.Id, out var section))
            {
                continue;
            }

            var top = section.TransformToAncestor(content).Transform(new Point(0, 0)).Y;

            if (atBottom || top <= ContentScroll.VerticalOffset + 24)
            {
                current = category;
            }
        }

        if (current is null || ReferenceEquals(current, viewModel.SelectedCategory))
        {
            return;
        }

        _selectionFromScroll = true;

        try
        {
            viewModel.SelectedCategory = current;
        }
        finally
        {
            _selectionFromScroll = false;
        }
    }

    /// <summary>A legutóbbi naplófájl megnyitása a társított programmal.</summary>
    private void OnOpenLogClick(object sender, RoutedEventArgs e)
    {
        var latest = Directory.Exists(LogFileLocator.LogDirectory)
            ? new DirectoryInfo(LogFileLocator.LogDirectory)
                .GetFiles("*.log")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault()
            : null;

        OpenWithShell(latest?.FullName ?? LogFileLocator.LogDirectory);
    }

    /// <summary>A konfigurációs mappa megnyitása (settings.json, metadata.json, quickaccess.json).</summary>
    /// <remarks>
    /// Az <see cref="Services.AppDataLocator"/>-t kérdezi: hordozható módban az
    /// adatok a program melletti <c>config</c> mappában vannak — a korábbi,
    /// fixen <c>%APPDATA%\Pilaster</c>-t nyitó gomb ott rossz helyre vitt.
    /// </remarks>
    private void OnOpenConfigFolderClick(object sender, RoutedEventArgs e) =>
        OpenWithShell(Services.AppDataLocator.Directory);

    private static void OpenWithShell(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            // Nincs társított program, vagy a mappa nem létezik — mindkettő
            // ártalmatlan; egy hibaüzenet itt aránytalan lenne.
        }
    }

    /// <summary>
    /// Mélyhivatkozás: a megadott azonosítójú beállításhoz görget, és rövid
    /// felvillantással kiemeli (spec F6).
    /// </summary>
    private void OnNavigateToSettingRequested(object? sender, string settingId)
    {
        // Background prioritás: a kategóriaváltás láthatóság-változásai csak a
        // következő elrendezési körben érvényesülnek, addig a célvezérlő
        // mérete nulla lenne, és a görgetés rossz helyre vinne.
        _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
        {
            if (FindByTag(ContentScroll, settingId) is not { } target)
            {
                return;
            }

            // A kategóriaváltás odagörgető animációja még futhat — le kell
            // állítani, különben a konkrét beállítás helyett a kategória
            // tetejére húzná vissza a nézetet.
            StopScrollAnimation();
            target.BringIntoView();
            Flash(target);
        });
    }

    private static FrameworkElement? FindByTag(DependencyObject root, string tag)
    {
        if (root is FrameworkElement { Tag: string value } element && value == tag)
        {
            return element;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            if (FindByTag(VisualTreeHelper.GetChild(root, i), tag) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>Rövid felvillantás — csak vizuális, semmilyen állapotot nem módosít.</summary>
    private static void Flash(FrameworkElement element)
    {
        var animation = new DoubleAnimation(1.0, 0.35, TimeSpan.FromMilliseconds(280))
        {
            AutoReverse = true,
            RepeatBehavior = new RepeatBehavior(2),
        };

        // Az animáció leválasztása után az Opacity újra szabadon állítható,
        // különben a rögzített érték „beragadna".
        animation.Completed += (_, _) =>
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = 1;
        };

        element.BeginAnimation(UIElement.OpacityProperty, animation);
    }

    /// <summary>Lásd BugReportViewModel.RegisterSecretClick: 10 kattintásra felnyílik a fejlesztői panel.</summary>
    private void OnBugReportHeaderClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel)
        {
            viewModel.BugReport.RegisterSecretClick();
        }
    }

    /// <summary>Lásd SettingsViewModel.RegisterVersionClick: 7 kattintásra rejtett üzenet bukkan fel.</summary>
    private void OnVersionClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel)
        {
            viewModel.RegisterVersionClick();
        }
    }

    /// <summary>
    /// A Rendszerintegráció kapcsolói szándékosan <c>Mode=OneWay</c>
    /// kötésűek: a kattintást ITT, előre elkapjuk (<c>e.Handled = true</c>
    /// minden ágon), hogy a vezérlő saját belső állapota SOHA ne térjen el a
    /// ViewModelben ténylegesen érvényesült állapottól — bekapcsolás előtt
    /// jóváhagyó párbeszéddel, sikertelen registry-művelet esetén pedig a
    /// ViewModel saját maga állítja vissza (lásd
    /// SettingsViewModel.OnFolderOpenRedirectEnabledChanged).
    /// </summary>
    private async void OnFolderOpenRedirectPreviewClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;

        if (DataContext is not SettingsViewModel viewModel)
        {
            return;
        }

        var turningOn = !viewModel.FolderOpenRedirectEnabled;

        if (turningOn && !await ConfirmEnableAsync(TranslationSource.Instance["ShellIntegration_ConfirmFolderOpen"]))
        {
            return;
        }

        viewModel.FolderOpenRedirectEnabled = turningOn;
    }

    private async void OnWinERedirectPreviewClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;

        if (DataContext is not SettingsViewModel viewModel)
        {
            return;
        }

        var turningOn = !viewModel.WinERedirectEnabled;

        if (turningOn && !await ConfirmEnableAsync(TranslationSource.Instance["ShellIntegration_ConfirmWinE"]))
        {
            return;
        }

        viewModel.WinERedirectEnabled = turningOn;
    }

    private async void OnContextMenuEntryPreviewClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;

        if (DataContext is not SettingsViewModel viewModel)
        {
            return;
        }

        var turningOn = !viewModel.ContextMenuEntryEnabled;

        if (turningOn && !await ConfirmEnableAsync(TranslationSource.Instance["ShellIntegration_ConfirmContextMenu"]))
        {
            return;
        }

        viewModel.ContextMenuEntryEnabled = turningOn;
    }

    private Task<bool> ConfirmEnableAsync(string message) =>
        ModernDialog.ConfirmAsync(this, TranslationSource.Instance["ShellIntegration_ConfirmTitle"], message, TranslationSource.Instance["Cmd_Enable"]);

    private void OnSupportEmailNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Win32Exception)
        {
            // Nincs alapértelmezett levelezőprogram beállítva — nincs jobb teendő.
        }

        e.Handled = true;
    }
}
