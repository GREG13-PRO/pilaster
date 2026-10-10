using Pilaster.Core.FileSystem;

namespace Pilaster.App.ViewModels;

/// <summary>A fájlablak-panel egy szakasza (nyitott fülek, gyorselérés).</summary>
public sealed record FileDialogCompanionSection(string Header, IReadOnlyList<FileDialogCompanionEntry> Entries);

/// <summary>Egy mappa a fájlablak-panelen — kattintásra a fájlablak ide ugrik.</summary>
/// <param name="Label">A megjelenő név.</param>
/// <param name="Path">A mappa teljes útvonala.</param>
/// <param name="Item">A shell-ikon betöltéséhez (<see cref="Controls.ShellIconImage"/>).</param>
public sealed record FileDialogCompanionEntry(string Label, string Path, FileSystemItem Item);
