using Microsoft.Extensions.Configuration;
using MS3DPRINT.Manager.Core.Migration;

namespace MS3DPRINT.Manager.Server.Tests;

public sealed class MigrationCommandTests
{
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
