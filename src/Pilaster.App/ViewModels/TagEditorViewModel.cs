using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pilaster.App.Converters;
using Pilaster.App.Services;
using Pilaster.Core.Metadata;

namespace Pilaster.App.ViewModels;

/// <summary>
/// Egy címke sora a Beállítások Címkék szakaszában — átnevezhető, színe
/// módosítható, törölhető.
/// </summary>
public sealed partial class TagEditorViewModel : ObservableObject
{
    private readonly FileMetadataService _metadata;
    private readonly bool _loaded;

    public TagEditorViewModel(FileMetadataService metadata, TagDefinition tag)
    {
        _metadata = metadata;
        Id = tag.Id;
        Color = tag.Color;
        ColorHex = tag.ColorHex;
        Name = tag.Name;
        CustomHexInput = ColorHex ?? string.Empty;
        _loaded = true;
    }

    public string Id { get; }

    /// <summary>A színválasztó rácsának 12 előre definiált színe.</summary>
    public IReadOnlyList<TagColor> Presets => TagPalette.Presets;

    [ObservableProperty]
    public partial TagColor Color { get; set; }

    /// <summary>Egyedi <c>#RRGGBB</c> szín, ha a felhasználó ilyet adott meg; egyébként <c>null</c>.</summary>
    [ObservableProperty]
    public partial string? ColorHex { get; set; }

    /// <summary>Igaz, amíg a színválasztó felugró nyitva van ezen a soron.</summary>
    [ObservableProperty]
    public partial bool IsPickerOpen { get; set; }

    /// <summary>A hex mező tartalma — csak az „Alkalmaz" gomb írja vissza a <see cref="ColorHex"/>-be.</summary>
    [ObservableProperty]
    public partial string CustomHexInput { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    partial void OnNameChanged(string value)
    {
        if (_loaded && value.Trim().Length > 0)
        {
            _metadata.RenameTag(Id, value.Trim());
        }
    }

    /// <summary>Egy paletta-szín választása — az egyedi hexet is törli, hogy tényleg a paletta érvényesüljön.</summary>
    [RelayCommand]
    private void SelectPreset(TagColor color)
    {
        Color = color;
        ColorHex = null;
        CustomHexInput = string.Empty;
        Persist();
        IsPickerOpen = false;
    }

    /// <summary>A hex mezőbe írt szín alkalmazása. Érvénytelen bevitelnél nem történik semmi.</summary>
    [RelayCommand]
    private void ApplyCustomHex()
    {
        if (!AccentColorService.TryParseHex(CustomHexInput, out var parsed))
        {
            return;
        }

        ColorHex = $"#{parsed.R:X2}{parsed.G:X2}{parsed.B:X2}";
        CustomHexInput = ColorHex;
        Persist();
        IsPickerOpen = false;
    }

    [RelayCommand]
    private void TogglePicker() => IsPickerOpen = !IsPickerOpen;

    [RelayCommand]
    private void Delete() => _metadata.DeleteTag(Id);

    private void Persist() => _metadata.SetTagColor(Id, Color, ColorHex);
}
