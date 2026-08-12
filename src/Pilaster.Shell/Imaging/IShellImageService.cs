using System.Windows.Media;
using Pilaster.Core.FileSystem;

namespace Pilaster.Shell.Imaging;

/// <summary>
/// Natív Windows ikonokat és bélyegképeket ad a fájllista elemeihez.
/// </summary>
public interface IShellImageService
{
    /// <summary>
    /// Kép kérése egy elemhez. A visszaadott <see cref="ImageSource"/> le van
    /// fagyasztva (<c>Freeze</c>), ezért szabadon átadható a UI-szálnak.
    /// </summary>
    /// <param name="item">Az elem, amihez kép kell.</param>
    /// <param name="size">A kért él hossza képpontban (16, 32, 48, 256 …).</param>
    /// <param name="cancellationToken">Megszakítási jelző.</param>
    /// <returns>A kép, vagy <c>null</c>, ha nem sikerült előállítani.</returns>
    ValueTask<ImageSource?> GetImageAsync(
        FileSystemItem item,
        int size,
        CancellationToken cancellationToken = default);

    /// <summary>Kiüríti a gyorsítótárat (pl. téma- vagy DPI-váltáskor).</summary>
    void ClearCache();
}
