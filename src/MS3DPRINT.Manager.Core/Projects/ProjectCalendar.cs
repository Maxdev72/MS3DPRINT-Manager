namespace MS3DPRINT.Manager.Core.Projects;

public sealed record ProjectCalendarDay(DateOnly? Date, IReadOnlyList<ProjectSummary> Projects);

/// <summary>A read-only view of the existing project deadlines, with Monday as the first weekday.</summary>
public static class ProjectCalendar
{
    public static IReadOnlyList<ProjectCalendarDay> Month(DateOnly month, IEnumerable<ProjectSummary> projects)
    {
        var first = new DateOnly(month.Year, month.Month, 1);
        var start = first.DayNumber - ((int)first.DayOfWeek + 6) % 7;
        var dates = Agenda(projects).ToLookup(p => p.Profile!.DueDate!.Value);
        return Enumerable.Range(0, 42).Select(i =>
        {
            var number = start + i;
            if (number < 0 || number > DateOnly.MaxValue.DayNumber) return new ProjectCalendarDay(null, []);
            var date = DateOnly.FromDayNumber(number);
            return new ProjectCalendarDay(date, dates[date].ToArray());
        }).ToArray();
    }

    public static IReadOnlyList<ProjectSummary> Agenda(IEnumerable<ProjectSummary> projects)
        => projects.Where(p => p.Profile?.DueDate is not null)
            .OrderBy(p => p.Profile!.DueDate).ThenByDescending(p => p.Priority)
            .ThenBy(p => p.Profile!.ProjectName, StringComparer.CurrentCultureIgnoreCase).ToArray();
}
