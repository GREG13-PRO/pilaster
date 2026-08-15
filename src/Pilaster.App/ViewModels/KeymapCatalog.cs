using Pilaster.App.Localization;
using Pilaster.Core.Settings;

namespace Pilaster.App.ViewModels;

/// <summary>Egy sor a kiosztás-táblázatban: művelet, billentyű, leírás.</summary>
/// <param name="Action">A művelet neve.</param>
/// <param name="Gesture">A billentyűkombináció, ahogy a felhasználó lenyomja.</param>
/// <param name="Description">Rövid magyarázat.</param>
public sealed record KeyBindingRow(string Action, string Gesture, string Description);

/// <summary>
/// A billentyűkiosztások leírása a Beállítások „Kiosztás megtekintése"
/// táblázatához (spec F1).
/// </summary>
/// <remarks>
/// Szándékosan LEÍRÓ katalógus, nem a kiosztás forrása: a tényleges kezelés a
/// <c>MainWindow.OnMainPreviewKeyDown</c>-ban él. A kettőt kézzel kell
/// szinkronban tartani — cserébe a táblázat rendezhető, fordítható és
/// magyarázatot is adhat, amit egy kódból generált lista nem tudna.
/// </remarks>
public static class KeymapCatalog
{
    public static IReadOnlyList<KeyBindingRow> Describe(KeymapPreset preset)
    {
        var s = TranslationSource.Instance;

        // A két preset KÖZÖS billentyűi — ezek mindkét kiosztásban élnek.
        List<KeyBindingRow> shared =
        [
            new(s["Cmd_NewTab"], "Ctrl+T", s["Keymap_NewTabHint"]),
            new(s["Cmd_CloseTab"], "Ctrl+W", s["Keymap_CloseTabHint"]),
            new(s["Keymap_NextTab"], "Ctrl+Tab", s["Keymap_NextTabHint"]),
            new(s["Cmd_Copy"], "Ctrl+C", s["Keymap_ClipboardHint"]),
            new(s["Cmd_Cut"], "Ctrl+X", s["Keymap_ClipboardHint"]),
            new(s["Cmd_Paste"], "Ctrl+V", s["Keymap_ClipboardHint"]),
            new(s["Keymap_SelectAll"], "Ctrl+A", s["Keymap_SelectAllHint"]),
            new(s["Cmd_Back"], "Alt+←", s["Keymap_BackHint"]),
            new(s["Cmd_Forward"], "Alt+→", s["Keymap_ForwardHint"]),
            new(s["Cmd_Up"], "Alt+↑", s["Keymap_UpHint"]),
        ];

        if (preset == KeymapPreset.Explorer)
        {
            return
            [
                new(s["Cmd_Refresh"], "F5 / Ctrl+R", s["Keymap_RefreshHint"]),
                new(s["Keymap_RefreshBoth"], "Alt+F5", s["Keymap_RefreshBothHint"]),
                new(s["Keymap_Rename"], "F2", s["Keymap_RenameHint"]),
                new(s["Cmd_Delete"], "Delete", s["Keymap_DeleteHint"]),
                new(s["Cmd_DeletePermanently"], "Shift+Delete", s["Keymap_DeletePermanentHint"]),
                new(s["Cmd_NewFolder"], "Ctrl+Shift+N", s["Keymap_NewFolderHint"]),
                .. shared,
            ];
        }

        return
        [
            new(s["FKey_View"], "F3", s["Keymap_ViewHint"]),
            new(s["FKey_Edit"], "F4", s["Keymap_EditHint"]),
            new(s["FKey_Copy"], "F5", s["Keymap_CopyHint"]),
            new(s["FKey_Move"], "F6", s["Keymap_MoveHint"]),
            new(s["FKey_NewFolder"], "F7", s["Keymap_NewFolderHint"]),
            new(s["FKey_Delete"], "F8", s["Keymap_DeleteHint"]),
            new(s["Cmd_Delete"], "Delete", s["Keymap_DeleteHint"]),
            new(s["Cmd_DeletePermanently"], "Shift+Delete", s["Keymap_DeletePermanentHint"]),
            new(s["Keymap_Rename"], "F2", s["Keymap_RenameHint"]),
            new(s["Keymap_SwitchPane"], "Tab", s["Keymap_SwitchPaneHint"]),
            new(s["Cmd_SwapPanes"], "Ctrl+U", s["Keymap_SwapPanesHint"]),
            new(s["Keymap_LeftToRight"], "Ctrl+L", s["Keymap_LeftToRightHint"]),
            new(s["Keymap_RightToLeft"], "Ctrl+R", s["Keymap_RightToLeftHint"]),
            new(s["Cmd_Refresh"], "Ctrl+Shift+R", s["Keymap_RefreshHint"]),
            new(s["Keymap_RefreshBoth"], "Alt+F5", s["Keymap_RefreshBothHint"]),
            new(s["Keymap_Mark"], "Insert", s["Keymap_MarkHint"]),
            new(s["Keymap_ToggleMark"], "Space", s["Keymap_ToggleMarkHint"]),
            new(s["Keymap_UnselectAll"], "Ctrl+D / Num-", s["Keymap_UnselectAllHint"]),
            new(s["Keymap_InvertSelection"], "Num*", s["Keymap_InvertSelectionHint"]),
            new(s["Keymap_QuickFilter"], "Alt+F7", s["Keymap_QuickFilterHint"]),
            .. shared,
        ];
    }
}
