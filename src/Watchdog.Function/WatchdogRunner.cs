using Watchdog.Core.Eams;
using Watchdog.Core.Notifications;

namespace Watchdog.Function;

public sealed record InvokeRequest
{
    public required string Topic { get; init; }
    public required string Account { get; init; }
    public required string Password { get; init; }

    public string? StateTopic { get; init; }
    public string? NtfyServerBaseUrl { get; init; }
    public string? SemesterId { get; init; }
}

public sealed record InvokeResponse
{
    public required bool Pushed { get; init; }
    public required bool BaselineInitialized { get; init; }
    public string? PublishId { get; init; }
    public string? Title { get; init; }
    public string? NtfyTopic { get; init; }
    public string? StateTopic { get; init; }
    public string? StatePublishId { get; init; }
    public string? SemesterId { get; init; }
    public string? CurrentHash { get; init; }
    public string? PreviousHash { get; init; }
}

public static class WatchdogRunner
{
    public static async Task<InvokeResponse> RunAsync(InvokeRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var notifyTopic = (request.Topic ?? string.Empty).Trim();
        if (notifyTopic.Length == 0)
            throw new ArgumentException("Topic is required.", nameof(request.Topic));

        var account = (request.Account ?? string.Empty).Trim();
        if (account.Length == 0)
            throw new ArgumentException("Account is required.", nameof(request.Account));

        var password = request.Password ?? string.Empty;
        if (password.Length == 0)
            throw new ArgumentException("Password is required.", nameof(request.Password));

        var ntfyServerBaseUrl = string.IsNullOrWhiteSpace(request.NtfyServerBaseUrl)
            ? "https://ntfy.sh"
            : request.NtfyServerBaseUrl.Trim();

        var stateTopic = string.IsNullOrWhiteSpace(request.StateTopic)
            ? $"{notifyTopic}-state"
            : request.StateTopic.Trim();
        if (stateTopic.Length == 0)
            throw new ArgumentException("State topic is required.", nameof(request.StateTopic));

        var latestState = await NtfyClient.GetLatestMessageAsync(
            serverBaseUrl: ntfyServerBaseUrl,
            topic: stateTopic,
            cancellationToken: cancellationToken);

        GradesStateV1? previousState = null;
        if (latestState is not null && GradesStateCodec.TryDecodeFromMessage(latestState.Message, out var parsed))
            previousState = parsed;

        var latestNotify = await NtfyClient.GetLatestMessageAsync(
            serverBaseUrl: ntfyServerBaseUrl,
            topic: notifyTopic,
            cancellationToken: cancellationToken);

        string? latestNotifiedHash = null;
        if (latestNotify is not null && TryExtractHashFromMessage(latestNotify.Message, out var notifyHash))
            latestNotifiedHash = notifyHash;

        var userDataDir = CreateTempUserDataDir(account);
        try
        {
            var options = BuildEamsClientOptions(userDataDir);

            await using var client = new EamsClient(options);
            var snapshot = await client.GetGradesSnapshotAsync(
                credentials: new EamsCredentials(account, password),
                semesterId: string.IsNullOrWhiteSpace(request.SemesterId) ? null : request.SemesterId.Trim());

            var currentState = GradesState.FromSnapshot(snapshot);
            var currentHash = GradesStateCodec.HashHex(currentState);

            var baseline = previousState is null;
            var diff = baseline
                ? GradesDiffComputer.Compute(
                    oldState: new GradesStateV1
                    {
                        SemesterId = currentState.SemesterId,
                        FinalGrades = Array.Empty<FinalGradeStateV1>(),
                        UsualGrades = Array.Empty<UsualGradeStateV1>(),
                    },
                    newState: currentState)
                : GradesDiffComputer.Compute(previousState!, currentState);

            var previousHash = previousState is null ? null : GradesStateCodec.HashHex(previousState);
            var alreadyNotified = latestNotifiedHash is not null &&
                                  string.Equals(latestNotifiedHash, currentHash, StringComparison.OrdinalIgnoreCase);

            string? title = null;
            string? publishId = null;
            var pushed = false;

            if (!alreadyNotified && (baseline || diff.TotalChangedOrAdded > 0))
            {
                title = NtfyMessageBuilder.BuildTitle(diff, isBaseline: baseline);
                var message = NtfyMessageBuilder.BuildNotificationMessage(snapshot, diff, currentHash);

                var published = await NtfyClient.SendAsync(
                    serverBaseUrl: ntfyServerBaseUrl,
                    topic: notifyTopic,
                    message: message,
                    title: title,
                    cancellationToken: cancellationToken);

                pushed = true;
                publishId = published.Id;
            }

            var stateMessage = NtfyStateMessageBuilder.BuildMessage(snapshot, currentState, currentHash);
            var statePublished = await NtfyClient.SendAsync(
                serverBaseUrl: ntfyServerBaseUrl,
                topic: stateTopic,
                message: stateMessage,
                title: "watchdog_state",
                cancellationToken: cancellationToken);

            return new InvokeResponse
            {
                Pushed = pushed,
                BaselineInitialized = baseline,
                PublishId = publishId,
                Title = title,
                NtfyTopic = notifyTopic,
                StateTopic = stateTopic,
                StatePublishId = statePublished.Id,
                SemesterId = snapshot.SemesterId,
                CurrentHash = currentHash,
                PreviousHash = previousHash,
            };
        }
        finally
        {
            var keep = ParseBoolOrDefault(Environment.GetEnvironmentVariable("KEEP_USER_DATA_DIR"), defaultValue: false);
            if (!keep)
                TryDeleteDirectory(userDataDir);
        }
    }

