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
        UpdateViewModel updates)
    {
        _settings = settings;
        _theme = theme;
        _accent = accent;
        _animations = animations;
        _shellIntegration = shellIntegration;
        _glass = glass;
        _metadata = metadata;
        Updates = updates;

        var current = settings.Current;

        _selectedTheme = current.Theme;
        _selectedAnimationLevel = _animations.Current;
        _liquidGlassEnabled = current.LiquidGlassEnabled;
        _totalCommanderKeybindingsEnabled = current.TotalCommanderKeybindingsEnabled;
        _externalEditorPath = current.ExternalEditorPath;

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

    /// <summary>Az előre definiált színkészlet, amiből egy új címkéhez választani lehet.</summary>
    public IReadOnlyList<TagColor> TagColors { get; } = Enum.GetValues<TagColor>();

    [ObservableProperty]
    private ThemeMode _selectedTheme;

    [ObservableProperty]
    private LanguageOption _selectedLanguage;

    [ObservableProperty]
    private AnimationLevel _selectedAnimationLevel;

    [ObservableProperty]
    private bool _liquidGlassEnabled;

    [ObservableProperty]
    private bool _totalCommanderKeybindingsEnabled;

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

    partial void OnTotalCommanderKeybindingsEnabledChanged(bool value)
    {
        if (!_loaded)
        {
            return;
        }

        _settings.Current.TotalCommanderKeybindingsEnabled = value;
        _settings.NotifyChanged();
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
