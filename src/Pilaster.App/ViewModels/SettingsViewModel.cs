using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pilaster.App.Diagnostics;
using Pilaster.App.Localization;
using Pilaster.App.Services;
using Pilaster.Core.Metadata;
using Pilaster.Core.Settings;

// Lásd a ThemeService-ben: a .NET 10-es WPF saját ThemeMode típusa ütközne.
using ThemeMode = Pilaster.Core.Settings.ThemeMode;

namespace Pilaster.App.ViewModels;

/// <summary>Egy választható nyelv a legördülőben.</summary>
/// <param name="Code">Kultúrakód, vagy <c>null</c> a rendszernyelv követéséhez.</param>
/// <param name="DisplayName">A nyelv saját nevén — így az is megtalálja, aki épp nem érti a felület nyelvét.</param>
public sealed record LanguageOption(string? Code, string DisplayName);

/// <summary>A Beállítások ablak állapota.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly ThemeService _theme;
    private readonly AccentColorService _accent;
    private readonly AnimationService _animations;
    private readonly ShellIntegrationCoordinator _shellIntegration;
    private readonly GlassEffectService _glass;
    private readonly FileMetadataService _metadata;
    private readonly ShellCrashGuard _shellCrashGuard;

    /// <summary>
    /// A saját futtatható fájl útvonala — ezt írjuk a registry „open"
    /// parancsába és a jobbklikk-menü bejegyzésbe.
    /// </summary>
    private static readonly string ExecutablePath = Environment.ProcessPath ?? Environment.GetCommandLineArgs()[0];

    /// <summary>
    /// Igaz, amíg a konstruktor tölti a mezőket. Enélkül a kezdeti értékek
    /// beállítása azonnal mentést és témaváltást váltana ki.
    /// </summary>
    private readonly bool _loaded;

    public SettingsViewModel(
        ISettingsService settings,
        ThemeService theme,
        AccentColorService accent,
        AnimationService animations,
        ShellIntegrationCoordinator shellIntegration,
        GlassEffectService glass,
        FileMetadataService metadata,
        IBugReportService bugReportService,
        UpdateViewModel updates,
        ShellCrashGuard shellCrashGuard)
    {
        _settings = settings;
        _theme = theme;
        _accent = accent;
        _animations = animations;
        _shellIntegration = shellIntegration;
        _glass = glass;
        _metadata = metadata;
        _shellCrashGuard = shellCrashGuard;
        Updates = updates;

        var current = settings.Current;

        _selectedTheme = current.Theme;
        _selectedAnimationLevel = _animations.Current;
        _liquidGlassEnabled = current.LiquidGlassEnabled;
        _selectedKeymap = current.Keymap;
        _externalEditorPath = current.ExternalEditorPath;

        _restoreSession = current.RestoreSession;
        _singleInstance = current.SingleInstance;
        _checkForUpdates = current.CheckForUpdates;
        _startupFolder = current.StartupFolder;
        _dualPaneRowStriping = current.DualPaneRowStriping;
        _density = current.Density;
        _showSystemItems = current.ShowSystemItems;
        _showExtensions = current.ShowExtensions;
        _foldersFirst = current.FoldersFirst;
        _binarySizeUnits = current.BinarySizeUnits;
        _shellExtensionsEnabled = current.ShellExtensionsEnabled;
        _shellMenuTimeoutMs = current.ShellMenuTimeoutMs;
        _shellItemsInOwnSection = current.ShellItemsInOwnSection;
        _shellBlacklistText = string.Join(Environment.NewLine, current.ShellHandlerBlacklist);
        _editorFontFamily = current.EditorFontFamily;
        _editorFontSize = current.EditorFontSize;
        _editorTabWidth = current.EditorTabWidth;
        _editorInsertSpaces = current.EditorInsertSpaces;
        _editorWordWrap = current.EditorWordWrap;
        _editorShowLineNumbers = current.EditorShowLineNumbers;
        _editorDefaultEncoding = current.EditorDefaultEncoding;
        _editorDefaultLineEnding = current.EditorDefaultLineEnding;
        _editorInSeparateWindow = current.EditorInSeparateWindow;
        _externalTerminalPath = current.ExternalTerminalPath;
        _logLevel = current.LogLevel;

        _selectedCategory = Categories[0];

        _folderOpenRedirectEnabled = current.ShellIntegration.FolderOpenRedirectEnabled;
        _winERedirectEnabled = current.ShellIntegration.WinERedirectEnabled;
        _contextMenuEntryEnabled = current.ShellIntegration.ContextMenuEntryEnabled;
        _selectedLanguage = Languages.FirstOrDefault(l => l.Code == current.Language) ?? Languages[0];

        _useSystemAccent = _accent.IsSystemAccent;
        _currentAccentColor = _accent.CurrentColor;
        _accentHexInput = ColorToHex(_currentAccentColor);

        QuickActions =
        [
            new QuickActionEditorViewModel("QuickAction_First", current.QuickAction1, OnQuickActionChanged),
            new QuickActionEditorViewModel("QuickAction_Second", current.QuickAction2, OnQuickActionChanged),
        ];

        Tags = new ObservableCollection<TagEditorViewModel>(
            _metadata.Tags.Select(t => new TagEditorViewModel(_metadata, t)));

        BugReport = new BugReportViewModel(bugReportService);

        _loaded = true;
    }

    /// <summary>A „Hibabejelentés" szakasz állapota.</summary>
    public BugReportViewModel BugReport { get; }

    // ================= F6: kategóriák, keresés, mélyhivatkozás =================

    /// <summary>A bal oldali kategórialista.</summary>
    public IReadOnlyList<SettingsCategoryViewModel> Categories { get; } = SettingsCatalog.BuildCategories();

    private readonly IReadOnlyList<SettingsDescriptor> _descriptors = SettingsCatalog.BuildDescriptors();

    [ObservableProperty]
    private SettingsCategoryViewModel? _selectedCategory;

    /// <summary>A felső keresőmező tartalma.</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>Az aktuális keresés találatai — üres keresésnél üres lista.</summary>
    [ObservableProperty]
    private IReadOnlyList<SettingsDescriptor> _searchResults = [];

    /// <summary>Igaz, amíg keresés van folyamatban — ekkor a találatlista látszik a kategória tartalma helyett.</summary>
    public bool IsSearching => !string.IsNullOrWhiteSpace(SearchText);

    /// <summary>
    /// A nézetnek jelez, hogy egy beállításhoz kell ugrania és felvillantania.
    /// Ugyanez a belépési pont a mélyhivatkozásé is (pl. egy hibaüzenetből).
    /// </summary>
    public event EventHandler<string>? NavigateToSettingRequested;

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(IsSearching));

        SearchResults = string.IsNullOrWhiteSpace(value)
            ? []
            : [.. _descriptors.Where(d => d.Matches(value.Trim()))];

        // Keresés közben csak azok a kategóriák maradnak a listában, amikben
        // van találat — így a bal oldal is szűkül, nem csak a jobb.
        var hits = SearchResults.Select(r => r.CategoryId).ToHashSet(StringComparer.Ordinal);

        foreach (var category in Categories)
        {
            category.IsVisible = !IsSearching || hits.Contains(category.Id);
        }
    }

    /// <summary>Egy találatra kattintva: a kategóriájára vált, majd odaugrik és felvillantja.</summary>
    [RelayCommand]
    private void GoToSetting(SettingsDescriptor? descriptor)
    {
        if (descriptor is null)
        {
            return;
        }

        SearchText = string.Empty;
        SelectedCategory = Categories.FirstOrDefault(c => c.Id == descriptor.CategoryId) ?? SelectedCategory;

        NavigateToSettingRequested?.Invoke(this, descriptor.Id);
    }

    /// <summary>
    /// Mélyhivatkozás azonosító alapján — más helyről (pl. hibaüzenetből)
    /// közvetlenül egy beállításhoz lehet ugrani vele.
    /// </summary>
    public void NavigateTo(string settingId)
    {
        if (_descriptors.FirstOrDefault(d => d.Id == settingId) is { } descriptor)
        {
            GoToSetting(descriptor);
        }
    }

    /// <summary>Az aktuális kategória beállításainak visszaállítása alapértelmezettre (spec F6).</summary>
    [RelayCommand]
    private void ResetCategory()
    {
        if (SelectedCategory is not { } category)
        {
            return;
        }

        var defaults = new AppSettings();
        var current = _settings.Current;

        switch (category.Id)
        {
            case SettingsCatalog.General:
                current.Language = defaults.Language;
                current.StartupFolder = defaults.StartupFolder;
                current.RestoreSession = defaults.RestoreSession;
                current.SingleInstance = defaults.SingleInstance;
                current.CheckForUpdates = defaults.CheckForUpdates;
                SelectedLanguage = Languages[0];
                RestoreSession = defaults.RestoreSession;
                break;

            case SettingsCatalog.Appearance:
                SelectedTheme = defaults.Theme;
                SelectedAnimationLevel = AnimationLevel.Full;
                LiquidGlassEnabled = defaults.LiquidGlassEnabled;
                UseSystemAccent = true;
                Density = defaults.Density;
                break;

            case SettingsCatalog.Panes:
                current.DualPaneEnabled = defaults.DualPaneEnabled;
                current.DualPaneVertical = defaults.DualPaneVertical;
                current.DualPaneSplitRatio = defaults.DualPaneSplitRatio;
                current.DualPaneRowStriping = defaults.DualPaneRowStriping;
                DualPaneRowStriping = defaults.DualPaneRowStriping;

                // Az oszlopszélességeknek nincs saját szerkesztő vezérlőjük
                // (lásd Settings_ColumnWidthsHint) — húzással állítódnak, itt
                // csak a visszaállítás éri el őket. A PaneViewModel a
                // _settings.Changed eseményen keresztül veszi át az új
                // értéket, ugyanúgy, mint a RowStriping-et.
                current.LeftPaneSizeColumnWidth = defaults.LeftPaneSizeColumnWidth;
                current.LeftPaneTypeColumnWidth = defaults.LeftPaneTypeColumnWidth;
                current.LeftPaneModifiedColumnWidth = defaults.LeftPaneModifiedColumnWidth;
                current.RightPaneSizeColumnWidth = defaults.RightPaneSizeColumnWidth;
                current.RightPaneTypeColumnWidth = defaults.RightPaneTypeColumnWidth;
                current.RightPaneModifiedColumnWidth = defaults.RightPaneModifiedColumnWidth;
                break;

            case SettingsCatalog.FileList:
                current.ShowHiddenItems = defaults.ShowHiddenItems;
                ShowSystemItems = defaults.ShowSystemItems;
                ShowExtensions = defaults.ShowExtensions;
                FoldersFirst = defaults.FoldersFirst;
                BinarySizeUnits = defaults.BinarySizeUnits;
                break;

            case SettingsCatalog.Keyboard:
                SelectedKeymap = defaults.Keymap;
                current.CustomKeyBindings.Clear();
                break;

            case SettingsCatalog.ContextMenu:
                ShellExtensionsEnabled = defaults.ShellExtensionsEnabled;
                ShellMenuTimeoutMs = defaults.ShellMenuTimeoutMs;
                ShellItemsInOwnSection = defaults.ShellItemsInOwnSection;
                current.ShellHandlerBlacklist.Clear();
                ShellBlacklistText = string.Empty;
                break;

            case SettingsCatalog.Editor:
                EditorFontFamily = defaults.EditorFontFamily;
                EditorFontSize = defaults.EditorFontSize;
                EditorTabWidth = defaults.EditorTabWidth;
                EditorInsertSpaces = defaults.EditorInsertSpaces;
                EditorWordWrap = defaults.EditorWordWrap;
                EditorShowLineNumbers = defaults.EditorShowLineNumbers;
                EditorDefaultEncoding = defaults.EditorDefaultEncoding;
                EditorDefaultLineEnding = defaults.EditorDefaultLineEnding;
                EditorInSeparateWindow = defaults.EditorInSeparateWindow;
                break;

            case SettingsCatalog.Integrations:
                ExternalTerminalPath = defaults.ExternalTerminalPath;
                ExternalEditorPath = defaults.ExternalEditorPath;
                ResetShellIntegrationCommand.Execute(null);
                break;

            case SettingsCatalog.Advanced:
                LogLevel = defaults.LogLevel;
                current.QuickAction1 = defaults.QuickAction1;
                current.QuickAction2 = defaults.QuickAction2;
                break;
        }

        _settings.NotifyChanged();
        StatusMessage = TranslationSource.Instance["Settings_ResetDone"];
    }

    /// <summary>Rövid visszajelzés az alsó sávban (visszaállítás, export/import).</summary>
    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>A teljes beállításkészlet exportálása JSON-be (spec F6).</summary>
    [RelayCommand]
    private void ExportSettings()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = "pilaster-settings.json",
            Filter = "JSON (*.json)|*.json",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        StatusMessage = TranslationSource.Instance[
            _settings.TryExport(dialog.FileName) ? "Settings_ExportDone" : "Settings_ExportFailed"];
    }

    /// <summary>Beállítások importálása JSON-ből.</summary>
    [RelayCommand]
    private void ImportSettings()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "JSON (*.json)|*.json" };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        StatusMessage = TranslationSource.Instance[
            _settings.TryImport(dialog.FileName) ? "Settings_ImportDone" : "Settings_ImportFailed"];
    }

    /// <summary>A „Frissítések" szakasz állapota — ugyanaz a példány, mint a főablak sávjáé.</summary>
    public UpdateViewModel Updates { get; }

    /// <summary>
    /// A választható nyelvek. A rendszernyelv-követés külön elem, mert az nem
    /// ugyanaz, mint a jelenlegi rendszernyelv rögzítése: ha a felhasználó
    /// később átállítja a Windowst, ez követi, a rögzített kód nem.
    /// </summary>
    public static IReadOnlyList<LanguageOption> Languages { get; } =
    [
        new(null, "Rendszernyelv / System language"),
        new("hu", "Magyar"),
        new("en", "English"),
    ];

    public IReadOnlyList<ThemeMode> Themes { get; } =
        [ThemeMode.System, ThemeMode.Light, ThemeMode.Dark];

    public IReadOnlyList<AnimationLevel> AnimationLevels { get; } =
        [AnimationLevel.Full, AnimationLevel.Reduced, AnimationLevel.Off];

    public ObservableCollection<QuickActionEditorViewModel> QuickActions { get; }

    /// <summary>A létrehozott címkék — átnevezhetők, törölhetők; lásd <see cref="TagEditorViewModel"/>.</summary>
    public ObservableCollection<TagEditorViewModel> Tags { get; }

    /// <summary>Az előre definiált 12 szín, amiből egy új címkéhez választani lehet — lásd <see cref="Converters.TagPalette.Presets"/>.</summary>
    public IReadOnlyList<TagColor> TagColors { get; } = Converters.TagPalette.Presets;

    [ObservableProperty]
    private ThemeMode _selectedTheme;

    [ObservableProperty]
    private LanguageOption _selectedLanguage;

    [ObservableProperty]
    private AnimationLevel _selectedAnimationLevel;

    [ObservableProperty]
    private bool _liquidGlassEnabled;

    [ObservableProperty]
    private KeymapPreset _selectedKeymap;

    // --- Egyszerű, közvetlenül a beállításmodellre író tulajdonságok ---
    // Mindegyik ugyanazt a mintát követi: mező a konstruktorban feltöltve,
    // OnXChanged-ben mentés. A _loaded őrzi, hogy a konstruktor kezdeti
    // értékadása ne váltson ki azonnali mentést.

    [ObservableProperty]
    private bool _restoreSession;

    partial void OnRestoreSessionChanged(bool value) => Persist(s => s.RestoreSession = value);

    [ObservableProperty]
    private bool _singleInstance;

    partial void OnSingleInstanceChanged(bool value) => Persist(s => s.SingleInstance = value);

    [ObservableProperty]
    private bool _checkForUpdates;

    partial void OnCheckForUpdatesChanged(bool value) => Persist(s => s.CheckForUpdates = value);

    [ObservableProperty]
    private string? _startupFolder;

    partial void OnStartupFolderChanged(string? value) => Persist(s => s.StartupFolder = string.IsNullOrWhiteSpace(value) ? null : value);

    [ObservableProperty]
    private string _density = "Comfortable";

    partial void OnDensityChanged(string value) => Persist(s => s.Density = value);

    /// <summary>Páros/páratlan sorok csíkozása a kétpaneles nézetben (spec K3, v1.0.1 — Beállítások-UI hozzáadva a 2. ellenőrzési körben).</summary>
    [ObservableProperty]
    private bool _dualPaneRowStriping;

    partial void OnDualPaneRowStripingChanged(bool value) => Persist(s => s.DualPaneRowStriping = value);

    [ObservableProperty]
    private bool _showSystemItems;

    partial void OnShowSystemItemsChanged(bool value) => Persist(s => s.ShowSystemItems = value);

    [ObservableProperty]
    private bool _showExtensions;

    partial void OnShowExtensionsChanged(bool value) => Persist(s => s.ShowExtensions = value);

    [ObservableProperty]
    private bool _foldersFirst;

    partial void OnFoldersFirstChanged(bool value) => Persist(s => s.FoldersFirst = value);

    [ObservableProperty]
    private bool _binarySizeUnits;

    partial void OnBinarySizeUnitsChanged(bool value) => Persist(s => s.BinarySizeUnits = value);

    [ObservableProperty]
    private bool _shellExtensionsEnabled;

    partial void OnShellExtensionsEnabledChanged(bool value) => Persist(s => s.ShellExtensionsEnabled = value);

    /// <summary>
    /// Igaz, ha a bővítmények egy korábbi összeomlás miatt vannak kikapcsolva
    /// — ekkor jelenik meg az „Újra bekapcsolás" gomb (spec P3).
    /// </summary>
    public bool ShellDisabledAfterCrash => _shellCrashGuard.CrashDetected;

    /// <summary>A legutóbbi összeomlás útvonala, hogy a felhasználó ki tudja feketelistázni a bűnöst.</summary>
    public string? ShellCrashPath => _shellCrashGuard.LastCrash?.Path;

    [RelayCommand]
    private void ReenableShellExtensions()
    {
        _shellCrashGuard.Acknowledge();
        OnPropertyChanged(nameof(ShellDisabledAfterCrash));
        StatusMessage = TranslationSource.Instance["Settings_ShellReenabled"];
    }

    [ObservableProperty]
    private int _shellMenuTimeoutMs;

    partial void OnShellMenuTimeoutMsChanged(int value) => Persist(s => s.ShellMenuTimeoutMs = Math.Clamp(value, 50, 5000));

    [ObservableProperty]
    private bool _shellItemsInOwnSection;

    partial void OnShellItemsInOwnSectionChanged(bool value) => Persist(s => s.ShellItemsInOwnSection = value);

    /// <summary>A shell-bővítmény feketelista soronként — a mentés sorokra bontja.</summary>
    [ObservableProperty]
    private string _shellBlacklistText = string.Empty;

    partial void OnShellBlacklistTextChanged(string value) => Persist(s =>
    {
        s.ShellHandlerBlacklist.Clear();
        s.ShellHandlerBlacklist.AddRange(
            value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    });

    [ObservableProperty]
    private string _editorFontFamily = "Cascadia Mono";

    partial void OnEditorFontFamilyChanged(string value) => Persist(s => s.EditorFontFamily = value);

    [ObservableProperty]
    private double _editorFontSize = 13;

    partial void OnEditorFontSizeChanged(double value) => Persist(s => s.EditorFontSize = Math.Clamp(value, 6, 48));

    [ObservableProperty]
    private int _editorTabWidth = 4;

    partial void OnEditorTabWidthChanged(int value) => Persist(s => s.EditorTabWidth = Math.Clamp(value, 1, 16));

    [ObservableProperty]
    private bool _editorInsertSpaces;

    partial void OnEditorInsertSpacesChanged(bool value) => Persist(s => s.EditorInsertSpaces = value);

    [ObservableProperty]
    private bool _editorWordWrap;

    partial void OnEditorWordWrapChanged(bool value) => Persist(s => s.EditorWordWrap = value);

    [ObservableProperty]
    private bool _editorShowLineNumbers;

    partial void OnEditorShowLineNumbersChanged(bool value) => Persist(s => s.EditorShowLineNumbers = value);

    [ObservableProperty]
    private string _editorDefaultEncoding = "utf-8";

    partial void OnEditorDefaultEncodingChanged(string value) => Persist(s => s.EditorDefaultEncoding = value);

    [ObservableProperty]
    private string _editorDefaultLineEnding = "CRLF";

    partial void OnEditorDefaultLineEndingChanged(string value) => Persist(s => s.EditorDefaultLineEnding = value);

    [ObservableProperty]
    private bool _editorInSeparateWindow;

    partial void OnEditorInSeparateWindowChanged(bool value) => Persist(s => s.EditorInSeparateWindow = value);

    [ObservableProperty]
    private string _externalTerminalPath = "wt.exe";

    partial void OnExternalTerminalPathChanged(string value) => Persist(s => s.ExternalTerminalPath = value);

    [ObservableProperty]
    private string _logLevel = "Information";

    partial void OnLogLevelChanged(string value) => Persist(s => s.LogLevel = value);

    /// <summary>A választható értékkészletek a legördülőkhöz.</summary>
    public IReadOnlyList<string> Densities { get; } = ["Compact", "Comfortable", "Relaxed"];

    public IReadOnlyList<string> Encodings { get; } = ["utf-8", "utf-8-bom", "cp1250", "cp852", "utf-16le", "utf-16be"];

    public IReadOnlyList<string> LineEndings { get; } = ["CRLF", "LF", "CR"];

    public IReadOnlyList<string> LogLevels { get; } = ["Information", "Debug", "Warning", "Error"];

    /// <summary>Egy beállítás írása és mentése — a konstruktor kezdeti értékadásait kihagyva.</summary>
    private void Persist(Action<AppSettings> mutate)
    {
        if (!_loaded)
        {
            return;
        }

        mutate(_settings.Current);
        _settings.NotifyChanged();
    }

    [ObservableProperty]
    private string _externalEditorPath = "notepad.exe";

    [ObservableProperty]
    private string _newTagName = string.Empty;

    [ObservableProperty]
    private TagColor _newTagColor = TagColor.Blue;

    /// <summary>Az az ablak, amelyiken a témaváltás átúsztatása fusson.</summary>
    public System.Windows.Window? AnimationHost { get; set; }

    partial void OnSelectedThemeChanged(ThemeMode value)
    {
        if (_loaded)
        {
            _ = _theme.SetAsync(value, AnimationHost);
        }
    }

    /// <summary>Az előre definiált akcentus-paletta — a Beállítások swatch-rácsához.</summary>
    public IReadOnlyList<Color> AccentPresets => AccentColorService.Presets;

    [ObservableProperty]
    private bool _useSystemAccent;

    /// <summary>Az aktuálisan érvényes akcentus szín — az előnézeti swatchhoz.</summary>
    [ObservableProperty]
    private Color _currentAccentColor;

    [ObservableProperty]
    private string _accentHexInput;

    [ObservableProperty]
    private string? _accentHexError;

    partial void OnUseSystemAccentChanged(bool value)
    {
        if (!_loaded || !value)
        {
            return;
        }

        _accent.SetSystemAccent();
        RefreshAccentPreview();
    }

    /// <summary>Egy paletta-swatch kiválasztása — a rendszerszín-követés kikapcsolásával jár.</summary>
    [RelayCommand]
    private void SelectAccentPreset(Color color)
    {
        AccentHexError = null;
        _accent.SetCustom(color);
        UseSystemAccent = false;
        RefreshAccentPreview();
    }

    /// <summary>A hex mezőbe beírt szín alkalmazása — érvénytelen bevitelnél hibaszöveg, alkalmazás nélkül.</summary>
    [RelayCommand]
    private void ApplyAccentHex()
    {
        if (!AccentColorService.TryParseHex(AccentHexInput, out var color))
        {
            AccentHexError = TranslationSource.Instance["Accent_InvalidHex"];
            return;
        }

        AccentHexError = null;
        _accent.SetCustom(color);
        UseSystemAccent = false;
        RefreshAccentPreview();
    }

    private void RefreshAccentPreview()
    {
        CurrentAccentColor = _accent.CurrentColor;
        AccentHexInput = ColorToHex(CurrentAccentColor);
    }

    private static string ColorToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    partial void OnSelectedAnimationLevelChanged(AnimationLevel value)
    {
        if (_loaded)
        {
            _animations.SetLevel(value);
        }
    }

    /// <summary>
    /// A „Rendszerintegráció" szakasz hibaüzenete — sikertelen registry-
    /// művelet esetén jelenik meg, egyébként <c>null</c>. Mivel a kapcsolók
    /// HKCU alá írnak, ez a gyakorlatban ritka (írásvédett profil,
    /// vállalati házirend, víruskereső-beavatkozás) — nem UAC-megtagadás,
    /// mert ehhez a HKCU-írásokhoz nem kell admin jog.
    /// </summary>
    [ObservableProperty]
    private string? _shellIntegrationError;

    /// <summary>
    /// Mappák/meghajtók dupla kattintásra ebben az appban nyíljanak meg. A
    /// bekapcsolás előtti jóváhagyó párbeszédet a kódmögöttes kezeli (lásd
    /// <c>SettingsWindow.OnFolderOpenRedirectPreviewClick</c>) — MIELŐTT ez a
    /// tulajdonság egyáltalán megváltozna, tehát megszakításkor a kapcsoló
    /// vizuálisan sem billen át.
    /// </summary>
    [ObservableProperty]
    private bool _folderOpenRedirectEnabled;

    partial void OnFolderOpenRedirectEnabledChanged(bool value)
    {
        if (!_loaded)
        {
            return;
        }

        var (ok, error) = _shellIntegration.SetFolderOpenRedirect(value, ExecutablePath);
        ShellIntegrationError = ok ? null : error;

        if (!ok)
        {
            _folderOpenRedirectEnabled = !value;
            OnPropertyChanged(nameof(FolderOpenRedirectEnabled));
        }
    }

    [ObservableProperty]
    private bool _winERedirectEnabled;

    partial void OnWinERedirectEnabledChanged(bool value)
    {
        if (_loaded)
        {
            _shellIntegration.SetWinERedirect(value);
        }
    }

    [ObservableProperty]
    private bool _contextMenuEntryEnabled;

    partial void OnContextMenuEntryEnabledChanged(bool value)
    {
        if (!_loaded)
        {
            return;
        }

        var (ok, error) = _shellIntegration.SetContextMenuEntry(
            value,
            ExecutablePath,
            TranslationSource.Instance["ShellIntegration_ContextMenuLabel"],
            ExecutablePath);

        ShellIntegrationError = ok ? null : error;

        if (!ok)
        {
            _contextMenuEntryEnabled = !value;
            OnPropertyChanged(nameof(ContextMenuEntryEnabled));
        }
    }

    /// <summary>„Minden visszaállítása alapértelmezettre" — a Rendszerintegráció szakasz gombja.</summary>
    [RelayCommand]
    private void ResetShellIntegration()
    {
        var (ok, error) = _shellIntegration.ResetAll(ExecutablePath);

        // Közvetlen mezőírás, NEM a generált tulajdonságon keresztül: azok
        // saját OnXChanged kezelője újra meghívná a registry-műveletet,
        // holott a ResetAll fentebb már elvégezte mindhármat.
#pragma warning disable MVVMTK0034
        _folderOpenRedirectEnabled = false;
        _winERedirectEnabled = false;
        _contextMenuEntryEnabled = false;
#pragma warning restore MVVMTK0034
        OnPropertyChanged(nameof(FolderOpenRedirectEnabled));
        OnPropertyChanged(nameof(WinERedirectEnabled));
        OnPropertyChanged(nameof(ContextMenuEntryEnabled));

        ShellIntegrationError = ok ? null : error;
    }

    partial void OnLiquidGlassEnabledChanged(bool value)
    {
        if (_loaded)
        {
            _glass.SetEnabled(value);
        }
    }

    partial void OnSelectedKeymapChanged(KeymapPreset value)
    {
        if (!_loaded)
        {
            return;
        }

        _settings.Current.Keymap = value;
        _settings.NotifyChanged();
    }

    /// <summary>A választható kiosztások — a felületen egyik sem visel idegen terméknevet.</summary>
    public IReadOnlyList<KeymapPreset> Keymaps { get; } =
        [KeymapPreset.PilasterClassic, KeymapPreset.Explorer, KeymapPreset.Custom];

    /// <summary>
    /// A kiválasztott preset teljes billentyűlistája — a „Kiosztás
    /// megtekintése" gomb ezt jeleníti meg táblázatban (spec F1).
    /// </summary>
    public IReadOnlyList<KeyBindingRow> KeymapRows => KeymapCatalog.Describe(SelectedKeymap);

    [ObservableProperty]
    private bool _isKeymapTableVisible;

    [RelayCommand]
    private void ToggleKeymapTable()
    {
        IsKeymapTableVisible = !IsKeymapTableVisible;
        OnPropertyChanged(nameof(KeymapRows));
    }

    partial void OnExternalEditorPathChanged(string value)
    {
        if (!_loaded)
        {
            return;
        }

        _settings.Current.ExternalEditorPath = value;
        _settings.NotifyChanged();
    }

    /// <summary>„Tallózás" az F4 (Szerkesztés) külső programjához.</summary>
    [RelayCommand]
    private void BrowseExternalEditor()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = TranslationSource.Instance["Settings_ExternalEditor"],
            Filter = "Executable (*.exe)|*.exe|Minden fájl (*.*)|*.*",
        };

        if (dialog.ShowDialog() == true)
        {
            ExternalEditorPath = dialog.FileName;
        }
    }

    partial void OnSelectedLanguageChanged(LanguageOption value)
    {
        if (!_loaded)
        {
            return;
        }

        _settings.Current.Language = value.Code;
        _settings.NotifyChanged();

        // A null kódot a rendszernyelvre oldjuk fel — a mentésben viszont
        // null marad, hogy a követés megmaradjon.
        TranslationSource.Instance.SetLanguage(
            value.Code ?? TranslationSource.ResolveSystemLanguage());
    }

    private void OnQuickActionChanged() => _settings.NotifyChanged();

    /// <summary>Új címke létrehozása a beírt névvel és a kiválasztott színnel.</summary>
    [RelayCommand]
    private void AddTag()
    {
        var name = NewTagName.Trim();

        if (name.Length == 0)
        {
            return;
        }

        var tag = _metadata.CreateTag(name, NewTagColor);
        Tags.Add(new TagEditorViewModel(_metadata, tag));
        NewTagName = string.Empty;
    }

    /// <summary>Egy címke törlése — a listából is eltűnik, nem csak a tárolóból.</summary>
    [RelayCommand]
    private void DeleteTag(TagEditorViewModel? tag)
    {
        if (tag is null)
        {
            return;
        }

        tag.DeleteCommand.Execute(null);
        Tags.Remove(tag);
    }

    /// <summary>
    /// Rejtett üzenetek, amik a verziószámra 7-szer kattintva véletlenszerűen
    /// előbukkannak — lásd <see cref="RegisterVersionClick"/>. Ugyanaz a
    /// „rejtett gesztus" minta, mint a Hibabejelentés fejlécének 10
    /// kattintásos fejlesztői panelje (<c>BugReportViewModel.RegisterSecretClick</c>),
    /// csak itt a jutalom egy ártalmatlan, magától eltűnő üzenet, nem
    /// funkció — semmilyen tartós állapotot nem módosít, tehát nincs mit
    /// visszafordítani.
    /// </summary>
    private static readonly string[] EasterEggMessages =
    [
        "Pilaster — mert a fájlkezelőnek is lehet stílusa.",
        "Tudtad? A pilaster egy falba épített féloszlop — pont mint az oszlopos nézet.",
        "Te vagy a 7. kattintás bajnoka!",
        "Ez itt egy titkos üzenet. Ne áruld el senkinek.",
        "Minden felesleges kattintás egy lépés a tökéletes fájlrendszer felé.",
    ];

    private static readonly Random EasterEggRandom = new();

    private int _versionClickCount;
    private int _easterEggGeneration;

    [ObservableProperty]
    private bool _showEasterEgg;

    [ObservableProperty]
    private string _easterEggMessage = string.Empty;

    /// <summary>A Beállítások „Frissítések" szekciójának verziószámára kattintva hívva.</summary>
    public void RegisterVersionClick()
    {
        _versionClickCount++;

        if (_versionClickCount < 7)
        {
            return;
        }

        _versionClickCount = 0;
        EasterEggMessage = EasterEggMessages[EasterEggRandom.Next(EasterEggMessages.Length)];
        ShowEasterEgg = true;

        _ = HideEasterEggAfterDelayAsync(++_easterEggGeneration);
    }

    /// <summary>
    /// Automatikus eltüntetés — a generációszám azt biztosítja, hogy ha a
    /// felhasználó gyorsan újra 7-et kattint, csak a LEGUTÓBBI időzítő zárja
    /// be az üzenetet, a korábbi ne csukja be idő előtt az újat.
    /// </summary>
    private async Task HideEasterEggAfterDelayAsync(int generation)
    {
        await Task.Delay(TimeSpan.FromSeconds(3.5));

        if (generation == _easterEggGeneration)
        {
            ShowEasterEgg = false;
        }
    }
}
