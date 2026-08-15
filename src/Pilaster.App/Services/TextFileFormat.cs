using System.IO;
using System.Text;

namespace Pilaster.App.Services;

/// <summary>Egy szövegfájl sorvégeinek fajtája.</summary>
public enum LineEndingKind
{
    /// <summary>Windows: <c>\r\n</c>.</summary>
    Crlf,

    /// <summary>Unix: <c>\n</c>.</summary>
    Lf,

    /// <summary>Klasszikus Mac: <c>\r</c>.</summary>
    Cr,

    /// <summary>Vegyes sorvégek — a fájl konvertálásra szorul.</summary>
    Mixed,
}

/// <summary>Egy beolvasott szövegfájl tartalma és formátuma.</summary>
/// <param name="Text">A fájl szövege.</param>
/// <param name="Encoding">A felismert (vagy kényszerített) kódolás.</param>
/// <param name="HasBom">Volt-e bájtsorrend-jel a fájl elején.</param>
/// <param name="LineEnding">A felismert sorvég.</param>
public sealed record TextFileContent(string Text, Encoding Encoding, bool HasBom, LineEndingKind LineEnding);

/// <summary>
/// Szövegfájlok kódolásának és sorvégeinek felismerése, valamint biztonságos
/// írása.
/// </summary>
public static class TextFileFormat
{
    /// <summary>E fölött a méret fölött a szerkesztő figyelmeztet és csak olvasható módban nyit (spec F2).</summary>
    public const long ReadOnlyThresholdBytes = 50L * 1024 * 1024;

    /// <summary>E fölött a méret fölött virtualizált renderelés kell — ez az AvalonEdit alapból megvan.</summary>
    public const long LargeFileThresholdBytes = 5L * 1024 * 1024;

    /// <summary>A bináris felismeréshez ennyi bájtot nézünk meg a fájl elejéről.</summary>
    private const int BinaryProbeBytes = 8000;

    /// <summary>A támogatott kódolások azonosítói — ugyanaz a készlet, mint a Beállításokban.</summary>
    public static IReadOnlyList<string> SupportedEncodings { get; } =
        ["utf-8", "utf-8-bom", "cp1250", "cp852", "utf-16le", "utf-16be"];

    /// <summary>
    /// A kódolás feloldása azonosítóból. Ismeretlen névnél UTF-8.
    /// </summary>
    /// <remarks>
    /// A CP1250 és a CP852 nem érhető el a .NET alapkészletében — a
    /// <see cref="CodePagesEncodingProvider"/> regisztrálása nélkül
    /// <see cref="NotSupportedException"/>-t dobna. Ezt egyszer, statikus
    /// konstruktorban intézzük el.
    /// </remarks>
    static TextFileFormat() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static Encoding Resolve(string id) => id switch
    {
        "utf-8-bom" => new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
        "cp1250" => GetCodePage(1250),
        "cp852" => GetCodePage(852),
        "utf-16le" => new UnicodeEncoding(bigEndian: false, byteOrderMark: true),
        "utf-16be" => new UnicodeEncoding(bigEndian: true, byteOrderMark: true),
        _ => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
    };

    private static Encoding GetCodePage(int codePage)
    {
        try
        {
            return Encoding.GetEncoding(codePage);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            // Csonkolt futtatókörnyezet — az UTF-8 mindig elérhető.
            return new UTF8Encoding(false);
        }
    }

    /// <summary>Az azonosító visszaképzése egy kódolásból — a státuszsorhoz.</summary>
    public static string Describe(Encoding encoding, bool hasBom) => encoding.CodePage switch
    {
        1200 => "utf-16le",
        1201 => "utf-16be",
        1250 => "cp1250",
        852 => "cp852",
        _ => hasBom ? "utf-8-bom" : "utf-8",
    };

