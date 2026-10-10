using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Pilaster.App.Localization;
using Pilaster.App.ViewModels;
using Pilaster.App.Views;
using Pilaster.Core.FileSystem;
using Pilaster.Core.Settings;
using Pilaster.Shell.Integration;
using Serilog;

namespace Pilaster.App.Services;

/// <summary>
/// Pilaster-panel a Windows Megnyitás/Mentés ablakai mellett (a böngészők
/// fájlfeltöltő ablaka is ilyen): a Pilasterben nyitott fülek és a gyorselérés
/// mappái, egy kattintással odaugratva a fájlablakot.
/// </summary>
/// <remarks>
/// A fájlablakot nem cseréli le és nem módosítja a programot, amelyik
/// megnyitotta: az előtérváltásról a Windows akadálymentesítési értesítéséből
/// tud (<see cref="ForegroundWindowWatcher"/>), a navigációt pedig a fájlablak
/// saját vezérlőin végzi (<see cref="FileDialogNavigator"/>).
/// </remarks>
public sealed class FileDialogCompanionService(ISettingsService settings, MainWindowViewModel mainViewModel)
{
    /// <summary>A panel a fájlablakhoz igazodik, amíg az látható — ennyi időnként néz rá.</summary>
    private static readonly TimeSpan TrackInterval = TimeSpan.FromMilliseconds(120);

    private readonly ForegroundWindowWatcher _watcher = new();
    private FileDialogCompanionWindow? _window;
    private DispatcherTimer? _timer;
    private nint _dialog;
    private int _checkGeneration;

    /// <summary>A beállításnak megfelelően indít vagy leállít.</summary>
    public void Apply()
    {
        if (settings.Current.FileDialogCompanionEnabled)
        {
            Start();
        }
        else
        {
            Stop();
        }
    }

    private void Start()
    {
        if (_watcher.IsRunning)
        {
            return;
        }

        _watcher.ForegroundChanged += OnForegroundChanged;
        _watcher.Start();

        // Ha bekapcsoláskor már nyitva van egy fájlablak, az is kapjon panelt.
        OnForegroundChanged(this, GetForegroundWindow());
    }

    private void Stop()
    {
        _watcher.ForegroundChanged -= OnForegroundChanged;
        _watcher.Stop();
        Hide();
    }

    private void OnForegroundChanged(object? sender, nint hwnd)
    {
        if (hwnd == 0)
        {
            return;
        }

        if (hwnd == _dialog)
        {
            Show();
            return;
        }

        Hide();

        // Csak párbeszédablak-osztályú ablakkal foglalkozunk — a többi
        // előtérváltásnál UI Automation sem fut.
        if (!FileDialogNavigator.IsDialogWindow(hwnd))
        {
            return;
        }

        // A UI Automation hívás a cél folyamatra vár, ezért háttérszálon fut;
        // a generációs számláló eldobja az időközben elavult eredményt.
        var generation = ++_checkGeneration;
        _ = Task.Run(() => WaitUntilFileDialog(hwnd, generation)).ContinueWith(
            check =>
            {
                if (!check.IsFaulted && check.Result && generation == _checkGeneration && GetForegroundWindow() == hwnd)
                {
                    Attach(hwnd);
                }
            },
            TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>
    /// Előtérbe kerüléskor a párbeszéd vezérlői gyakran még épülnek (MÉRVE:
    /// az első ellenőrzés ilyenkor nemet mondott, és a panel csak egy
    /// véletlen következő előtérváltáskor jelent meg). Ezért rövid ideig
    /// újrapróbálja, amíg az ablak előtérben marad.
    /// </summary>
    private bool WaitUntilFileDialog(nint hwnd, int generation)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (generation != _checkGeneration || GetForegroundWindow() != hwnd)
            {
                return false;
            }

            if (FileDialogNavigator.IsFileDialog(hwnd))
            {
                return true;
            }

            Thread.Sleep(150);
        }

        return false;
    }

