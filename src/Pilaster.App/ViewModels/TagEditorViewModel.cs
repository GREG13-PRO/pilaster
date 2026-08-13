using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pilaster.App.Services;
using Pilaster.Core.Metadata;

namespace Pilaster.App.ViewModels;

/// <summary>Egy címke sora a Beállítások Címkék szakaszában — átnevezhető, törölhető.</summary>
public sealed partial class TagEditorViewModel : ObservableObject
{
    private readonly FileMetadataService _metadata;
    private readonly bool _loaded;

    public TagEditorViewModel(FileMetadataService metadata, TagDefinition tag)
    {
        _metadata = metadata;
        Id = tag.Id;
        Color = tag.Color;
        Name = tag.Name;
        _loaded = true;
    }

    public string Id { get; }

    public TagColor Color { get; }

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    partial void OnNameChanged(string value)
    {
        if (_loaded && value.Trim().Length > 0)
        {
            _metadata.RenameTag(Id, value.Trim());
        }
    }

    [RelayCommand]
    private void Delete() => _metadata.DeleteTag(Id);
}
