using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Watchdog.Core.Eams;

namespace Watchdog.Function;

public sealed record GradesStateV1
{
    [JsonPropertyName("sem")]
    public required string SemesterId { get; init; }

    [JsonPropertyName("f")]
    public required IReadOnlyList<FinalGradeStateV1> FinalGrades { get; init; }

    [JsonPropertyName("u")]
    public required IReadOnlyList<UsualGradeStateV1> UsualGrades { get; init; }
}

public sealed record FinalGradeStateV1
{
    [JsonPropertyName("cc")]
    public required string CourseCode { get; init; }

    [JsonPropertyName("cid")]
    public required string CourseId { get; init; }

    [JsonPropertyName("n")]
    public required string CourseName { get; init; }

    [JsonPropertyName("fes")]
    public required string FinalExamScore { get; init; }

    [JsonPropertyName("os")]
    public required string OverallScore { get; init; }

    [JsonPropertyName("ms")]
    public required string MakeupScore { get; init; }

    [JsonPropertyName("fs")]
    public required string FinalScore { get; init; }

    [JsonPropertyName("g")]
    public required string Gpa { get; init; }
}

public sealed record UsualGradeStateV1
{
    [JsonPropertyName("cc")]
    public required string CourseCode { get; init; }

    [JsonPropertyName("cid")]
    public required string CourseId { get; init; }

    [JsonPropertyName("n")]
    public required string CourseName { get; init; }

    [JsonPropertyName("s")]
    public required string UsualScore { get; init; }
}

public static class GradesState
{
    public static GradesStateV1 FromSnapshot(GradesSnapshot snapshot)
    {
        if (snapshot is null)
            throw new ArgumentNullException(nameof(snapshot));

        return new GradesStateV1
        {
            SemesterId = (snapshot.SemesterId ?? string.Empty).Trim(),
            FinalGrades = snapshot.FinalGrades
                .Select(g => new FinalGradeStateV1
                {
                    CourseCode = (g.CourseCode ?? string.Empty).Trim(),
                    CourseId = (g.CourseId ?? string.Empty).Trim(),
                    CourseName = (g.CourseName ?? string.Empty).Trim(),
                    FinalExamScore = (g.FinalExamScore ?? string.Empty).Trim(),
                    OverallScore = (g.OverallScore ?? string.Empty).Trim(),
                    MakeupScore = (g.MakeupScore ?? string.Empty).Trim(),
                    FinalScore = (g.FinalScore ?? string.Empty).Trim(),
                    Gpa = (g.Gpa ?? string.Empty).Trim(),
                })
                .OrderBy(g => g.CourseCode, StringComparer.Ordinal)
                .ThenBy(g => g.CourseId, StringComparer.Ordinal)
                .ToArray(),
            UsualGrades = snapshot.UsualGrades
                .Select(g => new UsualGradeStateV1
                {
                    CourseCode = (g.CourseCode ?? string.Empty).Trim(),
                    CourseId = (g.CourseId ?? string.Empty).Trim(),
                    CourseName = (g.CourseName ?? string.Empty).Trim(),
                    UsualScore = (g.UsualScore ?? string.Empty).Trim(),
                })
                .OrderBy(g => g.CourseCode, StringComparer.Ordinal)
                .ThenBy(g => g.CourseId, StringComparer.Ordinal)
                .ToArray(),
        };
    }
}

public static class GradesStateCodec
{
    private const string Prefix = "watchdog_state:v1:";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public static string Encode(GradesStateV1 state)
    {
        if (state is null)
            throw new ArgumentNullException(nameof(state));

        var json = JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions);
        var compressed = Gzip(json);
        return $"{Prefix}{Base64UrlEncode(compressed)}";
    }

    public static bool TryDecodeFromMessage(string message, out GradesStateV1? state)
    {
        state = null;
        if (string.IsNullOrWhiteSpace(message))
            return false;

        var idx = message.LastIndexOf(Prefix, StringComparison.Ordinal);
        if (idx < 0)
            return false;

        var start = idx + Prefix.Length;
        var end = start;
        while (end < message.Length && IsBase64UrlChar(message[end]))
            end++;

        if (end <= start)
            return false;

        var token = message[start..end];
        try
        {
            var compressed = Base64UrlDecode(token);
            var json = Gunzip(compressed);
            state = JsonSerializer.Deserialize<GradesStateV1>(json, JsonOptions);
            return state is not null;
        }
        catch
        {
            state = null;
            return false;
        }
    }

    public static string HashHex(GradesStateV1 state)
    {
        if (state is null)
            throw new ArgumentNullException(nameof(state));

        var json = JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions);
        var hash = SHA256.HashData(json);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static byte[] Gzip(byte[] input)
    {
        using var ms = new MemoryStream();
        using (var gzip = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(input, 0, input.Length);
        }

        return ms.ToArray();
    }

    private static byte[] Gunzip(byte[] input)
    {
        using var compressed = new MemoryStream(input);
        using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }

    private static bool IsBase64UrlChar(char c)
    {
        return (c >= 'a' && c <= 'z')
               || (c >= 'A' && c <= 'Z')
               || (c >= '0' && c <= '9')
               || c == '-'
               || c == '_';
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        var base64 = Convert.ToBase64String(bytes);
        return base64
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    private static byte[] Base64UrlDecode(string base64Url)
    {
        var s = base64Url
            .Replace('-', '+')
            .Replace('_', '/');

        var padding = 4 - (s.Length % 4);
        if (padding is > 0 and < 4)
            s = s + new string('=', padding);

        return Convert.FromBase64String(s);
    }
}