    private void Attach(nint dialog)
    {
        _dialog = dialog;

        if (_window is null)
        {
            _window = new FileDialogCompanionWindow();
            _window.EntryRequested += OnEntryRequested;
        }

        _window.SetSections(BuildSections());
        Show();
    }

    private void Show()
    {
        if (_window is null || _dialog == 0)
        {
            return;
        }

        _window.PlaceBeside(_dialog);

        if (!_window.IsVisible)
        {
            _window.Show();
            _window.PlaceBeside(_dialog);
        }

        _timer ??= CreateTimer();
        _timer.Start();
    }

    private void Hide()
    {
        _timer?.Stop();
        _window?.Hide();
    }

    private DispatcherTimer CreateTimer()
    {
        var timer = new DispatcherTimer { Interval = TrackInterval };
        timer.Tick += (_, _) =>
        {
            if (_dialog == 0 || !IsWindow(_dialog) || !IsWindowVisible(_dialog) || IsIconic(_dialog))
            {
                _dialog = 0;
                Hide();
                return;
            }

            // Másik ablak került előtérbe: a panel elrejtőzik (a fájlablak
            // visszatérésekor az előtérváltás újra megjeleníti).
            if (GetForegroundWindow() != _dialog)
            {
                Hide();
                return;
            }

            _window?.PlaceBeside(_dialog);
        };

        return timer;
    }

    private void OnEntryRequested(object? sender, string path)
    {
        var dialog = _dialog;
        _window?.ShowStatus(null);

        _ = Task.Run(() =>
        {
            try
            {
                return FileDialogNavigator.NavigateTo(dialog, path);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "A fájlablak átnavigálása nem sikerült: {Path}", path);
                return false;
            }
        }).ContinueWith(
            navigation =>
            {
                if (!navigation.Result)
                {
                    _window?.ShowStatus(TranslationSource.Instance["FileDialogCompanion_Failed"]);
                }
            },
            TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>A Pilasterben nyitott fülek valódi mappái, majd a gyorselérés — ismétlés nélkül.</summary>
    private List<FileDialogCompanionSection> BuildSections()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sections = new List<FileDialogCompanionSection>();
        var strings = TranslationSource.Instance;

        var tabs = mainViewModel.LeftPane.Tabs
            .Concat(mainViewModel.RightPane.Tabs)
            .Where(tab => tab is { IsHome: false, IsRecycleBin: false } && IsRealFolder(tab.CurrentPath))
            .Where(tab => seen.Add(Path.TrimEndingDirectorySeparator(tab.CurrentPath!)))
            .Select(tab => CreateEntry(tab.Title, tab.CurrentPath!))
            .ToList();

        if (tabs.Count > 0)
        {
            sections.Add(new FileDialogCompanionSection(strings["FileDialogCompanion_OpenTabs"], tabs));
        }

        var quickAccess = mainViewModel.HomeQuickAccessItems
            .Where(item => !item.IsSeparator && IsRealFolder(item.Path))
            .Where(item => seen.Add(Path.TrimEndingDirectorySeparator(item.Path)))
            .Select(item => CreateEntry(item.Label, item.Path))
            .ToList();

        if (quickAccess.Count > 0)
        {
            sections.Add(new FileDialogCompanionSection(strings["Nav_QuickAccess"], quickAccess));
        }

        return sections;
    }

    private static bool IsRealFolder(string? path) =>
        path is { Length: > 0 } && !path.StartsWith("pilaster:", StringComparison.Ordinal) && Directory.Exists(path);

    private static FileDialogCompanionEntry CreateEntry(string label, string path)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        var isRoot = string.IsNullOrEmpty(name);

        var item = new FileSystemItem
        {
            FullPath = path,
            Name = isRoot ? path : name,
            Kind = isRoot ? FileSystemItemKind.Drive : FileSystemItemKind.Directory,
        };

        return new FileDialogCompanionEntry(string.IsNullOrWhiteSpace(label) ? item.Name : label, path, item);
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint hwnd);
}
