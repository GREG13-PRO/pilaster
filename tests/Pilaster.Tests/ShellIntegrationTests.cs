using Microsoft.Win32;
using Pilaster.Shell.Integration;

namespace Pilaster.Tests;

/// <summary>
/// Az elsődleges fájlkezelő mód registry-műveletei. Egy eldobható
/// <c>HKCU\Software\PilasterTests\…</c> kulcs alatt futnak (lásd
/// <see cref="ShellIntegrationService.Root"/>), a valódi beállításokhoz nem nyúlnak.
/// </summary>
public sealed class ShellIntegrationTests : IDisposable
{
    private const string DirectoryShell = @"Software\Classes\Directory\shell";
    private const string DriveShell = @"Software\Classes\Drive\shell";
    private const string ExplorerClsid = @"Software\Classes\CLSID\{52205fd8-5dfb-447d-801a-d0b52f2e83e1}";
    private const string PilasterExe = @"C:\Apps\Pilaster\Pilaster.exe";

    private readonly string _rootPath = $@"Software\PilasterTests\{Guid.NewGuid():N}";
    private readonly RegistryKey _root;
    private readonly RegistryKey _previousRoot;

    public ShellIntegrationTests()
    {
        _root = Registry.CurrentUser.CreateSubKey(_rootPath);
        _previousRoot = ShellIntegrationService.Root;
        ShellIntegrationService.Root = _root;
    }

    public void Dispose()
    {
        ShellIntegrationService.Root = _previousRoot;
        _root.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_rootPath, throwOnMissingSubKey: false);

        using var parent = Registry.CurrentUser.OpenSubKey(@"Software\PilasterTests", writable: true);

