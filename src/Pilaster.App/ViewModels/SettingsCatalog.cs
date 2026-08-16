using CommunityToolkit.Mvvm.ComponentModel;
using Pilaster.App.Localization;
using Wpf.Ui.Controls;

namespace Pilaster.App.ViewModels;

/// <summary>Egy kategória a Beállítások bal oldali listájában.</summary>
public sealed partial class SettingsCategoryViewModel : ObservableObject
{
    /// <summary>Stabil azonosító — a mélyhivatkozás (deep link) erre mutat.</summary>
    public required string Id { get; init; }

    public required string TitleKey { get; init; }

    public SymbolRegular Icon { get; init; } = SymbolRegular.Settings24;

    public string Title => TranslationSource.Instance[TitleKey];

    /// <summary>Igaz, ha a kategória megjelenik — keresés közben csak a találatot tartalmazók.</summary>
    [ObservableProperty]
    public partial bool IsVisible { get; set; } = true;
}

/// <summary>
/// Egy kereshető beállítás leírója.
/// </summary>
/// <remarks>
/// Nem maga a beállítás, csak a róla szóló metaadat: ebből épül a Beállítások
/// keresője (spec F6) és a mélyhivatkozás. A leíró és a tényleges vezérlő
/// összekötése az <see cref="Id"/>-n keresztül történik — a nézet ugyanezt az
/// azonosítót teszi a vezérlő <c>Tag</c>-jébe, hogy a találat oda tudjon
/// görgetni és felvillantani.
/// </remarks>
/// <param name="Id">Stabil azonosító, pl. <c>appearance.theme</c>.</param>
/// <param name="CategoryId">A tartalmazó kategória azonosítója.</param>
/// <param name="TitleKey">A beállítás nevének fordítási kulcsa.</param>
/// <param name="DescriptionKey">A segédszöveg fordítási kulcsa; <c>null</c>, ha nincs.</param>
/// <param name="Keywords">További, nem megjelenő keresőszavak (magyarul és angolul).</param>
public sealed record SettingsDescriptor(
    string Id,
    string CategoryId,
    string TitleKey,
    string? DescriptionKey = null,
    string? Keywords = null)
{
    /// <summary>
    /// Illeszkedik-e a keresőkifejezésre.
    /// </summary>
    /// <remarks>
    /// „Fuzzy" itt azt jelenti: a beírt szöveg SZAVANKÉNT, tetszőleges
    /// sorrendben, részszóként illeszkedik a névre, a leírásra vagy a
    /// kulcsszavakra. Ez pontosan az, amit egy beállításkeresőtől várni lehet
    /// („sötét téma" ugyanúgy megtalálja, mint a „téma sötét"), és nem hoz be
    /// egy közelítő-illesztő könyvtárat egyetlen mezőért.
    /// </remarks>
    public bool Matches(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var strings = TranslationSource.Instance;
        var haystack = string.Join(
            ' ',
            strings[TitleKey],
            DescriptionKey is null ? string.Empty : strings[DescriptionKey],
            Keywords ?? string.Empty,
            Id);

        return query
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(token => haystack.Contains(token, StringComparison.CurrentCultureIgnoreCase));
    }
}

/// <summary>
/// A Beállítások kategóriái és kereshető leírói (spec F6).
/// </summary>
public static class SettingsCatalog
{
    public const string General = "general";
    public const string Appearance = "appearance";
    public const string Panes = "panes";
    public const string FileList = "filelist";
    public const string Keyboard = "keyboard";
    public const string ContextMenu = "contextmenu";
    public const string Editor = "editor";
    public const string Tags = "tags";
    public const string Integrations = "integrations";
    public const string Advanced = "advanced";
    public const string About = "about";

