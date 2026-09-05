using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Vanara.PInvoke;

namespace Pilaster.Setup.Services;

/// <summary>
/// Parancsikon (.lnk) létrehozása, a System.AppUserModel.ID tulajdonsággal
/// együtt — enélkül a tálcára rögzítés más ikont és külön csoportot kapna,
/// mint a futó Pilaster.exe folyamat (lásd Pilaster.App/App.xaml.cs —
/// AppUserModelId, és a korábbi installer/Pilaster.iss ugyanerre vonatkozó
/// megjegyzése). A Vanara "barátságos" ShellLink csomagolója erre nincs
/// felkészítve, ezért a nyers COM-objektumot (CShellLinkW) használjuk,
/// amit a Vanara.PInvoke.Shell32 már típusosan biztosít.
/// </summary>
public static class ShortcutBuilder
{
    // A Windows Property System dokumentált, nyilvános, stabil azonosítója —
    // ugyanaz minden Windows-verzión, nem egy Vanara-verziótól függő érték.
    private static readonly Guid AppUserModelIdFmtId = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");
    private const uint AppUserModelIdPid = 5;

    public static void CreateShortcut(string shortcutPath, string targetExePath, string workingDirectory, string appUserModelId)
    {
        var link = new Shell32.CShellLinkW();

        try
        {
            var shellLink = (Shell32.IShellLinkW)link;
            shellLink.SetPath(targetExePath);
            shellLink.SetWorkingDirectory(workingDirectory);
            shellLink.SetIconLocation(targetExePath, 0);

            var propertyKey = new Ole32.PROPERTYKEY(AppUserModelIdFmtId, AppUserModelIdPid);
            var propertyStore = (PropSys.IPropertyStore)link;

            using (var propVariant = new Ole32.PROPVARIANT(appUserModelId))
            {
                propertyStore.SetValue(propertyKey, propVariant);
                propertyStore.Commit();
            }

            var persistFile = (IPersistFile)link;
            persistFile.Save(shortcutPath, false);
        }
        finally
        {
            Marshal.ReleaseComObject(link);
        }
    }
}
