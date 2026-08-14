namespace Pilaster.Core.Settings;

/// <summary>A felület színsémája.</summary>
public enum ThemeMode
{
    /// <summary>A Windows beállítását követi, és váltáskor magától igazodik.</summary>
    System,

    Light,

    Dark,
}

/// <summary>Az akcentus szín forrása.</summary>
public enum AccentColorMode
{
    /// <summary>A Windows Személyre szabás beállításából vett szín, élőben követve.</summary>
    System,

    /// <summary>Előre definiált paletta vagy egyedi hex érték — lásd <see cref="AppSettings.AccentColorHex"/>.</summary>
    Custom,
}

/// <summary>Az animációk mennyisége.</summary>
public enum AnimationLevel
{
    /// <summary>Teljes, a tervezett időzítésekkel.</summary>
    Full,

    /// <summary>Rövidebb, visszafogottabb átmenetek — a rendszer „csökkentett mozgás" szándékát követi.</summary>
    Reduced,

    /// <summary>Nincs animáció — minden állapotváltás azonnali.</summary>
    Off,
}

/// <summary>
/// A „Rendszerintegráció" beállítások — mindegyik alapból KIKAPCSOLVA, és
/// egyenként, függetlenül kapcsolható. A <c>*BackupCaptured</c>/<c>*BackupValue</c>
/// párok a bekapcsolás előtti registry-állapotot őrzik, hogy a kikapcsolás
/// PONTOSAN visszaállítsa azt (ne csak törölje) — lásd
/// <c>Pilaster.Shell.Integration.ShellIntegrationService</c>. Szándékosan
/// primitív mezők (nem a Shell projekt <c>RegistryBackup</c> típusa), mert a
/// Core réteg nem hivatkozhat a Shell rétegre.
/// </summary>
public sealed class ShellIntegrationSettings
{
    /// <summary>Mappák/meghajtók dupla kattintásra ebben az appban nyíljanak meg.</summary>
    public bool FolderOpenRedirectEnabled { get; set; }

    public bool DirectoryBackupCaptured { get; set; }

    public bool DirectoryBackupExisted { get; set; }

    public string? DirectoryBackupValue { get; set; }

    public bool DriveBackupCaptured { get; set; }

    public bool DriveBackupExisted { get; set; }

    public string? DriveBackupValue { get; set; }

    /// <summary>Win+E ezt az appot nyissa meg — csak addig hat, amíg a Pilaster fut.</summary>
    public bool WinERedirectEnabled { get; set; }

    /// <summary>„Megnyitás Pilaster-ben" bejegyzés mappák jobbklikk-menüjében.</summary>
    public bool ContextMenuEntryEnabled { get; set; }
}

/// <summary>Mit hozzon létre egy gyorsgomb.</summary>
public enum QuickActionKind
{
    Folder,

    File,
}

/// <summary>Hova hozza létre a gyorsgomb az új elemet.</summary>
public enum QuickActionTarget
{
    /// <summary>Az éppen megnyitott mappába.</summary>
    CurrentFolder,

    /// <summary>Egy rögzített, beállított útvonalra.</summary>
    FixedPath,
}

/// <summary>Egy testreszabható gyorsgomb beállításai.</summary>
public sealed class QuickActionSettings
{
    /// <summary>A gombon és a tooltipben megjelenő név.</summary>
    public string Label { get; set; } = string.Empty;

    public QuickActionKind Kind { get; set; } = QuickActionKind.Folder;

    /// <summary>Fájlnál a kiterjesztés pont nélkül; mappánál figyelmen kívül marad.</summary>
    public string Extension { get; set; } = "txt";

    /// <summary>
    /// A név mintája, helyőrzőkkel — lásd <c>NameTemplate</c>.
    /// </summary>
    public string NameTemplate { get; set; } = "Új mappa";

    public QuickActionTarget Target { get; set; } = QuickActionTarget.CurrentFolder;

    /// <summary>Rögzített célútvonal, ha a <see cref="Target"/> azt kéri.</summary>
    public string? FixedPath { get; set; }

    /// <summary>A WPF-UI ikon neve (pl. <c>FolderAdd24</c>).</summary>
    public string Icon { get; set; } = "FolderAdd24";
}

