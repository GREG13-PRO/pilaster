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

    public BugReportViewModel(IBugReportService service)
    {
        _service = service;
        IsConfigured = service.IsConfigured;

        if (!IsConfigured)
        {
            // Ez a figyelmeztetés — a küldés utáni visszajelzéssel ellentétben —
            // szándékosan nem tűnik el magától: amíg nincs webhook beállítva,
            // a Küldés gomb is inaktív marad, tehát az állapot végig érvényes.
            StatusMessage = string.Format(
                TranslationSource.Instance["BugReport_NotConfigured"],
                BugReportWebhookResolver.ConfigFilePath);
            StatusIsError = true;
            StatusVisible = true;
        }
    }

    /// <summary>Igaz, ha van beállított Discord webhook.</summary>
    public bool IsConfigured { get; }

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
