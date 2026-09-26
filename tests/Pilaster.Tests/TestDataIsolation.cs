using System.Runtime.CompilerServices;
using Pilaster.App.Services;

namespace Pilaster.Tests;

/// <summary>
/// A tesztkészlet SOHA ne írjon a fejlesztő valódi Pilaster-adataiba.
/// </summary>
/// <remarks>
/// A modul-inicializáló MINDEN teszt előtt lefut, és egy futásonként új,
/// ideiglenes adatmappát állít be (<see cref="AppDataLocator.OverrideVariable"/>).
/// Ezt örökli minden, a tesztekből indított Pilaster-folyamat (öntesztek) és
/// minden folyamaton belül létrehozott szolgáltatás is. Korábban az öntesztek
/// a valódi <c>%APPDATA%\Pilaster</c> fájljait írták — egy teszt által
/// létrehozott „Önteszt" címke így megjelent a fejlesztő saját oldalsávjában,
/// a párhuzamosan futó öntesztek pedig egymás beállításfájlját írták felül.
/// </remarks>
internal static class TestDataIsolation
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pilaster-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        Environment.SetEnvironmentVariable(AppDataLocator.OverrideVariable, directory);
    }
}
