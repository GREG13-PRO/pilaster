using System.Text.Json;

namespace Pilaster.App.Diagnostics;

/// <summary>
/// A Discord webhook JSON törzsének felépítése.
/// </summary>
/// <remarks>
/// Tiszta, HTTP-től független logika — ezért tesztelhető anélkül, hogy valódi
/// hálózati hívást kellene indítani vagy webhookot konfigurálni.
/// </remarks>
public static class DiscordPayloadBuilder
{
    /// <summary>
    /// A Discord beágyazás leírásmezőjének felső korlátja 4096 karakter; ennél
    /// kisebb küszöbbel dolgozunk, hogy a levágás jelzése is beleférjen.
    /// </summary>
    private const int MaxDescriptionLength = 3800;

    /// <summary>A Pilaster amber márkaszíne — hibabejelentés esetén.</summary>
    private const int BugColor = 0xE9B843;

    /// <summary>
    /// A Discord "blurple" színe — fejlesztési ötletnél, hogy a csatornán
    /// átfutva a szín önmagában is megkülönböztesse a hibától.
    /// </summary>
    private const int IdeaColor = 0x5865F2;

    public static string BuildEmbedJson(BugReportContext context)
    {
        var title = context.IsFeatureIdea
            ? "💡 [ÖTLET] Fejlesztési ötlet — Pilaster"
            : "🐛 [BUG] Hibabejelentés — Pilaster";

        var color = context.IsFeatureIdea ? IdeaColor : BugColor;

        var payload = new
        {
            embeds = new object[]
            {
                new
                {
                    title,
                    description = Truncate(context.Description, MaxDescriptionLength),
                    color,
                    fields = new object[]
                    {
                        new { name = "Verzió", value = context.AppVersion, inline = true },
                        new { name = "Platform", value = context.OsDescription, inline = true },
                        new { name = ".NET", value = context.RuntimeDescription, inline = true },
                    },
                    timestamp = context.TimestampUtc.ToString("O"),
                },
            },
        };

        return JsonSerializer.Serialize(payload);
    }

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : string.Concat(text.AsSpan(0, maxLength), "…");
}
