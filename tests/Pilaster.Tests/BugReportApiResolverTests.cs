using Pilaster.App.Diagnostics;

namespace Pilaster.Tests;

/// <summary>
/// A hibabejelentő bot API URL-jének és kulcsának feloldása környezeti
/// változóból.
/// </summary>
/// <remarks>
/// A fájlalapú ágat szándékosan nem teszteljük itt: az a futtató gép valódi
/// <c>%APPDATA%\Pilaster\bugreport-api.txt</c> állapotától függne, ami a
/// fejlesztői gépen és a CI-futtatón is eltérhet.
/// </remarks>
public class BugReportApiResolverTests : IDisposable
{
    private const string UrlEnvironmentVariableName = "PILASTER_BUG_REPORT_API_URL";
    private const string KeyEnvironmentVariableName = "PILASTER_BUG_REPORT_API_KEY";

    private readonly string? _originalUrl = Environment.GetEnvironmentVariable(UrlEnvironmentVariableName);
    private readonly string? _originalKey = Environment.GetEnvironmentVariable(KeyEnvironmentVariableName);

    [Fact]
    public void Resolve_KornyezetiValtozokbolOlvas()
    {
        Environment.SetEnvironmentVariable(UrlEnvironmentVariableName, "https://bot.example.com");
        Environment.SetEnvironmentVariable(KeyEnvironmentVariableName, "secret-key");

        var result = BugReportApiResolver.Resolve();

        Assert.Equal(("https://bot.example.com", "secret-key"), result);
    }

    [Fact]
    public void Resolve_KornyezetiValtozokatLevagja()
    {
        Environment.SetEnvironmentVariable(UrlEnvironmentVariableName, "  https://bot.example.com  ");
        Environment.SetEnvironmentVariable(KeyEnvironmentVariableName, "  secret-key  ");

        var result = BugReportApiResolver.Resolve();

        Assert.Equal(("https://bot.example.com", "secret-key"), result);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(UrlEnvironmentVariableName, _originalUrl);
        Environment.SetEnvironmentVariable(KeyEnvironmentVariableName, _originalKey);
    }
}