    /// <summary>
    /// Igaz, ha a fájl binárisnak látszik (null bájtot tartalmaz az első
    /// néhány kilobájtban) — ilyet a szerkesztő nem nyit meg.
    /// </summary>
    /// <remarks>
    /// A null bájt a legmegbízhatóbb, olcsó jel. Az UTF-16 szövegfájlok is
    /// tartalmaznak nullát, ezért a BOM-mal jelölt UTF-16 kivétel — enélkül
    /// minden UTF-16 fájl binárisnak minősülne.
    /// </remarks>
    public static bool LooksBinary(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var probe = new byte[Math.Min(BinaryProbeBytes, stream.Length)];
            var read = stream.ReadExactlyOrLess(probe);

            if (read >= 2 && ((probe[0] == 0xFF && probe[1] == 0xFE) || (probe[0] == 0xFE && probe[1] == 0xFF)))
            {
                return false;
            }

            return probe.AsSpan(0, read).IndexOf((byte)0) >= 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Egy szövegfájl beolvasása a kódolás automatikus felismerésével.
    /// </summary>
    /// <param name="path">A fájl útvonala.</param>
    /// <param name="forcedEncodingId">
    /// Ha meg van adva, a felismerés helyett ezt a kódolást használjuk — ez az
    /// „Újranyitás ezzel a kódolással" parancs.
    /// </param>
    /// <param name="progress">
    /// A beolvasott bájtok aránya (0–1). Nagy fájlnál ez táplálja a
    /// megszakítható folyamatjelzőt.
    /// </param>
    /// <param name="cancellationToken">A „Mégse" gomb ezt jelzi.</param>
    public static async Task<TextFileContent> ReadAsync(
        string path,
        string? forcedEncodingId = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var bytes = await ReadAllBytesWithProgressAsync(path, progress, cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        var (encoding, hasBom, bomLength) = forcedEncodingId is null
            ? Detect(bytes)
            : (Resolve(forcedEncodingId), false, 0);

        var text = encoding.GetString(bytes, bomLength, bytes.Length - bomLength);

        return new TextFileContent(text, encoding, hasBom, DetectLineEnding(text));
    }

    /// <summary>
    /// Beolvasás adagokban, folyamatjelzéssel.
    /// </summary>
    /// <remarks>
    /// A <see cref="File.ReadAllBytesAsync"/> egyszerűbb volna, de nem tud
    /// haladásról beszámolni, és menet közben sem szakítható meg. Egy 120 MB-os
    /// naplófájlnál mindkettőre szükség van: MÉRVE a teljes betöltés ~4,9
    /// másodperc, amiből visszajelzés nélkül a felhasználó csak annyit lát,
    /// hogy nem történik semmi.
    /// </remarks>
    private static async Task<byte[]> ReadAllBytesWithProgressAsync(
        string path,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: 1 << 16,
            useAsync: true);

        var length = stream.Length;

        if (length > int.MaxValue)
        {
            throw new IOException("A fájl nagyobb, mint amit egyetlen tömbben be lehetne olvasni.");
        }

        var buffer = new byte[length];
        var offset = 0;

        // 4 MB-os adagok: elég nagy ahhoz, hogy az átbocsátás ne romoljon, és
        // elég kicsi ahhoz, hogy a megszakítás gyorsan érvényesüljön.
        const int ChunkSize = 4 << 20;

        while (offset < buffer.Length)
        {
            var read = await stream
                .ReadAsync(buffer.AsMemory(offset, Math.Min(ChunkSize, buffer.Length - offset)), cancellationToken)
                .ConfigureAwait(false);

            if (read == 0)
            {
                break;
            }

            offset += read;
            progress?.Report((double)offset / buffer.Length);
        }

        return buffer;
    }

    /// <summary>
    /// A kódolás felismerése: előbb a bájtsorrend-jel, aztán heurisztika.
    /// </summary>
    /// <remarks>
    /// A heurisztika egyszerű, de a gyakorlatban megbízható: megpróbáljuk
    /// szigorú (hibára dobó) UTF-8-cal dekódolni. Az érvényes UTF-8 bájtsorok
    /// olyan szűk halmazt alkotnak, hogy egy CP1250-es szöveg szinte biztosan
    /// megbukik rajta — a régió alapértelmezett kódlapja pedig épp a CP1250.
    /// </remarks>
    private static (Encoding Encoding, bool HasBom, int BomLength) Detect(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return (new UTF8Encoding(true), true, 3);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return (new UnicodeEncoding(false, true), true, 2);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return (new UnicodeEncoding(true, true), true, 2);
        }

        try
        {
            new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
            return (new UTF8Encoding(false), false, 0);
        }
        catch (DecoderFallbackException)
        {
            return (GetCodePage(1250), false, 0);
        }
    }

    /// <summary>A sorvég felismerése. Vegyes fájlnál <see cref="LineEndingKind.Mixed"/>.</summary>
    public static LineEndingKind DetectLineEnding(string text)
    {
        int crlf = 0, lf = 0, cr = 0;

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    crlf++;
                    i++;
                }
                else
                {
                    cr++;
                }
            }
            else if (text[i] == '\n')
            {
                lf++;
            }
        }

        var kinds = (crlf > 0 ? 1 : 0) + (lf > 0 ? 1 : 0) + (cr > 0 ? 1 : 0);

        if (kinds > 1)
        {
            return LineEndingKind.Mixed;
        }

        return lf > 0 ? LineEndingKind.Lf : cr > 0 ? LineEndingKind.Cr : LineEndingKind.Crlf;
    }

    /// <summary>A szöveg sorvégeinek egységesítése.</summary>
    public static string NormalizeLineEndings(string text, LineEndingKind target)
    {
        var normalized = text.Replace("\r\n", "\n").Replace("\r", "\n");

        return target switch
        {
            LineEndingKind.Crlf => normalized.Replace("\n", "\r\n"),
            LineEndingKind.Cr => normalized.Replace("\n", "\r"),
            _ => normalized,
        };
    }

    /// <summary>
    /// Atomi mentés: ideiglenes fájlba írunk, és csak a SIKERES írás után
    /// cseréljük le az élest.
    /// </summary>
    /// <remarks>
    /// Enélkül egy áramszünet vagy összeomlás a mentés közben csonka fájlt
    /// hagyna hátra — egy szerkesztőnél ez a legsúlyosabb adatvesztési mód.
    /// A <c>File.Move(overwrite: true)</c> ugyanazon a köteten atomi.
    /// </remarks>
    public static async Task WriteAsync(string path, string text, Encoding encoding, LineEndingKind lineEnding)
    {
        var normalized = NormalizeLineEndings(text, lineEnding == LineEndingKind.Mixed ? LineEndingKind.Crlf : lineEnding);
        var temporary = path + ".pilaster.tmp";

        await File.WriteAllTextAsync(temporary, normalized, encoding).ConfigureAwait(false);

        try
        {
            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            // A csere meghiúsult (pl. a cél zárolt) — az ideiglenes fájlt nem
            // hagyjuk hátra szemétként.
            try
            {
                File.Delete(temporary);
            }
            catch (IOException)
            {
            }

            throw;
        }
    }

    private static int ReadExactlyOrLess(this Stream stream, byte[] buffer)
    {
        var total = 0;

        while (total < buffer.Length)
        {
            var read = stream.Read(buffer, total, buffer.Length - total);

            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }
}
