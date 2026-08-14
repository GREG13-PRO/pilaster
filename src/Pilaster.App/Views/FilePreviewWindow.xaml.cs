using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Pilaster.App.Localization;
using Pilaster.App.Services;
using Pilaster.Core.FileSystem;
using Wpf.Ui.Controls;

namespace Pilaster.App.Views;

/// <summary>
/// Total Commander F3 (Megtekintés) — csak olvasható, gyors előnézet: kép
/// közvetlen megjelenítéssel, szöveg/kód a meglévő (korábban árva)
/// <see cref="FilePreviewService"/>-vel, minden más pedig hexadecimális
/// dump-ként az első néhány kilobájtról.
/// </summary>
public partial class FilePreviewWindow : FluentWindow
{
    /// <summary>Ennyi bájtig olvassuk a hex-dumpot — bőven elég egy fejléc/aláírás-szintű előnézethez.</summary>
    private const int MaxHexBytes = 4096;

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "png", "jpg", "jpeg", "bmp", "gif", "webp", "ico", "tif", "tiff",
    };

    private readonly FilePreviewService _previewService;

    public FilePreviewWindow(FilePreviewService previewService)
    {
        _previewService = previewService;
        InitializeComponent();
    }

    public async Task LoadAsync(FileSystemItem item)
    {
        Title = item.Name;
        TitleBarHost.Title = item.Name;

        TextScroll.Visibility = Visibility.Collapsed;
        ImageScroll.Visibility = Visibility.Collapsed;
        PreviewImage.Source = null;

        if (ImageExtensions.Contains(item.Extension) && TryLoadImage(item.FullPath, out var image))
        {
            PreviewImage.Source = image;
            ImageScroll.Visibility = Visibility.Visible;
            return;
        }

        var kind = FilePreviewClassifier.Classify(item);

        if (kind is PreviewKind.Text or PreviewKind.Markdown)
        {
            var text = await _previewService.ReadTextPreviewAsync(item.FullPath);

            if (text is not null)
            {
                TextContent.TextWrapping = TextWrapping.Wrap;
                TextContent.Text = text;
                TextScroll.Visibility = Visibility.Visible;
                return;
            }
        }

        TextContent.TextWrapping = TextWrapping.NoWrap;
        TextContent.Text = BuildHexDump(item.FullPath);
        TextScroll.Visibility = Visibility.Visible;
    }

    private static bool TryLoadImage(string path, out BitmapImage? image)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path);
            bitmap.EndInit();
            bitmap.Freeze();

            image = bitmap;
            return true;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException)
        {
            image = null;
            return false;
        }
    }

    private static string BuildHexDump(string path)
    {
        byte[] bytes;

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            bytes = new byte[Math.Min(MaxHexBytes, stream.Length)];

            var read = 0;

            while (read < bytes.Length)
            {
                var chunk = stream.Read(bytes, read, bytes.Length - read);

                if (chunk == 0)
                {
                    break;
                }

                read += chunk;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return TranslationSource.Instance["Viewer_Unavailable"];
        }

        var sb = new StringBuilder();

        for (var offset = 0; offset < bytes.Length; offset += 16)
        {
            var count = Math.Min(16, bytes.Length - offset);

            sb.Append(offset.ToString("X8")).Append("  ");

            for (var i = 0; i < 16; i++)
            {
                sb.Append(i < count ? bytes[offset + i].ToString("X2") : "  ").Append(' ');
            }

            sb.Append(' ');

            for (var i = 0; i < count; i++)
            {
                var b = bytes[offset + i];
                sb.Append(b is >= 32 and < 127 ? (char)b : '.');
            }

            sb.AppendLine();
        }

        if (bytes.Length == MaxHexBytes)
        {
            sb.AppendLine().Append(TranslationSource.Instance["Viewer_HexTruncated"]);
        }

        return sb.ToString();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }
}
