using Microsoft.Win32;

namespace Pilaster.Shell.Integration;

/// <summary>Egy korábbi registry-verb mentése visszaállításhoz.</summary>
/// <param name="Captured">Igaz, ha ez a bejegyzés ténylegesen mentett állapotot hordoz.</param>
/// <param name="Existed">Igaz, ha maga a verb-kulcs (pl. <c>shell\open</c>) LÉTEZETT bekapcsolás előtt.</param>
/// <param name="CommandValue">
/// Az eredeti <c>command</c> alkulcs alapértelmezett értéke, ha <see cref="Existed"/>
/// igaz és a <c>command</c> alkulcs is létezett — egyébként <c>null</c>.
/// </param>
/// <param name="DefaultVerbCaptured">
/// Igaz, ha a szülő <c>shell</c> kulcs alapértelmezett igéje is mentve van.
/// A v1.2.2 előtti mentésekben hamis — lásd <see cref="ShellIntegrationService.Restore"/>.
/// </param>
/// <param name="DefaultVerbValue">A <c>shell</c> kulcs korábbi alapértelmezett igéje; <c>null</c>, ha nem volt.</param>
public readonly record struct RegistryBackup(
    bool Captured,
    bool Existed,
    string? CommandValue,
    bool DefaultVerbCaptured = false,
    string? DefaultVerbValue = null)
{
    public static readonly RegistryBackup None = new(false, false, null);
}

/// <summary>
/// A Pilaster elsődleges fájlkezelőként: a Directory/Drive „open" parancs és
/// az Intéző indítása (Win+E, „Új ablak") átirányítása, valamint a
/// jobbklikk-menü bejegyzés — kizárólag <c>HKEY_CURRENT_USER</c> alatt, tehát
/// admin jog (UAC) nélkül: a <c>HKCU\Software\Classes</c> a hivatalosan
/// támogatott, felhasználónkénti felülbírálási pont, amit maga az Intéző is
/// figyelembe vesz a gépenkénti (HKLM) beállítás előtt.
/// </summary>
/// <remarks>
/// Minden módosítás előtt elmenti az ÉRINTETT kulcsok pontos előző állapotát,
/// a visszaállítás tehát nem egyszerű törlés, hanem pontosan ugyanoda áll
/// vissza, ahonnan indult.
/// </remarks>
public static class ShellIntegrationService
{
    private const string DirectoryShellKey = @"Software\Classes\Directory\shell";
    private const string DriveShellKey = @"Software\Classes\Drive\shell";
    private const string DirectoryOpenVerbKey = DirectoryShellKey + @"\open";
    private const string DriveOpenVerbKey = DriveShellKey + @"\open";
    private const string ContextMenuVerbKey = DirectoryShellKey + @"\PilasterOpen";

    /// <summary>
    /// Az „Intéző" shell-objektum (Win+E, a tálca/Start „Fájlkezelő" új
    /// ablaka) indítóparancsa. Ugyanezt írják felül más Intéző-kiváltók is.
    /// </summary>
    private const string ExplorerLaunchClsidKey = @"Software\Classes\CLSID\{52205fd8-5dfb-447d-801a-d0b52f2e83e1}";
    private const string ExplorerLaunchCommandKey = ExplorerLaunchClsidKey + @"\shell\opennewwindow\command";

    /// <summary>
    /// Windows 11-en a <c>Directory\shell</c> és a <c>Drive\shell</c>
    /// alapértelmezett igéje „none", így a dupla kattintás a <c>Folder</c>
    /// osztály (az Intéző) igéjére esik vissza. Csak az <c>open</c> parancs
    /// felülírása ezért NEM elég: az igét alapértelmezetté is kell tenni.
    /// MÉRVE: enélkül csak a kifejezetten „open"-t kérő hívások jutottak el a
    /// Pilasterig, a dupla kattintás továbbra is az Intézőt nyitotta.
    /// </summary>
    private const string OpenVerbName = "open";

    /// <summary>
    /// A mappa-háttér és a meghajtó jobbklikk-verb kulcsa — a telepítő
    /// (Pilaster.Setup) mindhárom helyre felteszi a "Megnyitás Pilaster-ben"
    /// bejegyzést, a futásidejű Beállítások viszont csak a
    /// <see cref="ContextMenuVerbKey"/>-et (a fájl-elemek verbjét) kapcsolja.
    /// </summary>
    public const string BackgroundContextMenuVerbKey = @"Software\Classes\Directory\Background\shell\PilasterOpen";
    public const string DriveContextMenuVerbKey = DriveShellKey + @"\PilasterOpen";

    /// <summary>A registry gyökere — a tesztek egy eldobható alkulcsra cserélik, hogy ne a valódi beállításokat írják.</summary>
    internal static RegistryKey Root { get; set; } = Registry.CurrentUser;