    public static IReadOnlyList<SettingsCategoryViewModel> BuildCategories() =>
    [
        new() { Id = General, TitleKey = "SettingsCat_General", Icon = SymbolRegular.Home24 },
        new() { Id = Appearance, TitleKey = "SettingsCat_Appearance", Icon = SymbolRegular.PaintBrush24 },
        new() { Id = Panes, TitleKey = "SettingsCat_Panes", Icon = SymbolRegular.DualScreen24 },
        new() { Id = FileList, TitleKey = "SettingsCat_FileList", Icon = SymbolRegular.AppsList24 },
        new() { Id = Keyboard, TitleKey = "SettingsCat_Keyboard", Icon = SymbolRegular.Keyboard24 },
        new() { Id = ContextMenu, TitleKey = "SettingsCat_ContextMenu", Icon = SymbolRegular.ContentView24 },
        new() { Id = Editor, TitleKey = "SettingsCat_Editor", Icon = SymbolRegular.Code24 },
        new() { Id = Tags, TitleKey = "SettingsCat_Tags", Icon = SymbolRegular.Tag24 },
        new() { Id = Integrations, TitleKey = "SettingsCat_Integrations", Icon = SymbolRegular.PlugConnected24 },
        new() { Id = Advanced, TitleKey = "SettingsCat_Advanced", Icon = SymbolRegular.Wrench24 },
        new() { Id = About, TitleKey = "SettingsCat_About", Icon = SymbolRegular.Info24 },
    ];

