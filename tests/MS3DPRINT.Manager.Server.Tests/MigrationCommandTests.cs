using Microsoft.Extensions.Configuration;
using MS3DPRINT.Manager.Core.Migration;

namespace MS3DPRINT.Manager.Server.Tests;

public sealed class MigrationCommandTests
{
    [Fact]
    public void Portable_command_converts_a_private_copy_without_changing_the_configured_workspace()
    {
        using var host = new ServerFixture();
        var (client, _) = MigrationTests.Seed(host.Workspace);
        var profile = Path.Combine(host.Workspace, ".ms3dprint-manager", "clients", client.Id + ".json");
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(profile))!;
        json["RelativePath"] = @"01_CLIENTS\CLIENT_TEST";
        File.WriteAllText(profile, json.ToJsonString());
        var archive = Path.Combine(host.Root, "source.zip");
        var hash = new Core.Storage.WorkspaceBackupService().Create(host.Workspace, archive, true).ArchiveSha256;
        using var output = new StringWriter();
        Assert.Equal(0, MigrationCommands.Execute(["--prepare-portable-migration", archive, "--sha256", hash], Storage(host), output));
        var preparation = Assert.Single(Directory.GetDirectories(Path.Combine(host.State, "migrations")));
        Assert.True(File.Exists(Path.Combine(preparation, "portable-report.json")));
        Assert.True(File.Exists(Path.Combine(preparation, "portable.zip")));
        Assert.Contains("Aucune activation", output.ToString());
        Assert.Contains(@"01_CLIENTS\CLIENT_TEST", System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(profile))!["RelativePath"]!.GetValue<string>());
    }

    [Fact]
    public void Commands_prepare_in_private_state_and_leave_configured_workspace_unchanged()
    {
        using var host = new ServerFixture();
        MigrationTests.Seed(host.Workspace);
        var storage = Storage(host);
        var package = Path.Combine(host.Root, "package");
        using var output = new StringWriter();
        Assert.Equal(0, MigrationCommands.Execute(["--export-migration", package], storage, output));
        var report = System.Text.Json.JsonSerializer.Deserialize<MigrationReport>(File.ReadAllText(Path.Combine(package, "report.json")))!;
        Assert.Equal(0, MigrationCommands.Execute(["--stage-migration", Path.Combine(package, "workspace.zip"), "--sha256", report.ArchiveSha256!], storage, output));
        var migration = Assert.Single(Directory.GetDirectories(Path.Combine(host.State, "migrations")));
        Assert.True(File.Exists(Path.Combine(migration, "workspace", "01_CLIENTS", "CLIENT_TEST", "CT-2026-001_TEST", "piece.stl")));
        Assert.Equal(host.Workspace, storage.WorkspaceRoot);
        Assert.Contains("Aucune activation", output.ToString());
    }

    [Theory]
    [InlineData("--stage-migration")]
    [InlineData("--export-migration")]
    [InlineData("--prepare-portable-migration")]
    [InlineData("--unknown")]
    public void Invalid_arguments_do_not_create_a_preparation(string argument)
    {
        using var host = new ServerFixture();
        Directory.CreateDirectory(host.Workspace);
        var storage = Storage(host);
        Assert.Equal(1, MigrationCommands.Execute([argument], storage, new StringWriter()));
        Assert.False(Directory.Exists(Path.Combine(host.State, "migrations")));
    }

    [Fact]
    public void Failed_checks_return_nonzero_and_keep_a_report()
    {
        using var host = new ServerFixture();
        MigrationTests.Seed(host.Workspace);
        File.WriteAllText(Path.Combine(host.Workspace, "unmapped.txt"), "keep");
        var storage = Storage(host);
        var package = Path.Combine(host.Root, "package");
        Assert.Equal(2, MigrationCommands.Execute(["--export-migration", package], storage, new StringWriter()));
        Assert.True(File.Exists(Path.Combine(package, "report.json")));
        Assert.False(File.Exists(Path.Combine(package, "workspace.zip")));
    }

    private static ServerStorage Storage(ServerFixture host) => new(new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["Server:StateRoot"] = host.State, ["Server:WorkspaceRoot"] = host.Workspace }).Build());
}
