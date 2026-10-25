using MS3DPRINT.Manager.Core.Files;

namespace MS3DPRINT.Manager.Core.Tests.Files;

public sealed class FileCatalogFilterTests
{
    private static ProjectFileEntry Entry(string name, bool folder = false) => new(name, Path.Combine(Path.GetTempPath(), name), folder, folder ? null : 10, DateTimeOffset.Now);

    [Theory]
    [InlineData(FileFamily.Models, "support.STL")]
    [InlineData(FileFamily.Models, "plateau.3mf")]
    [InlineData(FileFamily.Models, "scan.obj")]
    [InlineData(FileFamily.Cad, "master.STEP")]
    [InlineData(FileFamily.Cad, "master.FCStd")]
    [InlineData(FileFamily.Pdf, "plan.pdf")]
    [InlineData(FileFamily.Images, "photo.JPEG")]
    [InlineData(FileFamily.Other, "notice.txt")]
    public void Formats_are_recognized_and_folders_remain_accessible(FileFamily family, string name)
    {
        var directory = Entry("Documents", true); var file = Entry(name);
        Assert.Equal(new[] { directory, file }, FileCatalogFilter.Filter(new[] { directory, file }, "", family));
        Assert.Equal(new[] { directory }, FileCatalogFilter.Filter(new[] { directory, file }, "", family == FileFamily.Other ? FileFamily.Pdf : FileFamily.Other));
    }

    [Fact]
    public void All_search_terms_match_ignoring_accents_and_case_without_modifying_source()
    {
        var entries = new[] { Entry("Pièce ÉTÉ.stl"), Entry("piece hiver.stl"), Entry("Été", true), Entry("été.pdf") };
        Assert.Equal(entries.Take(1), FileCatalogFilter.Filter(entries, "PIECE ete", FileFamily.Models));
        Assert.Equal(entries, FileCatalogFilter.Filter(entries, "", FileFamily.All));
        Assert.Empty(FileCatalogFilter.Filter(entries, "inconnu", FileFamily.All));
    }
}
