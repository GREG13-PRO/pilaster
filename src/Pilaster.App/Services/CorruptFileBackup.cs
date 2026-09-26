using System.IO;

namespace Pilaster.App.Services;

/// <summary>
/// Sérült/olvashatatlan JSON-adatfájl félretétele, mielőtt az alkalmazás
/// alapértékekkel folytatná.
/// </summary>
/// <remarks>
/// Minden adatfájl (beállítások, címkék, gyorselérés, felhő meghajtók)
/// betöltési hibánál üres dokumentummal indul — a következő mentés viszont
/// ezt írná rá az eredetire, és a felhasználó adatai VÉGLEG elvesznének.
/// Egy időbélyeges másolattal kézzel visszamenthetők maradnak.
/// </remarks>
internal static class CorruptFileBackup
{
    public static void Preserve(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Copy(path, path + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), overwrite: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Legjobb-erőfeszítés: ha a másolat sem készíthető el, nincs jobb teendő.
        }
    }
}
