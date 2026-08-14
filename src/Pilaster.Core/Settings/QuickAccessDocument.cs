namespace Pilaster.Core.Settings;

/// <summary>Egy gyorselérés-bejegyzés fajtája.</summary>
public enum QuickAccessEntryKind
{
    /// <summary>Egy mappára (vagy a virtuális Kezdőlapra) mutató, kattintható sor.</summary>
    Folder,

    /// <summary>Vízszintes elválasztó vonal — csak vizuális tagolás.</summary>
    Separator,
}

/// <summary>
/// Egyetlen bejegyzés a gyorselérésben.
/// </summary>
/// <remarks>
/// Az <see cref="Id"/> stabil azonosító, nem az útvonal: így egy bejegyzés
/// útvonala átírható anélkül, hogy a sorrendje vagy a rá mutató hivatkozások
/// elvesznének, és két, ugyanarra a mappára mutató bejegyzés is
/// megkülönböztethető marad.
/// </remarks>
public sealed class QuickAccessEntry
{
    public required string Id { get; set; }

    public QuickAccessEntryKind Kind { get; set; } = QuickAccessEntryKind.Folder;

    /// <summary>
    /// A megjelenő felirat. Szándékosan FÜGGETLEN a mappa nevétől: a
    /// felhasználó „Munka" néven rögzíthet egy mélyen fekvő projektmappát.
    /// Üresen hagyva a mappa saját neve jelenik meg.
    /// </summary>
    public string? Label { get; set; }

    /// <summary>
    /// Fordítási kulcs az előre definiált mappákhoz (pl. <c>Nav_Desktop</c>),
    /// hogy nyelvváltáskor a felirat is kövesse. Egyéni bejegyzésnél <c>null</c>.
    /// </summary>
    public string? LabelKey { get; set; }

    public string Path { get; set; } = string.Empty;

    /// <summary>A WPF-UI ikon neve (pl. <c>Folder24</c>).</summary>
    public string Icon { get; set; } = "Folder24";

    /// <summary>Egyedi ikonszín <c>#RRGGBB</c> alakban, vagy <c>null</c> az örökölt szöveg-színhez.</summary>
    public string? Color { get; set; }

    /// <summary>Opcionális csoportfejléc, ami e bejegyzés FÖLÖTT jelenik meg.</summary>
    public string? Group { get; set; }

    /// <summary>
    /// Igaz a kézzel rögzített bejegyzésekre. Hamis a „Legutóbbi" szekció
    /// automatikus elemeire, amiket a program maga tart karban.
    /// </summary>
    public bool Pinned { get; set; } = true;

    /// <summary>Sorrend a szekción belül; a szerkesztő húzással írja át.</summary>
    public int Order { get; set; }

    /// <summary>Hamis értékkel a bejegyzés megmarad, de nem jelenik meg az oldalsávban.</summary>
    public bool Visible { get; set; } = true;

    /// <summary>Legutóbbi elemeknél a megnyitás ideje — ez adja a sorrendjüket.</summary>
    public DateTimeOffset? LastOpenedUtc { get; set; }
}

/// <summary>
/// A gyorselérés teljes, lemezre írt állapota — <c>%APPDATA%\Pilaster\quickaccess.json</c>.
/// </summary>
/// <remarks>
/// Szándékosan KÜLÖN fájl a <c>settings.json</c>-tól: ez a felhasználó saját
/// tartalma (mint a <c>metadata.json</c>), nem alkalmazásbeállítás, és így
/// önállóan exportálható/importálható is.
/// </remarks>
public sealed class QuickAccessDocument
{
    /// <summary>A séma verziója — a migrációhoz, lásd <c>QuickAccessService.Migrate</c>.</summary>
    public int Version { get; set; } = CurrentVersion;

    /// <summary>A jelenlegi séma verziója.</summary>
    public const int CurrentVersion = 1;

    public List<QuickAccessEntry> Entries { get; set; } = [];

    /// <summary>Karbantartja-e a program a „Legutóbbi" szekciót.</summary>
    public bool RecentEnabled { get; set; } = true;

    /// <summary>Hány elem maradjon meg a „Legutóbbi" szekcióban.</summary>
    public int RecentLimit { get; set; } = 8;
}