    /// <summary>
    /// Minden kereshető beállítás. Új beállítás felvételekor IDE is fel kell
    /// venni — különben a kereső nem találja meg, és a mélyhivatkozás sem
    /// működik rá.
    /// </summary>
    public static IReadOnlyList<SettingsDescriptor> BuildDescriptors() =>
    [
        // Általános
        new("general.language", General, "Settings_Language", "Settings_LanguageHint", "nyelv language locale magyar english"),
        new("general.startupFolder", General, "Settings_StartupFolder", "Settings_StartupFolderHint", "indulás startup kezdő"),
        new("general.restoreSession", General, "Settings_RestoreSession", "Settings_RestoreSessionHint", "munkamenet session fülek tabs"),
        new("general.singleInstance", General, "Settings_SingleInstance", "Settings_SingleInstanceHint", "egypéldány single instance"),
        new("general.updates", General, "Settings_Updates", "Settings_UpdatesHint", "frissítés update verzió version"),

        // Megjelenés
        new("appearance.theme", Appearance, "Settings_Theme", "Settings_ThemeHint", "téma theme sötét dark világos light"),
        new("appearance.accent", Appearance, "Settings_Accent", "Settings_AccentHint", "akcentus accent szín colour color"),
        new("appearance.animations", Appearance, "Settings_Animations", "Settings_AnimationsHint", "animáció animation mozgás motion"),
        new("appearance.glass", Appearance, "Settings_LiquidGlass", "Settings_LiquidGlassHint", "átlátszó transparent glass mica acrylic"),
        new("appearance.density", Appearance, "Settings_Density", "Settings_DensityHint", "sűrűség density kompakt compact"),

        // Panelek
        new("panes.dual", Panes, "Settings_DualPane", "Settings_DualPaneHint", "kétpaneles dual pane panel"),
        new("panes.vertical", Panes, "Settings_DualPaneVertical", "Settings_DualPaneVerticalHint", "elrendezés layout vízszintes függőleges"),
        new("panes.splitRatio", Panes, "Settings_SplitRatio", "Settings_SplitRatioHint", "elválasztó splitter arány ratio"),
        new("panes.rowStriping", Panes, "Settings_RowStriping", "Settings_RowStripingHint", "csíkozás zebra sávozás sor row stripe striping"),
        new("panes.columnWidths", Panes, "Settings_ColumnWidths", "Settings_ColumnWidthsHint", "oszlop column szélesség width méret size típus type módosítva modified"),

        // Fájllista
        new("filelist.hidden", FileList, "Cmd_ToggleHidden", "Settings_HiddenHint", "rejtett hidden"),
        new("filelist.system", FileList, "Settings_ShowSystem", "Settings_ShowSystemHint", "rendszerfájl system"),
        new("filelist.extensions", FileList, "Settings_ShowExtensions", "Settings_ShowExtensionsHint", "kiterjesztés extension"),
        new("filelist.foldersFirst", FileList, "Settings_FoldersFirst", "Settings_FoldersFirstHint", "mappa folder rendezés sort"),
        new("filelist.sizeUnits", FileList, "Settings_BinarySizeUnits", "Settings_BinarySizeUnitsHint", "méret size kb kib mb mib"),

        // Billentyűzet
        new("keyboard.preset", Keyboard, "Settings_Keymap", "Settings_KeymapHint", "billentyű keyboard kiosztás keymap preset classic modern"),

        // Jobbklikk menü
        new("contextmenu.shell", ContextMenu, "Settings_ShellExtensions", "Settings_ShellExtensionsHint", "shell bővítmény extension 7-zip jobbklikk context"),
        new("contextmenu.timeout", ContextMenu, "Settings_ShellTimeout", "Settings_ShellTimeoutHint", "időkorlát timeout"),
        new("contextmenu.blacklist", ContextMenu, "Settings_ShellBlacklist", "Settings_ShellBlacklistHint", "feketelista blacklist clsid"),
        new("contextmenu.section", ContextMenu, "Settings_ShellOwnSection", "Settings_ShellOwnSectionHint", "szekció section egyéb other"),

        // Szerkesztő
        new("editor.font", Editor, "Settings_EditorFont", "Settings_EditorFontHint", "betűtípus font méret size"),
        new("editor.tabWidth", Editor, "Settings_EditorTabWidth", "Settings_EditorTabWidthHint", "tabulátor tab behúzás indent szóköz space"),
        new("editor.wordWrap", Editor, "Settings_EditorWordWrap", "Settings_EditorWordWrapHint", "sortörés wrap"),
        new("editor.encoding", Editor, "Settings_EditorEncoding", "Settings_EditorEncodingHint", "kódolás encoding utf-8 cp1250"),
        new("editor.lineEnding", Editor, "Settings_EditorLineEnding", "Settings_EditorLineEndingHint", "sorvég eol crlf lf"),
        new("editor.window", Editor, "Settings_EditorWindow", "Settings_EditorWindowHint", "ablak window fül tab"),

        // Címkék
        new("tags.list", Tags, "Nav_Tags", "Settings_TagsHint", "címke tag szín colour label"),

        // Integrációk
        new("integrations.terminal", Integrations, "Settings_Terminal", "Settings_TerminalHint", "terminál terminal powershell cmd wsl"),
        new("integrations.editor", Integrations, "Settings_ExternalEditor", "Settings_ExternalEditorHint", "szerkesztő editor külső external"),
        new("integrations.shell", Integrations, "Settings_ShellIntegration", "Settings_ShellIntegrationHint", "rendszerintegráció explorer win+e alapértelmezett default"),
        new("integrations.bugreport", Integrations, "Settings_BugReport", "Settings_BugReportHint", "hibabejelentés bug report discord"),

        // Speciális
        new("advanced.logLevel", Advanced, "Settings_LogLevel", "Settings_LogLevelHint", "napló log szint level"),
        new("advanced.logFile", Advanced, "Settings_OpenLog", "Settings_OpenLogHint", "naplófájl logfile"),
        new("advanced.configFolder", Advanced, "Settings_OpenConfig", "Settings_OpenConfigHint", "konfiguráció config mappa folder appdata"),
        new("advanced.quickActions", Advanced, "Settings_QuickActions", "Settings_QuickActionsHint", "gyorsgomb quick action"),

        // Névjegy
        new("about.version", About, "Settings_CurrentVersion", null, "verzió version build névjegy about"),
    ];
}
