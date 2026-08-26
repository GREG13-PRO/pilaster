using System.Windows.Input;
using Pilaster.App.Views;

namespace Pilaster.Tests;

/// <summary>
/// A panelek közötti húzás-ejtés DÖNTÉSI MÁTRIXA — melyik módosító melyik
/// hatást adja (spec A2, korábban egy kézi ellenőrzőlista pontja).
/// </summary>
/// <remarks>
/// Az egérrel húzás maga (a <c>DoDragDrop</c> gesztus, a kurzor visszajelzés)
/// TOVÁBBRA IS kézi ellenőrzés marad — ehhez valódi egér kell. Ami itt
/// automatizálható, az a <see cref="FilePaneView.ResolveDropEffect"/> és a
/// <see cref="FilePaneView.IsSameVolume"/> TISZTA, mellékhatás nélküli döntése,
/// amit a checklist eddig szemmel ellenőrzött.
/// </remarks>
public class PaneDragDropTests
{
    [Fact]
    public void AltMindigParancsikon()
    {
        var action = FilePaneView.ResolveDropEffect([@"C:\a.txt"], @"D:\cel", ModifierKeys.Alt);
        Assert.Equal(PaneDropAction.Shortcut, action);
    }

    [Fact]
    public void CtrlMindigMasolas()
    {
        // Ugyanazon a köteten is: a Ctrl felülírja az alapértelmezett
        // "azonos kötet = áthelyezés" szabályt.
        var action = FilePaneView.ResolveDropEffect([@"C:\a.txt"], @"C:\cel", ModifierKeys.Control);
        Assert.Equal(PaneDropAction.Copy, action);
    }

    [Fact]
    public void ShiftMindigAthelyezes()
    {
        // Eltérő köteten is: a Shift felülírja az alapértelmezett
        // "eltérő kötet = másolás" szabályt.
        var action = FilePaneView.ResolveDropEffect([@"C:\a.txt"], @"D:\cel", ModifierKeys.Shift);
        Assert.Equal(PaneDropAction.Move, action);
    }

    [Fact]
    public void ModositoNelkulAzonosKotetenAthelyezes()
    {
        var action = FilePaneView.ResolveDropEffect([@"C:\forras\a.txt"], @"C:\cel", ModifierKeys.None);
        Assert.Equal(PaneDropAction.Move, action);
    }

    [Fact]
    public void ModositoNelkulElteroKotetreMasolas()
    {
        var action = FilePaneView.ResolveDropEffect([@"C:\forras\a.txt"], @"D:\cel", ModifierKeys.None);
        Assert.Equal(PaneDropAction.Copy, action);
    }

    [Fact]
    public void VegyesForrasKotetNelMasolasAzAlapertelmezes()
    {
        // Az egyik forrás C:-n, a másik D:-n van — nincs egyértelmű "azonos
        // kötet", ezért a biztonságosabb másolás az alapértelmezés (a
        // forrásoldalon sosem szüntet meg semmit).
        var action = FilePaneView.ResolveDropEffect([@"C:\a.txt", @"D:\b.txt"], @"C:\cel", ModifierKeys.None);
        Assert.Equal(PaneDropAction.Copy, action);
    }

    [Fact]
    public void UresForrasnalNincsAzonosKotet()
    {
        Assert.False(FilePaneView.IsSameVolume([], @"C:\cel"));
    }

    [Fact]
    public void AltEsCtrlEgyuttAzAltNyer()
    {
        // A sorrend a spec szerint kötött: Alt > Ctrl > Shift > alapértelmezés.
        var action = FilePaneView.ResolveDropEffect([@"C:\a.txt"], @"C:\cel", ModifierKeys.Alt | ModifierKeys.Control);
        Assert.Equal(PaneDropAction.Shortcut, action);
    }
}