        if (parent is { SubKeyCount: 0, ValueCount: 0 })
        {
            Registry.CurrentUser.DeleteSubKey(@"Software\PilasterTests", throwOnMissingSubKey: false);
        }
    }

    private string? DefaultValue(string keyPath)
    {
        using var key = _root.OpenSubKey(keyPath);
        return key?.GetValue(null) as string;
    }

    private bool Exists(string keyPath)
    {
        using var key = _root.OpenSubKey(keyPath);
        return key is not null;
    }

    [Fact]
    public void BekapcsolasAzOpenIgetAlapertelmezetteTeszi()
    {
        // Windows 11-en a Directory\shell alapértelmezett igéje „none" — csak a
        // parancs felülírásával a dupla kattintás az Intézőnél maradt.
        ShellIntegrationService.SetFolderOpenCommand(PilasterExe);

        Assert.Equal("open", DefaultValue(DirectoryShell));
        Assert.Equal("open", DefaultValue(DriveShell));
        Assert.Equal($"\"{PilasterExe}\" \"%1\"", DefaultValue(DirectoryShell + @"\open\command"));
        Assert.Equal($"\"{PilasterExe}\" \"%1\"", DefaultValue(DriveShell + @"\open\command"));
        Assert.True(ShellIntegrationService.IsFolderOpenRedirectedTo(PilasterExe));
    }

    [Fact]
    public void KikapcsolasPontosanVisszaallitjaAzUresAllapotot()
    {
        using (_root.CreateSubKey(DirectoryShell + @"\PilasterOpen"))
        {
        }

        var directory = ShellIntegrationService.BackupDirectoryOpen();
        var drive = ShellIntegrationService.BackupDriveOpen();
        ShellIntegrationService.SetFolderOpenCommand(PilasterExe);

        ShellIntegrationService.RestoreDirectoryOpen(directory);
        ShellIntegrationService.RestoreDriveOpen(drive);

        Assert.Null(DefaultValue(DirectoryShell));
        Assert.Null(DefaultValue(DriveShell));
        Assert.False(Exists(DirectoryShell + @"\open"));
        Assert.False(Exists(DriveShell + @"\open"));
        Assert.True(Exists(DirectoryShell + @"\PilasterOpen"));
    }

    [Fact]
    public void KikapcsolasMegtartjaAKorabbiSajatBeallitast()
    {
        using (var shell = _root.CreateSubKey(DirectoryShell))
        {
            shell.SetValue(null, "masik");
        }

        using (var command = _root.CreateSubKey(DirectoryShell + @"\open\command"))
        {
            command.SetValue(null, "\"C:\\Masik\\fm.exe\" \"%1\"");
        }

        var backup = ShellIntegrationService.BackupDirectoryOpen();
        ShellIntegrationService.SetFolderOpenCommand(PilasterExe);
        ShellIntegrationService.RestoreDirectoryOpen(backup);

        Assert.Equal("masik", DefaultValue(DirectoryShell));
        Assert.Equal("\"C:\\Masik\\fm.exe\" \"%1\"", DefaultValue(DirectoryShell + @"\open\command"));
    }

    [Fact]
    public void RegiMentesVisszaallitasaIsLeveszAzOpenAlapertelmezest()
    {
        // v1.2.2 előtti mentés: az alapértelmezett igéről nem tud.
        var legacy = new RegistryBackup(Captured: true, Existed: false, CommandValue: null);
        ShellIntegrationService.SetFolderOpenCommand(PilasterExe);

        ShellIntegrationService.RestoreDirectoryOpen(legacy);

        Assert.Null(DefaultValue(DirectoryShell));
        Assert.False(Exists(DirectoryShell + @"\open"));
    }

    [Fact]
    public void WinEAtiranyitasRegistryAlapuEsNyomtalanulTorolheto()
    {
        ShellIntegrationService.SetExplorerLaunchCommand(PilasterExe);

        using (var command = _root.OpenSubKey(ExplorerClsid + @"\shell\opennewwindow\command"))
        {
            Assert.NotNull(command);
            Assert.Equal($"\"{PilasterExe}\"", command.GetValue(null));
            Assert.Equal(string.Empty, command.GetValue("DelegateExecute"));
        }

        Assert.True(ShellIntegrationService.IsExplorerLaunchRedirectedTo(PilasterExe));

        ShellIntegrationService.RemoveExplorerLaunchCommand();

        Assert.False(Exists(ExplorerClsid));
    }

    [Fact]
    public void EltavolitasCsakAzAdottMappabaMutatoAtiranyitasokatTorli()
    {
        ShellIntegrationService.SetFolderOpenCommand(PilasterExe);
        ShellIntegrationService.SetExplorerLaunchCommand(PilasterExe);

        // Hasonló nevű, de másik mappa: érintetlen marad.
        ShellIntegrationService.RemoveRedirectsTo(@"C:\Apps\Pilaster2");
        Assert.True(ShellIntegrationService.IsFolderOpenRedirectedTo(PilasterExe));
        Assert.True(ShellIntegrationService.IsExplorerLaunchRedirectedTo(PilasterExe));

        ShellIntegrationService.RemoveRedirectsTo(@"C:\Apps\Pilaster\");

        Assert.Null(DefaultValue(DirectoryShell));
        Assert.Null(DefaultValue(DriveShell));
        Assert.False(Exists(DirectoryShell + @"\open"));
        Assert.False(Exists(DriveShell + @"\open"));
        Assert.False(Exists(ExplorerClsid));
    }

    [Fact]
    public void MeghajtoGyokerParancssorbolIsFelismerheto()
    {
        // A "%1" → "C:\" parancssort a Windows C:" alakra bontja.
        Assert.Equal(@"C:\", Pilaster.App.App.ParseFolderArgument(["Pilaster.exe", "C:\""]));
        Assert.Equal(Path.GetTempPath().TrimEnd('\\'), Pilaster.App.App.ParseFolderArgument(["Pilaster.exe", Path.GetTempPath().TrimEnd('\\')]));
        Assert.Null(Pilaster.App.App.ParseFolderArgument(["Pilaster.exe"]));
        Assert.Null(Pilaster.App.App.ParseFolderArgument(["Pilaster.exe", @"C:\Nem\Letezo\Mappa-" + Guid.NewGuid().ToString("N")]));
    }
}