/// <summary>
/// Egy rögzített mappa a gyorselérésben.
/// </summary>
/// <remarks>
/// Az előre definiált mappáknál (Asztal, Dokumentumok stb.) a
/// <see cref="LabelKey"/> egy fordítási kulcs, hogy nyelvváltáskor a felirat
/// is kövesse — a felhasználó által rögzített egyéni mappáknál nincs
/// fordítás, ott a <see cref="CustomLabel"/> (a mappa saját neve) jelenik meg.
/// </remarks>
public sealed class PinnedFolder
{
    public required string Path { get; set; }

    /// <summary>Fordítási kulcs az előre definiált mappákhoz (pl. „Nav_Desktop"). Egyéni mappánál <c>null</c>.</summary>
    public string? LabelKey { get; set; }

    /// <summary>Egyéni (a felhasználó által rögzített) mappánál a megjelenítendő név.</summary>
    public string? CustomLabel { get; set; }

    /// <summary>A WPF-UI ikon neve (pl. <c>Desktop24</c>). Egyéni mappánál általános mappaikon.</summary>
    public string Icon { get; set; } = "Folder24";
}

/// <summary>Egy megnyitott fül menthető állapota — lásd <see cref="AppSession"/>.</summary>
public sealed class TabSession
{
    public string Path { get; set; } = string.Empty;

    public Pilaster.Core.FileSystem.ViewMode ViewMode { get; set; } = Pilaster.Core.FileSystem.ViewMode.Details;

    public Pilaster.Core.FileSystem.SortKey SortKey { get; set; } = Pilaster.Core.FileSystem.SortKey.Name;

    public bool SortDescending { get; set; }

    public bool ShowHiddenItems { get; set; }
}

/// <summary>Egy panel menthető állapota: a füljei és az aktív fül indexe.</summary>
public sealed class PaneSession
{
    public List<TabSession> Tabs { get; set; } = [];

    public int ActiveTabIndex { get; set; }
}

/// <summary>
/// A teljes munkamenet: mindkét panel összes füle.
/// </summary>
/// <remarks>
/// Kilépéskor mentődik, induláskor visszaáll — a Beállításokban
/// kikapcsolható (<see cref="AppSettings.RestoreSession"/>). Szándékosan
/// panelenként tagolt, nem egyetlen közös fül-listaként: a v1.0-ban a fülek a
/// PANELEKÉ, nem a főablaké (lásd <c>PaneViewModel</c>).
/// </remarks>
public sealed class AppSession
{
    public PaneSession Left { get; set; } = new();

    public PaneSession Right { get; set; } = new();
}

/// <summary>
/// Az alkalmazás menthető beállításai.
/// </summary>
/// <remarks>
/// Szándékosan sima osztály, csak adattal: ez a típus szerializálódik JSON-ba,
/// tehát minden új mező automatikusan mentődik. Az alapértékek úgy vannak
/// megválasztva, hogy egy hiányzó vagy sérült beállításfájl mellett is
/// használható legyen a program.
/// </remarks>
public sealed class AppSettings
{
    /// <summary>A séma verziója — későbbi migrációkhoz.</summary>
    public int Version { get; set; } = 1;

    public ThemeMode Theme { get; set; } = ThemeMode.System;

    public AccentColorMode AccentColor { get; set; } = AccentColorMode.System;

    /// <summary>
    /// Egyedi akcentus szín <c>"#RRGGBB"</c> alakban — csak akkor számít, ha
    /// <see cref="AccentColor"/> értéke <see cref="AccentColorMode.Custom"/>.
    /// Rendszerszínnél <c>null</c>, hogy visszaváltáskor ne maradjon elavult érték.
    /// </summary>
    public string? AccentColorHex { get; set; }

    /// <summary>
    /// A rendszerintegráció (Explorer-kiváltás) beállításai — alapból minden
    /// kapcsoló kikapcsolva, lásd <see cref="ShellIntegrationSettings"/>.
    /// </summary>
    public ShellIntegrationSettings ShellIntegration { get; set; } = new();

    /// <summary>
    /// A felület nyelve kultúrakóddal, vagy <c>null</c>, ha a rendszernyelvet
    /// kell követni. A <c>null</c> nem ugyanaz, mint egy konkrét kód: ha a
    /// felhasználó később átállítja a Windows nyelvét, a <c>null</c> követi,
    /// a rögzített kód nem.
    /// </summary>
    public string? Language { get; set; }

