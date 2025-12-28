using System.Text.Json.Serialization;

namespace Watchdog.Core.Storage;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ProfileState))]
public partial class WatchdogCoreJsonContext : JsonSerializerContext
{
}

