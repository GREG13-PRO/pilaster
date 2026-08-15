using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pilaster.App.Localization;
using Pilaster.App.Services;
using Pilaster.Core.Settings;

namespace Pilaster.App.ViewModels;

/// <summary>
/// A beépített szerkesztő (Pilaster Editor) állapota: több fül, fülönként egy
/// fájl.
/// </summary>
public sealed partial class EditorViewModel : ObservableObject
{
    private readonly ISettingsService _settings;

    public EditorViewModel(ISettingsService settings)
    {
        _settings = settings;
        Documents = [];
    }

    public ObservableCollection<EditorDocumentViewModel> Documents { get; }

    [ObservableProperty]
    public partial EditorDocumentViewModel? ActiveDocument { get; set; }

    /// <summary>A státuszsor sor:oszlop mezője — a nézet frissíti a kurzor mozgására.</summary>
    [ObservableProperty]
    public partial string CaretLabel { get; set; } = "1:1";

    /// <summary>A kijelölt karakterek száma a státuszsorban.</summary>
    [ObservableProperty]
    public partial int SelectionLength { get; set; }

    /// <summary>Beszúrás (INS) vagy felülírás (OVR) mód.</summary>
    [ObservableProperty]
    public partial bool IsOverwriteMode { get; set; }

    public string InsertModeLabel => IsOverwriteMode ? "OVR" : "INS";

    partial void OnIsOverwriteModeChanged(bool value) => OnPropertyChanged(nameof(InsertModeLabel));

    /// <summary>Rövid visszajelzés a státuszsorban (mentés sikertelen, kódolásváltás stb.).</summary>
    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    /// <summary>A választható kódolások — ugyanaz a készlet, mint a Beállításokban.</summary>
    public IReadOnlyList<string> Encodings => TextFileFormat.SupportedEncodings;

    public IReadOnlyList<LineEndingKind> LineEndings { get; } =
        [LineEndingKind.Crlf, LineEndingKind.Lf, LineEndingKind.Cr];

    /// <summary>A szerkesztő megjelenési beállításai — a nézet ezekre köt.</summary>
    public string FontFamily => _settings.Current.EditorFontFamily;

    public double FontSize => _settings.Current.EditorFontSize;

    public int TabWidth => _settings.Current.EditorTabWidth;

    public bool InsertSpaces => _settings.Current.EditorInsertSpaces;

    public bool WordWrap => _settings.Current.EditorWordWrap;

    public bool ShowLineNumbers => _settings.Current.EditorShowLineNumbers;

    /// <summary>Akkor jelez, ha a nézetnek meg kell erősíttetnie egy nem mentett fül bezárását.</summary>
    public event EventHandler<EditorCloseRequest>? CloseConfirmationRequested;

    /// <summary>Akkor jelez, ha a nézetnek „Mentés másként" párbeszédet kell nyitnia.</summary>
    public event EventHandler<EditorSaveAsRequest>? SaveAsRequested;

    /// <summary>Igaz, amíg egy fájl betöltése tart — ekkor látszik a folyamatjelző.</summary>
    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    /// <summary>A betöltés aránya (0–1).</summary>
    [ObservableProperty]
    public partial double LoadProgress { get; set; }

    /// <summary>A betöltés alatt álló fájl neve — a folyamatjelző felirata.</summary>
    [ObservableProperty]
    public partial string? LoadingFileName { get; set; }

    private CancellationTokenSource? _loadCancellation;

    /// <summary>A folyamatjelző „Mégse" gombja.</summary>
    [RelayCommand]
    private void CancelLoad() => _loadCancellation?.Cancel();

    /// <summary>Egy fájl megnyitása, vagy a már nyitott fülre váltás.</summary>
    /// <remarks>
    /// A betöltés MEGSZAKÍTHATÓ, és a nehéz munka háttérszálon fut (lásd
    /// <see cref="EditorDocumentViewModel.OpenAsync"/>). Megszakításnál nem
    /// jön létre fül — nem marad félig betöltött dokumentum.
    /// </remarks>
    public async Task<bool> OpenAsync(string path)
    {
        if (Documents.FirstOrDefault(d => string.Equals(d.FilePath, path, StringComparison.OrdinalIgnoreCase)) is { } existing)
        {
            ActiveDocument = existing;
            return true;
        }

        // Egy korábbi, még futó betöltést eldobunk: egyszerre egy fájl
        // töltődhet, különben a folyamatjelző két műveletet mutatna egyben.
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();

        var cancellation = new CancellationTokenSource();
        _loadCancellation = cancellation;

        IsLoading = true;
        LoadProgress = 0;
        LoadingFileName = Path.GetFileName(path);
        StatusMessage = null;

        try
        {
            var document = await EditorDocumentViewModel.OpenAsync(
                path,
                forcedEncodingId: null,
                new Progress<double>(value => LoadProgress = value),
                cancellation.Token);

            if (document is null)
            {
                // Bináris tartalom — a szerkesztő nem nyitja meg, a hívó ajánlja
                // fel a hex-előnézetet vagy a külső megnyitást (spec F2).
                StatusMessage = TranslationSource.Instance["Editor_BinaryRefused"];
                return false;
            }

            // A folyamatjelző SZÁNDÉKOSAN még látszik a fül aktiválása alatt is.
            // Az aktiválás átadja a dokumentumot az AvalonEdit nézetének, ami
            // 800 000 sorra felépíti a magasságfát — MÉRVE 196–1343 ms, és
            // kötelezően a UI-szálon. Ez alatt a felület nem rajzol újra; ha a
            // jelzőt előbb kapcsolnánk ki, az utolsó kirajzolt kép egy üres
            // szerkesztő lenne, és a szünet indokolatlan akadásnak látszana.
            Documents.Add(document);
            ActiveDocument = document;
            return true;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = TranslationSource.Instance["Editor_LoadCancelled"];
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = TranslationSource.Instance["Editor_LoadFailed"];
            return false;
        }
        finally
        {
            IsLoading = false;
            LoadingFileName = null;
        }
    }

