using CommunityToolkit.Mvvm.ComponentModel;
using Pilaster.App.Localization;
using Pilaster.App.Services;
using Pilaster.Shell.Network;

namespace Pilaster.App.ViewModels;

/// <summary>
/// A „Felhő meghajtó hozzáadása" ablak állapota (spec: NextCloud-támogatás).
/// </summary>
/// <remarks>
/// A jelszó SZÁNDÉKOSAN nem élő itt bekötött tulajdonságként — a
/// <see cref="AddCloudDriveWindow"/> kódmögöttese a <c>PasswordBox.Password</c>-t
/// közvetlenül olvassa ki kattintáskor, és paraméterként adja át
/// <see cref="ConnectAsync"/>-nek, hogy ne éljen fölöslegesen sokáig egy
/// megfigyelhető tulajdonságban.
/// </remarks>
public sealed partial class AddCloudDriveViewModel : ObservableObject
{
    private readonly CloudDriveService _cloudDrives;

    public AddCloudDriveViewModel(CloudDriveService cloudDrives)
    {
        _cloudDrives = cloudDrives;
    }

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ServerUrl { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Username { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool RememberCredentials { get; set; } = true;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    /// <summary>Sikeres kapcsolódás és mentés után tüzel — a View erre zárja be az ablakot.</summary>
    public event Action? Connected;

    public async Task ConnectAsync(string password)
    {
        var strings = TranslationSource.Instance;
        ErrorMessage = null;

        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorMessage = strings["CloudDrive_NameRequired"];
            return;
        }

        if (!WebDavConnector.TryBuildUncPath(ServerUrl, out _))
        {
            ErrorMessage = strings["CloudDrive_InvalidUrl"];
            return;
        }

        IsBusy = true;

        try
        {
            var serverUrl = ServerUrl.Trim();
            var username = Username.Trim();
            var remember = RememberCredentials;

            // A WNetAddConnection2 blokkoló hálózati hívás — háttérszálon fut,
            // hogy az ablak a kapcsolódás alatt is válaszoljon (pl. Mégse).
            var result = await Task.Run(() => WebDavConnector.Connect(serverUrl, username, password, remember));

            if (result.Outcome != WebDavConnectOutcome.Succeeded || result.UncPath is null)
            {
                ErrorMessage = result.Outcome == WebDavConnectOutcome.InvalidUrl
                    ? strings["CloudDrive_InvalidUrl"]
                    : strings["CloudDrive_ConnectFailed"];
                return;
            }

            _cloudDrives.Add(Name.Trim(), serverUrl, result.UncPath);
            Connected?.Invoke();
        }
        finally
        {
            IsBusy = false;
        }
    }
}
