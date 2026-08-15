using System.IO;
using System.Text;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;
using Pilaster.App.Localization;
using Pilaster.App.Services;

namespace Pilaster.App.ViewModels;

/// <summary>
/// Egy megnyitott fájl a beépített szerkesztőben — egy fül állapota.
/// </summary>
/// <remarks>
/// <para>
/// A szöveget az AvalonEdit saját <see cref="TextDocument"/>-je tartja: azon
/// keresztül működik a virtualizált renderelés, a korlátlan visszavonás és a
/// szintaxiskiemelés. A nézetmodell ezt a dokumentumot birtokolja, és köré
/// szervezi a fájl-szintű állapotot (kódolás, sorvég, módosítottság).
/// </para>
/// <para>
/// FONTOS: a <see cref="TextDocument"/> SZÁLHOZ KÖTÖTT (a WPF
/// <c>DispatcherObject</c>-jeihez hasonlóan). Egy háttérszálon létrehozott
/// dokumentumot a renderelés <c>NullReferenceException</c>-nel utasít vissza —
/// mérve, pontosan ez történt a szerkesztő első változatában.
/// </para>
/// <para>
/// A megoldás NEM az, hogy mindent a UI-szálon csinálunk: egy 122,7 MB-os
/// naplófájlnál a dokumentum felépítése önmagában 1117 ms, ami látható fagyás.
/// Az AvalonEdit szabályos utat ad rá: a létrehozó háttérszál a
/// <c>SetOwnerThread(null)</c> hívással ELENGEDI a dokumentumot, majd a
/// UI-szál a <c>SetOwnerThread(Thread.CurrentThread)</c>-del átveszi. Így a
/// nehéz munka háttérben fut, a tulajdonjog viszont a végén a UI-szálé lesz.
/// A dokumentumot ÉRINTŐ minden későbbi művelet (szöveg írása, újratöltés)
/// ezért továbbra is UI-szálon marad.
/// </para>
/// </remarks>
public sealed partial class EditorDocumentViewModel : ObservableObject, IDisposable
{
    private FileSystemWatcher? _watcher;
    private bool _suppressDirty;

    private EditorDocumentViewModel(TextDocument document)
    {
        Document = document;

        // A dokumentum minden változása „piszkossá" teszi a fület — az
        // AvalonEdit saját UndoStack-je adja a korlátlan visszavonást.
        Document.TextChanged += (_, _) =>
        {
            if (!_suppressDirty)
            {
                IsModified = true;
            }
        };
    }

    public TextDocument Document { get; }

    /// <summary>A fájl teljes útvonala; <c>null</c> egy még sosem mentett új fájlnál.</summary>
    [ObservableProperty]
    public partial string? FilePath { get; set; }

    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsModified { get; set; }

    partial void OnIsModifiedChanged(bool value) => OnPropertyChanged(nameof(TabTitle));

    partial void OnTitleChanged(string value) => OnPropertyChanged(nameof(TabTitle));

    /// <summary>A fülön megjelenő cím — módosított fájlnál pöttyel.</summary>
    public string TabTitle => IsModified ? "● " + Title : Title;

    /// <summary>Írásvédett fájl: a szerkesztő bannerrel nyitja, és mentésnél felajánlja a Mentés másként-et.</summary>
    [ObservableProperty]
    public partial bool IsReadOnly { get; set; }

    /// <summary>Miért csak olvasható — a banner ezt írja ki. <c>null</c>, ha szerkeszthető.</summary>
    [ObservableProperty]
    public partial string? ReadOnlyReason { get; set; }

    /// <summary>Igaz, ha a fájl a lemezen megváltozott, amíg nyitva volt.</summary>
    [ObservableProperty]
    public partial bool HasExternalChange { get; set; }

    [ObservableProperty]
    public partial Encoding Encoding { get; set; } = new UTF8Encoding(false);

    [ObservableProperty]
    public partial bool HasBom { get; set; }

    [ObservableProperty]
    public partial LineEndingKind LineEnding { get; set; } = LineEndingKind.Crlf;

    /// <summary>A státuszsorban megjelenő kódolás-azonosító.</summary>
    public string EncodingLabel => TextFileFormat.Describe(Encoding, HasBom);

    partial void OnEncodingChanged(Encoding value) => OnPropertyChanged(nameof(EncodingLabel));

    partial void OnHasBomChanged(bool value) => OnPropertyChanged(nameof(EncodingLabel));

    /// <summary>A szintaxiskiemelés definíciója; <c>null</c> sima szövegnél.</summary>
    [ObservableProperty]
    public partial IHighlightingDefinition? Highlighting { get; set; }

