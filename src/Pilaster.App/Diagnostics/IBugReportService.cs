namespace Pilaster.App.Diagnostics;

/// <summary>Egy elküldendő hibabejelentés.</summary>
/// <param name="Description">A felhasználó leírása a hibáról vagy ötletről.</param>
/// <param name="Screenshot">PNG-kódolt képernyőkép, vagy <c>null</c>.</param>
/// <param name="LogFilePath">A csatolandó naplófájl útvonala, vagy <c>null</c>.</param>
/// <param name="IsFeatureIdea">
/// Igaz, ha ez nem hibajelentés, hanem fejlesztési ötlet — a Discord üzenet
/// címkéje és színe ez alapján tér el.
/// </param>
public sealed record BugReportRequest(
    string Description,
    byte[]? Screenshot,
    string? LogFilePath,
    bool IsFeatureIdea = false);

/// <summary>Egy hibabejelentés küldésének eredménye.</summary>
/// <param name="Succeeded">Sikerült-e a küldés.</param>
/// <param name="ErrorMessageKey">
/// Sikertelenség esetén egy fordítási kulcs a felhasználónak mutatandó
/// üzenethez; siker esetén <c>null</c>.
/// </param>
public readonly record struct BugReportResult(bool Succeeded, string? ErrorMessageKey);

/// <summary>Hibabejelentés küldése egy külső csatornán (jelenleg Discord webhook).</summary>
public interface IBugReportService
{
    /// <summary>Igaz, ha van beállított webhook — enélkül a Küldés gomb inaktív.</summary>
    bool IsConfigured { get; }

    Task<BugReportResult> SendAsync(BugReportRequest request, CancellationToken cancellationToken = default);
}
