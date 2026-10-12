using MS3DPRINT.Manager.Core.Projects;

namespace MS3DPRINT.Manager.Core.Tests.Projects;

public sealed class ProjectLocatorTests
{
    [Fact]
    public void FindProjectDirectory_climbs_to_the_folder_named_like_a_project()
    {
        var file = Path.Combine(Path.GetTempPath(), "01_CLIENTS", "FELL_AGENCY", "FA-2026-001_SAMURAI", "03_CAO_3D", "sub", "piece.stl");

        Assert.Equal(Path.Combine(Path.GetTempPath(), "01_CLIENTS", "FELL_AGENCY", "FA-2026-001_SAMURAI"), ProjectLocator.FindProjectDirectory(file));
    }

    [Fact]
    public void FindProjectDirectory_is_null_outside_any_project()
        => Assert.Null(ProjectLocator.FindProjectDirectory(Path.Combine(Path.GetTempPath(), "02_MODELES_3D", "Calibration", "cube.stl")));

    [Fact]
    public void PhotosFolder_is_the_renders_folder_of_the_project()
    {
        var file = Path.Combine(Path.GetTempPath(), "01_CLIENTS", "ACME", "AC-2026-004_PIECE", "03_CAO_3D", "a.stl");

        Assert.Equal(Path.Combine(Path.GetTempPath(), "01_CLIENTS", "ACME", "AC-2026-004_PIECE", "06_PHOTOS_RENDUS"), ProjectLocator.PhotosFolder(file));
        Assert.Null(ProjectLocator.PhotosFolder(Path.Combine(Path.GetTempPath(), "ailleurs", "a.stl")));
    }
}
