using System.Security.Cryptography;
using System.Text.Json;
using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Migration;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Storage;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Server.Tests;

public sealed class MigrationTests
{
    [Fact]
    public void Export_and_stage_preserve_all_managed_bytes_and_never_change_the_source()
    {
        using var host = new ServerFixture();
        Seed(host.Workspace);
        var before = AllHashes(host.Workspace);
        var package = Path.Combine(host.Root, "package");
        var service = new WorkspaceMigrationService();
        var exported = service.Export(host.Workspace, package);
        Assert.True(exported.Succeeded, JsonSerializer.Serialize(exported.Issues));
        Assert.NotNull(exported.ArchiveSha256);
        Assert.Contains(exported.Files, f => f.Path.EndsWith("piece.stl"));
        var staged = service.Stage(Path.Combine(package, "workspace.zip"), Path.Combine(host.Root, "prepared"), exported.ArchiveSha256);
        Assert.True(staged.Succeeded, JsonSerializer.Serialize(staged.Issues));
        Assert.Equal(before, AllHashes(Path.Combine(host.Root, "prepared", "workspace")));
        Assert.Equal(before, AllHashes(host.Workspace));
        var saved = JsonSerializer.Deserialize<MigrationReport>(File.ReadAllText(Path.Combine(host.Root, "prepared", "report.json")));
        Assert.True(saved!.Succeeded);
        Assert.Equal(exported.ArchiveSha256, saved.ArchiveSha256);
    }

    [Theory]
    [InlineData("absolute-path")]
    [InlineData("relative-windows-path")]
    [InlineData("missing-client")]
    [InlineData("broken-json")]
    [InlineData("missing-folder")]
    [InlineData("excluded-data")]
    [InlineData("unindexed-client")]
    [InlineData("duplicate-reference")]
    [InlineData("broken-jsonl")]
    [InlineData("absolute-jsonl")]
    [InlineData("relative-traversal")]
    public void Export_reports_blockers_instead_of_silently_omitting_data(string problem)
    {
        using var host = new ServerFixture();
        var (client, project) = Seed(host.Workspace);
        var paths = new WorkspaceMetadataPaths(host.Workspace);
        if (problem is "absolute-path" or "relative-windows-path")
        {
            var profile = client with { RelativePath = problem == "absolute-path" ? @"C:\OldWorkshop\client" : @"01_CLIENTS\CLIENT_TEST" };
            File.WriteAllText(Path.Combine(paths.ClientsDirectory, client.Id + ".json"), JsonSerializer.Serialize(profile));
        }
        if (problem == "missing-client") File.Delete(Path.Combine(paths.ClientsDirectory, client.Id + ".json"));
        if (problem == "broken-json") File.WriteAllText(Path.Combine(paths.ProjectsDirectory, project.Id + ".json"), "broken-json");
        if (problem == "missing-folder") Directory.Delete(Path.Combine(host.Workspace, "01_CLIENTS", "CLIENT_TEST", project.FolderName), true);
        if (problem == "excluded-data") File.WriteAllText(Path.Combine(host.Workspace, "important.txt"), "keep");
        if (problem == "unindexed-client")
        {
            File.Delete(Path.Combine(paths.ClientsDirectory, client.Id + ".json"));
            File.Delete(Path.Combine(paths.ProjectsDirectory, project.Id + ".json"));
        }
        if (problem == "duplicate-reference")
        {
            var duplicate = project with { Id = Guid.NewGuid(), FolderName = "CT-2026-001_OTHER", RelativePath = "01_CLIENTS/CLIENT_TEST/CT-2026-001_OTHER" };
            Directory.CreateDirectory(Path.Combine(host.Workspace, duplicate.RelativePath));
            File.WriteAllText(Path.Combine(paths.ProjectsDirectory, duplicate.Id + ".json"), JsonSerializer.Serialize(duplicate));
        }
        if (problem is "broken-jsonl" or "absolute-jsonl")
        {
            Directory.CreateDirectory(Path.Combine(paths.MetadataDirectory, "purchases"));
            File.WriteAllText(Path.Combine(paths.MetadataDirectory, "purchases", "2026.jsonl"), problem == "broken-jsonl"
                ? "broken-json" : JsonSerializer.Serialize(new { DocumentPath = @"C:\old-workshop\ticket.pdf" }));
        }
        if (problem == "relative-traversal") File.WriteAllText(Path.Combine(paths.MetadataDirectory, "future-module.json"), "{\"DocumentPath\":\"../../secret\"}");
        var before = AllHashes(host.Workspace);
        var package = Path.Combine(host.Root, "package");
        var report = new WorkspaceMigrationService().Export(host.Workspace, package);
        Assert.False(report.Succeeded);
        Assert.Contains(report.Issues, i => i.Blocking);
        Assert.Equal(before, AllHashes(host.Workspace));
        Assert.True(File.Exists(Path.Combine(package, "report.json")));
    }

