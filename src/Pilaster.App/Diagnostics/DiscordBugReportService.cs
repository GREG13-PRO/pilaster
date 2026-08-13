using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;

namespace Pilaster.App.Diagnostics;

/// <summary>
/// <see cref="IBugReportService"/> megvalósítása a hibabejelentő bot API-jával.
/// </summary>
/// <remarks>
/// <para>
/// A régebbi, sima Discord webhookos megoldás nem volt elég a „Kész" gombos
/// archiváláshoz: egy gombkattintás (interakció) csak egy ténylegesen futó,
/// Discord Gateway-hez kapcsolódó bot alkalmazáshoz tud eljutni, egy puszta
/// bejövő webhookhoz nem. Ezért az üzenetet is a botnak kell küldenie — lásd
/// <c>discord-bot/</c> a repó gyökerében — nem közvetlenül a Discord API-nak.
/// </para>
/// <para>
/// A beágyazás (embed) felépítése változatlanul <see cref="DiscordPayloadBuilder"/>
/// dolga marad: az alkalmazás továbbra is ugyanazt a JSON-t építi fel és
/// küldi <c>multipart/form-data</c>-ban (<c>payload_json</c> mező +
/// <c>files[n]</c> csatolmányok) — csak a célcím és egy megosztott API-kulcs
/// fejléc változott, a bot pedig ezt egyszerűen továbbküldi a Discord
/// csatornára, kiegészítve a „Kész" gombbal.
/// </para>
/// </remarks>
public sealed class DiscordBugReportService : IBugReportService
{
    /// <summary>
    /// A csatolt naplórészlet felső korlátja — bőven elég egy hiba
    /// kontextusához, és gyorsabban is feltöltődik, mint a teljes napló.
    /// </summary>
    private const int MaxLogBytes = 200 * 1024;

    private const string ApiKeyHeaderName = "X-Api-Key";

    private readonly HttpClient _http;
    private readonly (string Url, string ApiKey)? _api;

    public DiscordBugReportService(HttpClient http)
    {
        _http = http;
        _api = BugReportApiResolver.Resolve();
    }

    public bool IsConfigured => _api is not null;

    public async Task<BugReportResult> SendAsync(
        BugReportRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_api is not { } api)
        {
            return new BugReportResult(false, "BugReport_NotConfigured");
        }

        var context = new BugReportContext(
            request.Description,
            AppVersionInfo.Current,
            RuntimeInformation.OSDescription,
            RuntimeInformation.FrameworkDescription,
            DateTimeOffset.UtcNow,
            request.IsFeatureIdea);

        var json = DiscordPayloadBuilder.BuildEmbedJson(context);

        try
        {
            using var response = await PostAsync(api.Url, api.ApiKey, json, request, cancellationToken)
                .ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return new BugReportResult(true, null);
            }

            return response.StatusCode == HttpStatusCode.Unauthorized
                ? new BugReportResult(false, "BugReport_NotConfigured")
                : new BugReportResult(false, "BugReport_Failure");
        }
        catch (HttpRequestException)
        {
            return new BugReportResult(false, "BugReport_ErrorNetwork");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Időtúllépés, nem a felhasználó általi megszakítás.
            return new BugReportResult(false, "BugReport_ErrorNetwork");
        }
    }

    private async Task<HttpResponseMessage> PostAsync(
        string apiUrl,
        string apiKey,
        string json,
        BugReportRequest request,
        CancellationToken cancellationToken)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent(json, Encoding.UTF8, "application/json"), "payload_json" },
        };

        if (request.Screenshot is { Length: > 0 } screenshot)
        {
            var imageContent = new ByteArrayContent(screenshot);
            imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(imageContent, "files[0]", "screenshot.png");
        }

        if (request.LogFilePath is { } logPath && TryReadLogTail(logPath, out var logBytes, out var logName))
        {
            var logContent = new ByteArrayContent(logBytes);
            logContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
            form.Add(logContent, "files[1]", logName);
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, CombineReportUrl(apiUrl))
        {
            Content = form,
        };
        httpRequest.Headers.Add(ApiKeyHeaderName, apiKey);

        return await _http.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
    }

    private static string CombineReportUrl(string apiUrl) =>
        apiUrl.TrimEnd('/') + "/report";

    /// <summary>
    /// A naplófájl utolsó szelete.
    /// </summary>
    /// <remarks>
    /// <see cref="FileShare.ReadWrite"/> kell, mert a Serilog fájlszinkje a
    /// naplót nyitva tartja írásra, amíg az alkalmazás fut.
    /// </remarks>
    private static bool TryReadLogTail(string path, out byte[] bytes, out string fileName)
    {
        fileName = Path.GetFileName(path);
        bytes = [];

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var take = (int)Math.Min(stream.Length, MaxLogBytes);

            if (take <= 0)
            {
                return false;
            }

            stream.Seek(-take, SeekOrigin.End);
            bytes = new byte[take];
            stream.ReadExactly(bytes);

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
