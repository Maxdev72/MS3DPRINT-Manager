using MS3DPRINT.Manager.App.ViewModels;
using MS3DPRINT.Manager.Core.Collections;
using MS3DPRINT.Manager.Core.Files;

namespace MS3DPRINT.Manager.Core.Tests.ViewModels;

public sealed class CollectionDetailViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MS3DPRINT-collection-detail-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void ReadFiles_ReturnsToCollectionRootWhenTheOpenedSubdirectoryWasRemoved()
    {
        var itemRoot = Path.Combine(_root, "02_MODELES_3D", "FIXATION");
        var openedDirectory = Path.Combine(itemRoot, "03_CAO_MASTER");
        Directory.CreateDirectory(openedDirectory);
        File.WriteAllText(Path.Combine(itemRoot, "notes.txt"), "x");
        var viewModel = new CollectionDetailViewModel(new CollectionItemSummary("FIXATION", itemRoot, DateTimeOffset.UtcNow));

        viewModel.ApplyFileListing(viewModel.ReadFilesForDirectory(new ProjectFileEntry("03_CAO_MASTER", openedDirectory, true, null, DateTimeOffset.UtcNow)));
        Directory.Delete(openedDirectory);

        var listing = viewModel.ReadFiles();

        Assert.Equal(itemRoot, listing.DirectoryPath);
        Assert.Equal("notes.txt", Assert.Single(listing.Entries).Name);
    }

    [Fact]
    public void CurrentPathLabel_ShowsShortCollectionBreadcrumb()
    {
        var itemRoot = Path.Combine(_root, "02_MODELES_3D", "FIXATION");
        var folder = Path.Combine(itemRoot, "03_CAO_MASTER", "STL");
        Directory.CreateDirectory(folder);
        var viewModel = new CollectionDetailViewModel(new CollectionItemSummary("FIXATION", itemRoot, DateTimeOffset.UtcNow));

        viewModel.ApplyFileListing(new CollectionFileListing(folder, []));

        Assert.Equal("FIXATION › 03_CAO_MASTER › STL", viewModel.CurrentPathLabel);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