    /// <summary>A státuszsor nyelv-mezője.</summary>
    public string LanguageLabel => Highlighting?.Name ?? TranslationSource.Instance["Editor_PlainText"];

    partial void OnHighlightingChanged(IHighlightingDefinition? value) => OnPropertyChanged(nameof(LanguageLabel));

    /// <summary>Új, üres dokumentum.</summary>
    public static EditorDocumentViewModel CreateNew(string defaultEncodingId, LineEndingKind lineEnding) =>
        new(new TextDocument())
        {
            Title = TranslationSource.Instance["Editor_Untitled"],
            Encoding = TextFileFormat.Resolve(defaultEncodingId),
            HasBom = defaultEncodingId == "utf-8-bom",
            LineEnding = lineEnding,
        };

    /// <summary>
    /// Egy fájl megnyitása.
    /// </summary>
    /// <returns>A dokumentum, vagy <c>null</c>, ha a fájl bináris (ilyet nem nyitunk meg).</returns>
    public static async Task<EditorDocumentViewModel?> OpenAsync(
        string path,
        string? forcedEncodingId = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // A UI-szál dispatchere, MÉG a háttérmunka előtt eltárolva. A
        // dokumentum tulajdonjogát a végén kifejezetten ENNEK a szálnak adjuk,
        // nem a „véletlenül épp aktuális" szálnak: egyetlen
        // ConfigureAwait(false) a köztes await-eken elejtené a UI-kontextust,
        // és a dokumentum egy szálpool-szálhoz tartozna. MÉRVE: pontosan ez
        // hozta vissza a renderelés NullReferenceException-jét.
        var uiDispatcher = Dispatcher.CurrentDispatcher;

        // A bináris-szondázás is fájl-I/O — háttérszálra megy, hogy egy lassú
        // hálózati meghajtón se akassza meg a felületet.
        if (await Task.Run(() => TextFileFormat.LooksBinary(path), cancellationToken).ConfigureAwait(true))
        {
            return null;
        }

        var info = new FileInfo(path);

        // A NEHÉZ MUNKA HÁTTÉRSZÁLON. MÉRVE egy 122,7 MB-os naplófájlon: a
        // beolvasás+dekódolás 1137 ms, a TextDocument felépítése további
        // 1117 ms — ez utóbbi korábban a UI-szálon futott, és önmagában több
        // mint egy másodperces fagyást okozott.
        //
        // A TextDocument szálhoz kötött, de az AvalonEdit ad rá szabályos utat:
        // a létrehozó szál a SetOwnerThread(null) hívással ELENGEDI, majd a
        // UI-szál átveszi. Enélkül a dokumentum a háttérszálhoz tartozna, és a
        // renderelés NullReferenceException-nel bukna (ez volt a korábbi hiba).
        var (content, textDocument) = await Task.Run(
            async () =>
            {
                var read = await TextFileFormat
                    .ReadAsync(path, forcedEncodingId, progress, cancellationToken)
                    .ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();

                var created = new TextDocument(read.Text);
                created.SetOwnerThread(null);

                return (read, created);
            },
            cancellationToken).ConfigureAwait(true);

        cancellationToken.ThrowIfCancellationRequested();

        // A tulajdonjogot a UI szála veszi át. Gazdátlan dokumentumnál ez
        // bármelyik szálról megtehető (az AvalonEdit csak akkor ellenőriz, ha
        // van tulajdonos), így a hívás akkor is helyes marad, ha a folytatás
        // valamiért nem a UI-szálon futna.
        textDocument.SetOwnerThread(uiDispatcher.Thread);

        var document = new EditorDocumentViewModel(textDocument)
        {
            FilePath = path,
            Title = Path.GetFileName(path),
            Encoding = content.Encoding,
            HasBom = content.HasBom,
            LineEnding = content.LineEnding,
            Highlighting = ResolveHighlighting(path),
        };

        // Nagy fájl: 50 MB fölött csak olvasható, hogy egy véletlen
        // szerkesztés ne indítson el egy percekig tartó mentést.
        if (info.Length > TextFileFormat.ReadOnlyThresholdBytes)
        {
            document.IsReadOnly = true;
            document.ReadOnlyReason = TranslationSource.Instance["Editor_TooLarge"];
        }
        else if (info.IsReadOnly)
        {
            document.IsReadOnly = true;
            document.ReadOnlyReason = TranslationSource.Instance["Editor_FileReadOnly"];
        }

        document.IsModified = false;
        document.StartWatching();

        return document;
    }

