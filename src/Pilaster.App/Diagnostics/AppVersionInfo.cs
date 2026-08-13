using System.Reflection;

namespace Pilaster.App.Diagnostics;

/// <summary>Az alkalmazás verziószáma, hibajelentésekhez és diagnosztikához.</summary>
public static class AppVersionInfo
{
    public static string Current { get; } = Resolve();

    private static string Resolve()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational))
        {
            return assembly.GetName().Version?.ToString() ?? "dev";
        }

        // A build-metaadat utótagot (pl. "+a1b2c3d" commit-hash) levágjuk: a
        // felhasználónak és a hibajelentésnek a verziószám számít, nem a hash.
        var plusIndex = informational.IndexOf('+');
        return plusIndex >= 0 ? informational[..plusIndex] : informational;
    }
}
