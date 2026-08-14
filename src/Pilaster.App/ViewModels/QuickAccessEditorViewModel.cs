using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pilaster.App.Localization;
using Pilaster.App.Services;
using Pilaster.Core.Settings;
using Wpf.Ui.Controls;

namespace Pilaster.App.ViewModels;

/// <summary>Egy szerkesztés alatt álló gyorselérés-sor.</summary>
/// <remarks>
/// A szerkesztő MÁSOLATOKON dolgozik, nem az élő bejegyzéseken — enélkül a
/// „Mégse" nem tudná visszavonni a változtatásokat, holott a spec ezt
/// kifejezetten kéri.
/// </remarks>
public sealed partial class QuickAccessRowViewModel : ObservableObject
{
    public QuickAccessRowViewModel(QuickAccessEntry source)
    {
        Id = source.Id;
        Kind = source.Kind;
        LabelKey = source.LabelKey;
        Label = source.Label ?? string.Empty;
        Path = source.Path;
        Icon = source.Icon;
        Color = source.Color;
        Group = source.Group;
        Visible = source.Visible;
    }

    public string Id { get; }

    public QuickAccessEntryKind Kind { get; }

    public string? LabelKey { get; }

    public bool IsSeparator => Kind == QuickAccessEntryKind.Separator;

    [ObservableProperty]
    public partial string Label { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Path { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Icon { get; set; } = "Folder24";

    [ObservableProperty]
    public partial string? Color { get; set; }

    [ObservableProperty]
    public partial string? Group { get; set; }

    [ObservableProperty]
    public partial bool Visible { get; set; } = true;

    /// <summary>A megjelenő felirat: az egyéni név, a fordított kulcs, vagy a mappa neve.</summary>
    public string DisplayLabel =>
        !string.IsNullOrWhiteSpace(Label) ? Label
        : LabelKey is { } key ? TranslationSource.Instance[key]
        : System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(Path));

    partial void OnLabelChanged(string value) => OnPropertyChanged(nameof(DisplayLabel));

    partial void OnPathChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayLabel));
        OnPropertyChanged(nameof(IsMissing));
    }

    /// <summary>Igaz, ha a mappa nem létezik — a sor szürkítve, figyelmeztető ikonnal jelenik meg.</summary>
    public bool IsMissing => !IsSeparator && !string.IsNullOrWhiteSpace(Path) && !Directory.Exists(Path);

    /// <summary>Visszaírás a tárolható modellbe — a „Mentés" gomb hívja.</summary>
    public QuickAccessEntry ToEntry() => new()
    {
        Id = Id,
        Kind = Kind,
        LabelKey = LabelKey,
        Label = string.IsNullOrWhiteSpace(Label) ? null : Label,
        Path = Path,
        Icon = Icon,
        Color = Color,
        Group = string.IsNullOrWhiteSpace(Group) ? null : Group,
        Visible = Visible,
        Pinned = true,
    };
}

/// <summary>A „Gyorselérés szerkesztése…" modális ablak állapota.</summary>
public sealed partial class QuickAccessEditorViewModel : ObservableObject
{
    private readonly QuickAccessService _quickAccess;

    public QuickAccessEditorViewModel(QuickAccessService quickAccess)
    {
        _quickAccess = quickAccess;
        Rows = [.. quickAccess.Pinned.Select(e => new QuickAccessRowViewModel(e))];
        RecentEnabled = quickAccess.RecentEnabled;
        RecentLimit = quickAccess.RecentLimit;
    }

    public ObservableCollection<QuickAccessRowViewModel> Rows { get; }

    /// <summary>A választható ikonok — a WPF-UI készletéből egy praktikus válogatás.</summary>
    public IReadOnlyList<string> IconChoices { get; } =
    [
        "Folder24", "FolderOpen24", "Home24", "Desktop24", "Document24", "ArrowDownload24",
        "Image24", "MusicNote124", "Video24", "Code24", "Briefcase24", "Star24",
        "Heart24", "Archive24", "Cloud24", "Storage24", "Pin24", "Bookmark24",
    ];

    /// <summary>A választható ikonszínek — ugyanaz a paletta, mint a címkéké, plusz az „örökölt" (null).</summary>
    public IReadOnlyList<string> ColorChoices { get; } =
    [
        "#E81123", "#F7630C", "#E09300", "#FFB900", "#76B900", "#109354",
        "#00998A", "#00B7C3", "#0078D4", "#4F4FC4", "#8864C7", "#E3008C",
    ];

    [ObservableProperty]
    public partial QuickAccessRowViewModel? SelectedRow { get; set; }

    [ObservableProperty]
    public partial bool RecentEnabled { get; set; }

    [ObservableProperty]
    public partial int RecentLimit { get; set; }

    /// <summary>Visszajelzés az import/export eredményéről; <c>null</c>, ha nincs mondanivaló.</summary>
    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    /// <summary>Igaz, ha a nézetnek be kell zárnia az ablakot mentéssel.</summary>
    public event EventHandler<bool>? CloseRequested;