    /// <summary>
    /// A szintaxiskiemelés kiválasztása kiterjesztés alapján.
    /// </summary>
    /// <remarks>
    /// Az AvalonEdit beépített készlete a legtöbb nyelvet fedi. A hiányzókat
    /// (<c>.sk</c>, <c>.yml/.yaml</c>, <c>.ini/.cfg/.conf/.properties</c>,
    /// <c>.log</c>, <c>.ts</c>) egy közeli rokon definícióra képezzük — ez
    /// vizuálisan lényegesen jobb, mint a kiemelés nélküli sima szöveg, és nem
    /// igényel saját <c>.xshd</c> karbantartását.
    /// </remarks>
    private static IHighlightingDefinition? ResolveHighlighting(string path)
    {
        HighlightingRegistry.EnsureRegistered();

        var extension = Path.GetExtension(path).ToLowerInvariant();
        var manager = HighlightingManager.Instance;

        // Kiterjesztések, amikre van közeli rokon definíció, de a nevük nem
        // egyezik. A .yml/.yaml/.sk és az .ini-család saját definíciót kap
        // (lásd HighlightingRegistry), ezek a szokásos úton oldódnak fel.
        var alias = extension switch
        {
            ".ts" => "JavaScript",
            ".sh" or ".bash" => "PowerShell",
            _ => null,
        };

        if (alias is not null && manager.GetDefinition(alias) is { } aliased)
        {
            return aliased;
        }

        return extension.Length > 1 ? manager.GetDefinitionByExtension(extension) : null;
    }

    /// <summary>Mentés a jelenlegi útvonalra. Hamis, ha nincs útvonal vagy az írás nem sikerült.</summary>
    public async Task<bool> SaveAsync()
    {
        if (FilePath is not { } path)
        {
            return false;
        }

        return await SaveAsAsync(path);
    }

    /// <summary>Mentés a megadott útvonalra, atomi cserével.</summary>
    public async Task<bool> SaveAsAsync(string path)
    {
        // A saját írásunk is kiváltaná a fájlfigyelőt — a mentés idejére
        // leállítjuk, különben minden mentés után „külső változás" jelenne meg.
        StopWatching();

        try
        {
            await TextFileFormat.WriteAsync(path, Document.Text, Encoding, LineEnding);

            FilePath = path;
            Title = Path.GetFileName(path);
            IsModified = false;
            HasExternalChange = false;
            Highlighting = ResolveHighlighting(path);

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
        finally
        {
            StartWatching();
        }
    }

    /// <summary>Újraolvasás a lemezről — a külső változás bannerének „Újratöltés" gombja.</summary>
    public async Task ReloadAsync(string? forcedEncodingId = null)
    {
        if (FilePath is not { } path || !File.Exists(path))
        {
            return;
        }

        // Az újraolvasás és a dekódolás háttérszálon fut, akárcsak a
        // megnyitásnál — a szöveg beállítása viszont a dokumentumot érinti,
        // tehát marad a tulajdonos (UI) szálon.
        var content = await Task.Run(() => TextFileFormat.ReadAsync(path, forcedEncodingId))
            .ConfigureAwait(true);

        // A visszatöltés nem „szerkesztés": a piszkos jelzést el kell nyomni,
        // különben egy újratöltött, érintetlen fájl módosítottnak látszana.
        _suppressDirty = true;

        try
        {
            Document.Text = content.Text;
        }
        finally
        {
            _suppressDirty = false;
        }

        Encoding = content.Encoding;
        HasBom = content.HasBom;
        LineEnding = content.LineEnding;
        IsModified = false;
        HasExternalChange = false;
    }

    /// <summary>
    /// A fájl figyelése: ha a lemezen módosul, a szerkesztő jelzi és
    /// felajánlja az újratöltést (spec F2).
    /// </summary>
    private void StartWatching()
    {
        if (FilePath is not { } path || Path.GetDirectoryName(path) is not { Length: > 0 } directory)
        {
            return;
        }

        try
        {
            _watcher = new FileSystemWatcher(directory, Path.GetFileName(path))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };

            _watcher.Changed += (_, _) => OnUi(() => HasExternalChange = true);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            // Hálózati vagy különleges mappa, ahol a figyelés nem támogatott —
            // a szerkesztés e nélkül is működik, csak nem szól a változásról.
            _watcher = null;
        }
    }

    private void StopWatching()
    {
        _watcher?.Dispose();
        _watcher = null;
    }

    private static void OnUi(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            _ = dispatcher.BeginInvoke(action);
        }
    }

    public void Dispose() => StopWatching();
}
