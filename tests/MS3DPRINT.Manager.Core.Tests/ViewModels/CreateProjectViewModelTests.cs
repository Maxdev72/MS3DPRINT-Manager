using MS3DPRINT.Manager.App.ViewModels;
using MS3DPRINT.Manager.Core.Storage;

namespace MS3DPRINT.Manager.Core.Tests.ViewModels;

public sealed class CreateProjectViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MS3DPRINT-project-preview-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void ReferencePreview_UsesTheClientProjectSnapshotBetweenEdits()
    {
        var clientPath = Path.Combine(_root, "01_CLIENTS", "MPO");
        Directory.CreateDirectory(Path.Combine(clientPath, "MPO-2026-001_EXISTANT"));
        var viewModel = new CreateProjectViewModel(_root, new ClientCodeRegistry(Path.Combine(_root, "data")))
        {
            SelectedClient = "MPO",
            ClientCode = "MPO",
            ProjectName = "Premier"
        };

        Assert.Equal("MPO-2026-002_PREMIER", viewModel.ReferencePreview);

        Directory.CreateDirectory(Path.Combine(clientPath, "MPO-2026-002_CREE_APRES_OUVERTURE"));
        viewModel.ProjectName = "Suivant";

        Assert.Equal("MPO-2026-002_SUIVANT", viewModel.ReferencePreview);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
