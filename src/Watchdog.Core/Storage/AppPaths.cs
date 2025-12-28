namespace Watchdog.Core.Storage;

public static class AppPaths
{
    public static string GetAppDataDir(string appName = "UESTCJWCWatchdog")
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(baseDir))
            baseDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var dir = Path.Combine(baseDir, appName);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string GetProfileDir(string account, string appName = "UESTCJWCWatchdog")
    {
        if (string.IsNullOrWhiteSpace(account))
            throw new ArgumentException("Account is required.", nameof(account));

        var sanitized = SanitizePathSegment(account);
        var dir = Path.Combine(GetAppDataDir(appName), "profiles", sanitized);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var buffer = new char[value.Length];
        var length = 0;
        foreach (var ch in value)
        {
            if (invalid.Contains(ch))
                continue;

            buffer[length++] = ch;
        }

        return length == 0 ? "default" : new string(buffer, 0, length);
    }
}