public sealed record GradesDiff(
    int FinalAdded,
    int FinalChanged,
    int UsualAdded,
    int UsualChanged,
    IReadOnlyList<string> Highlights,
    IReadOnlyList<string> CourseNames)
{
    public int TotalChangedOrAdded => FinalAdded + FinalChanged + UsualAdded + UsualChanged;
}

public static class GradesDiffComputer
{
    public static GradesDiff Compute(GradesStateV1 oldState, GradesStateV1 newState)
    {
        if (oldState is null)
            throw new ArgumentNullException(nameof(oldState));
        if (newState is null)
            throw new ArgumentNullException(nameof(newState));

        static string Key(string courseCode, string courseId) => $"{courseCode}#{courseId}";

        var highlights = new List<string>();
        var courseNames = new List<string>();
        var seenNames = new HashSet<string>(StringComparer.Ordinal);

        int finalAdded = 0, finalChanged = 0, usualAdded = 0, usualChanged = 0;

        var oldFinalMap = oldState.FinalGrades.ToDictionary(g => Key(g.CourseCode, g.CourseId), g => g, StringComparer.Ordinal);
        var newFinalMap = newState.FinalGrades.ToDictionary(g => Key(g.CourseCode, g.CourseId), g => g, StringComparer.Ordinal);
        foreach (var g in newState.FinalGrades)
        {
            var key = Key(g.CourseCode, g.CourseId);
            if (!oldFinalMap.TryGetValue(key, out var old))
            {
                finalAdded++;
                highlights.Add($"+ 期末 {g.CourseName}：{FormatFinalScore(g)}");
                AddCourseName(seenNames, courseNames, g.CourseName);
                continue;
            }

            if (!FinalEquals(old, g))
            {
                finalChanged++;
                highlights.Add($"~ 期末 {g.CourseName}：{FormatFinalScore(old)} -> {FormatFinalScore(g)}");
                AddCourseName(seenNames, courseNames, g.CourseName);
            }
        }

        foreach (var old in oldState.FinalGrades)
        {
            var key = Key(old.CourseCode, old.CourseId);
            if (newFinalMap.ContainsKey(key))
                continue;

            finalChanged++;
            highlights.Add($"~ 期末 {old.CourseName}：条目消失");
            AddCourseName(seenNames, courseNames, old.CourseName);
        }

        var oldUsualMap = oldState.UsualGrades.ToDictionary(g => Key(g.CourseCode, g.CourseId), g => g, StringComparer.Ordinal);
        var newUsualMap = newState.UsualGrades.ToDictionary(g => Key(g.CourseCode, g.CourseId), g => g, StringComparer.Ordinal);
        foreach (var g in newState.UsualGrades)
        {
            var key = Key(g.CourseCode, g.CourseId);
            if (!oldUsualMap.TryGetValue(key, out var old))
            {
                usualAdded++;
                highlights.Add($"+ 平时 {g.CourseName}：{NormalizeScore(g.UsualScore)}");
                AddCourseName(seenNames, courseNames, g.CourseName);
                continue;
            }

            if (!UsualEquals(old, g))
            {
                usualChanged++;
                highlights.Add($"~ 平时 {g.CourseName}：{NormalizeScore(old.UsualScore)} -> {NormalizeScore(g.UsualScore)}");
                AddCourseName(seenNames, courseNames, g.CourseName);
            }
        }

        foreach (var old in oldState.UsualGrades)
        {
            var key = Key(old.CourseCode, old.CourseId);
            if (newUsualMap.ContainsKey(key))
                continue;

            usualChanged++;
            highlights.Add($"~ 平时 {old.CourseName}：条目消失");
            AddCourseName(seenNames, courseNames, old.CourseName);
        }

        return new GradesDiff(
            FinalAdded: finalAdded,
            FinalChanged: finalChanged,
            UsualAdded: usualAdded,
            UsualChanged: usualChanged,
            Highlights: highlights,
            CourseNames: courseNames);
    }

