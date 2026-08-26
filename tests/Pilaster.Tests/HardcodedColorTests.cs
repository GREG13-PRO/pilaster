using System.Text.RegularExpressions;

namespace Pilaster.Tests;

/// <summary>
/// Regressziós védelem a B1 hiba ellen: minden szín téma-tokenből jöjjön.
/// </summary>
/// <remarks>
/// A v0.9-es hiba („világos módban nem minden vált világosra") részben azért
/// tudott létrejönni, mert a színek szétszórtan, beégetve éltek a
/// komponensekben. A gyökérokot (a <c>GlassPanelBrush</c> beragadását)
/// javítottuk, de az igazi védelem az, ha ÚJ beégetett szín nem tud
/// észrevétlenül bekerülni. Ez a teszt pontosan ezt őrzi.
/// </remarks>
public class HardcodedColorTests
{
    /// <summary>
    /// A repó gyökere. A teszt a bin mappából fut, ezért felfelé keressük a
    /// megoldásfájlt — így a teszt független attól, honnan indították.
    /// </summary>
    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Pilaster.slnx")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName
                ?? throw new InvalidOperationException("A repó gyökere (Pilaster.slnx) nem található.");
        }
    }

    private static IEnumerable<string> SourceFiles(string extension) =>
        Directory
            .EnumerateFiles(Path.Combine(RepoRoot, "src"), extension, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    /// <summary>
    /// XAML-ben egyáltalán nem lehet beégetett hex szín.
    /// </summary>
    /// <remarks>
    /// Nincs kivétel-lista: a Pilaster MINDEN tokenje kódban él
    /// (<c>ThemeTokenService</c>), tehát nem létezik olyan XAML, aminek
    /// jogosan kellene hexet tartalmaznia.
    /// </remarks>
    [Fact]
    public void XamlDoesNotContainHardcodedColors()
    {
        var pattern = new Regex(@"#(?:[0-9A-Fa-f]{3,4}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})\b");

        var offenders = SourceFiles("*.xaml")
            .SelectMany(file => File.ReadLines(file)
                .Select((line, index) => (file, number: index + 1, line))
                .Where(x => pattern.IsMatch(x.line) && !IsComment(x.line)))
            .Select(x => $"{Relative(x.file)}:{x.number}  {x.line.Trim()}")
            .ToList();

        Assert.True(offenders.Count == 0, JoinOffenders(offenders));
    }

    /// <summary>
    /// C#-ban is elbújhat beégetett szín — a színeket ott is központi helyről
    /// kell venni.
    /// </summary>
    /// <remarks>
    /// Négy fájl kivétel, és mindegyiknél a színkészlet MAGA a tartalom:
    /// a <c>ThemeTokenService</c> definiálja a tokeneket, a <c>TagPalette</c>
    /// a címkék fix, szándékosan nem téma-függő palettáját, az
    /// <c>AccentColorService</c> a választható akcentus-swatch-öket, a
    /// <c>QuickAccessService</c> pedig a Gyors elérés alapértelmezett
    /// mappáinak (Asztal, Dokumentumok stb.), a Lomtárnak és a Felhő
    /// meghajtóknak a fix, szándékosan nem téma-függő ikonszíneit.
    /// </remarks>
    [Fact]
    public void CSharpDoesNotContainHardcodedColorsOutsidePaletteFiles()
    {
        string[] allowed =
        [
            "Services/ThemeTokenService.cs",
            "Services/AccentColorService.cs",
            "Services/QuickAccessService.cs",
            "Converters/Converters.cs",
            "ViewModels/QuickAccessEditorViewModel.cs",
        ];

        var pattern = new Regex(
            @"Color\.FromRgb\(|Color\.FromArgb\(|new\s+SolidColorBrush\(|\bColors\.[A-Z]|""#[0-9A-Fa-f]{3,8}""");

        var offenders = SourceFiles("*.cs")
            .Where(file => !allowed.Any(a => Normalize(file).EndsWith(a, StringComparison.Ordinal)))
            .SelectMany(file => File.ReadLines(file)
                .Select((line, index) => (file, number: index + 1, line))
                .Where(x => pattern.IsMatch(x.line) && !IsComment(x.line)))
            .Select(x => $"{Relative(x.file)}:{x.number}  {x.line.Trim()}")
            .ToList();

        Assert.True(offenders.Count == 0, JoinOffenders(offenders));
    }

    /// <summary>
    /// A háttér- és szövegszínek DynamicResource-ból jöjjenek, ne
    /// StaticResource-ból: az utóbbi az induláskori témára fagy, és pontosan
    /// ez okozta a bejelentett hibát.
    /// </summary>
    [Fact]
    public void XamlUsesDynamicResourceForThemeBrushes()
    {
        var pattern = new Regex(
            @"(?:Background|Foreground|BorderBrush|Fill|Stroke)\s*=\s*""\{StaticResource\s+(\w+)");

        var offenders = SourceFiles("*.xaml")
            .SelectMany(file => File.ReadLines(file)
                .Select((line, index) => (file, number: index + 1, line))
                .Where(x => pattern.IsMatch(x.line) && !IsComment(x.line)))
            .Select(x => $"{Relative(x.file)}:{x.number}  {x.line.Trim()}")
            .ToList();

        Assert.True(offenders.Count == 0, JoinOffenders(offenders));
    }

    /// <summary>
    /// Minden <c>ThemeTokenService</c>-ben deklarált szín-token kapjon is
    /// értéket — egy elgépelt kulcs némán feloldatlan kötést hagyna.
    /// </summary>
    [Fact]
    public void EveryDeclaredTokenIsAssigned()
    {
        var source = File.ReadAllText(
            Path.Combine(RepoRoot, "src", "Pilaster.App", "Services", "ThemeTokenService.cs"));

        var declared = Regex
            .Matches(source, @"public const string (\w+) = ""(\w+)"";")
            .Select(m => m.Groups[1].Value)
            .ToList();

        // Az akcentus-eredetű tokeneket az AccentColorService írja, a
        // sűrűség-tokeneket az ApplyDensity — mindkettő ugyanebben a fájlban
        // vagy közvetlenül hivatkozva szerepel.
        var accentAssigned = File.ReadAllText(
            Path.Combine(RepoRoot, "src", "Pilaster.App", "Services", "AccentColorService.cs"));

        var missing = declared
            .Where(name =>
                !Regex.IsMatch(source, $@"(Set\(app, {name},|app\.Resources\[{name}\])")
                && !Regex.IsMatch(accentAssigned, $@"ThemeTokenService\.{name}\b"))
            .ToList();

        Assert.True(missing.Count == 0, "Deklarált, de sosem beállított token: " + string.Join(", ", missing));
    }

    private static bool IsComment(string line)
    {
        var trimmed = line.TrimStart();

        return trimmed.StartsWith("<!--", StringComparison.Ordinal)
            || trimmed.StartsWith("//", StringComparison.Ordinal)
            || trimmed.StartsWith("///", StringComparison.Ordinal)
            || trimmed.StartsWith("*", StringComparison.Ordinal);
    }

    private static string Normalize(string path) => path.Replace('\\', '/');

    private static string Relative(string path) =>
        Normalize(path)[(Normalize(RepoRoot).Length + 1)..];

    private static string JoinOffenders(IReadOnlyList<string> offenders) =>
        offenders.Count == 0
            ? string.Empty
            : $"{offenders.Count} beégetett szín:{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}";
}
