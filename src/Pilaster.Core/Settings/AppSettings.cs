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

    // ---------- Általános ----------

    /// <summary>
    /// Indításkor megnyíló mappa, vagy <c>null</c> a Kezdőlaphoz. Csak akkor
    /// számít, ha a <see cref="RestoreSession"/> ki van kapcsolva — különben a
    /// mentett munkamenet erősebb.
    /// </summary>
    public string? StartupFolder { get; set; }

    /// <summary>Egypéldányos futás: egy második indítás a meglévő ablakot hozza előtérbe.</summary>
    public bool SingleInstance { get; set; } = true;

    /// <summary>Induláskor keressen-e új verziót.</summary>
    public bool CheckForUpdates { get; set; } = true;

    // ---------- Fájllista ----------

    /// <summary>A rendszerfájlok külön kapcsolója — a rejtett elemektől függetlenül.</summary>
    public bool ShowSystemItems { get; set; }

    /// <summary>Kiterjesztések megjelenítése a névben.</summary>
    public bool ShowExtensions { get; set; } = true;

    /// <summary>A mappák a fájlok elé rendeződjenek.</summary>
    public bool FoldersFirst { get; set; } = true;

    /// <summary>Igaz = bináris (KiB/MiB), hamis = decimális (KB/MB) méretformátum.</summary>
    public bool BinarySizeUnits { get; set; }

    /// <summary>A fájllista sűrűsége: <c>Compact</c>, <c>Comfortable</c>, <c>Relaxed</c>.</summary>
    public string Density { get; set; } = "Comfortable";

    // ---------- Jobbklikk menü ----------

    /// <summary>Megjelenjenek-e a telepített shell-bővítmények elemei a saját menüben.</summary>
    public bool ShellExtensionsEnabled { get; set; } = true;

    /// <summary>
    /// Ennyi ezredmásodpercet várunk a shell-bővítmények betöltésére, mielőtt
    /// a menü nélkülük jelenne meg. A saját elemek AZONNAL láthatók, a
    /// shell-elemek utólag csúsznak be — lásd a jobbklikk-menü aszinkron
    /// betöltését.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Az alapérték MÉRÉSBŐL származik, nem becslésből. A fejlesztői gépen,
    /// egy fájlra (<c>notepad.exe</c>, 28 menüelem) mérve:
    /// </para>
    /// <list type="bullet">
    /// <item>hidegen, előmelegítés nélkül az első lekérdezés <b>2186 ms</b>;</item>
    /// <item>előmelegítés után (mappa + fájl, ~3 mp-cel az indulás után) az
    /// első lekérdezés <b>1132 ms</b>;</item>
    /// <item>a további lekérdezések mediánja <b>777 ms</b> (maximum 798 ms);</item>
    /// <item>mappára (<c>C:\Windows\System32</c>) ugyanez 1566 ms / 324 ms.</item>
    /// </list>
    /// <para>
    /// A v1.0 specifikációja 400 ms-ot javasolt, és 800 ms-ra való
    /// visszatérést, HA az előmelegítés utáni első lekérdezés stabilan 800 ms
    /// alatt marad. Nem marad: 1132 ms. Sőt, a fájlokra vonatkozó ÁLLANDÓSULT
    /// költség is 777 ms — a küszöböt épphogy súrolná, tehát a menük felénél
    /// levágná a bővítményeket.
    /// </para>
    /// <para>
    /// Az alapérték ezért az előmelegítés utáni legrosszabb mért értékből
    /// (1132 ms) számolt, 1,75-ös biztonsági szorzóval: <b>2000 ms</b>. Ez nem
    /// kerül semmibe: a menü megnyitása MÉRVE 96 ms (a saját elemek azonnal
    /// megjelennek), a shell elemek utólag csúsznak be, tehát a hosszabb
    /// időkorlát nem lassítja a felhasználót — csak azt engedi meg, hogy egy
    /// lassabb gépen is beérjenek.
    /// </para>
    /// </remarks>
    public int ShellMenuTimeoutMs { get; set; } = 2000;

    /// <summary>
    /// Kikapcsolt shell-bővítmények neve vagy CLSID-je. A lassú vagy hibás
    /// bővítményeket a felhasználó itt tudja kizárni.
    /// </summary>
    public List<string> ShellHandlerBlacklist { get; set; } = [];

    /// <summary>Igaz = a shell elemek külön „Egyéb alkalmazások" szekcióba, hamis = a saját elemek közé sorolva.</summary>
    public bool ShellItemsInOwnSection { get; set; } = true;

    // ---------- Szerkesztő ----------

    /// <summary>A beépített szerkesztő betűkészlete.</summary>
    public string EditorFontFamily { get; set; } = "Cascadia Mono";

    public double EditorFontSize { get; set; } = 13;

    public int EditorTabWidth { get; set; } = 4;

    /// <summary>Tabulátor helyett szóközök beszúrása.</summary>
    public bool EditorInsertSpaces { get; set; } = true;

    public bool EditorWordWrap { get; set; }

    public bool EditorShowLineNumbers { get; set; } = true;

    /// <summary>Új fájl alapértelmezett kódolása (<c>utf-8</c>, <c>utf-8-bom</c>, <c>cp1250</c>, …).</summary>
    public string EditorDefaultEncoding { get; set; } = "utf-8";

    /// <summary>Új fájl alapértelmezett sorvége: <c>CRLF</c>, <c>LF</c> vagy <c>CR</c>.</summary>
    public string EditorDefaultLineEnding { get; set; } = "CRLF";

    /// <summary>A szerkesztő külön ablakban (igaz) vagy a főablak fülében (hamis) nyíljon.</summary>
    public bool EditorInSeparateWindow { get; set; } = true;

    // ---------- Integrációk ----------

    /// <summary>A „Terminál megnyitása itt" parancs programja.</summary>
    public string ExternalTerminalPath { get; set; } = "wt.exe";

    // ---------- Speciális ----------

    /// <summary>Naplózási szint: <c>Information</c>, <c>Debug</c>, <c>Warning</c>, <c>Error</c>.</summary>
    public string LogLevel { get; set; } = "Information";

    /// <summary>Kétpaneles nézet be van-e kapcsolva.</summary>
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
    /// A v0.9-es, idegen terméknévvel futó kapcsoló.
    /// </summary>
    /// <remarks>
    /// CSAK a migráció miatt maradt itt (lásd <see cref="MigrateKeymap"/>): a
    /// régi <c>settings.json</c>-ökben ez a mező hordozza a felhasználó
    /// választását. Új kód SOHA ne olvassa — a <see cref="Keymap"/> az
    /// érvényes forrás.
    /// </remarks>
    [Obsolete("Csak a v0.9 -> v1.0 migrációhoz. Használd a Keymap tulajdonságot.")]
    public bool TotalCommanderKeybindingsEnabled { get; set; }

    /// <summary>
    /// A kiosztás nyers, mentett értéke.
    /// </summary>
    /// <remarks>
    /// Szándékosan <c>string</c>, nem közvetlenül az enum: így egy régi vagy
    /// kézzel elrontott érték sem akadályozza meg a betöltést, hanem a
    /// <see cref="KeymapPresetParser.Parse"/> képezi át (lásd az ottani
    /// alias-listát).
    /// </remarks>
    public string? KeymapPresetName { get; set; }

    /// <summary>Az érvényes billentyűkiosztás.</summary>
    public KeymapPreset Keymap
    {
        get => KeymapPresetParser.Parse(KeymapPresetName);
        set => KeymapPresetName = value.ToString();
    }

    /// <summary>
    /// A felhasználó egyedi billentyű-hozzárendelései: parancsazonosító →
    /// gesztus (pl. <c>"copy" -> "F5"</c>). Csak a
    /// <see cref="KeymapPreset.Custom"/> presetnél számít; a preset
    /// alapértékeit felülírja.
    /// </summary>
    public Dictionary<string, string> CustomKeyBindings { get; set; } = [];

    /// <summary>
    /// A v0.9-es kapcsoló átvétele az új <see cref="Keymap"/>-be. Egyszer fut
    /// le, az első v1.0-s indításkor, és a régi mezőt utána már nem olvassuk.
    /// </summary>
    public void MigrateKeymap()
    {
        if (KeymapPresetName is not null)
        {
            return;
        }

#pragma warning disable CS0618 // A migráció épp azért létezik, hogy ez legyen az UTOLSÓ olvasás.
        Keymap = TotalCommanderKeybindingsEnabled ? KeymapPreset.PilasterClassic : KeymapPreset.Explorer;
#pragma warning restore CS0618
    }

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
