namespace Pilaster.Shell.Menus;

/// <summary>
/// Egy Pilaster-saját parancs, amit a natív („Windows") jobbklikk-menü a
/// shell elemei ELÉ szúr be (spec v1.0.3) — azok a parancsok, amiknek NINCS
/// natív megfelelőjük (pl. „Megnyitás új fülön", „Terminál megnyitása itt"),
/// ezért a valódi Windows menüben egyébként végleg hiányoznának.
/// </summary>
/// <param name="CommandId">
/// EGYEDI azonosító <see cref="ShellMenuSession.NativeOwnCommandIdBase"/> és
/// afölött — ez a tartomány a shell parancsazonosítóin (1–0x7FFF) KÍVÜL esik,
/// hogy a <c>TrackPopupMenuEx</c> visszatérési értékéből egyértelműen
/// eldönthető legyen, melyikünk parancsáról van szó.
/// </param>
/// <param name="Text">A menüsor felirata (már lokalizálva).</param>
/// <param name="IconBitmap">
/// Előre legyártott <c>HBITMAP</c> a sor ikonjához, vagy <c>nint.Zero</c>, ha
/// nincs ikon (ekkor a sor egyszerű szöveges marad — ez sosem hiba, csak
/// kevésbé csinos). A hívó felelős az esetleges HBITMAP felszabadításáért a
/// megjelenítés UTÁN — <see cref="ShellMenuSession.ShowNativeAsync"/> ezt NEM
/// teszi meg, mert az ikon a hívó (App réteg) tulajdona, ami akár több
/// hívás közt újra felhasználhatja (gyorsítótárazott ikonok).
/// </param>
/// <param name="Enabled">Hamis esetén a sor szürkén, kattinthatatlanul jelenik meg.</param>
public sealed record NativeOwnCommand(uint CommandId, string Text, nint IconBitmap, bool Enabled = true);

/// <summary>A natív menü lezárásának módja — lásd <see cref="ShellMenuSession.ShowNativeAsync"/>.</summary>
public enum NativeMenuOutcome
{
    /// <summary>A felhasználó Esc-cel vagy máshova kattintva bezárta, parancs nélkül.</summary>
    Cancelled,

    /// <summary>Egy <see cref="NativeOwnCommand"/> lett kiválasztva — a hívó felel a végrehajtásáért.</summary>
    OwnCommand,

    /// <summary>
    /// Egy shell-parancs lett kiválasztva ÉS már végre is lett hajtva (a
    /// <see cref="ShellMenuSession"/> ugyanazon az STA szálon, közvetlenül a
    /// <c>TrackPopupMenuEx</c> visszatérése után hívta az
    /// <c>IContextMenu::InvokeCommand</c>-ot).
    /// </summary>
    ShellCommand,
}

/// <param name="Outcome">Melyik ág futott le.</param>
/// <param name="CommandId">
/// <see cref="NativeMenuOutcome.OwnCommand"/> esetén a kiválasztott
/// <see cref="NativeOwnCommand.CommandId"/>; egyébként nem értelmezett.
/// </param>
/// <param name="ForwardedInitMenuPopupCount">
/// Diagnosztikai/teszt-célú számláló — lásd
/// <see cref="NativeMenuOwnerWindow.ForwardedInitMenuPopupCount"/>. Éles
/// hívóknak nincs rá szükségük.
/// </param>
public readonly record struct NativeMenuResult(
    NativeMenuOutcome Outcome, uint CommandId, int ForwardedInitMenuPopupCount = 0, int TotalMessagesReceived = 0);