    [Fact]
    public void Stage_audits_a_valid_backup_before_allowing_a_favourable_report()
    {
        using var host = new ServerFixture();
        var (client, _) = Seed(host.Workspace);
        var paths = new WorkspaceMetadataPaths(host.Workspace);
        File.WriteAllText(Path.Combine(paths.ClientsDirectory, client.Id + ".json"), "broken-json");
        var archive = Path.Combine(host.Root, "archive.zip");
        var hash = new WorkspaceBackupService().Create(host.Workspace, archive, true).ArchiveSha256;
        var report = new WorkspaceMigrationService().Stage(archive, Path.Combine(host.Root, "prepared"), hash);
        Assert.False(report.Succeeded);
        Assert.Contains(report.Issues, i => i.Code == "InvalidJson");
        Assert.True(File.Exists(Path.Combine(host.Root, "prepared", "workspace", ".ms3dprint-manager", "clients", client.Id + ".json")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Grouped_clients_do_not_hide_an_unindexed_sibling(bool legacySibling)
    {
        using var host = new ServerFixture();
        var (client, project) = Seed(host.Workspace);
        var group = Path.Combine(host.Workspace, "01_CLIENTS", "GROUP");
        Directory.CreateDirectory(group);
        Directory.Move(Path.Combine(host.Workspace, "01_CLIENTS", "CLIENT_TEST"), Path.Combine(group, "KNOWN"));
        var paths = new WorkspaceMetadataPaths(host.Workspace);
        File.WriteAllText(Path.Combine(paths.ClientsDirectory, client.Id + ".json"), JsonSerializer.Serialize(client with
            { FolderName = "KNOWN", RelativePath = "01_CLIENTS/GROUP/KNOWN" }));
        File.WriteAllText(Path.Combine(paths.ProjectsDirectory, project.Id + ".json"), JsonSerializer.Serialize(project with
            { RelativePath = "01_CLIENTS/GROUP/KNOWN/" + project.FolderName }));
        if (legacySibling)
        {
            Directory.CreateDirectory(Path.Combine(group, "LEGACY"));
            File.WriteAllText(Path.Combine(group, "LEGACY", "document.txt"), "keep");
        }
        var report = new WorkspaceMigrationService().Export(host.Workspace, Path.Combine(host.Root, "package"));
        Assert.Equal(!legacySibling, report.Succeeded);
        if (legacySibling) Assert.Contains(report.Issues, i => i.Code == "ClientProfileMissing" && i.Path.EndsWith("LEGACY"));
    }

    [Fact]
    public void Linux_case_collisions_and_symbolic_links_are_refused()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var host = new ServerFixture();
        Seed(host.Workspace);
        var folder = Path.Combine(host.Workspace, "01_CLIENTS", "CLIENT_TEST");
        File.WriteAllText(Path.Combine(folder, "a.txt"), "a");
        File.WriteAllText(Path.Combine(folder, "A.txt"), "a");
        var service = new WorkspaceMigrationService();
        Assert.False(service.Export(host.Workspace, Path.Combine(host.Root, "case-package")).Succeeded);
        File.Delete(Path.Combine(folder, "A.txt"));
        File.CreateSymbolicLink(Path.Combine(folder, "link.txt"), Path.Combine(folder, "a.txt"));
        Assert.False(service.Export(host.Workspace, Path.Combine(host.Root, "link-package")).Succeeded);
    }

    [Fact]
    public void Existing_or_nested_destinations_are_never_overwritten()
    {
        using var host = new ServerFixture();
        Seed(host.Workspace);
        var service = new WorkspaceMigrationService();
        var existing = Path.Combine(host.Root, "existing");
        Directory.CreateDirectory(existing);
        File.WriteAllText(Path.Combine(existing, "keep.txt"), "keep");
        Assert.Throws<IOException>(() => service.Export(host.Workspace, existing));
        Assert.Throws<IOException>(() => service.Export(host.Workspace, Path.Combine(host.Workspace, "package")));
        Assert.False(Directory.Exists(Path.Combine(host.Workspace, "package")));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(existing, "keep.txt")));
        Assert.Throws<IOException>(() => service.Stage("missing.zip", existing, new string('0', 64)));
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("truncated")]
    [InlineData("metadata-only")]
    public void Bad_or_incomplete_archives_leave_a_failure_report_and_no_activated_workspace(string problem)
    {
        using var host = new ServerFixture();
        Seed(host.Workspace);
        var archive = Path.Combine(host.Root, "archive.zip");
        var preview = new WorkspaceBackupService().Create(host.Workspace, archive, problem != "metadata-only");
        if (problem == "truncated") File.WriteAllBytes(archive, [1, 2, 3]);
        var expected = problem == "hash" ? new string('0', 64) : preview.ArchiveSha256;
        var prepared = Path.Combine(host.Root, "prepared");
        var report = new WorkspaceMigrationService().Stage(archive, prepared, expected);
        Assert.False(report.Succeeded);
        Assert.Contains(report.Issues, i => i.Blocking);
        Assert.True(File.Exists(Path.Combine(prepared, "report.json")));
        Assert.False(Directory.Exists(Path.Combine(prepared, "workspace")));
        Assert.True(File.Exists(Path.Combine(host.Workspace, "01_CLIENTS", "CLIENT_TEST", "CT-2026-001_TEST", "piece.stl")));
    }

