using System.IO;

namespace Pilaster.App.Diagnostics;

/// <summary>
/// A Discord webhook URL feloldása.
/// </summary>
/// <remarks>
/// Az URL sosem szerepel a forráskódban. Két helyről jöhet, ebben a sorrendben:
/// <list type="number">
/// <item>a <c>PILASTER_BUG_REPORT_WEBHOOK</c> környezeti változóból;</item>
/// <item>egy helyi fájlból, ami soha nem kerül a repóba — ugyanabban a
/// mappában él, ahol a beállítások (<c>%APPDATA%\Pilaster</c>).</item>
/// </list>
/// Aki a Pilastert saját magának fordítja, a saját Discord-webhookját ide
/// teheti. Részletek: <c>docs/BUG_REPORTS.md</c>.
/// </remarks>
public static class BugReportWebhookResolver
{
    private const string EnvironmentVariableName = "PILASTER_BUG_REPORT_WEBHOOK";

    public static string ConfigFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Pilaster",
        "webhook.txt");

    public static string? Resolve()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(EnvironmentVariableName);

        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment.Trim();
        }

        try
        {
            if (!File.Exists(ConfigFilePath))
            {
                return null;
            }

            var content = File.ReadAllText(ConfigFilePath).Trim();
            return content.Length > 0 ? content : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
