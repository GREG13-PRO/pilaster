using System.IO;

namespace Pilaster.App.Diagnostics;

/// <summary>
/// A hibabejelentő bot API végpontjának és megosztott kulcsának feloldása.
/// </summary>
/// <remarks>
/// Sem az URL, sem a kulcs nem szerepel a forráskódban — ugyanaz a minta,
/// mint a korábbi <c>BugReportWebhookResolver</c>-nél volt: két helyről
/// jöhet, ebben a sorrendben:
/// <list type="number">
/// <item>a <c>PILASTER_BUG_REPORT_API_URL</c> / <c>PILASTER_BUG_REPORT_API_KEY</c> környezeti változókból;</item>
/// <item>egy helyi fájlból, ami soha nem kerül a repóba — ugyanabban a
/// mappában él, ahol a beállítások (<c>%APPDATA%\Pilaster</c>), első sorban
/// az URL-lel, másodikban a kulccsal.</item>
/// </list>
/// Aki a Pilastert saját magának fordítja, a saját bot-példánya adatait ide
/// teheti. Részletek: <c>docs/BUG_REPORTS.md</c>.
/// </remarks>
public static class BugReportApiResolver
{
    private const string UrlEnvironmentVariableName = "PILASTER_BUG_REPORT_API_URL";
    private const string KeyEnvironmentVariableName = "PILASTER_BUG_REPORT_API_KEY";

    public static string ConfigFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Pilaster",
        "bugreport-api.txt");

    public static (string Url, string ApiKey)? Resolve()
    {
        var url = Environment.GetEnvironmentVariable(UrlEnvironmentVariableName);
        var key = Environment.GetEnvironmentVariable(KeyEnvironmentVariableName);

        if (!string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(key))
        {
            return (url.Trim(), key.Trim());
        }

        try
        {
            if (!File.Exists(ConfigFilePath))
            {
                return null;
            }

            var lines = File.ReadAllLines(ConfigFilePath);

            if (lines.Length < 2)
            {
                return null;
            }

            var fileUrl = lines[0].Trim();
            var fileKey = lines[1].Trim();

            return fileUrl.Length > 0 && fileKey.Length > 0 ? (fileUrl, fileKey) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
