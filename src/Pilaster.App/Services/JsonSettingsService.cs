using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;
using Pilaster.Core.Settings;

namespace Pilaster.App.Services;

/// <summary>
/// A <see cref="AppSettings"/> forrásgenerált szerializálási környezete.
/// </summary>
/// <remarks>
/// Forrásgenerálással a JSON-kezelés nem reflexióból dolgozik: gyorsabban indul,
/// és trimmelés mellett sem törik el. Egy fájlkezelőnél, ahol a hidegindulás
/// kiemelt szempont, ez nem elhanyagolható.
/// </remarks>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;

/// <summary>
/// Beállítások JSON-fájlban, a felhasználói profilban.
/// </summary>
public sealed class JsonSettingsService : ISettingsService, IDisposable
{
    /// <summary>
    /// Ennyit várunk az utolsó módosítás után a lemezre írással. Elég rövid,
    /// hogy egy váratlan leálláskor se vesszen el érdemi beállítás, és elég
    /// hosszú, hogy a gépelés ne írja a lemezt karakterenként.
    /// </summary>
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(600);

    private readonly string _filePath;
    private readonly DispatcherTimer _saveTimer;
    private readonly Lock _fileLock = new();

    public JsonSettingsService()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Pilaster");

        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "settings.json");

        Current = Load();

        _saveTimer = new DispatcherTimer { Interval = SaveDelay };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            Flush();
        };
    }

    public AppSettings Current { get; private set; }

    public event EventHandler? Changed;

    public void Save()
    {
        // Az időzítő újraindítása összevonja a gyors egymásutáni módosításokat.
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void NotifyChanged()
    {
        Changed?.Invoke(this, EventArgs.Empty);
        Save();
    }

    public void Flush()
    {
        lock (_fileLock)
        {
            try
            {
                var json = JsonSerializer.Serialize(Current, SettingsJsonContext.Default.AppSettings);

                // Atomi csere: előbb egy ideiglenes fájlba írunk, és csak a
                // sikeres írás után cseréljük le az élest. Így egy áramszünet
                // vagy összeomlás nem hagy hátra fél beállításfájlt.
                var temporary = _filePath + ".tmp";
                File.WriteAllText(temporary, json);
                File.Move(temporary, _filePath, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A beállítások mentése soha ne akassza meg a programot. A
                // felhasználó legfeljebb annyit vesz észre, hogy a választása
                // nem élte túl az újraindítást.
            }
        }
    }

    private AppSettings Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(_filePath);
            var loaded = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings);

            return loaded ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Sérült vagy olvashatatlan fájl: alapértékekkel indulunk, nem
            // hibaüzenettel. A következő mentés úgyis felülírja.
            return new AppSettings();
        }
    }

    public void Dispose()
    {
        _saveTimer.Stop();
        Flush();
    }
}
