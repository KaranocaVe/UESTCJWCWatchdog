using System.Text.Json.Serialization;

namespace Watchdog.Core.Eams;

public sealed record SemesterOption
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public sealed record FinalGrade
{
    public required string Semester { get; init; }
    public required string CourseCode { get; init; }
    public required string CourseId { get; init; }
    public required string CourseName { get; init; }
    public required string CourseType { get; init; }
    public required string Credit { get; init; }
    public required string FinalExamScore { get; init; }
    public required string OverallScore { get; init; }
    public required string MakeupScore { get; init; }
    public required string FinalScore { get; init; }
    public required string Gpa { get; init; }
}

public sealed record UsualGrade
{
    public required string Semester { get; init; }
    public required string CourseCode { get; init; }
    public required string CourseId { get; init; }
    public required string CourseName { get; init; }
    public required string CourseType { get; init; }
    public required string Credit { get; init; }
    public required string UsualScore { get; init; }
}

public sealed record GradesSnapshot
{
    public required string SemesterId { get; init; }
    public required DateTimeOffset FetchedAt { get; init; }
    public required IReadOnlyList<FinalGrade> FinalGrades { get; init; }
    public required IReadOnlyList<UsualGrade> UsualGrades { get; init; }
}
