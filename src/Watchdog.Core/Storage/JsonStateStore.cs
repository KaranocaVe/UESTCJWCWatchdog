using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Watchdog.Core.Storage;

public sealed class JsonStateStore<T> where T : class
{
    private readonly string _path;
    private readonly JsonTypeInfo<T> _typeInfo;

    public JsonStateStore(string path, JsonTypeInfo<T> typeInfo)
    {
        _path = path;
        _typeInfo = typeInfo ?? throw new ArgumentNullException(nameof(typeInfo));
    }

    public async Task<T?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
            return null;

        var json = await File.ReadAllTextAsync(_path, cancellationToken);
        return JsonSerializer.Deserialize(json, _typeInfo);
    }

    public async Task SaveAsync(T state, CancellationToken cancellationToken = default)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(state, _typeInfo);
        await File.WriteAllTextAsync(_path, json, cancellationToken);
    }
}
