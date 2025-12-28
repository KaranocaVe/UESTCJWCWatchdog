namespace Watchdog.Core.Eams;

public enum SemesterTerm
{
    First = 1,
    Second = 2,
}

/// <summary>
/// UESTC EAMS 学期ID计算器（由旧项目迁移并做了更严格的校验与辅助方法）。
/// </summary>
public static class SemesterIdCalculator
{
    // Legacy formula:
    // term1Id = (startYear - 2013) * 40 + 3
    // term2Id = term1Id + 20
    public const int BaseStartYear = 2013;
    public const int BaseSemesterId = 3;
    public const int YearStep = 40;
    public const int TermStep = 20;

    /// <summary>
    /// 学年第一学期一般从 9 月开始；第二学期一般从 2 月开始。
    /// 该规则用于“当前学期”的本地推断（避免依赖页面默认下拉选项顺序）。
    /// </summary>
    public const int FirstTermStartMonth = 9;
    public const int SecondTermStartMonth = 2;

    public static string Calculate(int startYear, SemesterTerm term)
    {
        if (startYear < BaseStartYear)
            throw new ArgumentOutOfRangeException(nameof(startYear), $"Start year must be >= {BaseStartYear}.");

        var term1Id = (startYear - BaseStartYear) * YearStep + BaseSemesterId;
        var id = term switch
        {
            SemesterTerm.First => term1Id,
            SemesterTerm.Second => term1Id + TermStep,
            _ => throw new ArgumentOutOfRangeException(nameof(term), "Term must be First or Second."),
        };

        return id.ToString();
    }

    public static string Calculate(string yearRange, SemesterTerm term)
    {
        if (!TryParseYearRange(yearRange, out var startYear, out _))
            throw new ArgumentException("学年范围格式应为 'YYYY-YYYY'，且结束年份应等于开始年份+1。", nameof(yearRange));

        return Calculate(startYear, term);
    }

    public static bool TryParseYearRange(string? yearRange, out int startYear, out int endYear)
    {
        startYear = 0;
        endYear = 0;

        if (string.IsNullOrWhiteSpace(yearRange))
            return false;

        var parts = yearRange.Trim().Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
            return false;

        if (!int.TryParse(parts[0], out startYear))
            return false;

        if (!int.TryParse(parts[1], out endYear))
            return false;

        return endYear == startYear + 1;
    }

    public static string ToYearRange(int startYear)
    {
        if (startYear < 0)
            throw new ArgumentOutOfRangeException(nameof(startYear));

        return $"{startYear}-{startYear + 1}";
    }

    public static (int StartYear, SemesterTerm Term) GetAcademicTerm(DateTimeOffset now)
    {
        var year = now.Year;
        var month = now.Month;

        if (month >= FirstTermStartMonth)
            return (year, SemesterTerm.First);

        if (month >= SecondTermStartMonth)
            return (year - 1, SemesterTerm.Second);

        // January: still considered the first term of the previous academic year.
        return (year - 1, SemesterTerm.First);
    }

    public static string GetCurrentSemesterId(DateTimeOffset now)
    {
        var (startYear, term) = GetAcademicTerm(now);
        return Calculate(startYear, term);
    }

    public static string GetAcademicTermDisplay(DateTimeOffset now)
    {
        var (startYear, term) = GetAcademicTerm(now);
        var yearRange = ToYearRange(startYear);
        var termText = term == SemesterTerm.First ? "第一学期" : "第二学期";
        return $"{yearRange} {termText}";
    }

    public static bool TryDecode(string? semesterId, out int startYear, out SemesterTerm term)
    {
        startYear = 0;
        term = default;

        if (string.IsNullOrWhiteSpace(semesterId))
            return false;

        if (!int.TryParse(semesterId.Trim(), out var id))
            return false;

        // Check term1 form
        var raw1 = id - BaseSemesterId;
        if (raw1 >= 0 && raw1 % YearStep == 0)
        {
            startYear = BaseStartYear + raw1 / YearStep;
            term = SemesterTerm.First;
            return true;
        }

        // Check term2 form
        var raw2 = id - (BaseSemesterId + TermStep);
        if (raw2 >= 0 && raw2 % YearStep == 0)
        {
            startYear = BaseStartYear + raw2 / YearStep;
            term = SemesterTerm.Second;
            return true;
        }

        return false;
    }
}