    /// <summary>A jelenlegi állapot mentése visszaállításhoz — hívd a bekapcsolás ELŐTT.</summary>
    public static RegistryBackup Backup(string verbKeyPath)
    {
        var defaultVerb = ReadDefaultVerb(ParentPath(verbKeyPath));
        using var verbKey = Root.OpenSubKey(verbKeyPath);

        if (verbKey is null)
        {
            return new RegistryBackup(Captured: true, Existed: false, CommandValue: null, DefaultVerbCaptured: true, DefaultVerbValue: defaultVerb);
        }

        using var commandKey = verbKey.OpenSubKey("command");
        return new RegistryBackup(true, true, commandKey?.GetValue(null) as string, true, defaultVerb);
    }

    /// <summary>Pontos visszaállítás egy korábbi <see cref="Backup"/> alapján.</summary>
    public static void Restore(string verbKeyPath, RegistryBackup backup)
    {
        if (!backup.Captured)
        {
            return;
        }

        var shellKeyPath = ParentPath(verbKeyPath);

        if (backup.DefaultVerbCaptured)
        {
            WriteDefaultVerb(shellKeyPath, backup.DefaultVerbValue);
        }
        else if (ReadDefaultVerb(shellKeyPath) == OpenVerbName)
        {
            // Régi (v1.2.2 előtti) mentés: az alapértelmezett igét akkor még
            // nem írtuk, tehát ha most „open", azt mi állítottuk be.
            WriteDefaultVerb(shellKeyPath, null);
        }

        if (!backup.Existed)
        {
            // A verb-kulcs maga sem létezett — a TELJES ágat töröljük, amit
            // létrehoztunk, üres kulcsot sem hagyva magunk után.
            DeleteKeyTree(verbKeyPath);
            return;
        }

        using var verbKey = Root.CreateSubKey(verbKeyPath);

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

    /// <summary>
    /// Mappák és meghajtók megnyitása (dupla kattintás, más programok
    /// „megnyitás" kérése) a megadott futtatható fájllal.
    /// </summary>
    public static void SetFolderOpenCommand(string exePath)
    {
        var commandLine = $"\"{exePath}\" \"%1\"";
        WriteCommand(DirectoryOpenVerbKey, commandLine);
        WriteCommand(DriveOpenVerbKey, commandLine);
        WriteDefaultVerb(DirectoryShellKey, OpenVerbName);
        WriteDefaultVerb(DriveShellKey, OpenVerbName);
    }

    public static RegistryBackup BackupDirectoryOpen() => Backup(DirectoryOpenVerbKey);

    public static RegistryBackup BackupDriveOpen() => Backup(DriveOpenVerbKey);

    public static void RestoreDirectoryOpen(RegistryBackup backup) => Restore(DirectoryOpenVerbKey, backup);

    public static void RestoreDriveOpen(RegistryBackup backup) => Restore(DriveOpenVerbKey, backup);

    /// <summary>
    /// A Win+E és az Intéző „Új ablak" parancsa a megadott futtatható fájlt
    /// indítsa. Registry-alapú, tehát akkor is hat, ha a Pilaster épp nem fut.
    /// </summary>
    /// <remarks>
    /// Az üres <c>DelegateExecute</c> kötelező: e nélkül a gépenkénti (HKLM)
    /// COM-kezelő marad érvényben, és a parancs figyelmen kívül marad.
    /// </remarks>
    public static void SetExplorerLaunchCommand(string exePath)
    {
        using var commandKey = Root.CreateSubKey(ExplorerLaunchCommandKey);
        commandKey.SetValue(null, $"\"{exePath}\"");
        commandKey.SetValue("DelegateExecute", string.Empty);
    }

    /// <summary>Az Intéző-indítás felülbírálásának eltávolítása — a Windows ismét a saját Intézőjét nyitja.</summary>
    /// <remarks>
    /// A HKCU alatt ezt a CLSID-t gyárilag semmi nem használja; csak a saját
    /// <c>opennewwindow</c> águnkat töröljük, és utána a kiürült szülőkulcsokat.
    /// </remarks>
    public static void RemoveExplorerLaunchCommand()
    {
        DeleteKeyTree(ParentPath(ExplorerLaunchCommandKey));
        DeleteKeyIfEmpty(ExplorerLaunchClsidKey + @"\shell");
        DeleteKeyIfEmpty(ExplorerLaunchClsidKey);
    }

    /// <summary>Igaz, ha a Win+E/Intéző-indítás jelenleg a megadott futtatható fájlra mutat.</summary>
    public static bool IsExplorerLaunchRedirectedTo(string exePath)
    {
        using var commandKey = Root.OpenSubKey(ExplorerLaunchCommandKey);
        return commandKey?.GetValue(null) is string command
            && command.Contains(exePath, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Eltávolításkor: minden átirányítás, ami a megadott mappában lévő
    /// programra mutat, visszaáll a Windows alapértelmezésére. Enélkül egy
    /// Beállításokban bekapcsolt átirányítás túlélte az eltávolítást, és a
    /// mappák dupla kattintásra egy már nem létező programot kerestek.
    /// </summary>
    public static void RemoveRedirectsTo(string installDirectory)
    {
        RemoveFolderOpenRedirectsTo(installDirectory);

        if (CommandPointsInto(ParentPath(ExplorerLaunchCommandKey), AsDirectoryPrefix(installDirectory)))
        {
            RemoveExplorerLaunchCommand();
        }
    }

    /// <summary>
    /// A mappa-/meghajtó-megnyitás átirányításának eltávolítása, ha a megadott
    /// mappában lévő programra mutat — mentés nélküli visszaállítás a Windows
    /// alapértelmezésére (pl. a telepítő kapcsolta be, nem a Beállítások).
    /// </summary>
    public static void RemoveFolderOpenRedirectsTo(string installDirectory)
    {
        var directory = AsDirectoryPrefix(installDirectory);

        foreach (var (shellKeyPath, verbKeyPath) in new[] { (DirectoryShellKey, DirectoryOpenVerbKey), (DriveShellKey, DriveOpenVerbKey) })
        {
            if (!CommandPointsInto(verbKeyPath, directory))
            {
                continue;
            }

            DeleteKeyTree(verbKeyPath);

            if (ReadDefaultVerb(shellKeyPath) == OpenVerbName)
            {
                WriteDefaultVerb(shellKeyPath, null);
            }
        }
    }

    /// <summary>Igaz, ha a mappák dupla kattintásra a megadott futtatható fájlt indítják.</summary>
    public static bool IsFolderOpenRedirectedTo(string exePath)
    {
        using var commandKey = Root.OpenSubKey(DirectoryOpenVerbKey + @"\command");
        return ReadDefaultVerb(DirectoryShellKey) == OpenVerbName
            && commandKey?.GetValue(null) is string command
            && command.Contains(exePath, StringComparison.OrdinalIgnoreCase);
    }

    private static string AsDirectoryPrefix(string directory) => directory.TrimEnd('\\', '/') + "\\";

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
        using (var verbKey = Root.CreateSubKey(verbKeyPath))
        {
            verbKey.SetValue(null, displayLabel);
            verbKey.SetValue("Icon", $"\"{iconPath}\"");
        }

        WriteCommand(verbKeyPath, $"\"{exePath}\" \"{placeholder}\"");
    }

    public static void RemoveContextMenuEntry() => RemoveContextMenuEntry(ContextMenuVerbKey);

    public static void RemoveContextMenuEntry(string verbKeyPath) => DeleteKeyTree(verbKeyPath);

    private static bool CommandPointsInto(string verbKeyPath, string directory)
    {
        using var commandKey = Root.OpenSubKey(verbKeyPath + @"\command");
        return commandKey?.GetValue(null) is string command
            && command.TrimStart('"').StartsWith(directory, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadDefaultVerb(string shellKeyPath)
    {
        using var shellKey = Root.OpenSubKey(shellKeyPath);
        return shellKey?.GetValue(null) as string;
    }

    private static void WriteDefaultVerb(string shellKeyPath, string? verb)
    {
        if (verb is null)
        {
            using var existing = Root.OpenSubKey(shellKeyPath, writable: true);
            existing?.DeleteValue(string.Empty, throwOnMissingValue: false);
            return;
        }

        using var shellKey = Root.CreateSubKey(shellKeyPath);
        shellKey.SetValue(null, verb);
    }

    private static void WriteCommand(string verbKeyPath, string commandLine)
    {
        using var verbKey = Root.CreateSubKey(verbKeyPath);
        using var commandKey = verbKey.CreateSubKey("command");
        commandKey.SetValue(null, commandLine);
    }

    private static string ParentPath(string keyPath) => keyPath[..keyPath.LastIndexOf('\\')];

    private static void DeleteKeyTree(string keyPath)
    {
        using var parent = Root.OpenSubKey(ParentPath(keyPath), writable: true);
        parent?.DeleteSubKeyTree(keyPath[(keyPath.LastIndexOf('\\') + 1)..], throwOnMissingSubKey: false);
    }

    private static void DeleteKeyIfEmpty(string keyPath)
    {
        using (var key = Root.OpenSubKey(keyPath))
        {
            if (key is null || key.SubKeyCount > 0 || key.ValueCount > 0)
            {
                return;
            }
        }

        DeleteKeyTree(keyPath);
    }
}
