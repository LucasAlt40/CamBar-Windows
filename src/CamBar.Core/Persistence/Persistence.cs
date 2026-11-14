using System.Text.Json;
using System.Text.Json.Nodes;

namespace CamBar.Core;

/// <summary>Where the camera list is persisted.</summary>
public interface ICameraRepository
{
    List<Camera> Load();
    void Save(IReadOnlyList<Camera> cameras);
}

public sealed class JsonFileCameraRepository : ICameraRepository
{
    private readonly string _path;

    public JsonFileCameraRepository(string? path = null) =>
        _path = path ?? Path.Combine(AppPaths.DataDirectory, "cameras.json");

    public List<Camera> Load()
    {
        try
        {
            return File.Exists(_path)
                ? JsonSerializer.Deserialize<List<Camera>>(File.ReadAllText(_path), CameraJson.Options) ?? []
                : [];
        }
        catch (Exception ex) when (ex is JsonException or IOException) { return []; }
    }

    public void Save(IReadOnlyList<Camera> cameras) =>
        JsonFile.WriteAtomically(_path, JsonSerializer.Serialize(cameras, CameraJson.Options));
}

/// <summary>In-memory repository for checks.</summary>
public sealed class InMemoryCameraRepository : ICameraRepository
{
    public List<Camera> Cameras { get; private set; }

    public InMemoryCameraRepository(IEnumerable<Camera>? cameras = null) => Cameras = (cameras ?? []).ToList();

    public List<Camera> Load() => Cameras.ToList();
    public void Save(IReadOnlyList<Camera> cameras) => Cameras = cameras.ToList();
}

/// <summary>Small preferences (keep connected, pinned camera…).</summary>
public interface ISettings
{
    string? GetString(string key);
    bool? GetBool(string key);
    double? GetDouble(string key);
    void Set(string key, string value);
    void Set(string key, bool value);
    void Set(string key, double value);
    void Remove(string key);
}

public sealed class JsonSettings : ISettings
{
    private readonly string _path;
    private readonly object _gate = new();
    private JsonObject _root;

    public JsonSettings(string? path = null)
    {
        _path = path ?? Path.Combine(AppPaths.DataDirectory, "settings.json");
        try { _root = File.Exists(_path) ? JsonNode.Parse(File.ReadAllText(_path)) as JsonObject ?? new() : new(); }
        catch (Exception ex) when (ex is JsonException or IOException) { _root = new(); }
    }

    public string? GetString(string key) { lock (_gate) return _root[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null; }
    public bool? GetBool(string key) { lock (_gate) return _root[key] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null; }
    public double? GetDouble(string key) { lock (_gate) return _root[key] is JsonValue v && v.TryGetValue<double>(out var d) ? d : null; }

    public void Set(string key, string value) => Store(key, JsonValue.Create(value));
    public void Set(string key, bool value) => Store(key, JsonValue.Create(value));
    public void Set(string key, double value) => Store(key, JsonValue.Create(value));
    public void Remove(string key) => Store(key, null);

    private void Store(string key, JsonNode? value)
    {
        lock (_gate)
        {
            _root[key] = value;
            if (value is null) _root.Remove(key);
            JsonFile.WriteAtomically(_path, _root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}

/// <summary>In-memory settings for checks.</summary>
public sealed class InMemorySettings : ISettings
{
    private readonly Dictionary<string, object> _values = [];

    public string? GetString(string key) => _values.GetValueOrDefault(key) as string;
    public bool? GetBool(string key) => _values.GetValueOrDefault(key) as bool?;
    public double? GetDouble(string key) => _values.GetValueOrDefault(key) as double?;
    public void Set(string key, string value) => _values[key] = value;
    public void Set(string key, bool value) => _values[key] = value;
    public void Set(string key, double value) => _values[key] = value;
    public void Remove(string key) => _values.Remove(key);
}

internal static class JsonFile
{
    /// <summary>Writes to a temp file and swaps it in, so a crash never leaves half a file.</summary>
    public static void WriteAtomically(string path, string contents)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, contents);
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
