using Pilaster.App.Diagnostics;

namespace Pilaster.Tests;

/// <summary>
/// A webhook URL feloldása környezeti változóból.
/// </summary>
/// <remarks>
/// A fájlalapú ágat szándékosan nem teszteljük itt: az a futtató gép valódi
/// <c>%APPDATA%\Pilaster\webhook.txt</c> állapotától függne, ami a fejlesztői
/// gépen és a CI-futtatón is eltérhet — egy ilyen teszt esetlegesen hamisan
/// bukna. A környezeti változós út önmagában lefedi az elsőbbségi és
/// levágási logikát.
/// </remarks>
public class BugReportWebhookResolverTests : IDisposable
{
    private const string EnvironmentVariableName = "PILASTER_BUG_REPORT_WEBHOOK";
    private readonly string? _originalValue = Environment.GetEnvironmentVariable(EnvironmentVariableName);

    [Fact]
    public void Resolve_KornyezetiValtozobolOlvas()
    {
        Environment.SetEnvironmentVariable(EnvironmentVariableName, "https://discord.com/api/webhooks/test");

        Assert.Equal("https://discord.com/api/webhooks/test", BugReportWebhookResolver.Resolve());
    }

    [Fact]
    public void Resolve_KornyezetiValtozotLevagja()
    {
        Environment.SetEnvironmentVariable(EnvironmentVariableName, "  https://discord.com/api/webhooks/test  ");

        Assert.Equal("https://discord.com/api/webhooks/test", BugReportWebhookResolver.Resolve());
    }

    public void Dispose() =>
        Environment.SetEnvironmentVariable(EnvironmentVariableName, _originalValue);
}