    public bool ShowHiddenItems { get; set; }

    /// <summary>
    /// <c>null</c> = még sosem lett testreszabva — ilyenkor a rendszer
    /// „csökkentett mozgás" beállítása dönti el az induló értéket (lásd
    /// <c>AnimationService</c>), utána explicit értékként el is mentődik,
    /// hogy a döntés stabil maradjon a rendszerbeállítás későbbi váltásaitól
    /// függetlenül.
    /// </summary>
    public AnimationLevel? Animations { get; set; }

    /// <summary>
    /// Áttetsző „liquid glass" felület — az oldalsáv, a felső sáv, a
    /// jobbklikk-menük és a Beállítások panel áttetsző rétegként jelennek
    /// meg a Mica háttér felett. Gyengébb gépeken kikapcsolható.
    /// </summary>
    public bool LiquidGlassEnabled { get; set; } = true;

    /// <summary>Az utoljára használt nézetmód — új fül ezzel nyílik.</summary>
    public Pilaster.Core.FileSystem.ViewMode LastViewMode { get; set; } = Pilaster.Core.FileSystem.ViewMode.Details;

    /// <summary>Kétablakos (Total Commander-stílusú) nézet be van-e kapcsolva.</summary>
    public bool DualPaneEnabled { get; set; }

    /// <summary>Igaz = a két panel egymás alatt (függőleges elrendezés), hamis = egymás mellett.</summary>
    public bool DualPaneVertical { get; set; }

    /// <summary>
    /// A kétpaneles elválasztó helyzete: a BAL (illetve függőleges
    /// elrendezésben a FELSŐ) panel aránya, 0 és 1 között. Dupla kattintás az
    /// elválasztón visszaállítja 0,5-re.
    /// </summary>
    public double DualPaneSplitRatio { get; set; } = 0.5;

    /// <summary>
    /// Kilépéskor mentődjön-e a nyitott fülek állapota, és induláskor
    /// álljon-e vissza. Kikapcsolva mindkét panel egy Kezdőlap-füllel indul.
    /// </summary>
    public bool RestoreSession { get; set; } = true;

    /// <summary>A legutóbbi munkamenet — lásd <see cref="RestoreSession"/>.</summary>
    public AppSession? Session { get; set; }

    /// <summary>
    /// Total Commander-stílusú billentyűkiosztás (F3 Megtekint, F4 Szerkeszt,
    /// F5 Másol, F6 Áthelyez, F7 Új mappa, F8/Delete Töröl stb.). Kikapcsolva
    /// a hagyományos Intéző-szerű működés marad (F5 nincs lefoglalva).
    /// </summary>
    public bool TotalCommanderKeybindingsEnabled { get; set; }

    /// <summary>Az F4 (Szerkesztés) ezt a programot indítja — alapból Jegyzettömb.</summary>
    public string ExternalEditorPath { get; set; } = "notepad.exe";

    /// <summary>
    /// A gyorselérés rögzített mappái, sorrendben. <c>null</c> = még sosem
    /// lett testreszabva — ilyenkor a nézet az alapértelmezett hat mappával
    /// (Asztal, Dokumentumok, Letöltések, Képek, Zene, Videók) tölti fel
    /// első használatkor, és el is menti, hogy onnantól ez legyen az igazság
    /// forrása. Így törlés/rögzítés/átrendezés után soha nem áll vissza az
    /// alapértelmezésre.
    /// </summary>
    public List<PinnedFolder>? QuickAccessPins { get; set; }

    /// <summary>A felső sáv első gyorsgombja.</summary>
    public QuickActionSettings QuickAction1 { get; set; } = new()
    {
        Kind = QuickActionKind.Folder,
        NameTemplate = "Új mappa",
        Icon = "FolderAdd24",
        Target = QuickActionTarget.CurrentFolder,
    };

    /// <summary>A felső sáv második gyorsgombja.</summary>
    public QuickActionSettings QuickAction2 { get; set; } = new()
    {
        Kind = QuickActionKind.File,
        Extension = "txt",
        NameTemplate = "Új szöveges fájl",
        Icon = "DocumentAdd24",
        Target = QuickActionTarget.CurrentFolder,
    };
}
