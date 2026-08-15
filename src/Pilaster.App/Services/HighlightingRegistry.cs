using System.IO;
using System.Windows;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace Pilaster.App.Services;

/// <summary>
/// A saját szintaxis-definíciók regisztrálása az AvalonEdit
/// <see cref="HighlightingManager"/>-ébe.
/// </summary>
/// <remarks>
/// Az AvalonEdit beépített készlete a legtöbb nyelvet fedi (C#, JavaScript,
/// HTML, XML, CSS, C++, Java, PHP, PowerShell, SQL, Markdown, …), de a spec
/// által kiemelten kért <c>.yml</c>/<c>.sk</c> és az <c>.ini</c>-család
/// hiányzik belőle — MÉRVE: enélkül ezek a fájlok kiemelés nélküli sima
/// szövegként nyíltak meg. Ezt a két definíciót ezért magunk szállítjuk.
/// </remarks>
public static class HighlightingRegistry
{
    private static bool _registered;

    /// <summary>Egyszer futtatandó, az első szerkesztő-megnyitás előtt.</summary>
    public static void EnsureRegistered()
    {
        if (_registered)
        {
            return;
        }

        _registered = true;

        Register("Yaml.xshd", [".yml", ".yaml", ".sk"]);
        Register("Ini.xshd", [".ini", ".cfg", ".conf", ".properties"]);

        // A .log és a .sh nincs a beépített készletben, és nincs is értelmes
        // kiemelésük — ezeket szándékosan sima szövegként hagyjuk.
    }

    private static void Register(string fileName, string[] extensions)
    {
        try
        {
            var uri = new Uri($"pack://application:,,,/Pilaster;component/Resources/Highlighting/{fileName}");
            using var stream = Application.GetResourceStream(uri)?.Stream;

            if (stream is null)
            {
                Serilog.Log.Warning("A szintaxiskiemelés erőforrása nem található: {File}", fileName);
                return;
            }

            using var reader = new XmlTextReader(stream);
            var definition = HighlightingLoader.Load(reader, HighlightingManager.Instance);

            HighlightingManager.Instance.RegisterHighlighting(definition.Name, extensions, definition);
        }
        catch (Exception ex) when (ex is XmlException or IOException or HighlightingDefinitionInvalidException)
        {
            Serilog.Log.Warning(ex, "A szintaxiskiemelés betöltése nem sikerült: {File}", fileName);
            // Egy hibás definíció nem akadályozhatja meg a szerkesztő
            // megnyitását — legfeljebb kiemelés nélkül jelenik meg a fájl.
        }
    }
}