    [RelayCommand]
    private void NewFile()
    {
        var document = EditorDocumentViewModel.CreateNew(
            _settings.Current.EditorDefaultEncoding,
            ParseLineEnding(_settings.Current.EditorDefaultLineEnding));

        Documents.Add(document);
        ActiveDocument = document;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (ActiveDocument is not { } document)
        {
            return;
        }

        if (document.FilePath is null)
        {
            SaveAs();
            return;
        }

        if (document.IsReadOnly)
        {
            // Írásvédett fájl: nem próbálkozunk csendben, hanem felajánljuk a
            // Mentés másként-et (spec F2).
            StatusMessage = TranslationSource.Instance["Editor_ReadOnlySaveHint"];
            SaveAs();
            return;
        }

        StatusMessage = await document.SaveAsync()
            ? null
            : TranslationSource.Instance["Editor_SaveFailed"];
    }

    [RelayCommand]
    private void SaveAs()
    {
        if (ActiveDocument is { } document)
        {
            SaveAsRequested?.Invoke(this, new EditorSaveAsRequest(document));
        }
    }

    /// <summary>Fül bezárása. Nem mentett módosításnál előbb megerősítést kér.</summary>
    [RelayCommand]
    private void CloseDocument(EditorDocumentViewModel? document)
    {
        document ??= ActiveDocument;

        if (document is null)
        {
            return;
        }

        if (document.IsModified)
        {
            CloseConfirmationRequested?.Invoke(this, new EditorCloseRequest(document));
            return;
        }

        ForceClose(document);
    }

    /// <summary>Bezárás megerősítés nélkül — a nézet hívja, miután a felhasználó döntött.</summary>
    public void ForceClose(EditorDocumentViewModel document)
    {
        var index = Documents.IndexOf(document);

        if (index < 0)
        {
            return;
        }

        Documents.RemoveAt(index);
        document.Dispose();

        ActiveDocument = Documents.Count == 0
            ? null
            : Documents[Math.Clamp(index, 0, Documents.Count - 1)];
    }

    /// <summary>Igaz, ha van nem mentett fül — a nézet kilépéskor ezt kérdezi meg.</summary>
    public bool HasUnsavedChanges => Documents.Any(d => d.IsModified);

    /// <summary>„Újranyitás ezzel a kódolással" — a lemezről olvas újra, a megadott kódolással.</summary>
    [RelayCommand]
    private async Task ReopenWithEncodingAsync(string? encodingId)
    {
        if (ActiveDocument is not { } document || encodingId is null)
        {
            return;
        }

        await document.ReloadAsync(encodingId);
        StatusMessage = null;
    }

    /// <summary>
    /// „Mentés ezzel a kódolással" — csak a cél kódolást állítja át; a
    /// tényleges átalakítás a következő mentéskor történik.
    /// </summary>
    /// <remarks>
    /// Szándékosan KÜLÖN parancs az újranyitástól: az egyik a beolvasás
    /// értelmezését javítja (rosszul felismert kódolás), a másik a kimenetet
    /// változtatja meg. A kettő összemosása némán tönkretenné a fájlt.
    /// </remarks>
    [RelayCommand]
    private void SaveWithEncoding(string? encodingId)
    {
        if (ActiveDocument is not { } document || encodingId is null)
        {
            return;
        }

        document.Encoding = TextFileFormat.Resolve(encodingId);
        document.HasBom = encodingId == "utf-8-bom";
        document.IsModified = true;
    }

    /// <summary>Sorvég-konvertálás — azonnal módosítottá teszi a fület.</summary>
    [RelayCommand]
    private void ConvertLineEnding(LineEndingKind kind)
    {
        if (ActiveDocument is { } document && document.LineEnding != kind)
        {
            document.LineEnding = kind;
            document.IsModified = true;
        }
    }

    private static LineEndingKind ParseLineEnding(string value) => value switch
    {
        "LF" => LineEndingKind.Lf,
        "CR" => LineEndingKind.Cr,
        _ => LineEndingKind.Crlf,
    };
}

/// <summary>Egy nem mentett fül bezárási kérése — a nézet dönt róla.</summary>
public sealed record EditorCloseRequest(EditorDocumentViewModel Document);

/// <summary>„Mentés másként" kérése — a nézet nyitja a fájlpárbeszédet.</summary>
public sealed record EditorSaveAsRequest(EditorDocumentViewModel Document);
