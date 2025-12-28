using System.Text;

namespace Watchdog.Core;

public static class DotEnv
{
    public static IReadOnlyDictionary<string, string> Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Unable to find .env file.", path);

        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in File.ReadLines(path, Encoding.UTF8))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            var equalsIndex = line.IndexOf('=');
            if (equalsIndex <= 0)
                continue;

            var key = line[..equalsIndex].Trim();
            var value = line[(equalsIndex + 1)..].Trim();
            if (key.Length == 0)
                continue;

            dict[key] = Unquote(value);
        }

        return dict;
    }

    public static string GetRequired(IReadOnlyDictionary<string, string> env, string key)
    {
        if (!env.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Missing required key '{key}' in .env.");

        return value;
    }

    private static string Unquote(string value)
    {
        if (value.Length < 2)
            return value;

        var first = value[0];
        var last = value[^1];
        if ((first == '"' && last == '"') || (first == '\'' && last == '\''))
            return value[1..^1];

        return value;
    }
}

