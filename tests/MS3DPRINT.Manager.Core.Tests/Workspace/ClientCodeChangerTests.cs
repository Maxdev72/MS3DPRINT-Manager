using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Estimates;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Storage;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Tests.Workspace;

public sealed class ClientCodeChangerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MS3DPRINT-client-code-" + Guid.NewGuid().ToString("N"));
    private readonly string _clientPath;
    private readonly ClientProfile _profile;
    private readonly ClientSummary _client;

    public ClientCodeChangerTests()
    {
        _clientPath = Path.Combine(_root, "01_CLIENTS", "FELL_AGENCY");
        Directory.CreateDirectory(_clientPath);
        var paths = new WorkspaceMetadataPaths(_root);
        paths.EnsureMetadataDirectories();
        _profile = new ClientProfile(Guid.NewGuid(), ClientKind.Professional, "FELL_AGENCY", "F", "FELL AGENCY", null, null, null, null,
            new PrimaryContact(null, null, null, null, null), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, RelativePath: "01_CLIENTS/FELL_AGENCY");
        new ClientProfileStore(paths).Create(_profile);
        _client = new ClientSummary(_clientPath, "FELL_AGENCY", "FELL AGENCY", "F", ClientKind.Professional, _profile, 2);

        var creation = new ProjectCreationService(new FolderTreeService());
        creation.Create(_clientPath, "F", 2026, "Samurai");                                            // F-2026-001, no fiche (historic project)
        creation.CreateWithProfile(_client, 2026, "Pièce test", new ProjectProfileStore(paths), null, null); // F-2026-002 with its fiche
        var inputs = new EstimateInputs(1, 1, 0, 0, 0, 0, 0, 0, 0, 30, 5, 0);
        new EstimateStore(_root).Create(new EstimateProfile(Guid.NewGuid(), "Devis samurai", "PLA", "A1", inputs, EstimateCalculator.Calculate(inputs), DateTimeOffset.UtcNow, ProjectReference: "F-2026-001"));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private ClientCodeChanger Changer() => new(_root);

    [Fact]
    public void Plan_lists_every_project_of_the_client_with_its_new_reference()
    {
        var plan = Changer().Plan(_client, "fa", [_client]);

        Assert.Equal("FA", plan.NewCode);
        Assert.Equal(["FA-2026-001", "FA-2026-002"], plan.Projects.Select(rename => rename.NewReference).Order());
        Assert.Equal("FA-2026-001_SAMURAI", plan.Projects.Single(rename => rename.OldReference == "F-2026-001").NewFolderName);
        Assert.Equal(1, plan.Projects.Single(rename => rename.OldReference == "F-2026-001").EstimateCount);
        Assert.Null(plan.Projects.Single(rename => rename.OldReference == "F-2026-001").Profile);
        Assert.NotNull(plan.Projects.Single(rename => rename.OldReference == "F-2026-002").Profile);
    }

    [Theory]
    [InlineData("F", "identique")]
    [InlineData("", "1 à 6")]
    [InlineData("F A", "1 à 6")]
    [InlineData("ÉCOLE", "1 à 6")]
    [InlineData("ABCDEFG", "1 à 6")]
    public void Plan_refuses_an_unusable_code(string code, string reason)
        => Assert.Contains(reason, Assert.Throws<ArgumentException>(() => Changer().Plan(_client, code, [_client])).Message);

    [Fact]
    public void Plan_refuses_a_code_already_used_by_another_client()
    {
        var other = new ClientSummary(Path.Combine(_root, "01_CLIENTS", "OTHER"), "OTHER", "OTHER", "FA", ClientKind.Professional, null, 0);

        Assert.Contains("déjà utilisé", Assert.Throws<ArgumentException>(() => Changer().Plan(_client, "FA", [_client, other])).Message);
    }

    [Fact]
    public void Plan_refuses_a_client_without_a_fiche()
    {
        var legacy = _client with { Profile = null };

        Assert.Throws<ArgumentException>(() => Changer().Plan(legacy, "FA", [legacy]));
    }

    [Fact]
    public void Apply_renames_folders_fiches_reservations_estimates_and_the_client_code()
    {
        var changer = Changer();

        changer.Apply(changer.Plan(_client, "FA", [_client]));

        Assert.True(Directory.Exists(Path.Combine(_clientPath, "FA-2026-001_SAMURAI")));
        Assert.True(Directory.Exists(Path.Combine(_clientPath, "FA-2026-002_PIECE_TEST")));
        Assert.False(Directory.Exists(Path.Combine(_clientPath, "F-2026-001_SAMURAI")));
        var paths = new WorkspaceMetadataPaths(_root);
        var project = new ProjectProfileStore(paths).LoadAll().Single();
        Assert.Equal(("FA", "FA-2026-002", "FA-2026-002_PIECE_TEST"), (project.ClientCode, project.Reference, project.FolderName));
        Assert.EndsWith("FA-2026-002_PIECE_TEST", project.RelativePath);
        Assert.True(File.Exists(Path.Combine(_root, ".ms3dprint-manager", "project-references", "FA-2026-001.json")));
        Assert.True(File.Exists(Path.Combine(_root, ".ms3dprint-manager", "project-references", "F-2026-001.json")), "the old reference stays reserved");
        Assert.Equal("FA-2026-001", new EstimateStore(_root).LoadAll().Single().ProjectReference);
        Assert.Equal("FA", new ClientProfileStore(paths).LoadAll().Single().ClientCode);
    }

    [Fact]
    public void Apply_puts_everything_back_when_a_step_fails()
    {
        var changer = Changer();
        var plan = changer.Plan(_client, "FA", [_client]);
        // the second folder disappears between the preview and the confirmation
        var second = plan.Projects.OrderBy(rename => rename.OldReference).Last();
        Directory.Delete(second.OldFolderPath, recursive: true);

        Assert.ThrowsAny<Exception>(() => changer.Apply(plan));

        Assert.True(Directory.Exists(Path.Combine(_clientPath, "F-2026-001_SAMURAI")), "the first folder is back under its old name");
        Assert.False(Directory.Exists(Path.Combine(_clientPath, "FA-2026-001_SAMURAI")));
        Assert.Equal("F", new ClientProfileStore(new WorkspaceMetadataPaths(_root)).LoadAll().Single().ClientCode);
        Assert.Equal("F-2026-001", new EstimateStore(_root).LoadAll().Single().ProjectReference);
        Assert.False(File.Exists(Path.Combine(_root, ".ms3dprint-manager", "project-references", "FA-2026-001.json")));
    }
}
