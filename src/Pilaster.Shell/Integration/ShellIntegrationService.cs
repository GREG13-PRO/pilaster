using Microsoft.Win32;

namespace Pilaster.Shell.Integration;

/// <summary>Egy korábbi registry-verb mentése visszaállításhoz.</summary>
/// <param name="Captured">Igaz, ha ez a bejegyzés ténylegesen mentett állapotot hordoz.</param>
/// <param name="Existed">Igaz, ha maga a verb-kulcs (pl. <c>shell\open</c>) LÉTEZETT bekapcsolás előtt.</param>
/// <param name="CommandValue">
/// Az eredeti <c>command</c> alkulcs alapértelmezett értéke, ha <see cref="Existed"/>
/// igaz és a <c>command</c> alkulcs is létezett — egyébként <c>null</c>.
/// </param>
public readonly record struct RegistryBackup(bool Captured, bool Existed, string? CommandValue)
{
    public static readonly RegistryBackup None = new(false, false, null);
}

/// <summary>
/// A Directory/Drive „open" parancs átirányítása és a jobbklikk-menü
/// bejegyzés — kizárólag <see cref="Registry.CurrentUser"/> alatt, tehát
/// admin jog (UAC) nélkül működik: a <c>HKCU\Software\Classes</c> a
/// hivatalosan támogatott, felhasználónkénti felülbírálási pont, amit maga
/// az Intéző is figyelembe vesz a gépenkénti (HKLM) beállítás előtt.
/// </summary>
/// <remarks>
/// Minden módosítás előtt elmenti az ÉRINTETT verb-kulcs pontos előző
/// állapotát (létezett-e, és ha igen, mi volt a parancsa) — a visszaállítás
/// emiatt nem egyszerű törlés, hanem pontosan ugyanoda áll vissza, ahonnan
/// indult: ha a verb-kulcs (pl. <c>Directory\shell\open</c>) korábban
/// EGYÁLTALÁN nem létezett, kikapcsoláskor a TELJES kulcsot törli (nem csak a
/// benne lévő <c>command</c> alkulcsot) — enélkül üres <c>shell\open</c>
/// kulcs maradna a registryben, ami ugyan ártalmatlan, de nem „pontos"
/// visszaállítás.
/// </remarks>
public static class ShellIntegrationService
{
    private const string DirectoryOpenVerbKey = @"Software\Classes\Directory\shell\open";
    private const string DriveOpenVerbKey = @"Software\Classes\Drive\shell\open";
    private const string ContextMenuVerbKey = @"Software\Classes\Directory\shell\PilasterOpen";

    /// <summary>
    /// A mappa-háttér és a meghajtó jobbklikk-verb kulcsa — a telepítő
    /// (Pilaster.Setup) mindhárom helyre felteszi a "Megnyitás Pilaster-ben"
    /// bejegyzést, a futásidejű Beállítások viszont csak a
    /// <see cref="ContextMenuVerbKey"/>-et (a fájl-elemek verbjét) kapcsolja.
    /// </summary>
    public const string BackgroundContextMenuVerbKey = @"Software\Classes\Directory\Background\shell\PilasterOpen";
    public const string DriveContextMenuVerbKey = @"Software\Classes\Drive\shell\PilasterOpen";

    /// <summary>A jelenlegi állapot mentése visszaállításhoz — hívd a bekapcsolás ELŐTT.</summary>
    public static RegistryBackup Backup(string verbKeyPath)
    {
        using var verbKey = Registry.CurrentUser.OpenSubKey(verbKeyPath);

        if (verbKey is null)
        {
            return new RegistryBackup(Captured: true, Existed: false, CommandValue: null);
        }

        using var commandKey = verbKey.OpenSubKey("command");
        return new RegistryBackup(Captured: true, Existed: true, CommandValue: commandKey?.GetValue(null) as string);
    }

