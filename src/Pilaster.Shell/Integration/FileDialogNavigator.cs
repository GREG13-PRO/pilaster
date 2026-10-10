using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace Pilaster.Shell.Integration;

/// <summary>
/// Egy megnyitott Windows „Megnyitás"/„Mentés" párbeszédablak átnavigálása egy
/// megadott mappába — a Pilaster épp nyitott mappájára. A böngészők
/// fájlfeltöltő ablaka is ilyen közös párbeszédablak.
/// </summary>
/// <remarks>
/// <para>
/// NEM fecskendez kódot a cél folyamatba (böngésző, Office stb.): a „Fájlnév"
/// mezőt a UI Automation (akadálymentesítési) felületen át tölti ki — ugyanúgy,
/// ahogy a Narrátor is hozzáfér —, a „Megnyitás"/„Mentés" gombot pedig a
/// szabványos <c>WM_COMMAND</c> üzenettel nyomja meg, amit maga a gomb is küld
/// kattintáskor. Billentyűt nem szimulál, így semmi nem kerülhet más ablakba.
/// </para>
/// <para>
/// Az elv a Listary-éval azonos: ha a „Fájlnév" mezőben egy LÉTEZŐ MAPPA
/// útvonala áll, a gomb nem zárja be a párbeszédet, hanem belép abba a mappába.
/// </para>
/// </remarks>
public static class FileDialogNavigator
{
    // A közös fájlpárbeszéd szabványos azonosítói — nyelvtől függetlenek.
    private const string DialogClassName = "#32770";
    private const string FileNameComboId = "1148"; // „Fájlnév:" legördülő (Megnyitás)
    private const string SaveFileNameHostId = "FileNameControlHost"; // ugyanez a Mentés ablakban
    private const string FileNameEditId = "1001"; // a beírható szövegmező (Mentés)
    private const string AddressBarId = "1001"; // címsor (eszköztár); a neve: „Cím: C:\…"
    private const string OkButtonId = "1"; // „Megnyitás"/„Mentés" (IDOK)
    private const string Win32ButtonClass = "Button";

    private const int IdOk = 1;
    private const uint WmCommand = 0x0111;
    private const int BnClicked = 0;

    /// <summary>Igaz, ha az ablak egy közös fájlpárbeszéd (van „Fájlnév" mezője).</summary>
    public static bool IsFileDialog(nint hwnd) => TryResolve(hwnd, out _, out _);

    /// <summary>
    /// Olcsó előszűrő (csak az ablakosztály): igaz, ha az ablak lehet
    /// fájlpárbeszéd. UI Automation nélkül, így minden előtérváltásnál futhat.
    /// </summary>
    public static bool IsDialogWindow(nint hwnd) => hwnd != 0 && IsWindow(hwnd) && GetClassNameOf(hwnd) == DialogClassName;

    /// <summary>
    /// A párbeszéd átnavigálása <paramref name="folderPath"/>-ba. Igaz, ha a
    /// címsor szerint a párbeszéd ténylegesen odaért.
    /// </summary>
    public static bool NavigateTo(nint hwnd, string folderPath, TimeSpan? timeout = null)
    {
        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
        {
            return false;
        }

        if (!TryResolve(hwnd, out var dialog, out var field))
        {
            return false;
        }

        if (!field.TryGetCurrentPattern(ValuePattern.Pattern, out var valueObj)
            || valueObj is not ValuePattern value
            || value.Current.IsReadOnly)
        {
            return false;
        }

        // A „Megnyitás" gomb: AutomationId „1" ÉS valódi Win32 gombablak. Az
        // azonosító önmagában KEVÉS — a fájllista elemei is viselhetik az „1"-et,
        // és egy listaelem aktiválása egy egészen más mappát nyitna meg.
        var okButton = dialog.FindFirst(
            TreeScope.Descendants,
            new AndCondition(
                new PropertyCondition(AutomationElement.AutomationIdProperty, OkButtonId),
                new PropertyCondition(AutomationElement.ClassNameProperty, Win32ButtonClass)));

        if (okButton is null || okButton.Current.NativeWindowHandle is not (var buttonHandle and not 0))
        {
            return false;
        }

        var before = ReadAddress(dialog);
        value.SetValue(folderPath);

        // Ugyanaz az üzenet, amit a gomb kattintáskor a szülőjének küld. Nem
        // blokkol, és nem függ a fókusztól (a BM_CLICK aktív ablakot kíván).
        var parent = GetParent(buttonHandle);
        PostMessage(parent == 0 ? hwnd : parent, WmCommand, (nint)((BnClicked << 16) | IdOk), buttonHandle);

        return WaitForAddress(dialog, folderPath, before, timeout ?? TimeSpan.FromSeconds(2));
    }