    internal static (ClientProfile Client, ProjectProfile Project) Seed(string root)
    {
        var folder = Path.Combine(root, "01_CLIENTS", "CLIENT_TEST", "CT-2026-001_TEST");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "piece.stl"), [0, 1, 2, 255]);
        var now = DateTimeOffset.UtcNow;
        var client = new ClientProfile(Guid.NewGuid(), ClientKind.Professional, "CLIENT_TEST", "CT", "Client test", null, null,
            null, null, new(null, null, null, null, null), now, now, RelativePath: "01_CLIENTS/CLIENT_TEST");
        var project = new ProjectProfile(Guid.NewGuid(), client.Id, "CT", "CT-2026-001", "CT-2026-001_TEST", "Projet test",
            ProjectStatus.InProgress, now, null, null, null, now, RelativePath: "01_CLIENTS/CLIENT_TEST/CT-2026-001_TEST");
        var paths = new WorkspaceMetadataPaths(root);
        new ClientProfileStore(paths).Create(client);
        new ProjectProfileStore(paths).Create(project);
        File.WriteAllText(Path.Combine(paths.MetadataDirectory, "future-module.json"), "{\"Version\":1}");
        File.WriteAllText(Path.Combine(paths.HistoryDirectory, "old.json"), "{\"History\":true}");
        return (client, project);
    }

    private static string[] AllHashes(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
        .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/') + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))))
        .Order(StringComparer.Ordinal).ToArray();
}
