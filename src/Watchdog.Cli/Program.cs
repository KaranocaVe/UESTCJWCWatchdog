using Watchdog.Core;
using Watchdog.Core.Eams;
using Watchdog.Core.Storage;

static bool ParseBool(string? value, bool defaultValue)
{
    if (string.IsNullOrWhiteSpace(value))
        return defaultValue;

    return value.Trim().ToLowerInvariant() switch
    {
        "1" => true,
        "0" => false,
        "true" => true,
        "false" => false,
        "yes" => true,
        "no" => false,
        _ => defaultValue,
    };
}

static string? GetArgValue(string[] args, string name)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            return args[i + 1];
    }

    return null;
}

static float? ParseFloat(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
        return null;

    return float.TryParse(value, out var f) ? f : null;
}

static (int width, int height)? ParseWindowSize(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
        return null;

    var trimmed = value.Trim();
    var parts = trimmed.Split(['x', 'X', ',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    if (parts.Length != 2)
        return null;

    return int.TryParse(parts[0], out var w) && int.TryParse(parts[1], out var h) && w > 0 && h > 0
        ? (w, h)
        : null;
}

var envPath = Path.Combine(Environment.CurrentDirectory, ".env");
var env = DotEnv.Load(envPath);
var account = DotEnv.GetRequired(env, "account");
var password = DotEnv.GetRequired(env, "password");

var semesterId = GetArgValue(args, "--semester");
var headless = ParseBool(GetArgValue(args, "--headless") ?? Environment.GetEnvironmentVariable("HEADLESS"), defaultValue: false);
var headlessMode = GetArgValue(args, "--headless-mode") ?? Environment.GetEnvironmentVariable("HEADLESS_MODE") ?? "playwright";
var headlessArg = GetArgValue(args, "--headless-arg") ?? Environment.GetEnvironmentVariable("HEADLESS_ARG") ?? "--headless=new";
var legacyArgs = ParseBool(GetArgValue(args, "--legacy-args") ?? Environment.GetEnvironmentVariable("LEGACY_ARGS"), defaultValue: true);
var noViewport = ParseBool(GetArgValue(args, "--no-viewport") ?? Environment.GetEnvironmentVariable("NO_VIEWPORT"), defaultValue: false);
var bypassCsp = ParseBool(GetArgValue(args, "--bypass-csp") ?? Environment.GetEnvironmentVariable("BYPASS_CSP"), defaultValue: false);
var stealthScripts = ParseBool(GetArgValue(args, "--stealth-scripts") ?? Environment.GetEnvironmentVariable("STEALTH_SCRIPTS"), defaultValue: false);
var userAgent = GetArgValue(args, "--user-agent") ?? Environment.GetEnvironmentVariable("USER_AGENT");
var windowSize = ParseWindowSize(GetArgValue(args, "--window-size") ?? Environment.GetEnvironmentVariable("WINDOW_SIZE")) ?? (1920, 1080);
var channel = GetArgValue(args, "--channel") ?? Environment.GetEnvironmentVariable("BROWSER_CHANNEL") ?? "chrome";
var executablePath = GetArgValue(args, "--executable-path") ?? Environment.GetEnvironmentVariable("EXECUTABLE_PATH");
var userDataDirOverride = GetArgValue(args, "--user-data-dir") ?? Environment.GetEnvironmentVariable("USER_DATA_DIR");
var antibotWaitMsRaw = GetArgValue(args, "--antibot-wait-ms") ?? Environment.GetEnvironmentVariable("ANTIBOT_WAIT_MS");
var antibotWaitMs = int.TryParse(antibotWaitMsRaw, out var parsed) ? parsed : 60_000;
var printFingerprint = ParseBool(GetArgValue(args, "--fingerprint") ?? Environment.GetEnvironmentVariable("FINGERPRINT"), defaultValue: false);
var printCookies = ParseBool(GetArgValue(args, "--cookies") ?? Environment.GetEnvironmentVariable("COOKIES"), defaultValue: false);
var slowmo = ParseFloat(GetArgValue(args, "--slowmo") ?? Environment.GetEnvironmentVariable("SLOWMO_MS"));
var debugDir = GetArgValue(args, "--debug-dir") ?? Environment.GetEnvironmentVariable("DEBUG_DIR");

var extraHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
void AddHeaderFromEnvOrArg(string headerName, string argName, string envName)
{
    var value = GetArgValue(args, argName) ?? Environment.GetEnvironmentVariable(envName);
    if (!string.IsNullOrWhiteSpace(value))
        extraHeaders[headerName] = value;
}

AddHeaderFromEnvOrArg("accept-language", "--accept-language", "ACCEPT_LANGUAGE");
AddHeaderFromEnvOrArg("sec-ch-ua", "--sec-ch-ua", "SEC_CH_UA");
AddHeaderFromEnvOrArg("sec-ch-ua-mobile", "--sec-ch-ua-mobile", "SEC_CH_UA_MOBILE");
AddHeaderFromEnvOrArg("sec-ch-ua-platform", "--sec-ch-ua-platform", "SEC_CH_UA_PLATFORM");

var profileDir = AppPaths.GetProfileDir(account);
var userDataDir = string.IsNullOrWhiteSpace(userDataDirOverride)
    ? Path.Combine(profileDir, "user-data")
    : userDataDirOverride.Trim();
Directory.CreateDirectory(userDataDir);

var statePath = Path.Combine(profileDir, "state.json");
var storageStatePath = Path.Combine(profileDir, "storage-state.json");

var stateStore = new JsonStateStore<ProfileState>(statePath);
var state = await stateStore.LoadAsync() ?? new ProfileState { Account = account };

var options = new EamsClientOptions
{
    UserDataDir = userDataDir,
    Headless = headless,
    SlowMoMs = slowmo,
    HeadlessImplementation = headlessMode.Equals("arg", StringComparison.OrdinalIgnoreCase)
        ? HeadlessImplementation.ChromiumArg
        : HeadlessImplementation.Playwright,
    ChromiumHeadlessArg = headlessArg,
    ApplyLegacySpiderArgs = legacyArgs,
    WindowWidth = windowSize.width,
    WindowHeight = windowSize.height,
    NoViewport = noViewport,
    UserAgent = userAgent,
    Channel = channel,
    ExecutablePath = executablePath,
    AntiBotWaitMs = antibotWaitMs,
    BypassCsp = bypassCsp,
    EnableStealthScripts = stealthScripts,
    DiagnosticsDir = debugDir,
    ExtraHttpHeaders = extraHeaders.Count == 0 ? null : extraHeaders,
};

await using var client = new EamsClient(options);
if (printFingerprint)
{
    Console.WriteLine(await client.GetFingerprintJsonAsync());
    return;
}
if (printCookies)
{
    foreach (var (name, domain, path) in await client.GetCookiesAsync())
        Console.WriteLine($"{name}\t{domain}\t{path}");
    return;
}
GradesSnapshot snapshot;
try
{
    snapshot = await client.GetGradesSnapshotAsync(new EamsCredentials(account, password), semesterId);
}
catch
{
    if (!string.IsNullOrWhiteSpace(debugDir))
        await client.DumpDebugAsync(debugDir);
    throw;
}

state.SemesterId = snapshot.SemesterId;
state.LastFetchedAt = snapshot.FetchedAt;
state.FinalGrades = snapshot.FinalGrades.ToList();
state.UsualGrades = snapshot.UsualGrades.ToList();
await stateStore.SaveAsync(state);

await client.SaveStorageStateAsync(storageStatePath);

Console.WriteLine($"OK: semester={snapshot.SemesterId}, final={snapshot.FinalGrades.Count}, usual={snapshot.UsualGrades.Count}, at={snapshot.FetchedAt:yyyy-MM-dd HH:mm:ss}");
