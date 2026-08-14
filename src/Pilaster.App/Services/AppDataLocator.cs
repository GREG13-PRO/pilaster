using System.IO;

namespace Pilaster.App.Services;

/// <summary>
/// Hol tárolja az alkalmazás a felhasználó adatait (beállítások, címkék,
/// gyorselérés).
/// </summary>
/// <remarks>
/// <para>
/// Normál telepítésnél ez a <c>%APPDATA%\Pilaster</c>. HORDOZHATÓ módban
/// viszont a program saját mappáján belüli <c>config</c> könyvtár — így a
/// Pilaster pendrive-ról futtatva sem hagy nyomot a gépen, és a beállításai
/// vele utaznak.
/// </para>
/// <para>
/// A módot egy jelzőfájl (<c>portable.marker</c>) jelenléte dönti el a
/// futtatható fájl mellett; ezt a telepítő hozza létre a „Hordozható"
/// telepítési típusnál. Szándékosan FÁJL, nem beállítás: a beállításokat
/// magukat is ez alapján kell megtalálni, tehát nem lehet bennük tárolni.
/// </para>
/// </remarks>
public static class AppDataLocator
{
    private const string PortableMarkerFileName = "portable.marker";

    private static readonly Lazy<string> LazyDirectory = new(Resolve);

    /// <summary>Az adatmappa. Létrehozásáról a hívó gondoskodik.</summary>
    public static string Directory => LazyDirectory.Value;

    /// <summary>Igaz, ha hordozható módban futunk.</summary>
    public static bool IsPortable { get; private set; }

    private static string Resolve()
    {
        var exeDirectory = Path.GetDirectoryName(Environment.ProcessPath ?? string.Empty);

        if (!string.IsNullOrEmpty(exeDirectory)
            && File.Exists(Path.Combine(exeDirectory, PortableMarkerFileName)))
        {
            IsPortable = true;
            return Path.Combine(exeDirectory, "config");
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Pilaster");
    }
}
