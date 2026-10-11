using MS3DPRINT.Manager.Desktop.Dashboard;

namespace MS3DPRINT.Manager.Core.Tests.Desktop;

public sealed class WorkspaceCollectionCreatorTests
{
    [Fact]
    public void Create_model_creates_its_standard_tree()
    {
        var root = Path.Combine(Path.GetTempPath(), "ms3dprint-model-" + Guid.NewGuid().ToString("N"));
        try
        {
            var item = new WorkspaceCollectionCreator().Create(root, "02_MODELES_3D", "Support bobine");

            Assert.True(Directory.Exists(item.Path));
            Assert.True(Directory.Exists(Path.Combine(item.Path, "05_STL")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
