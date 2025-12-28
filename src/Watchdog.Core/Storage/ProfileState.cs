using Watchdog.Core.Eams;

namespace Watchdog.Core.Storage;

public sealed record ProfileState
{
    public required string Account { get; init; }

    public string? SemesterId { get; set; }
    public DateTimeOffset? LastFetchedAt { get; set; }

    public List<FinalGrade> FinalGrades { get; set; } = [];
    public List<UsualGrade> UsualGrades { get; set; } = [];
}

