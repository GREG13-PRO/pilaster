using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Pilaster.App.ViewModels;

namespace Pilaster.App.Views;

/// <summary>
/// A Megnyitás/Mentés ablak mellé tapadó Pilaster-panel — lásd
/// <see cref="Services.FileDialogCompanionService"/>.
/// </summary>
public partial class FileDialogCompanionWindow : Window
{
    private const int GwlExStyle = -20;
    private const long WsExNoActivate = 0x08000000;
    private const long WsExToolWindow = 0x00000080;
    private const long WsExAppWindow = 0x00040000;

    /// <summary>A panel átlátszó széle (DIP) — ebben fér el az árnyék, lásd a XAML Margin-t.</summary>
    private const double ShadowMargin = 10;

    /// <summary>Távolság a fájlablak szélétől (DIP).</summary>
    private const double Gap = 4;

    public FileDialogCompanionWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            // Sosem aktiválódik és nem jelenik meg a tálcán/Alt+Tabban: a
            // fájlablak marad fókuszban, a kattintás mégis eljut a gombokig.
            var handle = new WindowInteropHelper(this).Handle;
            var style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
            style = (style | WsExNoActivate | WsExToolWindow) & ~WsExAppWindow;
            SetWindowLongPtr(handle, GwlExStyle, new nint(style));
        };
    }

    /// <summary>Egy bejegyzésre kattintottak — a szolgáltatás navigálja oda a fájlablakot.</summary>
    public event EventHandler<string>? EntryRequested;

    public void SetSections(IReadOnlyList<FileDialogCompanionSection> sections)
    {
        SectionList.ItemsSource = sections;
        Scroller.ScrollToTop();
        ShowStatus(null);
    }

    public void ShowStatus(string? message)
    {
        StatusText.Text = message ?? string.Empty;
        StatusText.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// A panel a fájlablak jobb oldalához igazodik (ha ott nincs hely, a
    /// balhoz), azonos magassággal. Fizikai képpontban számol a fájlablak
    /// monitorának DPI-jével, mert a fájlablak egy másik folyamaté.
    /// </summary>
    public void PlaceBeside(nint dialog)
    {
        if (!TryGetVisualBounds(dialog, out var bounds))
        {
            return;
        }

        var scale = GetDpiForWindow(dialog) / 96.0;

        if (scale <= 0)
        {
            scale = 1;
        }

        Height = (bounds.Bottom - bounds.Top) / scale + (2 * ShadowMargin);

        var widthPx = (int)Math.Round(Width * scale);
        var marginPx = (int)Math.Round(ShadowMargin * scale);
        var gapPx = (int)Math.Round(Gap * scale);
        var x = bounds.Right + gapPx - marginPx;

        if (TryGetWorkArea(dialog, out var work) && x + widthPx - marginPx > work.Right)
        {
            x = bounds.Left - gapPx - widthPx + marginPx;

            // Teljes méretű fájlablaknál egyik oldalon sincs hely: a panel a
            // képernyőn belül marad, a fájlablak szélére fedve.
            if (x + marginPx < work.Left)
            {
                x = work.Right - widthPx + marginPx;
            }
        }

        var handle = new WindowInteropHelper(this).EnsureHandle();
        SetWindowPos(handle, HwndTopmost, x, bounds.Top - marginPx, 0, 0, SwpNoSize | SwpNoActivate);
    }

    private void OnEntryClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string path })
        {
            EntryRequested?.Invoke(this, path);
        }
    }

    /// <summary>A fájlablak LÁTHATÓ kerete — a <c>GetWindowRect</c> a láthatatlan átméretező szegélyt is beszámítaná.</summary>
    private static bool TryGetVisualBounds(nint hwnd, out Rect32 bounds)
    {
        if (DwmGetWindowAttribute(hwnd, DwmwaExtendedFrameBounds, out bounds, Marshal.SizeOf<Rect32>()) == 0)
        {
            return true;
        }

        return GetWindowRect(hwnd, out bounds);
    }

    private static bool TryGetWorkArea(nint hwnd, out Rect32 work)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);

        if (monitor != 0 && GetMonitorInfo(monitor, ref info))
        {
            work = info.Work;
            return true;
        }

        work = default;
        return false;
    }

    private const int DwmwaExtendedFrameBounds = 9;
    private const uint MonitorDefaultToNearest = 2;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoActivate = 0x0010;
    private static readonly nint HwndTopmost = -1;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect32
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect32 Monitor;
        public Rect32 Work;
        public uint Flags;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out Rect32 value, int size);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hwnd, out Rect32 rect);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
}
