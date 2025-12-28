namespace Watchdog.Core.Eams;

public enum HeadlessImplementation
{
    Playwright,
    ChromiumArg,
}

public sealed record EamsClientOptions
{
    public required string UserDataDir { get; init; }

    public string? Channel { get; init; } = "chrome";
    public string? ExecutablePath { get; init; }
    public bool Headless { get; init; } = false;
    public HeadlessImplementation HeadlessImplementation { get; init; } = HeadlessImplementation.Playwright;
    public string ChromiumHeadlessArg { get; init; } = "--headless=new";
    public bool ApplyLegacySpiderArgs { get; init; } = true;
    public int WindowWidth { get; init; } = 1920;
    public int WindowHeight { get; init; } = 1080;
    public bool NoViewport { get; init; } = false;

    public string? UserAgent { get; init; }
    public bool AutoFixHeadlessUserAgent { get; init; } = true;
    public float? SlowMoMs { get; init; }
    public bool AllowManualLogin { get; init; } = true;
    public int ManualLoginTimeoutMs { get; init; } = 180_000;

    public string BaseUrl { get; init; } = "https://eams.uestc.edu.cn/eams/";

    public int DefaultTimeoutMs { get; init; } = 30_000;
    public int NavigationTimeoutMs { get; init; } = 45_000;
    public int AntiBotWaitMs { get; init; } = 60_000;
    public bool AutoRecoverBadRequest { get; init; } = true;

    /// <summary>
    /// Apply a small set of "stealth" patches via AddInitScript for headless runs.
    /// </summary>
    public bool EnableStealthScripts { get; init; } = false;

    /// <summary>
    /// Disables Content-Security-Policy checks in the browser for this context.
    /// Useful when pages use strict CSP that would otherwise block injected init scripts.
    /// </summary>
    public bool BypassCsp { get; init; } = false;

    /// <summary>Optional directory for diagnostic logs (network/console).</summary>
    public string? DiagnosticsDir { get; init; }

    /// <summary>Extra headers applied to all requests from this context.</summary>
    public IReadOnlyDictionary<string, string>? ExtraHttpHeaders { get; init; }
}
