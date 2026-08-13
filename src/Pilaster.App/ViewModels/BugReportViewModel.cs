using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pilaster.App.Diagnostics;
using Pilaster.App.Localization;

namespace Pilaster.App.ViewModels;

/// <summary>A Beállítások „Hibabejelentés" szakaszának állapota.</summary>
public sealed partial class BugReportViewModel : ObservableObject
{
    private readonly IBugReportService _service;
    private int _statusGeneration;
    private int _secretClickCount;

    public BugReportViewModel(IBugReportService service)
    {
        _service = service;
        IsConfigured = service.IsConfigured;

        if (!IsConfigured)
        {
            // Ez a figyelmeztetés — a küldés utáni visszajelzéssel ellentétben —
            // szándékosan nem tűnik el magától: amíg nincs bot API beállítva,
            // a Küldés gomb is inaktív marad, tehát az állapot végig érvényes.
            StatusMessage = string.Format(
                TranslationSource.Instance["BugReport_NotConfigured"],
                BugReportApiResolver.ConfigFilePath);
            StatusIsError = true;
            StatusVisible = true;
        }
    }

    /// <summary>Igaz, ha van beállított Discord webhook.</summary>
    public bool IsConfigured { get; }

    /// <summary>A végfelhasználóknak mutatott, publikus visszajelzési cím.</summary>
    public string SupportEmail => ContactInfo.SupportEmail;

    /// <summary>
    /// Igaz, ha a fejlesztői (Discord-integrációs) hibabejelentő panel fel
    /// van oldva.
    /// </summary>
    /// <remarks>
    /// Alapból rejtve: a hétköznapi felhasználó csak az egyszerű e-mail-
    /// címet látja, a Discord-specifikus beállítás (webhook/bot-állapot,
    /// képernyőkép-/napló-csatolás) nem rá tartozik. A szekció fejlécének
    /// 10-szeri kattintása nyitja fel — lásd <see cref="RegisterSecretClick"/>.
    /// </remarks>
    [ObservableProperty]
    public partial bool IsAdminPanelUnlocked { get; set; }

    /// <summary>A Beállítások „Hibabejelentés" szekciófejlécére kattintva hívva.</summary>
    public void RegisterSecretClick()
    {
        if (IsAdminPanelUnlocked)
        {
            return;
        }

        _secretClickCount++;

        if (_secretClickCount >= 10)
        {
            IsAdminPanelUnlocked = true;
        }
    }

    [ObservableProperty]
    public partial string Description { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool AttachScreenshot { get; set; }

    [ObservableProperty]
    public partial bool AttachLog { get; set; } = true;

    /// <summary>
    /// Igaz, ha ez nem hibajelentés, hanem fejlesztési ötlet — a Discord
    /// üzenet ez alapján kap [BUG]/[ÖTLET] címkét és eltérő színt.
    /// </summary>
    [ObservableProperty]
    public partial bool IsFeatureIdea { get; set; }

    [ObservableProperty]
    public partial bool IsSending { get; set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    [ObservableProperty]
    public partial bool StatusIsError { get; set; }

    [ObservableProperty]
    public partial bool StatusVisible { get; set; }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        IsSending = true;

        try
        {
            var screenshot = AttachScreenshot && Application.Current?.MainWindow is { } window
                ? WindowCapture.CapturePng(window)
                : null;

            var logPath = AttachLog ? LogFileLocator.FindLatest() : null;

            var result = await _service.SendAsync(
                new BugReportRequest(Description.Trim(), screenshot, logPath, IsFeatureIdea));

            ShowStatus(
                success: result.Succeeded,
                message: result.Succeeded
                    ? TranslationSource.Instance["BugReport_Success"]
                    : ResolveErrorMessage(result.ErrorMessageKey));

            if (result.Succeeded)
            {
                Description = string.Empty;
                IsFeatureIdea = false;
            }
        }
        finally
        {
            IsSending = false;
        }
    }

    private bool CanSend() => IsConfigured && !IsSending && !string.IsNullOrWhiteSpace(Description);

    partial void OnDescriptionChanged(string value) => SendCommand.NotifyCanExecuteChanged();

    partial void OnIsSendingChanged(bool value) => SendCommand.NotifyCanExecuteChanged();

    private static string ResolveErrorMessage(string? key) =>
        TranslationSource.Instance[key ?? "BugReport_Failure"];

    /// <summary>
    /// A visszajelzés megjelenítése, majd önmagától elhalványítása.
    /// </summary>
    /// <remarks>
    /// Generációs számláló véd az ellen, hogy egy korábbi küldés lejáró
    /// időzítője idő előtt eltüntesse egy újabb küldés friss üzenetét — ugyanaz
    /// a minta, mint a <c>ShellIconImage</c> lusta ikonbetöltésénél.
    /// </remarks>
    private async void ShowStatus(bool success, string message)
    {
        var generation = ++_statusGeneration;

        StatusIsError = !success;
        StatusMessage = message;
        StatusVisible = true;

        await Task.Delay(TimeSpan.FromSeconds(5));

        if (generation == _statusGeneration)
        {
            StatusVisible = false;
        }
    }
}
