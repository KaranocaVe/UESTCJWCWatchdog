using System.Text.Json.Serialization;

namespace Watchdog.App.Models;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
public partial class WatchdogAppJsonContext : JsonSerializerContext
{
}

