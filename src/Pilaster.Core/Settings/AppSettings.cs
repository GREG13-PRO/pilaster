namespace Pilaster.Core.Settings;

/// <summary>A felület színsémája.</summary>
public enum ThemeMode
{
    /// <summary>A Windows beállítását követi, és váltáskor magától igazodik.</summary>
    System,

    Light,

    Dark,
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

    /// <summary>
    /// A felület nyelve kultúrakóddal, vagy <c>null</c>, ha a rendszernyelvet
    /// kell követni. A <c>null</c> nem ugyanaz, mint egy konkrét kód: ha a
    /// felhasználó később átállítja a Windows nyelvét, a <c>null</c> követi,
    /// a rögzített kód nem.
    /// </summary>
    public string? Language { get; set; }

    public bool ShowHiddenItems { get; set; }

    public bool AnimationsEnabled { get; set; } = true;

    /// <summary>
    /// Áttetsző „liquid glass" felület — az oldalsáv, a felső sáv, a
    /// jobbklikk-menük és a Beállítások panel áttetsző rétegként jelennek
    /// meg a Mica háttér felett. Gyengébb gépeken kikapcsolható.
    /// </summary>
    public bool LiquidGlassEnabled { get; set; } = true;

    /// <summary>Az utoljára használt nézetmód — új fül ezzel nyílik.</summary>
    public Pilaster.Core.FileSystem.ViewMode LastViewMode { get; set; } = Pilaster.Core.FileSystem.ViewMode.Details;

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
