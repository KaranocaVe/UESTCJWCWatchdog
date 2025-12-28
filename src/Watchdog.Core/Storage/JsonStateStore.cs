using System.Text.Encodings.Web;
using System.Text.Json;

namespace Watchdog.Core.Storage;

public sealed class JsonStateStore<T> where T : class
{
    private static readonly JsonSerializerOptions DefaultJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _path;

    public JsonStateStore(string path)
    {
        _path = path;
    }

    public async Task<T?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
            return null;

        var json = await File.ReadAllTextAsync(_path, cancellationToken);
        return JsonSerializer.Deserialize<T>(json, DefaultJsonOptions);
    }

    public async Task SaveAsync(T state, CancellationToken cancellationToken = default)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(state, DefaultJsonOptions);
        await File.WriteAllTextAsync(_path, json, cancellationToken);
    }
}

