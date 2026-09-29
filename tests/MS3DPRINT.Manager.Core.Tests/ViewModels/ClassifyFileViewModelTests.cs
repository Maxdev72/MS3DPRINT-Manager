using MS3DPRINT.Manager.App.ViewModels;

namespace MS3DPRINT.Manager.Core.Tests.ViewModels;

public sealed class ClassifyFileViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MS3DPRINT-classify-view-model-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void SelectingPdfAndProject_ProposesInvoiceFolderAndReferencedName()
    {
        var project = Path.Combine(_root, "01_CLIENTS", "MPO", "MPO-2026-001_OUTILLAGE");
        Directory.CreateDirectory(Path.Combine(project, "01_DEVIS_FACTURES"));
        Directory.CreateDirectory(Path.Combine(project, "02_FICHIERS_CLIENT"));
        var viewModel = new ClassifyFileViewModel(_root)
        {
            SourcePath = Path.Combine(_root, "DEV2026-05.pdf")
        };

        viewModel.SelectedProject = Assert.Single(viewModel.Projects);

        Assert.Equal("01_DEVIS_FACTURES", viewModel.SelectedRelativeDirectory);
        Assert.Equal("DEV2026-05__MPO-2026-001.pdf", viewModel.FinalFileName);
    }

    [Fact]
    public void Constructor_ExcludesClientMetadataAndArchiveFoldersFromProjects()
    {
        var client = Path.Combine(_root, "01_CLIENTS", "MPO");
        Directory.CreateDirectory(Path.Combine(client, "00_CLIENT"));
        Directory.CreateDirectory(Path.Combine(client, "99_ARCHIVES"));
        Directory.CreateDirectory(Path.Combine(client, "MPO-2026-001_OUTILLAGE"));

        var viewModel = new ClassifyFileViewModel(_root);

        Assert.Equal(["MPO — MPO-2026-001_OUTILLAGE"], viewModel.Projects);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
