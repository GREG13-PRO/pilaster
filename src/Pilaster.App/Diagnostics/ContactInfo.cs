namespace Pilaster.App.Diagnostics;

/// <summary>
/// Publikus elérhetőség a végfelhasználóknak — nem titok, ezért (a bot
/// API-kulcsával vagy a frissítő repóazonosítójával ellentétben) nyugodtan
/// egyetlen konstansként élhet a kódban.
/// </summary>
public static class ContactInfo
{
    public const string SupportEmail = "pilaster-explorer@proton.me";
}
