using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Search;
using Pilaster.App.Localization;
using Pilaster.App.Services;
using Pilaster.App.ViewModels;
using Pilaster.Core.Settings;
using Wpf.Ui.Controls;

using MenuItem = System.Windows.Controls.MenuItem;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace Pilaster.App.Views;

/// <summary>
/// A beépített szövegszerkesztő (Pilaster Editor) ablaka — több fül,
/// fülönként egy fájl.
/// </summary>
/// <remarks>
/// A szerkesztő motorja az AvalonEdit: onnan jön a virtualizált renderelés, a
/// korlátlan visszavonás, a szintaxiskiemelés és a kereső/csere panel
/// (<c>Ctrl+F</c>/<c>Ctrl+H</c>, regex/kis-nagybetű/egész szó opciókkal).
/// Ez a kódmögöttes köti össze a nézetmodellel, és adja hozzá a
/// sorműveleteket, amiket az AvalonEdit maga nem szállít.
/// </remarks>
public partial class EditorWindow : FluentWindow
{
    private readonly EditorViewModel _viewModel;
    private readonly ISettingsService _settings;
    private EditorDocumentViewModel? _boundDocument;

    public EditorWindow(EditorViewModel viewModel, ISettingsService settings)
    {
        _viewModel = viewModel;
        _settings = settings;
        DataContext = viewModel;

        InitializeComponent();

        SearchPanel.Install(Editor);
        ApplyEditorOptions();

        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(EditorViewModel.ActiveDocument))
            {
                BindActiveDocument();
            }
        };

        viewModel.SaveAsRequested += OnSaveAsRequested;
        viewModel.CloseConfirmationRequested += OnCloseConfirmationRequested;

        Editor.TextArea.Caret.PositionChanged += OnCaretChanged;
        Editor.TextArea.SelectionChanged += OnSelectionChanged;
        Editor.TextArea.OptionChanged += (_, _) => viewModel.IsOverwriteMode = Editor.TextArea.OverstrikeMode;

        PreviewKeyDown += OnEditorPreviewKeyDown;
        Closing += OnWindowClosing;

        BindActiveDocument();
    }

    private void ApplyEditorOptions()
    {
        var current = _settings.Current;

        Editor.FontFamily = ResolveMonospaceFont(current.EditorFontFamily);
        Editor.FontSize = current.EditorFontSize;
        Editor.Options.IndentationSize = current.EditorTabWidth;
        Editor.Options.ConvertTabsToSpaces = current.EditorInsertSpaces;
        Editor.Options.HighlightCurrentLine = true;
        Editor.Options.ShowBoxForControlCharacters = true;
        Editor.Options.EnableRectangularSelection = true;

        // Behúzás-vezetővonalak és zárójel-párosítás: az AvalonEdit ezeket a
        // beállításokat a TextArea rétegein keresztül adja.
        Editor.Options.EnableTextDragDrop = true;
        Editor.Options.AllowScrollBelowDocument = true;
    }

    /// <summary>
    /// A beállított betűkészlet feloldása, telepítettség-ellenőrzéssel.
    /// </summary>
    /// <remarks>
    /// Ez NEM óvatoskodás: egy nem telepített családnév (pl. a „Cascadia Mono",
    /// ami a Windows Terminallal érkezik, nem magával a Windowsszal)
    /// olyan <see cref="FontFamily"/>-t ad, amiből az AvalonEdit
    /// <c>TextView</c>-ja nem tud betűtípust képezni, és a MÉRÉS fázisában
    /// dob <see cref="NullReferenceException"/>-t — vagyis a szerkesztő ablak
    /// megnyitáskor azonnal, folyamatosan hibázik. Mérve: pontosan ez történt
    /// az első futásnál.
    /// </remarks>
    private static FontFamily ResolveMonospaceFont(string requested)
    {
        string[] candidates = [requested, "Cascadia Mono", "Consolas", "Courier New"];

        var installed = Fonts.SystemFontFamilies
            .Select(f => f.Source)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && installed.Contains(candidate))
            {
                return new FontFamily(candidate);
            }
        }

        // Végső esély: a WPF általános monospace álneve, ami mindig feloldható.
        return new FontFamily("Global Monospace");
    }

    /// <summary>Az aktív fül dokumentumának bekötése a szerkesztőbe.</summary>
    private void BindActiveDocument()
    {
        _boundDocument = _viewModel.ActiveDocument;

        if (_boundDocument is null)
        {
            Editor.Document = new ICSharpCode.AvalonEdit.Document.TextDocument();
            Editor.IsReadOnly = true;
            return;
        }

        Editor.Document = _boundDocument.Document;
        Editor.SyntaxHighlighting = _boundDocument.Highlighting;
        Editor.IsReadOnly = _boundDocument.IsReadOnly;

        UpdateCaretLabel();
    }

    private void OnCaretChanged(object? sender, EventArgs e) => UpdateCaretLabel();

    private void UpdateCaretLabel()
    {
        var caret = Editor.TextArea.Caret;
        _viewModel.CaretLabel = string.Create(CultureInfo.InvariantCulture, $"{caret.Line}:{caret.Column}");
    }

    private void OnSelectionChanged(object? sender, EventArgs e) =>
        _viewModel.SelectionLength = Editor.SelectionLength;

    private void OnTabClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is EditorDocumentViewModel document)
        {
            _viewModel.ActiveDocument = document;
        }
    }

    private async void OnOpenClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Multiselect = true };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        foreach (var file in dialog.FileNames)
        {
            await _viewModel.OpenAsync(file);
        }
    }

    private void OnFindClick(object sender, RoutedEventArgs e) =>
        ApplicationCommands.Find.Execute(null, Editor.TextArea);

    private void OnReloadClick(object sender, RoutedEventArgs e) =>
        _ = _viewModel.ActiveDocument?.ReloadAsync();

    /// <summary>Ctrl+G — ugrás a megadott sorra.</summary>
    private void OnGoToLineClick(object sender, RoutedEventArgs e)
    {
        var input = PromptForLine();

        if (input is null || !int.TryParse(input, out var line))
        {
            return;
        }

        line = Math.Clamp(line, 1, Editor.Document.LineCount);
        Editor.ScrollToLine(line);
        Editor.TextArea.Caret.Line = line;
        Editor.TextArea.Caret.Column = 1;
        Editor.TextArea.Caret.BringCaretToView();
        Editor.Focus();
    }

    private string? PromptForLine()
    {
        var box = new System.Windows.Controls.TextBox { Margin = new Thickness(0, 0, 0, 12) };
        var ok = new Wpf.Ui.Controls.Button
        {
            Content = TranslationSource.Instance["Cmd_Ok"],
            Appearance = ControlAppearance.Primary,
            Margin = new Thickness(0, 0, 6, 0),
        };
        var cancel = new Wpf.Ui.Controls.Button { Content = TranslationSource.Instance["Cmd_Cancel"] };

        var buttons = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(box);
        panel.Children.Add(buttons);

        var window = new FluentWindow
        {
            Title = TranslationSource.Instance["Editor_GoToLine"],
            Width = 300,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowBackdropType = WindowBackdropType.Mica,
            Content = panel,
        };

        ok.Click += (_, _) => { window.DialogResult = true; window.Close(); };
        cancel.Click += (_, _) => { window.DialogResult = false; window.Close(); };
        box.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter)
            {
                window.DialogResult = true;
                window.Close();
            }
        };

        window.Loaded += (_, _) => box.Focus();

        return window.ShowDialog() == true ? box.Text : null;
    }

    /// <summary>A kódolás menüje: külön „újranyitás ezzel" és „mentés ezzel".</summary>
    private void OnEncodingClick(object sender, RoutedEventArgs e)
    {
        var menu = new System.Windows.Controls.ContextMenu { PlacementTarget = (UIElement)sender };
        var strings = TranslationSource.Instance;

        var reopen = new MenuItem { Header = strings["Editor_ReopenWith"] };
        var saveWith = new MenuItem { Header = strings["Editor_SaveWith"] };

        foreach (var id in _viewModel.Encodings)
        {
            var reopenItem = new MenuItem { Header = id };
            reopenItem.Click += (_, _) => _viewModel.ReopenWithEncodingCommand.Execute(id);
            reopen.Items.Add(reopenItem);

            var saveItem = new MenuItem { Header = id };
            saveItem.Click += (_, _) => _viewModel.SaveWithEncodingCommand.Execute(id);
            saveWith.Items.Add(saveItem);
        }

        menu.Items.Add(reopen);
        menu.Items.Add(saveWith);
        menu.IsOpen = true;
    }

    private void OnLineEndingClick(object sender, RoutedEventArgs e)
    {
        var menu = new System.Windows.Controls.ContextMenu { PlacementTarget = (UIElement)sender };

        foreach (var kind in _viewModel.LineEndings)
        {
            var item = new MenuItem { Header = kind.ToString().ToUpperInvariant() };
            item.Click += (_, _) => _viewModel.ConvertLineEndingCommand.Execute(kind);
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }

    /// <summary>
    /// A sorműveletek, amiket az AvalonEdit nem szállít: sor duplikálása,
    /// törlése és mozgatása (spec F2).
    /// </summary>
    private void OnEditorPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);

        if (Editor.IsReadOnly && (ctrl || alt))
        {
            return;
        }

        switch (e.Key)
        {
            case Key.S when ctrl && shift:
                e.Handled = true;
                _viewModel.SaveAsCommand.Execute(null);
                break;

            case Key.S when ctrl:
                e.Handled = true;
                _ = _viewModel.SaveCommand.ExecuteAsync(null);
                break;

            case Key.N when ctrl:
                e.Handled = true;
                _viewModel.NewFileCommand.Execute(null);
                break;

            case Key.W when ctrl:
                e.Handled = true;
                _viewModel.CloseDocumentCommand.Execute(null);
                break;

            case Key.G when ctrl:
                e.Handled = true;
                OnGoToLineClick(sender, new RoutedEventArgs());
                break;

            case Key.D when ctrl:
                e.Handled = true;
                DuplicateLine();
                break;

            case Key.K when ctrl && shift:
                e.Handled = true;
                DeleteLine();
                break;

            case Key.Up when alt:
                e.Handled = true;
                MoveLine(-1);
                break;

            case Key.Down when alt:
                e.Handled = true;
                MoveLine(+1);
                break;
        }
    }

    /// <summary>Ctrl+D — az aktuális sor megkettőzése közvetlenül alatta.</summary>
    private void DuplicateLine()
    {
        var line = Editor.Document.GetLineByNumber(Editor.TextArea.Caret.Line);
        var text = Editor.Document.GetText(line.Offset, line.Length);

        Editor.Document.Insert(line.EndOffset, Environment.NewLine + text);
    }

    /// <summary>Ctrl+Shift+K — az aktuális sor törlése, a sorvégével együtt.</summary>
    private void DeleteLine()
    {
        var line = Editor.Document.GetLineByNumber(Editor.TextArea.Caret.Line);

        Editor.Document.Remove(line.Offset, line.TotalLength);
    }

    /// <summary>
    /// Alt+↑/↓ — az aktuális sor mozgatása. A két sor tartalmát cseréljük, nem
    /// a szöveget vágjuk-illesztjük: így egyetlen visszavonási lépés lesz
    /// belőle, és a sorvégek sem sérülnek.
    /// </summary>
    private void MoveLine(int delta)
    {
        var document = Editor.Document;
        var current = Editor.TextArea.Caret.Line;
        var target = current + delta;

        if (target < 1 || target > document.LineCount)
        {
            return;
        }

        var currentLine = document.GetLineByNumber(current);
        var targetLine = document.GetLineByNumber(target);

        var currentText = document.GetText(currentLine.Offset, currentLine.Length);
        var targetText = document.GetText(targetLine.Offset, targetLine.Length);

        using (document.RunUpdate())
        {
            // A KÉSŐBBI sort írjuk elsőnek: így a korábbi eltolásai
            // érvényesek maradnak a második írásig.
            if (delta > 0)
            {
                document.Replace(targetLine.Offset, targetLine.Length, currentText);
                document.Replace(currentLine.Offset, currentLine.Length, targetText);
            }
            else
            {
                document.Replace(currentLine.Offset, currentLine.Length, targetText);
                document.Replace(targetLine.Offset, targetLine.Length, currentText);
            }
        }

        Editor.TextArea.Caret.Line = target;
        Editor.TextArea.Caret.BringCaretToView();
    }

    private void OnSaveAsRequested(object? sender, EditorSaveAsRequest request)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = request.Document.Title,
            Filter = "Minden fájl (*.*)|*.*",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _ = SaveAsCoreAsync(request.Document, dialog.FileName);
    }

    private async Task SaveAsCoreAsync(EditorDocumentViewModel document, string path)
    {
        if (await document.SaveAsAsync(path))
        {
            document.IsReadOnly = false;
            document.ReadOnlyReason = null;
            Editor.IsReadOnly = false;
            _viewModel.StatusMessage = null;
        }
        else
        {
            _viewModel.StatusMessage = TranslationSource.Instance["Editor_SaveFailed"];
        }
    }

    /// <summary>Nem mentett fül bezárása: Mentés / Elvetés / Mégse.</summary>
    private void OnCloseConfirmationRequested(object? sender, EditorCloseRequest request)
    {
        var strings = TranslationSource.Instance;

        var result = MessageBox.Show(
            string.Format(CultureInfo.CurrentCulture, strings["Editor_ConfirmClose"], request.Document.Title),
            strings["Editor_Title"],
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        switch (result)
        {
            case MessageBoxResult.Yes:
                _ = SaveThenCloseAsync(request.Document);
                break;

            case MessageBoxResult.No:
                _viewModel.ForceClose(request.Document);
                break;
        }
    }

    private async Task SaveThenCloseAsync(EditorDocumentViewModel document)
    {
        if (document.FilePath is null)
        {
            OnSaveAsRequested(this, new EditorSaveAsRequest(document));
            return;
        }

        if (await document.SaveAsync())
        {
            _viewModel.ForceClose(document);
        }
        else
        {
            _viewModel.StatusMessage = TranslationSource.Instance["Editor_SaveFailed"];
        }
    }

    /// <summary>Kilépéskor rákérdez, ha van nem mentett fül (spec F2).</summary>
    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_viewModel.HasUnsavedChanges)
        {
            return;
        }

        var strings = TranslationSource.Instance;

        var result = MessageBox.Show(
            strings["Editor_ConfirmExit"],
            strings["Editor_Title"],
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        e.Cancel = result != MessageBoxResult.Yes;
    }
}
