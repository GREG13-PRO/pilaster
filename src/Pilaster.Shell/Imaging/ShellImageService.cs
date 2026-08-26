using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Pilaster.Core.FileSystem;
using Pilaster.Shell.Interop;
using static Pilaster.Shell.Interop.NativeMethods;

namespace Pilaster.Shell.Imaging;

/// <summary>
/// Ikon- és bélyegkép-szolgáltatás az <c>IShellItemImageFactory</c> COM
/// interfészre építve.
/// </summary>
/// <remarks>
/// <para>
/// A gyorsítótárazás kulcskérdés: egy 200 000 elemű mappában a legtöbb fájl
/// ugyanazt az ikont kapja. Ezért két külön kulcsolást használunk:
/// </para>
/// <list type="bullet">
/// <item>
/// A <b>bélyegképes</b> típusok (kép, videó, PDF …) egyedi tartalmúak, ezért a
/// teljes útvonal + módosítási idő a kulcs — így egy szerkesztett kép új
/// bélyegképet kap.
/// </item>
/// <item>
/// Minden más fájl a <b>kiterjesztése</b> alapján kulcsolódik, hiszen minden
/// <c>.txt</c> ugyanazt az ikont kapja. Ez a döntés fogja vissza a memóriát és
/// a COM-hívások számát nagyságrendekkel.
/// </item>
/// </list>
/// </remarks>
public sealed class ShellImageService : IShellImageService
{
    /// <summary>
    /// Ezeknél a kiterjesztéseknél tartalom-specifikus bélyegkép várható, ezért
    /// útvonal szerint gyorsítótárazunk, nem kiterjesztés szerint.
    /// </summary>
    private static readonly HashSet<string> ThumbnailExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // képek
        "jpg", "jpeg", "png", "gif", "bmp", "webp", "tif", "tiff", "heic", "heif",
        "avif", "jxl", "ico", "svg", "psd", "ai", "raw", "cr2", "cr3", "nef", "arw",
        "dng", "orf", "rw2",
        // videók
        "mp4", "mkv", "avi", "mov", "wmv", "webm", "flv", "m4v", "mpg", "mpeg", "m2ts",
        // dokumentumok
        "pdf", "docx", "xlsx", "pptx", "doc", "xls", "ppt", "odt", "ods", "odp",
        // egyéb
        "exe", "dll", "lnk", "url",
    };

    private readonly ConcurrentDictionary<string, ImageSource?> _cache = new(StringComparer.Ordinal);

    public void ClearCache() => _cache.Clear();

    public async ValueTask<ImageSource?> GetImageAsync(
        FileSystemItem item,
        int size,
        CancellationToken cancellationToken = default)
    {
        // Lomtár-elemeknél a FullPath szintetikus (nincs mögötte valódi
        // shell-elem) — a nem-bélyegkép fájlok kiterjesztés SZERINT
        // gyorsítótáraznak (lásd BuildCacheKey), tehát egy itt sikertelen
        // (null) találat SZENNYEZNÉ az adott kiterjesztés MEGOSZTOTT
        // gyorsítótár-bejegyzését minden valódi fájlnál is. Ezért ki sem
        // próbáljuk, sem nem gyorsítótárazzuk.
        if (item.IsRecycled)
        {
            return null;
        }

        var key = BuildCacheKey(item, size);

        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        // A shell hívások lemezhez és COM-hoz nyúlnak, ezért soha nem a UI-szálon.
        var image = await Task.Run(
            () => Extract(item.FullPath, size, IsThumbnailCandidate(item)),
            cancellationToken).ConfigureAwait(false);

        // Versenyhelyzetben ugyanaz az eredmény születne, ezért az elsőt tartjuk meg.
        _cache.TryAdd(key, image);
        return image;
    }

    private static bool IsThumbnailCandidate(FileSystemItem item) =>
        item.Kind == FileSystemItemKind.File
        && ThumbnailExtensions.Contains(item.Extension);

    private static string BuildCacheKey(FileSystemItem item, int size)
    {
        if (item.Kind is FileSystemItemKind.Directory or FileSystemItemKind.Drive)
        {
            // A mappák egyedi ikont kaphatnak (desktop.ini), ezért útvonal szerint.
            return $"d|{size}|{item.FullPath}";
        }

        if (IsThumbnailCandidate(item))
        {
            // A módosítási idő a kulcsban tartja frissen a szerkesztett képeket.
            return $"t|{size}|{item.ModifiedUtc.Ticks}|{item.FullPath}";
        }

        return $"x|{size}|{item.Extension}";
    }

    /// <summary>
    /// <c>E_PENDING</c> — az <c>IShellItemImageFactory::GetImage</c> ezt adja
    /// vissza, amikor az ikon még nincs kész a shell gyorsítótárában (jellemzően
    /// a <c>desktop.ini</c>-vel egyedi ikonra állított ismert mappáknál — pl.
    /// Dokumentumok, Letöltések, Képek, Zene —, ahol a shell aszinkron
    /// olvassa be az <c>IconResource</c>-ot). Az Asztal ezt SOHA nem dobja,
    /// mert nincs ilyen egyedi ikon-felülbírálása — ezért tűnt úgy elsőre,
    /// mintha csak néhány mappánál lenne hiba.
    /// </summary>
    private const int EPending = unchecked((int)0x8000000A);

    private const int MaxPendingRetries = 8;
    private static readonly TimeSpan PendingRetryDelay = TimeSpan.FromMilliseconds(30);

    private static int GetImageWithPendingRetry(IShellItemImageFactory factory, NativeMethods.Size size, ShellImageFlags flags, out nint hBitmap)
    {
        for (var attempt = 0; ; attempt++)
        {
            var hr = factory.GetImage(size, flags, out hBitmap);

            if (hr != EPending || attempt >= MaxPendingRetries)
            {
                return hr;
            }

            Thread.Sleep(PendingRetryDelay);
        }
    }

    private static ImageSource? Extract(string path, int size, bool preferThumbnail)
    {
        nint factoryPtr = 0;
        nint hBitmap = 0;

        try
        {
            var hr = SHCreateItemFromParsingName(
                path, 0, in IidShellItemImageFactory, out factoryPtr);

            if (hr != 0 || factoryPtr == 0)
            {
                return null;
            }

            var factory = (IShellItemImageFactory)Marshal.GetObjectForIUnknown(factoryPtr);

            var flags = ShellImageFlags.ScaleUp | ShellImageFlags.BiggerSizeOk;
            if (!preferThumbnail)
            {
                flags |= ShellImageFlags.IconOnly;
            }

            var requested = new NativeMethods.Size { Width = size, Height = size };

            if (GetImageWithPendingRetry(factory, requested, flags, out hBitmap) != 0 || hBitmap == 0)
            {
                // Bélyegkép hiányában essünk vissza a típusikonra.
                if (!preferThumbnail)
                {
                    return null;
                }

                if (GetImageWithPendingRetry(factory, requested, flags | ShellImageFlags.IconOnly, out hBitmap) != 0
                    || hBitmap == 0)
                {
                    return null;
                }
            }

            return ConvertToBitmapSource(hBitmap);
        }
        catch (COMException)
        {
            // Elérhetetlen vagy sérült elem — a nézet általános ikont rajzol.
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        finally
        {
            if (hBitmap != 0)
            {
                DeleteObject(hBitmap);
            }

            if (factoryPtr != 0)
            {
                Marshal.Release(factoryPtr);
            }
        }
    }

    /// <summary>
    /// HBITMAP átalakítása WPF képforrássá.
    /// </summary>
    /// <remarks>
    /// Szándékosan NEM a kézenfekvő <c>Imaging.CreateBitmapSourceFromHBitmap</c>
    /// fut itt: az eldobja az alfa-csatornát, amitől az átlátszó ikonok fekete
    /// háttérrel jelennek meg. Helyette a DIB bájtjait olvassuk ki, és
    /// <c>Pbgra32</c>-ként értelmezzük — a shell eleve előszorzott alfájú
    /// bitmapet ad vissza, tehát ez pontos egyezés.
    /// </remarks>
    private static ImageSource? ConvertToBitmapSource(nint hBitmap)
    {
        var info = default(NativeMethods.Bitmap);

        if (GetObject(hBitmap, Marshal.SizeOf<NativeMethods.Bitmap>(), ref info) == 0
            || info.Width <= 0
            || info.Height <= 0)
        {
            return null;
        }

        var width = info.Width;
        var height = info.Height;
        var stride = width * 4;
        var pixels = new byte[stride * height];

        var header = new BitmapInfo
        {
            Header = new BitmapInfoHeader
            {
                Size = Marshal.SizeOf<BitmapInfoHeader>(),
                Width = width,

                // Negatív magasság = felülről lefelé sorrend, így nem kell tükrözni.
                Height = -height,
                Planes = 1,
                BitCount = 32,
                Compression = BiRgb,
            },
        };

        var hdc = GetDC(0);

        try
        {
            if (GetDIBits(hdc, hBitmap, 0, (uint)height, pixels, ref header, DibRgbColors) == 0)
            {
                return null;
            }
        }
        finally
        {
            ReleaseDC(0, hdc);
        }

        EnsureVisibleAlpha(pixels);

        var source = BitmapSource.Create(
            width, height, 96, 96, PixelFormats.Pbgra32, palette: null, pixels, stride);

        // Fagyasztás nélkül a háttérszálon készült kép nem köthető a UI-hoz.
        source.Freeze();
        return source;
    }

    /// <summary>
    /// Néhány régi shell-kiterjesztés 32 bites bitmapet ad vissza, de az
    /// alfa-csatornát végig nullán hagyja. Ilyenkor a kép teljesen átlátszó
    /// lenne, ezért ha egyetlen látható pixel sincs, átlátszatlanná tesszük.
    /// </summary>
    private static void EnsureVisibleAlpha(byte[] pixels)
    {
        for (var i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] != 0)
            {
                return;
            }
        }

        for (var i = 3; i < pixels.Length; i += 4)
        {
            pixels[i] = 255;
        }
    }
}
