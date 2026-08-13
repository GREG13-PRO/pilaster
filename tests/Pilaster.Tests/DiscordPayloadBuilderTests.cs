using System.Text.Json;
using Pilaster.App.Diagnostics;

namespace Pilaster.Tests;

/// <summary>
/// A Discord webhook JSON törzsének felépítése — hálózat nélkül tesztelhető,
/// mert a <see cref="DiscordPayloadBuilder"/> tiszta logika.
/// </summary>
public class DiscordPayloadBuilderTests
{
    private static readonly BugReportContext SampleContext = new(
        Description: "A rácsnézet lefagy nagy mappánál.",
        AppVersion: "0.3.0",
        OsDescription: "Windows 11 (10.0.26200)",
        RuntimeDescription: ".NET 10.0.11",
        TimestampUtc: new DateTimeOffset(2026, 8, 13, 21, 45, 3, TimeSpan.Zero));

    [Fact]
    public void BuildEmbedJson_ErvenyesJsontAd()
    {
        var json = DiscordPayloadBuilder.BuildEmbedJson(SampleContext);

        // Ha nem elemezhető JSON-ként, az önmagában bukás — a Discord API
        // egyből elutasítaná a kérést.
        using var document = JsonDocument.Parse(json);

        Assert.Equal(JsonValueKind.Array, document.RootElement.GetProperty("embeds").ValueKind);
    }

    [Fact]
    public void BuildEmbedJson_TartalmazzaALeirastEsAKontextust()
    {
        var json = DiscordPayloadBuilder.BuildEmbedJson(SampleContext);
        using var document = JsonDocument.Parse(json);

        var embed = document.RootElement.GetProperty("embeds")[0];

        Assert.Equal(SampleContext.Description, embed.GetProperty("description").GetString());

        var fields = embed.GetProperty("fields").EnumerateArray().ToList();
        Assert.Contains(fields, f => f.GetProperty("value").GetString() == SampleContext.AppVersion);
        Assert.Contains(fields, f => f.GetProperty("value").GetString() == SampleContext.OsDescription);
        Assert.Contains(fields, f => f.GetProperty("value").GetString() == SampleContext.RuntimeDescription);
    }

    /// <summary>
    /// A Discord beágyazás leírásmezője 4096 karakternél nagyobbat elutasítana
    /// — a program ezért levágja, mielőtt egyáltalán elküldené.
    /// </summary>
    [Fact]
    public void BuildEmbedJson_TulHosszuLeirastLevagja()
    {
        var longDescription = new string('a', 5000);
        var context = SampleContext with { Description = longDescription };

        var json = DiscordPayloadBuilder.BuildEmbedJson(context);
        using var document = JsonDocument.Parse(json);

        var description = document.RootElement
            .GetProperty("embeds")[0]
            .GetProperty("description")
            .GetString();

        Assert.NotNull(description);
        Assert.True(description!.Length < longDescription.Length);
        Assert.EndsWith("…", description);
    }

    [Fact]
    public void BuildEmbedJson_RovidLeirastNemVagjaLe()
    {
        var json = DiscordPayloadBuilder.BuildEmbedJson(SampleContext);
        using var document = JsonDocument.Parse(json);

        var description = document.RootElement
            .GetProperty("embeds")[0]
            .GetProperty("description")
            .GetString();

        Assert.Equal(SampleContext.Description, description);
    }
}
