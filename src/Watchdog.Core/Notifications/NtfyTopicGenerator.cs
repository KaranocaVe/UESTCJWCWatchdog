using System.Security.Cryptography;
using System.Text;

namespace Watchdog.Core.Notifications;

public static class NtfyTopicGenerator
{
    public static string Generate(string prefix = "uestcjwc-")
    {
        var bytes = RandomNumberGenerator.GetBytes(12);
        var sb = new StringBuilder(prefix.Length + bytes.Length * 2);
        sb.Append(prefix);
        foreach (var b in bytes)
            sb.AppendFormat("{0:x2}", b);
        return sb.ToString();
    }
}

