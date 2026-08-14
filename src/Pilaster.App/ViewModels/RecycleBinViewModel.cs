using System.Collections.ObjectModel;

// A WPF projektek implicit using-készlete nem tartalmazza a System.IO-t.
using System.IO;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pilaster.App.Localization;
using Pilaster.Shell.Recycle;

namespace Pilaster.App.ViewModels;

/// <summary>Egy sor a Lomtár-ablakban.</summary>
public sealed class RecycledItemViewModel(RecycledItem model)
{
    public RecycledItem Model { get; } = model;

    public string Name => Model.Name;

    public string? OriginalFolder => Model.OriginalFolder;

    public bool IsDirectory => Model.IsDirectory;
}

/// <summary>A Lomtár-ablak állapota: tartalom, visszaállítás, végleges törlés, ürítés.</summary>
public sealed partial class RecycleBinViewModel : ObservableObject
{
    public RecycleBinViewModel() => _ = RefreshAsync();

    public ObservableCollection<RecycledItemViewModel> Items { get; } = [];

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    /// <summary>Igaz, ha a betöltés lezárult és nincs elem — a nézet ekkor „üres" jelzést mutat.</summary>
    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsLoading = true;

        // A Lomtár COM-alapú felsorolása lassú lehet sok elemnél — háttérszálon fut.
        var items = await Task.Run(RecycleBinService.GetItems).ConfigureAwait(true);

        Items.Clear();

        foreach (var item in items.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            Items.Add(new RecycledItemViewModel(item));
        }

        IsEmpty = Items.Count == 0;
        IsLoading = false;
    }

    /// <summary>Elem visszaállítása az eredeti helyére.</summary>
    [RelayCommand]
    private Task RestoreAsync(RecycledItemViewModel? item)
    {
        if (item is null)
        {
            return Task.CompletedTask;
        }

        try
        {
            // NEM Task.Run: a Shell COM műveletek (IFileOperation) STA szálat
            // követelnek meg, a WPF UI-szál STA, egy háttér-Task viszont MTA —
            // onnan hívva ThreadStateException-nel szállna el minden alkalommal.
            RecycleBinService.Restore(item.Model);
            Items.Remove(item);
            IsEmpty = Items.Count == 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException or ThreadStateException)
        {
            StatusMessage = string.Format(TranslationSource.Instance["RecycleBin_RestoreFailed"], item.Name);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Egy elem végleges törlése. A megerősítést a nézet (code-behind) kéri
    /// be, mielőtt ezt a parancsot meghívná — lásd <c>RecycleBinWindow</c>.
    /// </summary>
    [RelayCommand]
    private Task DeleteAsync(RecycledItemViewModel? item)
    {
        if (item is null)
        {
            return Task.CompletedTask;
        }

        try
        {
            // Lásd a RestoreAsync megjegyzését: a Shell COM törlés STA szálat
            // követel, ezért nem Task.Run-nal, hanem közvetlenül a hívó
            // (UI) szálon fut.
            RecycleBinService.Delete(item.Model);
            Items.Remove(item);
            IsEmpty = Items.Count == 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException or ThreadStateException)
        {
            StatusMessage = string.Format(TranslationSource.Instance["RecycleBin_DeleteFailed"], item.Name);
        }

        return Task.CompletedTask;
    }

    /// <summary>A teljes Lomtár ürítése — a megerősítést szintén a nézet kéri be előbb.</summary>
    [RelayCommand]
    private Task EmptyAsync()
    {
        // Lásd a RestoreAsync megjegyzését: STA-t igénylő Shell COM hívás.
        RecycleBinService.Empty();
        Items.Clear();
        IsEmpty = true;
        return Task.CompletedTask;
    }

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }
}