    private static void AddCourseName(HashSet<string> seen, List<string> list, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;

        var trimmed = name.Trim();
        if (seen.Add(trimmed))
            list.Add(trimmed);
    }

    private static bool FinalEquals(FinalGradeStateV1 a, FinalGradeStateV1 b)
    {
        return string.Equals(a.FinalExamScore, b.FinalExamScore, StringComparison.Ordinal)
               && string.Equals(a.OverallScore, b.OverallScore, StringComparison.Ordinal)
               && string.Equals(a.MakeupScore, b.MakeupScore, StringComparison.Ordinal)
               && string.Equals(a.FinalScore, b.FinalScore, StringComparison.Ordinal)
               && string.Equals(a.Gpa, b.Gpa, StringComparison.Ordinal);
    }

    private static bool UsualEquals(UsualGradeStateV1 a, UsualGradeStateV1 b)
    {
        return string.Equals(a.UsualScore, b.UsualScore, StringComparison.Ordinal);
    }

    private static string FormatFinalScore(FinalGradeStateV1 g)
    {
        var parts = new List<string>(capacity: 4);
        if (!string.IsNullOrWhiteSpace(g.OverallScore))
            parts.Add($"总评 {NormalizeScore(g.OverallScore)}");
        if (!string.IsNullOrWhiteSpace(g.FinalExamScore))
            parts.Add($"期末 {NormalizeScore(g.FinalExamScore)}");
        if (!string.IsNullOrWhiteSpace(g.MakeupScore))
            parts.Add($"补考 {NormalizeScore(g.MakeupScore)}");
        if (!string.IsNullOrWhiteSpace(g.FinalScore))
            parts.Add($"最终 {NormalizeScore(g.FinalScore)}");

        return parts.Count == 0 ? "-" : string.Join(" / ", parts);
    }

    private static string NormalizeScore(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "-";
        return raw.Trim();
    }
}

public static class NtfyMessageBuilder
{
    public static string BuildTitle(GradesDiff diff, bool isBaseline)
    {
        if (isBaseline)
            return "监控初始化";

        var hasAdded = diff.Highlights.Any(h => h.TrimStart().StartsWith("+", StringComparison.Ordinal));
        var hasChanged = diff.Highlights.Any(h => h.TrimStart().StartsWith("~", StringComparison.Ordinal));

        var prefix = hasAdded && !hasChanged
            ? "新成绩"
            : hasChanged && !hasAdded
                ? "成绩更新"
                : "成绩变动";

        if (diff.CourseNames.Count == 0)
            return prefix;

        const int maxNames = 4;
        var shown = diff.CourseNames.Take(maxNames).ToArray();
        var title = $"{prefix}：{string.Join('、', shown)}";
        if (diff.CourseNames.Count > maxNames)
            title = $"{title}…({diff.CourseNames.Count})";

        return title;
    }

    public static string BuildMessage(
        GradesSnapshot snapshot,
        GradesDiff diff,
        GradesStateV1 state)
    {
        var sb = new StringBuilder();

        if (SemesterIdCalculator.TryDecode(snapshot.SemesterId, out var startYear, out var term))
        {
            var yearRange = SemesterIdCalculator.ToYearRange(startYear);
            var termText = term == SemesterTerm.First ? "第一学期" : "第二学期";
            sb.AppendLine($"{yearRange} {termText}");
        }
        else
        {
            sb.AppendLine($"学期ID：{snapshot.SemesterId}");
        }

        sb.AppendLine($"时间：{snapshot.FetchedAt:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"期末：新增 {diff.FinalAdded}，更新 {diff.FinalChanged}；平时：新增 {diff.UsualAdded}，更新 {diff.UsualChanged}");

        if (diff.Highlights.Count > 0)
        {
            sb.AppendLine();
            const int maxLines = 10;
            foreach (var line in diff.Highlights.Take(maxLines))
                sb.AppendLine(line);

            if (diff.Highlights.Count > maxLines)
                sb.AppendLine($"… 还有 {diff.Highlights.Count - maxLines} 项");
        }

        sb.AppendLine();
        sb.AppendLine(GradesStateCodec.Encode(state));
        sb.AppendLine($"hash={GradesStateCodec.HashHex(state)}");

        return sb.ToString().TrimEnd();
    }
}

