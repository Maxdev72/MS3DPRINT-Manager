using MS3DPRINT.Manager.Core.Files;
using MS3DPRINT.Manager.Core.Templates;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Tests.Templates;

public sealed class ProjectTemplateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ms3d-template-" + Guid.NewGuid().ToString("N"));

    public ProjectTemplateTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static ProjectTemplate Template(params (string Path, ProjectFileCategory? Category)[] folders)
        => new(folders.Select(folder => new ProjectTemplateFolder(folder.Path, folder.Category)).ToArray());

    [Fact]
    public void The_standard_template_is_valid_and_keeps_the_built_in_folders()
    {
        Assert.Null(ProjectTemplate.Default.Validate());
        Assert.Equal(FolderTemplates.Project, ProjectTemplate.Default.Paths);
    }

    [Theory]
    [InlineData(ProjectFileCategory.Documents, "01_DEVIS_FACTURES")]
    [InlineData(ProjectFileCategory.ClientFiles, "02_FICHIERS_CLIENT")]
    public void The_standard_template_sorts_like_before(ProjectFileCategory category, string expected)
        => Assert.Equal(expected, ProjectTemplate.Default.DirectoryFor(category));

    [Fact]
    public void The_standard_template_sorts_nested_folders_with_the_system_separator()
    {
        Assert.Equal(Path.Combine("03_CAO_3D", "03_STL"), ProjectTemplate.Default.DirectoryFor(ProjectFileCategory.Stl));
        Assert.Equal(Path.Combine("04_IMPRESSION_3D", "02_3MF"), ProjectTemplate.Default.DirectoryFor(ProjectFileCategory.ThreeMf));
    }

    [Fact]
    public void A_category_without_a_folder_falls_back_to_the_client_files_folder()
    {
        var template = Template(("Brief", null), ("Tout", ProjectFileCategory.ClientFiles));

        Assert.Equal("Tout", template.DirectoryFor(ProjectFileCategory.Stl));
    }

    [Fact]
    public void Imports_follow_a_renamed_folder()
    {
        var template = Template(("Fichiers", ProjectFileCategory.ClientFiles), ("Modeles/Impression", ProjectFileCategory.Stl));

        var destination = ProjectFileClassifier.Suggest("pion.stl", "CLI-2026-001_TEST", template);

        Assert.Equal(Path.Combine("Modeles", "Impression"), destination.RelativeDirectory);
    }

    [Theory]
    [InlineData("")]
    [InlineData("..")]
    [InlineData("A/../B")]
    [InlineData("A//B")]
    [InlineData("A\\B")]
    [InlineData("C:/Temp")]
    [InlineData("/abs")]
    [InlineData("a:b")]
    [InlineData("fin.")]
    [InlineData("CON")]
    [InlineData("nul.txt")]
    public void Unsafe_folder_names_are_refused(string path)
        => Assert.NotNull(Template((path, null), ("Client", ProjectFileCategory.ClientFiles)).Validate());

    [Fact]
    public void Duplicates_are_refused_whatever_the_case()
        => Assert.NotNull(Template(("Photos", null), ("photos", null), ("Client", ProjectFileCategory.ClientFiles)).Validate());

    [Fact]
    public void A_category_can_have_only_one_folder()
        => Assert.NotNull(Template(("A", ProjectFileCategory.Stl), ("B", ProjectFileCategory.Stl), ("C", ProjectFileCategory.ClientFiles)).Validate());

    [Fact]
    public void A_client_files_folder_is_required()
        => Assert.NotNull(Template(("A", ProjectFileCategory.Stl)).Validate());

    [Fact]
    public void An_empty_template_is_refused()
        => Assert.NotNull(new ProjectTemplate([]).Validate());

    [Fact]
    public void Nothing_saved_gives_the_standard_template()
        => Assert.Equal(ProjectTemplate.Default.Paths, new ProjectTemplateStore(new WorkspaceMetadataPaths(_root)).Load().Paths);

    [Fact]
    public void A_saved_template_is_read_back()
    {
        var store = new ProjectTemplateStore(new WorkspaceMetadataPaths(_root));
        store.Save(Template(("Brief", null), ("Pieces/STL", ProjectFileCategory.Stl), ("Divers", ProjectFileCategory.ClientFiles)));

        var loaded = store.Load();

        Assert.Equal(["Brief", "Pieces/STL", "Divers"], loaded.Paths);
        Assert.Equal(ProjectFileCategory.Stl, loaded.Folders[1].Category);
    }

    [Fact]
    public void An_invalid_template_is_not_saved()
    {
        var store = new ProjectTemplateStore(new WorkspaceMetadataPaths(_root));

        Assert.Throws<ArgumentException>(() => store.Save(Template(("..", ProjectFileCategory.ClientFiles))));
        Assert.Equal(ProjectTemplate.Default.Paths, store.Load().Paths);
    }

    [Fact]
    public void A_damaged_file_gives_the_standard_template()
    {
        var paths = new WorkspaceMetadataPaths(_root);
        Directory.CreateDirectory(paths.MetadataDirectory);
        File.WriteAllText(Path.Combine(paths.MetadataDirectory, "project-template.json"), "{ not json");

        Assert.Equal(ProjectTemplate.Default.Paths, new ProjectTemplateStore(paths).Load().Paths);
    }

    [Fact]
    public void A_file_holding_an_unsafe_template_gives_the_standard_template()
    {
        var paths = new WorkspaceMetadataPaths(_root);
        Directory.CreateDirectory(paths.MetadataDirectory);
        File.WriteAllText(Path.Combine(paths.MetadataDirectory, "project-template.json"), "{\"Folders\":[{\"Path\":\"../evil\",\"Category\":\"ClientFiles\"}]}");

        Assert.Equal(ProjectTemplate.Default.Paths, new ProjectTemplateStore(paths).Load().Paths);
    }

    [Fact]
    public void Reset_goes_back_to_the_standard_template()
    {
        var store = new ProjectTemplateStore(new WorkspaceMetadataPaths(_root));
        store.Save(Template(("Seul", ProjectFileCategory.ClientFiles)));

        store.Reset();

        Assert.Equal(ProjectTemplate.Default.Paths, store.Load().Paths);
    }
}
