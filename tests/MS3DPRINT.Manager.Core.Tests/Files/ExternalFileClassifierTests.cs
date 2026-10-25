using System.IO.Compression;
using MS3DPRINT.Manager.Core.Files;
using MS3DPRINT.Manager.Core.Templates;

namespace MS3DPRINT.Manager.Core.Tests.Files;

public sealed class ExternalFileClassifierTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ms3d-extfiles-" + Guid.NewGuid().ToString("N"));
    private string Project => Path.Combine(_root, "DUP-2026-001_Support");
    private const string ProjectName = "DUP-2026-001_Support";
    private static readonly DateTimeOffset Later = DateTimeOffset.UtcNow.AddMinutes(5);
    public ExternalFileClassifierTests() { Directory.CreateDirectory(Path.Combine(Project, "03_CAO_3D", "03_STL")); Directory.CreateDirectory(Path.Combine(Project, "04_IMPRESSION_3D", "02_3MF")); }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private static string Zip(string path, string application, params string[] objectNames)
    {
        using var stream = new FileStream(path, FileMode.Create);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        void Add(string name, string text) { var e = archive.CreateEntry(name); using var w = new StreamWriter(e.Open()); w.Write(text); }
        Add("3D/3dmodel.model", $"<model><metadata name=\"Application\">{application}</metadata><metadata name=\"Title\"></metadata></model>");
        Add("Metadata/model_settings.config", "<config>" + string.Concat(objectNames.Select(n => $"<object id=\"1\"><metadata key=\"name\" value=\"{n}\"/></object>")) + "</config>");
        return path;
    }

    private string Stl(string folder, string name) { var p = Path.Combine(Project, folder, name); File.WriteAllText(p, "solid"); return p; }

    [Fact]
    public void Bambu_project_next_to_its_stl_is_proposed_for_the_3mf_folder_with_the_project_reference()
    {
        Stl(Path.Combine("03_CAO_3D", "03_STL"), "Support__DUP-2026-001.stl");
        var file = Zip(Path.Combine(Project, "03_CAO_3D", "03_STL", "Support.3mf"), "BambuStudio-02.06.00.51", "Support__DUP-2026-001.stl", "Support__DUP-2026-001.stl-Dowel");
        var proposal = Assert.Single(ExternalFileClassifier.Scan(Project, ProjectName, ProjectTemplate.Default, now: Later));
        Assert.Equal(file, proposal.FilePath);
        Assert.Equal("Bambu Studio", proposal.SlicerName);
        Assert.Equal("Support__DUP-2026-001.stl", proposal.SourceModel);
        Assert.Equal(ProjectFileCategory.ThreeMf, proposal.Destination.Category);
        Assert.Equal(Path.Combine("04_IMPRESSION_3D", "02_3MF"), proposal.Destination.RelativeDirectory);
        Assert.Equal("Support__DUP-2026-001.3mf", proposal.Destination.FileName);
        Assert.Contains("modèle source", proposal.Reason);
    }

    [Fact]
    public void Orca_project_without_a_matching_stl_is_still_proposed_but_says_so()
    {
        Zip(Path.Combine(Project, "Divers.3mf"), "OrcaSlicer-2.1.0", "Autre.stl");
        var proposal = Assert.Single(ExternalFileClassifier.Scan(Project, ProjectName, ProjectTemplate.Default, now: Later));
        Assert.Equal("OrcaSlicer", proposal.SlicerName); Assert.Null(proposal.SourceModel); Assert.Contains("aucun STL", proposal.Reason);
    }

    [Fact]
    public void Files_already_in_place_unknown_origins_archives_and_non_3mf_are_never_proposed()
    {
        Zip(Path.Combine(Project, "04_IMPRESSION_3D", "02_3MF", "Deja.3mf"), "BambuStudio-02.06");
        Zip(Path.Combine(Project, "03_CAO_3D", "03_STL", "Cao.3mf"), "Fusion360 2.0");
        Directory.CreateDirectory(Path.Combine(Project, "99_ARCHIVES")); Zip(Path.Combine(Project, "99_ARCHIVES", "Vieux.3mf"), "BambuStudio-02.06");
        File.WriteAllText(Path.Combine(Project, "03_CAO_3D", "03_STL", "faux.3mf"), "pas un zip");
        File.WriteAllText(Path.Combine(Project, "03_CAO_3D", "03_STL", "note.txt"), "x");
        Assert.Empty(ExternalFileClassifier.Scan(Project, ProjectName, ProjectTemplate.Default, now: Later));
    }

    [Fact]
    public void A_file_written_a_moment_ago_or_held_open_is_not_proposed()
    {
        var recent = Zip(Path.Combine(Project, "Recent.3mf"), "BambuStudio-02.06");
        Assert.Empty(ExternalFileClassifier.Scan(Project, ProjectName, ProjectTemplate.Default));
        var locked = Zip(Path.Combine(Project, "Ouvert.3mf"), "BambuStudio-02.06");
        File.SetLastWriteTimeUtc(locked, DateTime.UtcNow.AddHours(-1)); File.SetLastWriteTimeUtc(recent, DateTime.UtcNow.AddHours(-1));
        using var hold = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None);
        var proposal = Assert.Single(ExternalFileClassifier.Scan(Project, ProjectName, ProjectTemplate.Default));
        Assert.Equal(recent, proposal.FilePath);
    }

    [Fact]
    public void Existing_destination_gets_a_numbered_name_and_ignored_files_are_skipped()
    {
        File.WriteAllText(Path.Combine(Project, "04_IMPRESSION_3D", "02_3MF", "Plaque__DUP-2026-001.3mf"), "existant");
        Zip(Path.Combine(Project, "Plaque.3mf"), "BambuStudio-02.06");
        var proposal = Assert.Single(ExternalFileClassifier.Scan(Project, ProjectName, ProjectTemplate.Default, now: Later));
        Assert.Equal("Plaque__DUP-2026-001 (2).3mf", proposal.Destination.FileName);
        Assert.Empty(ExternalFileClassifier.Scan(Project, ProjectName, ProjectTemplate.Default, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Plaque.3mf" }, Later));
    }

    [Fact]
    public void Accepting_a_proposal_moves_the_file_without_overwriting()
    {
        Zip(Path.Combine(Project, "Piece.3mf"), "BambuStudio-02.06");
        var proposal = Assert.Single(ExternalFileClassifier.Scan(Project, ProjectName, ProjectTemplate.Default, now: Later));
        var target = new ProjectFileTransferService().Move(proposal.FilePath, Project, proposal.Destination);
        Assert.True(File.Exists(target)); Assert.False(File.Exists(proposal.FilePath));
        Assert.Equal(Path.Combine(Project, "04_IMPRESSION_3D", "02_3MF", "Piece__DUP-2026-001.3mf"), target);
        Assert.Empty(ExternalFileClassifier.Scan(Project, ProjectName, ProjectTemplate.Default, now: Later));
    }

    [Fact]
    public void Invalid_project_folder_names_or_missing_folders_give_no_proposals_and_inspector_rejects_bad_files()
    {
        Assert.Empty(ExternalFileClassifier.Scan(Project, "pas-une-reference", ProjectTemplate.Default, now: Later));
        Assert.Empty(ExternalFileClassifier.Scan(Path.Combine(_root, "absent"), ProjectName, ProjectTemplate.Default, now: Later));
        Assert.Null(ThreeMfInspector.Inspect(Path.Combine(_root, "absent.3mf")));
        var junk = Path.Combine(_root, "junk.3mf"); File.WriteAllText(junk, "x");
        Assert.Null(ThreeMfInspector.Inspect(junk));
        var bambu = Zip(Path.Combine(_root, "ok.3mf"), "BambuStudio-02.06.00.51", "A.stl_1");
        var inspection = ThreeMfInspector.Inspect(bambu)!;
        Assert.Equal(SlicerKind.BambuStudio, inspection.Slicer); Assert.Equal("02.06.00.51", inspection.ApplicationVersion); Assert.Equal(new[] { "A.stl_1" }, inspection.ModelNames);
    }

    [Fact]
    public void Ignored_store_persists_relative_paths_case_insensitively()
    {
        var store = new IgnoredClassificationStore(_root);
        Assert.Empty(store.Load());
        store.Add(@"CLIENTS\X\Projet\Piece.3mf"); store.Add("CLIENTS/X/Projet/Autre.3mf");
        var loaded = new IgnoredClassificationStore(_root).Load();
        Assert.Contains("clients/x/projet/piece.3mf", loaded); Assert.Equal(2, loaded.Count);
    }
}
