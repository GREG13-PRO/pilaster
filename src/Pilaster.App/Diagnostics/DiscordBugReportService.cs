using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;

namespace Pilaster.App.Diagnostics;

/// <summary>
/// <see cref="IBugReportService"/> megvalósítása Discord bejövő webhookkal.
/// </summary>
/// <remarks>
/// Csatolmány nélkül egyszerű JSON törzzsel POST-ol; képernyőkép vagy napló
/// csatolásakor <c>multipart/form-data</c>-ra vált, ahogy a Discord webhook
/// API a fájlfeltöltést várja (<c>payload_json</c> mező + <c>files[n]</c>).
/// </remarks>
public sealed class DiscordBugReportService : IBugReportService
{
    /// <summary>
    /// A csatolt naplórészlet felső korlátja. A Discord webhookok fájlmérete
    /// jellemzően 25 MB-ig szabad, de a legutóbbi néhány száz kilobájt bőven
    /// elég egy hiba kontextusához, és gyorsabban is feltöltődik.
    /// </summary>
    private const int MaxLogBytes = 200 * 1024;

    private readonly HttpClient _http;
    private readonly string? _webhookUrl;

    public DiscordBugReportService(HttpClient http)
    {
        _http = http;
        _webhookUrl = BugReportWebhookResolver.Resolve();
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_webhookUrl);

    public async Task<BugReportResult> SendAsync(
        BugReportRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_webhookUrl is not { Length: > 0 } webhookUrl)
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
            using var response = await PostAsync(webhookUrl, json, request, cancellationToken)
                .ConfigureAwait(false);

            return response.IsSuccessStatusCode
                ? new BugReportResult(true, null)
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

    private Task<HttpResponseMessage> PostAsync(
        string webhookUrl,
        string json,
        BugReportRequest request,
        CancellationToken cancellationToken)
    {
        var hasAttachment = request.Screenshot is { Length: > 0 } || request.LogFilePath is not null;

        if (!hasAttachment)
        {
            var jsonContent = new StringContent(json, Encoding.UTF8, "application/json");
            return _http.PostAsync(webhookUrl, jsonContent, cancellationToken);
        }

        var form = new MultipartFormDataContent();
        form.Add(new StringContent(json, Encoding.UTF8, "application/json"), "payload_json");

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

        return _http.PostAsync(webhookUrl, form, cancellationToken);
    }

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
