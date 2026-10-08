using MS3DPRINT.Manager.Desktop.Dashboard;

namespace MS3DPRINT.Manager.Core.Tests.Desktop;

public sealed class WorkspaceClientReaderTests
{
    [Fact]
    public void Load_returns_clients_from_the_existing_workspace_layout()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ms3dprint-clients-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "01_CLIENTS", "ACME"));

        try
        {
            var clients = new WorkspaceClientReader().Load(root);

            var client = Assert.Single(clients);
            Assert.Equal("ACME", client.DisplayName);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
