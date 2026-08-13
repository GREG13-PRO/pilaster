using System.Reflection;

namespace Pilaster.App.Diagnostics;

/// <summary>
/// Melyik GitHub repóból jönnek a Pilaster kiadásai.
/// </summary>
/// <remarks>
/// Az azonosító a Pilaster.App.csproj <c>AssemblyMetadata</c> elemeiből
/// olvasódik — ez az egyetlen hely, ahol szerepel, nincs szétszórva a
/// forrásban. Aki saját forkból fordítja, csak ott kell módosítania.
/// </remarks>
public static class GitHubRepositoryInfo
{
    public static string Owner { get; } = ReadMetadata("RepositoryOwner", "GREG13-PRO");

    public static string Name { get; } = ReadMetadata("RepositoryName", "pilaster");

    public static string ReleasesApiUrl => $"https://api.github.com/repos/{Owner}/{Name}/releases/latest";

    public static string ReleasesPageUrl => $"https://github.com/{Owner}/{Name}/releases";

    private static string ReadMetadata(string key, string fallback)
    {
        var value = Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == key)?
            .Value;

        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}
