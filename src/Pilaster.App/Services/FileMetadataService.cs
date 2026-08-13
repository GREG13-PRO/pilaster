using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;
using Pilaster.Core.Metadata;

namespace Pilaster.App.Services;

/// <summary>
/// A <see cref="FileMetadataDocument"/> forrásgenerált szerializálási környezete.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(FileMetadataDocument))]
internal sealed partial class FileMetadataJsonContext : JsonSerializerContext;

/// <summary>
/// Címkék és kedvencek tárolása — külön fájlban a beállításoktól
/// (<c>metadata.json</c>, a felhasználói profilban), mert ez a felhasználó
/// TARTALMÁRA vonatkozó adat, nem alkalmazásbeállítás. A Windows natív
/// fájlrendszerét nem módosítja: sem ADS-t, sem attribútumot nem ír.
/// </summary>
public sealed class FileMetadataService : IDisposable
{
    /// <summary>Lásd <c>JsonSettingsService</c> — ugyanaz az elv: rövid, gépelést összevonó késleltetés.</summary>
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(600);

    private readonly string _filePath;
    private readonly DispatcherTimer _saveTimer;
    private readonly Lock _fileLock = new();
    private FileMetadataDocument _document;

    public FileMetadataService()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Pilaster");

        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "metadata.json");

        _document = Load();

        _saveTimer = new DispatcherTimer { Interval = SaveDelay };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            Flush();
        };
    }

    /// <summary>Bármilyen változás (címke, kedvenc) után tüzel — a feliratkozók újra lekérdezik, amit nézniük kell.</summary>
    public event EventHandler? Changed;

    public IReadOnlyList<TagDefinition> Tags => _document.Tags;

    public IReadOnlyList<TagDefinition> GetTags(string path) =>
        _document.Items.TryGetValue(path, out var entry) && entry.TagIds.Count > 0
            ? _document.Tags.Where(t => entry.TagIds.Contains(t.Id)).ToList()
            : [];

    public bool IsFavorite(string path) =>
        _document.Items.TryGetValue(path, out var entry) && entry.IsFavorite;

    public void AddTag(string path, string tagId)
    {
        var entry = GetOrCreateEntry(path);

        if (entry.TagIds.Contains(tagId))
        {
            return;
        }

        entry.TagIds.Add(tagId);
        NotifyChanged();
    }

    public void RemoveTag(string path, string tagId)
    {
        if (!_document.Items.TryGetValue(path, out var entry) || !entry.TagIds.Remove(tagId))
        {
            return;
        }

        PruneIfEmpty(path, entry);
        NotifyChanged();
    }

    public void ToggleFavorite(string path) => SetFavorite(path, !IsFavorite(path));

    public void SetFavorite(string path, bool value)
    {
        var entry = GetOrCreateEntry(path);

        if (entry.IsFavorite == value)
        {
            return;
        }

        entry.IsFavorite = value;
        PruneIfEmpty(path, entry);
        NotifyChanged();
    }

    public TagDefinition CreateTag(string name, TagColor color)
    {
        var tag = new TagDefinition { Id = Guid.NewGuid().ToString("N"), Name = name, Color = color };
        _document.Tags.Add(tag);
        NotifyChanged();
        return tag;
    }

    public void RenameTag(string tagId, string name)
    {
        if (_document.Tags.FirstOrDefault(t => t.Id == tagId) is not { } tag || tag.Name == name)
        {
            return;
        }

        tag.Name = name;
        NotifyChanged();
    }

    public void DeleteTag(string tagId)
    {
        if (_document.Tags.RemoveAll(t => t.Id == tagId) == 0)
        {
            return;
        }

        var affected = _document.Items.Where(kv => kv.Value.TagIds.Remove(tagId)).Select(kv => kv.Key).ToList();

        foreach (var path in affected)
        {
            PruneIfEmpty(path, _document.Items[path]);
        }

        NotifyChanged();
    }

    public IReadOnlyList<string> GetPathsWithTag(string tagId) =>
        _document.Items.Where(kv => kv.Value.TagIds.Contains(tagId)).Select(kv => kv.Key).ToList();

    public IReadOnlyList<string> GetFavoritePaths() =>
        _document.Items.Where(kv => kv.Value.IsFavorite).Select(kv => kv.Key).ToList();

    private FileMetadataEntry GetOrCreateEntry(string path)
    {
        if (_document.Items.TryGetValue(path, out var entry))
        {
            return entry;
        }

        entry = new FileMetadataEntry();
        _document.Items[path] = entry;
        return entry;
    }

    private void PruneIfEmpty(string path, FileMetadataEntry entry)
    {
        if (entry.IsEmpty)
        {
            _document.Items.Remove(path);
        }
    }

    private void NotifyChanged()
    {
        Changed?.Invoke(this, EventArgs.Empty);
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void Flush()
    {
        lock (_fileLock)
        {
            try
            {
                var json = JsonSerializer.Serialize(_document, FileMetadataJsonContext.Default.FileMetadataDocument);
                var temporary = _filePath + ".tmp";
                File.WriteAllText(temporary, json);
                File.Move(temporary, _filePath, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Lásd JsonSettingsService.Flush: egy sikertelen mentés soha ne
                // akassza meg a programot.
            }
        }
    }

    private FileMetadataDocument Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return new FileMetadataDocument { Items = new(StringComparer.OrdinalIgnoreCase) };
            }

            var json = File.ReadAllText(_filePath);
            var loaded = JsonSerializer.Deserialize(json, FileMetadataJsonContext.Default.FileMetadataDocument)
                ?? new FileMetadataDocument();

            // A kis-nagybetű-érzéketlen összehasonlító nem éli túl a
            // szerializálást — a betöltött szótárból újat kell építeni vele.
            return new FileMetadataDocument
            {
                Tags = loaded.Tags,
                Items = new Dictionary<string, FileMetadataEntry>(loaded.Items, StringComparer.OrdinalIgnoreCase),
            };
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new FileMetadataDocument { Items = new(StringComparer.OrdinalIgnoreCase) };
        }
    }

    public void Dispose()
    {
        _saveTimer.Stop();
        Flush();
    }
}
