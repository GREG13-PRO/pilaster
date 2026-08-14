using System.IO;
using Pilaster.Core.FileSystem;

namespace Pilaster.App.Services;

public enum PreviewKind
{
    /// <summary>Sima szöveg vagy kód — monospace szövegdobozban jelenik meg.</summary>
    Text,

    /// <summary>Markdown — renderelve jelenik meg, lásd <see cref="MarkdownFlowDocumentBuilder"/>.</summary>
    Markdown,

    /// <summary>
    /// Minden más: kép, PDF, videó, Office-dokumentum stb. — ezekhez a natív
    /// shell-bélyegkép (<see cref="Pilaster.Shell.Imaging.IShellImageService"/>,
    /// nagy méretben kérve) ad előnézetet, saját renderelő kód nélkül.
    /// </summary>
    Thumbnail,
}

/// <summary>Egy fájl előnézeti típusának eldöntése a kiterjesztése alapján.</summary>
public static class FilePreviewClassifier
{
    private static readonly HashSet<string> MarkdownExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "md", "markdown",
    };

    /// <summary>
    /// Szöveges/kód kiterjesztések. Szándékosan felsorolásos (nem
    /// "minden, ami nem ismert bináris"): egy tévesen szövegként megnyitott
    /// bináris fájl értelmetlen karakterhalmazt mutatna.
    /// </summary>
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "txt", "log", "ini", "cfg", "conf", "config", "json", "jsonc", "xml", "xaml",
        "yaml", "yml", "toml", "csv", "tsv", "env",
        "cs", "csx", "vb", "fs", "js", "mjs", "cjs", "ts", "tsx", "jsx", "py", "rb",
        "java", "kt", "go", "rs", "c", "h", "cpp", "hpp", "cc", "cxx", "swift",
        "php", "pl", "lua", "sql", "sh", "bash", "ps1", "psm1", "bat", "cmd",
        "html", "htm", "css", "scss", "sass", "less",
        "gitignore", "gitattributes", "editorconfig", "dockerfile", "npmrc",
        "props", "targets", "csproj", "sln", "resx",
    };

    public static PreviewKind Classify(FileSystemItem item)
    {
        if (item.Kind != FileSystemItemKind.File)
        {
            return PreviewKind.Thumbnail;
        }

        if (MarkdownExtensions.Contains(item.Extension))
        {
            return PreviewKind.Markdown;
        }

        return TextExtensions.Contains(item.Extension) ? PreviewKind.Text : PreviewKind.Thumbnail;
    }
}

/// <summary>Szöveges fájlok tartalmának beolvasása előnézethez.</summary>
public sealed class FilePreviewService
{
    /// <summary>
    /// Ennyi karakterig olvasunk — bőven elég egy előnézethez, és egy
    /// véletlenül szövegként felismert, valójában óriási naplófájl se
    /// fagyassza be a felületet.
    /// </summary>
    private const int MaxPreviewChars = 150_000;

    public async Task<string?> ReadTextPreviewAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, useAsync: true);

            using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);

            var buffer = new char[MaxPreviewChars];
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            var text = new string(buffer, 0, read);

            // Egy plusz karakter beolvasásának megkísérlése ASZINKRON módon
            // dönti el, van-e még tartalom — a StreamReader.EndOfStream
            // szinkron read-ahead-et végezne, ami blokkolna egy async
            // metódusban (lásd CA2024).
            var probe = new char[1];
            var more = await reader.ReadAsync(probe.AsMemory(), cancellationToken).ConfigureAwait(false);

            return more == 0
                ? text
                : text + "\n\n… (a fájl ennél hosszabb — csak az eleje látszik az előnézetben)";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
