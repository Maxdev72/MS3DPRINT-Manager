using MS3DPRINT.Manager.App.ViewModels;
using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Projects;

namespace MS3DPRINT.Manager.Core.Tests.ViewModels;

public sealed class DashboardSnapshotTests
{
    [Fact]
    public void Create_OrdersRecentProjectsAndReportsProjectsRequiringAttention()
    {
        var older = Project("OLD-2026-001", "Ancien", ProjectStatus.Completed, new DateOnly(2026, 10, 1), new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero));
        var overdue = Project("LATE-2026-001", "À livrer", ProjectStatus.InProgress, new DateOnly(2026, 10, 7), new DateTimeOffset(2026, 10, 7, 9, 0, 0, TimeSpan.Zero));
        var recent = Project("NEW-2026-001", "Récent", ProjectStatus.Quote, null, new DateTimeOffset(2026, 10, 8, 10, 0, 0, TimeSpan.Zero));

        var snapshot = DashboardSnapshot.Create([], [older, overdue, recent], new DateOnly(2026, 10, 8));

        Assert.Collection(snapshot.RecentProjects,
            row => Assert.Equal("NEW-2026-001", row.Reference),
            row => Assert.Equal("LATE-2026-001", row.Reference),
            row => Assert.Equal("OLD-2026-001", row.Reference));
        Assert.Contains(snapshot.Alerts, alert => alert.Message.Contains("LATE-2026-001", StringComparison.Ordinal));
    }

    private static ProjectSummary Project(string reference, string name, ProjectStatus status, DateOnly? dueDate, DateTimeOffset updatedAt)
    {
        var profile = new ProjectProfile(Guid.NewGuid(), Guid.NewGuid(), "ACME", reference, reference + "_" + name,
            name, status, updatedAt.AddDays(-1), dueDate, null, null, updatedAt);
        return new ProjectSummary("ACME", @"C:\Workspace\ACME", Path.Combine(@"C:\Workspace\ACME", profile.FolderName), reference, profile.FolderName, profile);
    }
}
