using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using Pilaster.Shell.Integration;

namespace Pilaster.Tests;

/// <summary>
/// A fájlablak-panel navigációja egy valódi Windows Megnyitás/Mentés
/// ablakon (ugyanaz a komponens, amit a böngészők feltöltéskor használnak).
/// A párbeszédet a teszt maga nyitja meg és a Mégse gombbal zárja be —
/// billentyűt nem szimulál.
/// </summary>
public sealed class FileDialogNavigatorTests : IDisposable
{
    private readonly string _target = Path.Combine(Path.GetTempPath(), "pilaster-dlgnav-" + Guid.NewGuid().ToString("N"));
    private readonly string _marker = "ZZ_jelolo_" + Guid.NewGuid().ToString("N")[..6];

    public FileDialogNavigatorTests() => Directory.CreateDirectory(Path.Combine(_target, _marker));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_target, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MegnyitasEsMentesAblakAtnavigalhatoEsNyitvaMarad(bool save)
    {
        var dialog = OpenDialog(save, out var thread);

        try
        {
            Assert.True(FileDialogNavigator.IsFileDialog(dialog));
            Assert.True(FileDialogNavigator.NavigateTo(dialog, _target, TimeSpan.FromSeconds(5)));
            Assert.True(WaitFor(() => HasElementNamed(dialog, _marker)), "A célmappa tartalma nem jelent meg.");
            Assert.True(IsWindow(dialog), "A párbeszéd bezáródott, pedig mappába kellett volna lépnie.");
        }
        finally
        {
            Cancel(dialog);
            thread.Join(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public void NemFajlablakotElutasit()
    {
        Assert.False(FileDialogNavigator.IsFileDialog(GetDesktopWindow()));
        Assert.False(FileDialogNavigator.IsFileDialog(0));
        Assert.False(FileDialogNavigator.NavigateTo(GetDesktopWindow(), _target));
    }

    private static nint OpenDialog(bool save, out Thread thread)
    {
        thread = new Thread(() =>
        {
            Microsoft.Win32.FileDialog dialog = save ? new Microsoft.Win32.SaveFileDialog() : new Microsoft.Win32.OpenFileDialog();
            dialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            dialog.ShowDialog();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        nint found = 0;
        Assert.True(WaitFor(() => (found = FindOwnDialog()) != 0), "A párbeszéd nem nyílt meg.");

        // A vezérlők a megjelenés után még épülnek.
        Assert.True(WaitFor(() => FileDialogNavigator.IsFileDialog(found)), "A párbeszéd vezérlői nem készültek el.");
        return found;
    }

    private static bool WaitFor(Func<bool> condition)
    {
        var deadline = Environment.TickCount64 + 10_000;

        while (Environment.TickCount64 < deadline)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(100);
        }

        return false;
    }

    private static bool HasElementNamed(nint hwnd, string name) =>
        AutomationElement.FromHandle(hwnd).FindFirst(
            TreeScope.Descendants,
            new PropertyCondition(AutomationElement.NameProperty, name)) is not null;

    private static void Cancel(nint hwnd)
    {
        if (!IsWindow(hwnd))
        {
            return;
        }

        var cancel = AutomationElement.FromHandle(hwnd).FindFirst(
            TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, "2"));

        if (cancel?.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern) == true && pattern is InvokePattern invoke)
        {
            invoke.Invoke();
        }
    }

    private static nint FindOwnDialog()
    {
        nint found = 0;
        var pid = (uint)Environment.ProcessId;

        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var owner);
            var name = new StringBuilder(64);
            GetClassName(hwnd, name, name.Capacity);

            if (owner == pid && IsWindowVisible(hwnd) && name.ToString() == "#32770")
            {
                found = hwnd;
                return false;
            }

            return true;
        }, 0);

        return found;
    }

    private delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint hwnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern nint GetDesktopWindow();
}