    private static string CreateTempUserDataDir(string account)
    {
        var safeAccount = string.Concat(account.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_'));
        if (safeAccount.Length == 0)
            safeAccount = "account";

        var dir = Path.Combine(Path.GetTempPath(), "uestc-watchdog", safeAccount, Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static EamsClientOptions BuildEamsClientOptions(string userDataDir)
    {
        var channel = Environment.GetEnvironmentVariable("BROWSER_CHANNEL") ?? "chromium";
        var executablePath = Environment.GetEnvironmentVariable("EXECUTABLE_PATH");

        var antibotWaitMs = ParseIntOrNull(Environment.GetEnvironmentVariable("ANTIBOT_WAIT_MS")) ?? 60_000;
        var bypassCsp = ParseBoolOrDefault(Environment.GetEnvironmentVariable("BYPASS_CSP"), defaultValue: false);
        var stealthScripts = ParseBoolOrDefault(Environment.GetEnvironmentVariable("STEALTH_SCRIPTS"), defaultValue: false);
        var debugDir = Environment.GetEnvironmentVariable("DEBUG_DIR");

        var acceptLanguage = Environment.GetEnvironmentVariable("ACCEPT_LANGUAGE");
        var secChUa = Environment.GetEnvironmentVariable("SEC_CH_UA");
        var secChUaMobile = Environment.GetEnvironmentVariable("SEC_CH_UA_MOBILE");
        var secChUaPlatform = Environment.GetEnvironmentVariable("SEC_CH_UA_PLATFORM");

        var extraHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(acceptLanguage))
            extraHeaders["accept-language"] = acceptLanguage.Trim();
        if (!string.IsNullOrWhiteSpace(secChUa))
            extraHeaders["sec-ch-ua"] = secChUa.Trim();
        if (!string.IsNullOrWhiteSpace(secChUaMobile))
            extraHeaders["sec-ch-ua-mobile"] = secChUaMobile.Trim();
        if (!string.IsNullOrWhiteSpace(secChUaPlatform))
            extraHeaders["sec-ch-ua-platform"] = secChUaPlatform.Trim();

        return new EamsClientOptions
        {
            UserDataDir = userDataDir,
            Channel = channel,
            ExecutablePath = executablePath,
            Headless = true,
            HeadlessImplementation = HeadlessImplementation.Playwright,
            ApplyLegacySpiderArgs = true,
            AllowManualLogin = false,
            AntiBotWaitMs = antibotWaitMs,
            BypassCsp = bypassCsp,
            EnableStealthScripts = stealthScripts,
            DiagnosticsDir = string.IsNullOrWhiteSpace(debugDir) ? null : debugDir.Trim(),
            ExtraHttpHeaders = extraHeaders.Count == 0 ? null : extraHeaders,
        };
    }

    private static void TryDeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // ignore
        }
    }

    private static int? ParseIntOrNull(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        return int.TryParse(raw.Trim(), out var i) ? i : null;
    }

    private static bool ParseBoolOrDefault(string? raw, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return defaultValue;

        return raw.Trim().ToLowerInvariant() switch
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
}
