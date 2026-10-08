using MS3DPRINT.Manager.App.ViewModels;
using MS3DPRINT.Manager.Core.Clients;

namespace MS3DPRINT.Manager.Core.Tests.Views;

public sealed class ProjectCreationFlowTests
{
    [Fact]
    public void SelectingClientAndMovingBetweenSteps_PreservesProjectDraft()
    {
        var flow = new ProjectCreationFlow(2026)
        {
            ProjectName = "Support imprimé",
            DueDate = new DateOnly(2026, 11, 15),
            Description = "Prototype client"
        };

        flow.SelectClient(CompleteClient());
        flow.MoveToProject();
        flow.MoveToConfirmation();
        flow.MoveToClient();

        Assert.Equal(ProjectCreationStep.Client, flow.Step);
        Assert.Equal("Support imprimé", flow.ProjectName);
        Assert.Equal(new DateOnly(2026, 11, 15), flow.DueDate);
        Assert.Equal("Prototype client", flow.Description);
        Assert.True(flow.CanContinue);
        Assert.True(flow.CanCreate);
    }

    [Fact]
    public void MissingOrIncompleteClient_PreventsProjectContinuation()
    {
        var flow = new ProjectCreationFlow(2026) { ProjectName = "Support" };

        Assert.False(flow.CanContinue);
        flow.SelectClient(new ClientSummary("C:\\workspace\\client", "CLIENT", "Client", "CL", null, null, 0));

        Assert.False(flow.CanContinue);
        Assert.Throws<InvalidOperationException>(flow.MoveToProject);
    }

    private static ClientSummary CompleteClient()
    {
        var now = DateTimeOffset.UtcNow;
        var profile = new ClientProfile(Guid.NewGuid(), ClientKind.Professional, "CLIENT", "CL", "Client", null, null,
            null, null, new PrimaryContact(null, null, null, null, null), now, now);
        return new ClientSummary("C:\\workspace\\client", "CLIENT", "Client", "CL", ClientKind.Professional, profile, 0);
    }
}
