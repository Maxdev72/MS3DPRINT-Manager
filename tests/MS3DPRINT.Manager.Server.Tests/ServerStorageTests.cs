using Microsoft.Extensions.Configuration;

namespace MS3DPRINT.Manager.Server.Tests;

public sealed class ServerStorageTests
{
    [Theory]
    [InlineData("same")]
    [InlineData("nested-state")]
    [InlineData("nested-workspace")]
    [InlineData("relative")]
    [InlineData("missing")]
    public void Invalid_configuration_is_rejected_before_creating_state(string kind)
    {
        using var host = new ServerFixture();
        var state = kind switch
        {
            "same" => host.Workspace,
            "nested-state" => Path.Combine(host.Workspace, "state"),
            "relative" => "state",
            "missing" => null,
            _ => host.State
        };
        var workspace = kind == "nested-workspace" ? Path.Combine(host.State, "workspace") : host.Workspace;
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Server:StateRoot"] = state, ["Server:WorkspaceRoot"] = workspace }).Build();
        Assert.Throws<InvalidOperationException>(() => new ServerStorage(config));
        Assert.False(Directory.Exists(host.State));
    }
}
