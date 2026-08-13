using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace Pilaster.App.Diagnostics;

/// <summary>
/// <see cref="IUpdateService"/> megvalósítása a GitHub Releases API-val.
/// </summary>
/// <remarks>
/// A frissítés telepítése a legkényesebb rész: a telepítő nem tudja felülírni
/// a futó <c>Pilaster.exe</c>-t, amíg az fut (a Windows zárolja a saját
/// folyamatához tartozó fájlt). Ezért a <see cref="BeginInstall"/> egy
/// leválasztott PowerShell-segédszkriptet indít, ami megvárja a jelenlegi
/// folyamat PID-jének kilépését, csak utána futtatja csendben a telepítőt,
/// majd újraindítja az alkalmazást — a hívónak (lásd
/// <c>MainWindow.OnUpdateRestartRequested</c>) ezután azonnal be kell zárnia
/// az alkalmazást, különben a szkript örökké várakozna.
/// </remarks>
public sealed class GitHubUpdateService : IUpdateService
{
    private readonly HttpClient _http;

    public GitHubUpdateService(HttpClient http)
    {
        _http = http;
    }

    public async Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, GitHubRepositoryInfo.ReleasesApiUrl);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Pilaster", AppVersionInfo.Current));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        HttpResponseMessage response;

        try
        {
            response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return new UpdateCheckResult(UpdateCheckStatus.NetworkError, null);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new UpdateCheckResult(UpdateCheckStatus.NetworkError, null);
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            {
                return new UpdateCheckResult(UpdateCheckStatus.RateLimited, null);
            }

            if (!response.IsSuccessStatusCode)
            {
                return new UpdateCheckResult(UpdateCheckStatus.Error, null);
            }

            JsonDocument document;

            try
            {
                var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException)
            {
                return new UpdateCheckResult(UpdateCheckStatus.Error, null);
            }

            using (document)
            {
                return ParseRelease(document.RootElement);
            }
        }
    }

    private static UpdateCheckResult ParseRelease(JsonElement root)
    {
        if (!root.TryGetProperty("tag_name", out var tagElement)
            || !SemanticVersion.TryParse(tagElement.GetString(), out var latest))
        {
            return new UpdateCheckResult(UpdateCheckStatus.Error, null);
        }

        if (!SemanticVersion.TryParse(AppVersionInfo.Current, out var current) || latest <= current)
        {
            return new UpdateCheckResult(UpdateCheckStatus.UpToDate, null);
        }

        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return new UpdateCheckResult(UpdateCheckStatus.NoAssetForPlatform, null);
        }

        var archTag = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        var installerSuffix = $"-{archTag}-setup.exe";

        string? installerUrl = null;
        string? installerName = null;

        foreach (var asset in assets.EnumerateArray())
        {
            var name = GetString(asset, "name");

            if (name is null || !name.EndsWith(installerSuffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            installerName = name;
            installerUrl = GetString(asset, "browser_download_url");
            break;
        }

        if (installerUrl is null || installerName is null)
        {
            return new UpdateCheckResult(UpdateCheckStatus.NoAssetForPlatform, null);
        }

        var checksumUrl = FindAssetUrl(assets, installerName + ".sha256");

        if (checksumUrl is null)
        {
            return new UpdateCheckResult(UpdateCheckStatus.NoAssetForPlatform, null);
        }

        var releaseUrl = GetString(root, "html_url") ?? GitHubRepositoryInfo.ReleasesPageUrl;

        var info = new UpdateInfo(latest.ToString(), releaseUrl, installerUrl, checksumUrl, installerName);
        return new UpdateCheckResult(UpdateCheckStatus.UpdateAvailable, info);
    }

    private static string? FindAssetUrl(JsonElement assets, string name)
    {
        foreach (var asset in assets.EnumerateArray())
        {
            if (string.Equals(GetString(asset, "name"), name, StringComparison.OrdinalIgnoreCase))
            {
                return GetString(asset, "browser_download_url");
            }
        }

        return null;
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) ? value.GetString() : null;

    public async Task<UpdateDownloadResult> DownloadInstallerAsync(
        UpdateInfo update,
        CancellationToken cancellationToken = default)
    {
        var installerPath = Path.Combine(UpdateTempDirectory, update.InstallerFileName);

        try
        {
            Directory.CreateDirectory(UpdateTempDirectory);

            var expectedHashLine = await _http.GetStringAsync(update.InstallerChecksumUrl, cancellationToken)
                .ConfigureAwait(false);
            var expectedHash = ExtractHash(expectedHashLine);

            await using (var httpStream = await _http.GetStreamAsync(update.InstallerDownloadUrl, cancellationToken)
                .ConfigureAwait(false))
            await using (var fileStream = new FileStream(installerPath, FileMode.Create, FileAccess.Write))
            {
                await httpStream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
            }

            var actualHash = await ComputeSha256Async(installerPath, cancellationToken).ConfigureAwait(false);

            if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(installerPath);
                return new UpdateDownloadResult(UpdateDownloadStatus.ChecksumMismatch, null);
            }

            return new UpdateDownloadResult(UpdateDownloadStatus.Succeeded, installerPath);
        }
        catch (HttpRequestException)
        {
            TryDelete(installerPath);
            return new UpdateDownloadResult(UpdateDownloadStatus.NetworkError, null);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryDelete(installerPath);
            return new UpdateDownloadResult(UpdateDownloadStatus.NetworkError, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(installerPath);
            return new UpdateDownloadResult(UpdateDownloadStatus.Error, null);
        }
    }

    public void BeginInstall(string installerPath)
    {
        var currentExePath = Environment.ProcessPath;

        if (currentExePath is null)
        {
            return;
        }

        var scriptPath = Path.Combine(UpdateTempDirectory, "install.ps1");

        var script = $"""
            Wait-Process -Id {Environment.ProcessId} -ErrorAction SilentlyContinue
            Start-Process -FilePath '{EscapeForPowerShell(installerPath)}' -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART' -Wait
            Start-Process -FilePath '{EscapeForPowerShell(currentExePath)}'
            Remove-Item -Path '{EscapeForPowerShell(installerPath)}' -Force -ErrorAction SilentlyContinue
            """;

        File.WriteAllText(scriptPath, script);

        Process.Start(new ProcessStartInfo("powershell.exe")
        {
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        });
    }

    private static string UpdateTempDirectory { get; } = Path.Combine(Path.GetTempPath(), "Pilaster-update");

    private static string EscapeForPowerShell(string value) => value.Replace("'", "''");

    /// <summary>
    /// A <c>.sha256</c> fájl első tokenje — a formátum
    /// <c>"hash  fájlnév"</c>, ahogy a release workflow írja (lásd
    /// <c>.github/workflows/release.yml</c>).
    /// </summary>
    private static string ExtractHash(string checksumFileContent)
    {
        var trimmed = checksumFileContent.AsSpan().Trim();
        var spaceIndex = trimmed.IndexOfAny(' ', '\t');
        return (spaceIndex > 0 ? trimmed[..spaceIndex] : trimmed).ToString();
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }
}
