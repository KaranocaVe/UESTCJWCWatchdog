namespace Watchdog.App.Models;

public sealed class AppSettings
{
    public string? Account { get; set; }
    public string? Password { get; set; }
    public bool SavePassword { get; set; }

    public bool Headless { get; set; } = true;

    public bool AutoRefreshEnabled { get; set; }

    /// <summary>Allowed values: 30, 60, 120.</summary>
    public int AutoRefreshMinutes { get; set; } = 60;

    public bool NtfyEnabled { get; set; } = true;
    public string NtfyServerBaseUrl { get; set; } = "https://ntfy.sh";
    public string? NtfyTopic { get; set; }

    /// <summary>
    /// When null (old settings.json), the app infers this from <see cref="SemesterId"/> presence.
    /// </summary>
    public bool? UseAutoSemester { get; set; }

    public string? SemesterYear { get; set; }

    /// <summary>1 for first term, 2 for second term.</summary>
    public int? SemesterTerm { get; set; }

    /// <summary>
    /// Optional EAMS semester id (kept for backward compatibility). When null/empty the client auto-detects.
    /// </summary>
    public string? SemesterId { get; set; }

    public string Channel { get; set; } = "chrome";
}