    [RelayCommand]
    private void AddFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = TranslationSource.Instance["QuickAccess_AddFolder"],
            Multiselect = true,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        foreach (var path in dialog.FolderNames)
        {
            Rows.Add(new QuickAccessRowViewModel(new QuickAccessEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Path = path,
                Label = System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(path)),
            }));
        }

        SelectedRow = Rows.LastOrDefault();
    }

    /// <summary>Kézzel beírt útvonal hozzáadása — hálózati megosztáshoz, ahol a tallózó lassú lenne.</summary>
    [RelayCommand]
    private void AddTypedPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        Rows.Add(new QuickAccessRowViewModel(new QuickAccessEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            Path = path.Trim(),
            Label = System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(path.Trim())),
        }));

        SelectedRow = Rows[^1];
    }

    [RelayCommand]
    private void AddSeparator()
    {
        var index = SelectedRow is null ? Rows.Count : Rows.IndexOf(SelectedRow) + 1;

        Rows.Insert(index, new QuickAccessRowViewModel(new QuickAccessEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            Kind = QuickAccessEntryKind.Separator,
        }));
    }

    [RelayCommand]
    private void Remove(QuickAccessRowViewModel? row)
    {
        row ??= SelectedRow;

        if (row is not null)
        {
            Rows.Remove(row);
        }
    }

    [RelayCommand]
    private void MoveUp(QuickAccessRowViewModel? row) => Move(row, -1);

    [RelayCommand]
    private void MoveDown(QuickAccessRowViewModel? row) => Move(row, +1);

    private void Move(QuickAccessRowViewModel? row, int delta)
    {
        row ??= SelectedRow;

        if (row is null)
        {
            return;
        }

        var index = Rows.IndexOf(row);
        var target = index + delta;

        if (index < 0 || target < 0 || target >= Rows.Count)
        {
            return;
        }

        Rows.Move(index, target);
        SelectedRow = row;
    }

    /// <summary>Húzásos átrendezés — a nézet hívja a leejtés helyének indexével.</summary>
    public void MoveTo(QuickAccessRowViewModel row, int newIndex)
    {
        var index = Rows.IndexOf(row);

        if (index < 0)
        {
            return;
        }

        Rows.Move(index, Math.Clamp(newIndex, 0, Rows.Count - 1));
        SelectedRow = row;
    }

    [RelayCommand]
    private void ResetDefaults()
    {
        _quickAccess.ResetToDefaults();

        Rows.Clear();

        foreach (var entry in _quickAccess.Pinned)
        {
            Rows.Add(new QuickAccessRowViewModel(entry));
        }

        StatusMessage = TranslationSource.Instance["QuickAccess_ResetDone"];
    }

    [RelayCommand]
    private void Export()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = "pilaster-quickaccess.json",
            Filter = "JSON (*.json)|*.json",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        // Előbb a jelenlegi, még nem mentett szerkesztés kerül a tárolóba,
        // különben az export a képernyőn látottól eltérő állapotot írna ki.
        _quickAccess.ReplacePinned(Rows.Select(r => r.ToEntry()));

        StatusMessage = TranslationSource.Instance[
            _quickAccess.TryExport(dialog.FileName) ? "QuickAccess_ExportDone" : "QuickAccess_ExportFailed"];
    }

    [RelayCommand]
    private void Import()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "JSON (*.json)|*.json" };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (!_quickAccess.TryImport(dialog.FileName))
        {
            StatusMessage = TranslationSource.Instance["QuickAccess_ImportFailed"];
            return;
        }

        Rows.Clear();

        foreach (var entry in _quickAccess.Pinned)
        {
            Rows.Add(new QuickAccessRowViewModel(entry));
        }

        StatusMessage = TranslationSource.Instance["QuickAccess_ImportDone"];
    }

    [RelayCommand]
    private void Save()
    {
        _quickAccess.ReplacePinned(Rows.Select(r => r.ToEntry()));
        _quickAccess.RecentEnabled = RecentEnabled;
        _quickAccess.RecentLimit = RecentLimit;
        _quickAccess.Flush();

        CloseRequested?.Invoke(this, true);
    }

    /// <summary>„Mégse" — a szerkesztés MÁSOLATOKON folyt, ezért itt tényleg nincs mit visszavonni.</summary>
    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, false);

    [RelayCommand]
    private void ClearRecent()
    {
        _quickAccess.ClearRecent();
        StatusMessage = TranslationSource.Instance["QuickAccess_RecentCleared"];
    }

    /// <summary>Ikonnév feloldása a nézet számára; ismeretlen névnél általános mappaikon.</summary>
    public static SymbolRegular ParseIcon(string name) =>
        Enum.TryParse<SymbolRegular>(name, ignoreCase: true, out var parsed) ? parsed : SymbolRegular.Folder24;
}
