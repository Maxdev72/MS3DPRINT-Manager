using MS3DPRINT.Manager.Core.Projects;

namespace MS3DPRINT.Manager.Core.Tests.Projects;

public sealed class ProjectCalendarTests
{
    [Fact]
    public void Month_starts_on_monday_includes_leap_day_and_keeps_same_day_projects()
    {
        var date=new DateOnly(2028,2,29);
        var a=Project("Un",date,ProjectPriority.Low);
        var b=Project("Deux",date,ProjectPriority.High);
        var none=Project("Sans date",null);
        var days=ProjectCalendar.Month(new DateOnly(2028,2,15),[a,b,none]);
        Assert.Equal(42,days.Count);
        Assert.Equal(DayOfWeek.Monday,days[0].Date!.Value.DayOfWeek);
        Assert.Equal(new DateOnly(2028,1,31),days[0].Date);
        Assert.Equal(new[]{"Deux","Un"},days.Single(d=>d.Date==date).Projects.Select(p=>p.Profile!.ProjectName));
        Assert.Equal(2,days.Sum(d=>d.Projects.Count));
    }

    [Fact]
    public void Agenda_orders_overdue_and_future_dates_and_separates_unplanned()
    {
        var projects=new[]{Project("Plus tard",new(2026,11,2)),Project("Sans date",null),Project("En retard",new(2026,10,20))};
        Assert.Equal(new[]{"En retard","Plus tard"},ProjectCalendar.Agenda(projects).Select(p=>p.Profile!.ProjectName));
    }

    [Fact]
    public void Last_month_does_not_overflow_the_date_range()
    {
        var days=ProjectCalendar.Month(new DateOnly(9999,12,1),[]);
        Assert.Contains(days,d=>d.Date==DateOnly.MaxValue);
        Assert.Equal(42,days.Count);
    }

    private static ProjectSummary Project(string name,DateOnly? date,ProjectPriority priority=ProjectPriority.None)
    {
        var p=new ProjectProfile(Guid.NewGuid(),Guid.NewGuid(),"CT",name,name,name,ProjectStatus.Quote,DateTimeOffset.UtcNow,date,null,null,DateTimeOffset.UtcNow,Priority:priority);
        return new("Client","client","project",name,name,p);
    }
}
