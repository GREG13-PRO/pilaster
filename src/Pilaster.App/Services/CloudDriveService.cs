using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;
using Pilaster.Core.Settings;

namespace Pilaster.App.Services;

/// <summary>A <see cref="CloudDriveDocument"/> forrásgenerált szerializálási környezete.</summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(CloudDriveDocument))]
internal sealed partial class CloudDriveJsonContext : JsonSerializerContext;

/// <summary>
/// A csatlakoztatott felhő meghajtók (NextCloud stb.) perzisztens tárolása.
/// </summary>
/// <remarks>
/// Ugyanazt a mentési mintát követi, mint a <see cref="QuickAccessService"/>
/// (300 ms-os összevont mentés egy külön, verziózott JSON-fájlba), de nála
/// jóval egyszerűbb: nincs sorrend-átrendezés-drag, nincs elérhetőség-
/// gyorsítótár (az oldalsáv erre a <c>QuickAccessService.IsReachable</c>-t
/// hívja újra, ugyanúgy, ahogy a Gyorselérés bejegyzéseinél).
/// </remarks>
public sealed class CloudDriveService : IDisposable
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(300);

    private readonly string _filePath;
    private readonly DispatcherTimer _saveTimer;
    private readonly Lock _fileLock = new();

    private CloudDriveDocument _document;

    public CloudDriveService(string? storageDirectory = null)
    {
        var directory = storageDirectory ?? AppDataLocator.Directory;

        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "clouddrives.json");

        _document = Load();

        _saveTimer = new DispatcherTimer { Interval = SaveDelay };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            Flush();
        };
    }

    public event EventHandler? Changed;

    public IReadOnlyList<CloudDriveEntry> Entries => [.. _document.Entries.OrderBy(e => e.Order)];

    public void Add(string label, string serverUrl, string uncPath)
    {
        _document.Entries.Add(new CloudDriveEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            Label = label,
            ServerUrl = serverUrl,
            UncPath = uncPath,
            Order = _document.Entries.Count,
        });

        NotifyChanged();
    }

    public void Remove(string id)
    {
        var removed = _document.Entries.RemoveAll(e => e.Id == id);

        if (removed > 0)
        {
            NotifyChanged();
        }
    }

    private void NotifyChanged()
    {
        Changed?.Invoke(this, EventArgs.Empty);
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private CloudDriveDocument Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return new CloudDriveDocument();
            }

            var loaded = JsonSerializer.Deserialize(File.ReadAllText(_filePath), CloudDriveJsonContext.Default.CloudDriveDocument);

            return loaded ?? new CloudDriveDocument();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new CloudDriveDocument();
        }
    }

    public void Flush()
    {
        try
        {
            lock (_fileLock)
            {
                var json = JsonSerializer.Serialize(_document, CloudDriveJsonContext.Default.CloudDriveDocument);
                File.WriteAllText(_filePath, json);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Legjobb-erőfeszítéses mentés — ugyanaz a döntés, mint a QuickAccessService-nél.
        }
    }

    public void Dispose()
    {
        _saveTimer.Stop();
        Flush();
    }
}
