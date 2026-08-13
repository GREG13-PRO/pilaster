using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pilaster.App.Diagnostics;
using Pilaster.App.Localization;

namespace Pilaster.App.ViewModels;

/// <summary>
/// Frissítés-ellenőrzés és -telepítés állapota.
/// </summary>
/// <remarks>
/// Egyetlen, DI-ben szingletonként regisztrált példány — ezt osztja meg a
/// főablak (nem tolakodó sáv) és a Beállítások ablak (kézi „Frissítés
/// keresése" gomb), hogy a kettő állapota mindig összhangban legyen.
/// </remarks>
public sealed partial class UpdateViewModel : ObservableObject
{
    private readonly IUpdateService _updateService;

    private UpdateInfo? _pendingUpdate;
    private string? _downloadedInstallerPath;

    public UpdateViewModel(IUpdateService updateService)
    {
        _updateService = updateService;
    }

    [ObservableProperty]
    public partial bool IsBannerVisible { get; set; }

    [ObservableProperty]
    public partial string BannerText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>A Beállítások „Frissítések" kártyáján megjelenő státuszszöveg; <c>null</c>, ha nincs.</summary>
    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    /// <summary>A megtalált, még nem telepített frissítés száma — a megerősítő párbeszédhez.</summary>
    [ObservableProperty]
    public partial string? PendingVersion { get; set; }

    public string CurrentVersion => AppVersionInfo.Current;

    /// <summary>
    /// Jelzi, hogy a frissítés letöltve és ellenőrizve, a nézetnek meg kell
    /// erősítést kérnie az újraindításhoz — lásd <see cref="BeginInstallAndExit"/>.
    /// </summary>
    public event EventHandler? RestartRequested;

    /// <summary>Csendes ellenőrzés induláskor — sikertelenség vagy naprakész állapot nem jelenik meg sehol.</summary>
    public async Task CheckSilentlyAsync()
    {
        var result = await _updateService.CheckForUpdateAsync().ConfigureAwait(true);

        if (result is { Status: UpdateCheckStatus.UpdateAvailable, Update: { } update })
        {
            ShowAvailableUpdate(update);
        }
    }

    [RelayCommand]
    private async Task CheckNowAsync()
    {
        IsBusy = true;
        StatusMessage = null;

        try
        {
            var result = await _updateService.CheckForUpdateAsync().ConfigureAwait(true);
            var strings = TranslationSource.Instance;

            switch (result.Status)
            {
                case UpdateCheckStatus.UpdateAvailable when result.Update is { } update:
                    ShowAvailableUpdate(update);
                    StatusMessage = BannerText;
                    break;
                case UpdateCheckStatus.UpToDate:
                    StatusMessage = strings["Update_UpToDate"];
                    break;
                case UpdateCheckStatus.RateLimited:
                    StatusMessage = strings["Update_ErrorRateLimit"];
                    break;
                case UpdateCheckStatus.NetworkError:
                    StatusMessage = strings["Update_ErrorNetwork"];
                    break;
                case UpdateCheckStatus.NoAssetForPlatform:
                    StatusMessage = strings["Update_NoAssetForPlatform"];
                    break;
                default:
                    StatusMessage = strings["Update_ErrorGeneric"];
                    break;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void DismissBanner() => IsBannerVisible = false;

    [RelayCommand]
    private async Task InstallUpdateAsync()
    {
        if (_pendingUpdate is not { } update)
        {
            return;
        }

        IsBusy = true;

        try
        {
            var download = await _updateService.DownloadInstallerAsync(update).ConfigureAwait(true);

            if (!download.Succeeded || download.InstallerPath is not { } installerPath)
            {
                StatusMessage = TranslationSource.Instance[download.Status switch
                {
                    UpdateDownloadStatus.ChecksumMismatch => "Update_ChecksumMismatch",
                    UpdateDownloadStatus.NetworkError => "Update_ErrorNetwork",
                    _ => "Update_DownloadFailed",
                }];

                return;
            }

            _downloadedInstallerPath = installerPath;
            RestartRequested?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// A felhasználó megerősítette az újraindítást — a háttér-segédfolyamat
    /// elindítása. A hívónak (a nézetnek) ezután azonnal be kell zárnia az
    /// alkalmazást, hogy a segédfolyamat folytathassa a telepítést.
    /// </summary>
    public void BeginInstallAndExit()
    {
        if (_downloadedInstallerPath is { } path)
        {
            _updateService.BeginInstall(path);
        }
    }

    private void ShowAvailableUpdate(UpdateInfo update)
    {
        _pendingUpdate = update;
        PendingVersion = update.Version;
        BannerText = string.Format(TranslationSource.Instance["Update_Available"], update.Version);
        IsBannerVisible = true;
    }
}
