using Pilaster.Core.Settings;

namespace Pilaster.Tests;

/// <summary>
/// Memóriabeli <see cref="ISettingsService"/> a tesztekhez — nem ír lemezre,
/// csak az <see cref="AppSettings"/> alapértékeit adja vissza. A
/// <see cref="PaneViewModel"/> v1.0.1-től igényli (oszlopszélesség-mentés,
/// spec K1), a legtöbb teszt viszont nem a beállításokat vizsgálja.
/// </summary>
public sealed class FakeSettingsService : ISettingsService
{
    public AppSettings Current { get; } = new();

    public void Save()
    {
    }

    public void Flush()
    {
    }

    public bool TryExport(string path) => false;

    public bool TryImport(string path) => false;

    public event EventHandler? Changed;

    public void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
