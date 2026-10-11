using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Workspace;
using MS3DPRINT.Manager.Desktop.Dashboard;

namespace MS3DPRINT.Manager.Core.Tests.Desktop;

public sealed class WorkspaceProfileEditorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ms3dprint-editor-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Update_project_persists_the_operational_information()
    {
        var paths = new WorkspaceMetadataPaths(_root);
        paths.EnsureMetadataDirectories();
        var client = new ClientProfile(Guid.NewGuid(), ClientKind.Professional, "ACME", "ACME", "ACME", null, null, null, null,
            new PrimaryContact(null, null, null, null, null), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, RelativePath: "01_CLIENTS/ACME");
        new ClientProfileStore(paths).Create(client);
        var profile = new ProjectProfile(Guid.NewGuid(), client.Id, "ACME", "ACME-2026-001", "ACME-2026-001_SUPPORT", "Support", ProjectStatus.Quote,
            DateTimeOffset.UtcNow, null, null, null, DateTimeOffset.UtcNow, "01_CLIENTS/ACME/ACME-2026-001_SUPPORT");
        new ProjectProfileStore(paths).Create(profile);

        var updated = new WorkspaceProfileEditor().UpdateProject(_root, profile, ProjectStatus.InProgress,
            new DateOnly(2026, 12, 20), "Support destiné à l'atelier", "Valider le prototype.");

        Assert.Equal(ProjectStatus.InProgress, updated.Status);
        Assert.Equal(new DateOnly(2026, 12, 20), updated.DueDate);
        Assert.Equal("Support destiné à l'atelier", updated.Description);
        Assert.Equal("Valider le prototype.", new ProjectProfileStore(paths).LoadAll().Single().Notes);
    }

    [Fact]
    public void Update_client_persists_contact_notes()
    {
        var paths = new WorkspaceMetadataPaths(_root);
        paths.EnsureMetadataDirectories();
        var profile = new ClientProfile(Guid.NewGuid(), ClientKind.Professional, "ACME", "ACME", "ACME", null, null, null, null,
            new PrimaryContact(null, null, null, null, null), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, RelativePath: "01_CLIENTS/ACME");
        new ClientProfileStore(paths).Create(profile);

        var updated = new WorkspaceProfileEditor().UpdateClient(_root, profile, "12 rue de l'Impression", "À rappeler jeudi", "Camille", "Durand", "Achats", "0600000000", "camille@example.test",
            companyName: "ACME Impression", siret: "12345678900012");

        Assert.Equal("12 rue de l'Impression", updated.Address);
        Assert.Equal("À rappeler jeudi", new ClientProfileStore(paths).Load(profile.Id).Notes);
        Assert.Equal("camille@example.test", updated.PrimaryContact.Email);
        Assert.Equal("ACME Impression", updated.CompanyName);
        Assert.Equal("12345678900012", updated.Siret);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