    private static bool WaitForAddress(AutomationElement dialog, string folderPath, string? before, TimeSpan timeout)
    {
        var target = Path.TrimEndingDirectorySeparator(folderPath);
        var leaf = Path.GetFileName(target);
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;

        while (Environment.TickCount64 < deadline)
        {
            Thread.Sleep(80);
            var address = ReadAddress(dialog);

            if (address is null || address == before)
            {
                continue;
            }

            // A címsor általában a teljes útvonalat mutatja; ismert helyeknél
            // (Dokumentumok, Asztal) csak a nevét — ezt is elfogadjuk.
            if (address.Contains(target, StringComparison.OrdinalIgnoreCase)
                || (leaf.Length > 0 && address.EndsWith(leaf, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A címsor neve („Cím: C:\Windows"), vagy <c>null</c>.</summary>
    private static string? ReadAddress(AutomationElement dialog)
    {
        try
        {
            var bar = dialog.FindFirst(
                TreeScope.Descendants,
                new AndCondition(
                    new PropertyCondition(AutomationElement.AutomationIdProperty, AddressBarId),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ToolBar)));
            return bar?.Current.Name;
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
    }

    private static bool TryResolve(nint hwnd, out AutomationElement dialog, out AutomationElement field)
    {
        dialog = null!;
        field = null!;

        // Csak valódi párbeszédablak: enélkül pl. az Asztal elemfáján a keresés
        // az ÖSSZES ablakot bejárta volna, és egy másik ablak mezőjét találta.
        if (hwnd == 0 || !IsWindow(hwnd) || GetClassNameOf(hwnd) != DialogClassName)
        {
            return false;
        }

        AutomationElement? root;

        try
        {
            root = AutomationElement.FromHandle(hwnd);
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or ArgumentException)
        {
            return false;
        }

        if (root is null)
        {
            return false;
        }

        // A Megnyitás ablakban a „Fájlnév" legördülő azonosítója „1148", a
        // Mentés ablakban „FileNameControlHost"; mindkettőben egy szövegmező
        // él. Végső esetben maga a szövegmező („1001", Edit — a címsor is
        // „1001", de az eszköztár).
        var combo = root.FindFirst(
            TreeScope.Descendants,
            new AndCondition(
                new OrCondition(
                    new PropertyCondition(AutomationElement.AutomationIdProperty, FileNameComboId),
                    new PropertyCondition(AutomationElement.AutomationIdProperty, SaveFileNameHostId)),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ComboBox)));

        var edit = combo?.FindFirst(
            TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit))
            ?? root.FindFirst(
                TreeScope.Descendants,
                new AndCondition(
                    new PropertyCondition(AutomationElement.AutomationIdProperty, FileNameEditId),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit)));

        if (edit is null && combo is null)
        {
            return false;
        }

        dialog = root;
        field = edit ?? combo!;
        return true;
    }

    private static string GetClassNameOf(nint hwnd)
    {
        var buffer = new StringBuilder(64);
        return GetClassName(hwnd, buffer, buffer.Capacity) > 0 ? buffer.ToString() : string.Empty;
    }

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern nint GetParent(nint hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint hwnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(nint hwnd, uint msg, nint wParam, nint lParam);
}
