using System.Windows.Media;

namespace Pilaster.Shell.Menus;

/// <summary>
/// Egy shell-bővítménytől származó menüelem, a natív <c>HMENU</c>-től már
/// FÜGGETLEN, sima adatobjektumként.
/// </summary>
/// <remarks>
/// A leválasztás szándékos: a saját menüt a Pilaster rajzolja (lekerekítés,
/// animáció, téma), tehát a natív menüfát egyszer beolvassuk, majd a
/// megjelenítés már tisztán WPF. A natív oldalról csak a
/// <see cref="CommandId"/> marad meg, amivel a kattintás visszahívható —
/// lásd <c>ShellMenuSession.InvokeAsync</c>.
/// </remarks>
public sealed class ShellMenuNode
{
    public string Text { get; init; } = string.Empty;

    public bool IsSeparator { get; init; }

    public bool IsEnabled { get; init; } = true;

    public bool IsChecked { get; init; }

    /// <summary>A natív menü azonosítója. Elválasztónál és almenü-szülőnél 0.</summary>
    public uint CommandId { get; init; }

    /// <summary>A bővítmény saját ikonja, már WPF-képként; <c>null</c>, ha nincs.</summary>
    public ImageSource? Icon { get; init; }

    public IReadOnlyList<ShellMenuNode> Children { get; init; } = [];

    public bool HasChildren => Children.Count > 0;

    public override string ToString() => IsSeparator ? "———" : Text;
}