    /// <summary>Pontos visszaállítás egy korábbi <see cref="Backup"/> alapján.</summary>
    public static void Restore(string verbKeyPath, RegistryBackup backup)
    {
        if (!backup.Captured)
        {
            return;
        }

        if (!backup.Existed)
        {
            // A verb-kulcs maga sem létezett — a TELJES ágat töröljük, amit
            // létrehoztunk, üres kulcsot sem hagyva magunk után.
            DeleteVerbKey(verbKeyPath, recursive: true);
            return;
        }

        using var verbKey = Registry.CurrentUser.CreateSubKey(verbKeyPath);

        if (backup.CommandValue is null)
        {
            verbKey.DeleteSubKey("command", throwOnMissingSubKey: false);
        }
        else
        {
            using var commandKey = verbKey.CreateSubKey("command");
            commandKey.SetValue(null, backup.CommandValue);
        }
    }

    /// <summary>Mappa/meghajtó „Megnyitás" parancsának átirányítása a megadott futtatható fájlra.</summary>
    public static void SetFolderOpenCommand(string exePath)
    {
        var commandLine = $"\"{exePath}\" \"%1\"";
        WriteCommand(DirectoryOpenVerbKey, commandLine);
        WriteCommand(DriveOpenVerbKey, commandLine);
    }

    public static RegistryBackup BackupDirectoryOpen() => Backup(DirectoryOpenVerbKey);

    public static RegistryBackup BackupDriveOpen() => Backup(DriveOpenVerbKey);

    public static void RestoreDirectoryOpen(RegistryBackup backup) => Restore(DirectoryOpenVerbKey, backup);

    public static void RestoreDriveOpen(RegistryBackup backup) => Restore(DriveOpenVerbKey, backup);

    /// <summary>
    /// „Megnyitás Pilaster-ben" jobbklikk-menü bejegyzés hozzáadása mappákhoz.
    /// Tisztán ADDITÍV — új, korábban nem létező verbet hoz létre, tehát nincs
    /// mit visszamenteni: kikapcsoláskor egyszerűen törlődik a teljes ág.
    /// </summary>
    public static void AddContextMenuEntry(string exePath, string displayLabel, string iconPath) =>
        AddContextMenuEntry(ContextMenuVerbKey, "%1", exePath, displayLabel, iconPath);

    /// <summary>
    /// Ugyanaz, de tetszőleges verb-kulcsra (lásd <see cref="BackgroundContextMenuVerbKey"/>,
    /// <see cref="DriveContextMenuVerbKey"/>) és parancssori helyettesítő tokenre — a
    /// mappa-háttér verbje <c>%V</c>-t vár (a háttéren jobbklikkelt mappa útvonalát),
    /// a fájl- és meghajtó-verbek <c>%1</c>-et.
    /// </summary>
    public static void AddContextMenuEntry(string verbKeyPath, string placeholder, string exePath, string displayLabel, string iconPath)
    {
        using (var verbKey = Registry.CurrentUser.CreateSubKey(verbKeyPath))
        {
            verbKey.SetValue(null, displayLabel);
            verbKey.SetValue("Icon", $"\"{iconPath}\"");
        }

        WriteCommand(verbKeyPath, $"\"{exePath}\" \"{placeholder}\"");
    }

    public static void RemoveContextMenuEntry() => RemoveContextMenuEntry(ContextMenuVerbKey);

    public static void RemoveContextMenuEntry(string verbKeyPath) => DeleteVerbKey(verbKeyPath, recursive: true);

    private static void WriteCommand(string verbKeyPath, string commandLine)
    {
        using var verbKey = Registry.CurrentUser.CreateSubKey(verbKeyPath);
        using var commandKey = verbKey.CreateSubKey("command");
        commandKey.SetValue(null, commandLine);
    }

    private static void DeleteVerbKey(string verbKeyPath, bool recursive)
    {
        var separatorIndex = verbKeyPath.LastIndexOf('\\');
        var parentPath = verbKeyPath[..separatorIndex];
        var leafName = verbKeyPath[(separatorIndex + 1)..];

        using var parent = Registry.CurrentUser.OpenSubKey(parentPath, writable: true);

        if (parent is null)
        {
            return;
        }

        if (recursive)
        {
            parent.DeleteSubKeyTree(leafName, throwOnMissingSubKey: false);
        }
        else
        {
            parent.DeleteSubKey(leafName, throwOnMissingSubKey: false);
        }
    }
}
