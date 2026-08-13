using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Pilaster.Core;
using Pilaster.Core.Settings;

namespace Pilaster.App.ViewModels;

/// <summary>Egy gyorsgomb szerkesztője a beállításokban.</summary>
public sealed partial class QuickActionEditorViewModel : ObservableObject
{
    private readonly QuickActionSettings _model;
    private readonly Action _onChanged;

    public QuickActionEditorViewModel(string header, QuickActionSettings model, Action onChanged)
    {
        Header = header;
        _model = model;
        _onChanged = onChanged;

        _label = model.Label;
        _kind = model.Kind;
        _extension = model.Extension;
        _nameTemplate = model.NameTemplate;
        _useFixedPath = model.Target == QuickActionTarget.FixedPath;
        _fixedPath = model.FixedPath ?? string.Empty;
    }

    /// <summary>A szakasz címe, pl. „Első gomb".</summary>
    public string Header { get; }

    [ObservableProperty]
    private string _label;

    /// <summary>
    /// Mit hoz létre a gomb. Közvetlenül a felsorolás, nem <c>bool</c>: így a
    /// rádiógombok kikapcsolása nem tud rossz értéket visszaírni.
    /// </summary>
    [ObservableProperty]
    private QuickActionKind _kind;

    /// <summary>Csak megjelenítéshez: a kiterjesztés-mező láthatóságához.</summary>
    public bool IsFolder => Kind == QuickActionKind.Folder;

    [ObservableProperty]
    private string _extension;

    [ObservableProperty]
    private string _nameTemplate;

    [ObservableProperty]
    private bool _useFixedPath;

    [ObservableProperty]
    private string _fixedPath;

    /// <summary>
    /// Mit adna most a sablon. Élőben frissül gépelés közben, hogy a
    /// felhasználónak ne kelljen kipróbálnia a gombot ahhoz, hogy lássa,
    /// jól írta-e a helyőrzőt.
    /// </summary>
    public string Preview
    {
        get
        {
            // Teljes névvel, mert a NameTemplate tulajdonság elfedi az azonos
            // nevű statikus osztályt.
            var expanded = Core.Templates.NameTemplate.Expand(SafeTemplate(NameTemplate));

            return IsFolder || string.IsNullOrWhiteSpace(Extension)
                ? expanded
                : $"{expanded}.{Extension.TrimStart('.')}";
        }
    }

    /// <summary>Üres sablonnál se legyen üres az előnézet.</summary>
    private static string SafeTemplate(string template) =>
        string.IsNullOrWhiteSpace(template) ? "Névtelen" : template;

    partial void OnLabelChanged(string value)
    {
        _model.Label = value;
        _onChanged();
    }

    partial void OnKindChanged(QuickActionKind value)
    {
        _model.Kind = value;
        OnPropertyChanged(nameof(IsFolder));
        OnPropertyChanged(nameof(Preview));
        _onChanged();
    }

    partial void OnExtensionChanged(string value)
    {
        _model.Extension = value;
        OnPropertyChanged(nameof(Preview));
        _onChanged();
    }

    partial void OnNameTemplateChanged(string value)
    {
        _model.NameTemplate = value;
        OnPropertyChanged(nameof(Preview));
        _onChanged();
    }

    partial void OnUseFixedPathChanged(bool value)
    {
        _model.Target = value ? QuickActionTarget.FixedPath : QuickActionTarget.CurrentFolder;
        _onChanged();
    }

    partial void OnFixedPathChanged(string value)
    {
        _model.FixedPath = value;
        _onChanged();
    }

    [RelayCommand]
    private void BrowseFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = Header,
            InitialDirectory = System.IO.Directory.Exists(FixedPath) ? FixedPath : null,
        };

        if (dialog.ShowDialog() == true)
        {
            FixedPath = dialog.FolderName;
            UseFixedPath = true;
        }
    }
}
