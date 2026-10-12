using MS3DPRINT.Manager.Desktop.Dashboard;
using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Tests.Desktop;

public sealed class WorkspaceProjectCreatorTests
{
    [Fact]
    public void Create_builds_the_standard_project_tree()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ms3dprint-create-{Guid.NewGuid():N}");
        var client = Path.Combine(root, "01_CLIENTS", "ACME");
        Directory.CreateDirectory(client);
        try
        {
            var project = new WorkspaceProjectCreator().Create(client, "ACME", 2026, "Support bobine");
            Assert.True(Directory.Exists(Path.Combine(client, project.FolderName)));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Complete_existing_project_creates_only_its_profile()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ms3dprint-legacy-project-{Guid.NewGuid():N}");
        var clientPath = Path.Combine(root, "01_CLIENTS", "ACME");
        var projectPath = Path.Combine(clientPath, "ACME-2026-001_SUPPORT");
        Directory.CreateDirectory(projectPath);
        try
        {
            var paths = new WorkspaceMetadataPaths(root); paths.EnsureMetadataDirectories();
            var client = new ClientProfile(Guid.NewGuid(), ClientKind.Professional, "ACME", "ACME", "ACME", null, null, null, null,
                new PrimaryContact(null, null, null, null, null), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, RelativePath: "01_CLIENTS/ACME");
            new ClientProfileStore(paths).Create(client);
            var project = new ProjectSummary("ACME", clientPath, projectPath, "ACME-2026-001", "ACME-2026-001_SUPPORT", null);

            var profile = new WorkspaceProjectCreator().CompleteExisting(root, project, client, "Support bobine");

            Assert.True(Directory.Exists(projectPath));
            Assert.Equal("Support bobine", new ProjectProfileStore(paths).LoadAll().Single().ProjectName);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void CreateTracked_keeps_the_typed_name_in_the_fiche_while_the_folder_is_normalised()
    {
        var root = Path.Combine(Path.GetTempPath(), "MS3DPRINT-tracked-" + Guid.NewGuid().ToString("N"));
        try
        {
            var clientPath = Path.Combine(root, "01_CLIENTS", "ACME");
            Directory.CreateDirectory(clientPath);
            var paths = new WorkspaceMetadataPaths(root); paths.EnsureMetadataDirectories();
            var client = new ClientProfile(Guid.NewGuid(), ClientKind.Professional, "ACME", "ACME", "ACME", null, null, null, null,
                new PrimaryContact(null, null, null, null, null), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, RelativePath: "01_CLIENTS/ACME");
            new ClientProfileStore(paths).Create(client);
            var summary = new ClientSummary(clientPath, "ACME", "ACME", "ACME", ClientKind.Professional, client, 0);

            var reference = new WorkspaceProjectCreator().CreateTracked(root, summary, 2026, "Épée d’échecs");

            Assert.EndsWith("_EPEE_D_ECHECS", reference.FolderName);
            Assert.Equal("Épée d’échecs", new ProjectProfileStore(paths).LoadAll().Single().ProjectName);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void CreateTracked_only_makes_the_folders_for_a_client_without_a_fiche()
    {
        var root = Path.Combine(Path.GetTempPath(), "MS3DPRINT-tracked-legacy-" + Guid.NewGuid().ToString("N"));
        try
        {
            var clientPath = Path.Combine(root, "01_CLIENTS", "LEGACY");
            Directory.CreateDirectory(clientPath);
            var summary = new ClientSummary(clientPath, "LEGACY", "LEGACY", "LG", ClientKind.Professional, null, 0);

            var reference = new WorkspaceProjectCreator().CreateTracked(root, summary, 2026, "Pièce test");

            Assert.True(Directory.Exists(Path.Combine(clientPath, reference.FolderName)));
            Assert.False(Directory.Exists(Path.Combine(root, ".ms3dprint-manager", "projects")) && Directory.EnumerateFiles(Path.Combine(root, ".ms3dprint-manager", "projects")).Any());
        }
        finally { Directory.Delete(root, true); }
    }
}
