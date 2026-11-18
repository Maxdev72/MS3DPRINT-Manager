using MS3DPRINT.Manager.Core.Files;

namespace MS3DPRINT.Manager.Core.Tests.Files;

public sealed class ProjectFileBrowserAllFilesTests
{
    [Fact]
    public void EnumerateAll_lists_files_of_every_folder_with_their_relative_path_and_no_folders()
    {
        var root = Path.Combine(Path.GetTempPath(), "ms3d-allfiles-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "01_DEVIS", "sous"));
            File.WriteAllText(Path.Combine(root, "a.txt"), "a");
            File.WriteAllText(Path.Combine(root, "01_DEVIS", "b.pdf"), "b");
            File.WriteAllText(Path.Combine(root, "01_DEVIS", "sous", "c.stl"), "c");

            var names = new ProjectFileBrowser().EnumerateAll(root, root).Select(e => e.Name).Order().ToArray();

            Assert.Equal(new[] { "01_DEVIS" + Path.DirectorySeparatorChar + "b.pdf", Path.Combine("01_DEVIS", "sous", "c.stl"), "a.txt" }.Order().ToArray(), names);
        }
        finally { Directory.Delete(root, true); }
    }
}
