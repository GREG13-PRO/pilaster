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

    /// <summary>
    /// A hibajelentés alapértelmezett — a régi tesztek (és a régi hívók)
    /// szándékosan a paraméter megadása nélkül is fordulnak.
    /// </summary>
    [Fact]
    public void BuildEmbedJson_AlapbanHibajelentesCimket_Kap()
    {
        var json = DiscordPayloadBuilder.BuildEmbedJson(SampleContext);
        using var document = JsonDocument.Parse(json);

        var title = document.RootElement.GetProperty("embeds")[0].GetProperty("title").GetString();

        Assert.Contains("[BUG]", title);
        Assert.DoesNotContain("[ÖTLET]", title);
    }

    /// <summary>
    /// Ötletnél a cím és a szín is eltér a hibajelentésétől — a Discord
    /// csatornán átfutva a kettő elsőre megkülönböztethető legyen.
    /// </summary>
    [Fact]
    public void BuildEmbedJson_OtletCimketEsElteroSzintKap()
    {
        var ideaContext = SampleContext with { IsFeatureIdea = true };

        var bugJson = DiscordPayloadBuilder.BuildEmbedJson(SampleContext);
        var ideaJson = DiscordPayloadBuilder.BuildEmbedJson(ideaContext);

        using var bugDocument = JsonDocument.Parse(bugJson);
        using var ideaDocument = JsonDocument.Parse(ideaJson);

        var ideaTitle = ideaDocument.RootElement.GetProperty("embeds")[0].GetProperty("title").GetString();
        Assert.Contains("[ÖTLET]", ideaTitle);
        Assert.DoesNotContain("[BUG]", ideaTitle);

        var bugColor = bugDocument.RootElement.GetProperty("embeds")[0].GetProperty("color").GetInt32();
        var ideaColor = ideaDocument.RootElement.GetProperty("embeds")[0].GetProperty("color").GetInt32();
        Assert.NotEqual(bugColor, ideaColor);
    }
}
