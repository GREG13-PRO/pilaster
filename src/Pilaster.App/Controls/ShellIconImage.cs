using System.Windows;
using System.Windows.Controls;
using Pilaster.Core.FileSystem;
using Pilaster.Shell.Imaging;

namespace Pilaster.App.Controls;

/// <summary>
/// Egy fájlrendszer-elem natív ikonját vagy bélyegképét mutató kép.
/// </summary>
/// <remarks>
/// <para>
/// A betöltés akkor indul, amikor a vezérlő ténylegesen kap egy elemet. Mivel
/// a listák virtualizálva vannak és a sorkonténereket a WPF újrahasznosítja,
/// ez természetes módon lustává teszi az ikonbetöltést: egy 200 000 elemű
/// mappában is csak a képernyőn lévő néhány tucat sorra fut le COM-hívás.
/// </para>
/// <para>
/// Az újrahasznosítás miatt viszont egy konténer élete során többször is
/// elemet vált. Ezért minden betöltés saját generációs számot kap, és a
/// beérkező kép csak akkor kerül ki, ha közben nem váltott az elem — különben
/// görgetéskor idegen ikonok villannának be a sorokba.
/// </para>
/// </remarks>
public sealed class ShellIconImage : Image
{
    private static IShellImageService? _service;

    private int _generation;

    public static readonly DependencyProperty ItemProperty =
        DependencyProperty.Register(
            nameof(Item),
            typeof(FileSystemItem),
            typeof(ShellIconImage),
            new PropertyMetadata(null, OnItemChanged));

    public static readonly DependencyProperty IconSizeProperty =
        DependencyProperty.Register(
            nameof(IconSize),
            typeof(int),
            typeof(ShellIconImage),
            new PropertyMetadata(16, OnItemChanged));

    /// <summary>Az elem, amihez ikont kérünk.</summary>
    public FileSystemItem? Item
    {
        get => (FileSystemItem?)GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }

    /// <summary>A kért ikonméret képpontban.</summary>
    public int IconSize
    {
        get => (int)GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    /// <summary>
    /// A képszolgáltatás beállítása induláskor. Statikus, mert a vezérlőt a
    /// XAML példányosítja, így nem kaphat konstruktoron át függőséget.
    /// </summary>
    public static void Initialize(IShellImageService service) => _service = service;

    private static void OnItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ShellIconImage control)
        {
            control.BeginLoad();
        }
    }

    private void BeginLoad()
    {
        // A korábbi betöltés eredménye ezzel érvénytelenné válik.
        var generation = ++_generation;

        Source = null;

        if (_service is null || Item is not { } item)
        {
            return;
        }

        _ = LoadAsync(item, IconSize, generation);
    }

    private async Task LoadAsync(FileSystemItem item, int size, int generation)
    {
        try
        {
            var image = await _service!.GetImageAsync(item, size).ConfigureAwait(true);

            // Közben a konténer másik elemet kaphatott — akkor ez a kép elavult.
            if (generation == _generation)
            {
                Source = image;
            }
        }
        catch (OperationCanceledException)
        {
            // Görgetés közben megszakadt betöltés — nincs teendő.
        }
    }
}
