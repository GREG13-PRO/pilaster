namespace Pilaster.App.Diagnostics;

/// <summary>
/// Egy hibabejelentés kontextusa: minden, amit a felhasználó leírásán kívül a
/// Discord üzenet tartalmaz.
/// </summary>
public sealed record BugReportContext(
    string Description,
    string AppVersion,
    string OsDescription,
    string RuntimeDescription,
    DateTimeOffset TimestampUtc,
    bool IsFeatureIdea = false);
