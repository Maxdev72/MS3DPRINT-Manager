using System.IO.Compression;
using System.Text;
using MS3DPRINT.Manager.Desktop.Viewer;

namespace MS3DPRINT.Manager.Core.Tests.Preview;

public sealed class ThreeMfReaderTests : IDisposable
{
    private const string Core = "http://schemas.microsoft.com/3dmanufacturing/core/2015/02";
    private const string Production = "http://schemas.microsoft.com/3dmanufacturing/production/2015/06";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ms3d-3mf-" + Guid.NewGuid().ToString("N"));

    public ThreeMfReaderTests() => Directory.CreateDirectory(_directory);

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }

    private const string CubeMesh = """
        <mesh>
          <vertices>
            <vertex x="0" y="0" z="0"/><vertex x="10" y="0" z="0"/><vertex x="10" y="10" z="0"/><vertex x="0" y="10" z="0"/>
            <vertex x="0" y="0" z="10"/><vertex x="10" y="0" z="10"/><vertex x="10" y="10" z="10"/><vertex x="0" y="10" z="10"/>
          </vertices>
          <triangles>
            <triangle v1="0" v2="2" v3="1"/><triangle v1="0" v2="3" v3="2"/><triangle v1="4" v2="5" v3="6"/><triangle v1="4" v2="6" v3="7"/>
            <triangle v1="0" v2="1" v3="5"/><triangle v1="0" v2="5" v3="4"/><triangle v1="1" v2="2" v3="6"/><triangle v1="1" v2="6" v3="5"/>
            <triangle v1="2" v2="3" v3="7"/><triangle v1="2" v2="7" v3="6"/><triangle v1="3" v2="0" v3="4"/><triangle v1="3" v2="4" v3="7"/>
          </triangles>
        </mesh>
        """;

    private string Archive(params (string Name, string Content)[] entries)
    {
        var path = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".3mf");
        using var stream = File.Create(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
            writer.Write(content);
        }
        return path;
    }

    private static string Model(string body, string unit = "millimeter", string extra = "")
        => $"""<?xml version="1.0" encoding="UTF-8"?><model unit="{unit}" xmlns="{Core}" xmlns:p="{Production}" {extra}>{body}</model>""";

    [Fact]
    public void A_single_object_is_read_with_its_triangles_and_size()
    {
        var path = Archive(("3D/3dmodel.model", Model($"""<resources><object id="1" type="model">{CubeMesh}</object></resources><build><item objectid="1"/></build>""")));

        var mesh = ThreeMfReader.Load(path);

        Assert.Equal(12, mesh.TriangleCount);
        Assert.Equal(10f, mesh.Size.X, 3);
        Assert.Equal(10f, mesh.Size.Z, 3);
    }

    [Fact]
    public void The_position_of_a_build_item_is_applied()
    {
        var path = Archive(("3D/3dmodel.model", Model($"""<resources><object id="1">{CubeMesh}</object></resources><build><item objectid="1" transform="1 0 0 0 1 0 0 0 1 100 20 5"/></build>""")));

        var mesh = ThreeMfReader.Load(path);

        Assert.Equal(100f, mesh.Min.X, 3);
        Assert.Equal(20f, mesh.Min.Y, 3);
        Assert.Equal(5f, mesh.Min.Z, 3);
    }

    [Fact]
    public void Two_items_of_the_plate_are_both_shown()
    {
        var path = Archive(("3D/3dmodel.model", Model($"""<resources><object id="1">{CubeMesh}</object></resources><build><item objectid="1"/><item objectid="1" transform="1 0 0 0 1 0 0 0 1 50 0 0"/></build>""")));

        var mesh = ThreeMfReader.Load(path);

        Assert.Equal(24, mesh.TriangleCount);
        Assert.Equal(60f, mesh.Size.X, 3);
    }

    [Fact]
    public void Components_kept_in_another_model_file_are_assembled_like_a_Bambu_Studio_plate()
    {
        var root = Model("""
            <resources><object id="2" type="model"><components><component p:path="/3D/Objects/part.model" objectid="1" transform="2 0 0 0 2 0 0 0 2 0 0 0"/></components></object></resources>
            <build><item objectid="2" transform="1 0 0 0 1 0 0 0 1 10 0 0"/></build>
            """);
        var part = Model($"""<resources><object id="1" type="model">{CubeMesh}</object></resources>""");
        var path = Archive(("3D/3dmodel.model", root), ("3D/Objects/part.model", part));

        var mesh = ThreeMfReader.Load(path);

        Assert.Equal(12, mesh.TriangleCount);
        Assert.Equal(20f, mesh.Size.X, 3);   // the 10 mm cube scaled by 2
        Assert.Equal(10f, mesh.Min.X, 3);    // then moved by the item
    }

    [Theory]
    [InlineData("inch", 254f)]
    [InlineData("centimeter", 100f)]
    [InlineData("meter", 10000f)]
    [InlineData("millimeter", 10f)]
    public void The_size_is_given_in_millimetres_whatever_the_unit_of_the_file(string unit, float expectedWidth)
    {
        var path = Archive(("3D/3dmodel.model", Model($"""<resources><object id="1">{CubeMesh}</object></resources><build><item objectid="1"/></build>""", unit)));

        Assert.Equal(expectedWidth, ThreeMfReader.Load(path).Size.X, 2);
    }

    [Fact]
    public void Supports_and_items_that_are_not_printable_are_left_out()
    {
        var path = Archive(("3D/3dmodel.model", Model($"""
            <resources><object id="1">{CubeMesh}</object><object id="2" type="support">{CubeMesh}</object><object id="3">{CubeMesh}</object></resources>
            <build><item objectid="1"/><item objectid="2"/><item objectid="3" printable="0" transform="1 0 0 0 1 0 0 0 1 500 0 0"/></build>
            """)));

        var mesh = ThreeMfReader.Load(path);

        Assert.Equal(12, mesh.TriangleCount);
        Assert.Equal(10f, mesh.Size.X, 3);
    }

    [Fact]
    public void The_main_model_is_found_through_the_package_relationships()
    {
        var relationships = """<?xml version="1.0"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Target="/3D/plate.model" Id="rel0" Type="http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel"/></Relationships>""";
        var path = Archive(("_rels/.rels", relationships),
            ("3D/plate.model", Model($"""<resources><object id="1">{CubeMesh}</object></resources><build><item objectid="1"/></build>""")));

        Assert.Equal(12, ThreeMfReader.Load(path).TriangleCount);
    }

    [Fact]
    public void A_file_without_a_build_section_shows_every_object_that_has_a_shape()
    {
        var path = Archive(("3D/3dmodel.model", Model($"""<resources><object id="1">{CubeMesh}</object></resources>""")));

        Assert.Equal(12, ThreeMfReader.Load(path).TriangleCount);
    }

    [Fact]
    public void A_triangle_that_points_to_a_missing_vertex_is_refused()
    {
        var broken = """<mesh><vertices><vertex x="0" y="0" z="0"/></vertices><triangles><triangle v1="0" v2="1" v3="2"/></triangles></mesh>""";
        var path = Archive(("3D/3dmodel.model", Model($"""<resources><object id="1">{broken}</object></resources><build><item objectid="1"/></build>""")));

        Assert.Throws<InvalidDataException>(() => ThreeMfReader.Load(path));
    }

    [Fact]
    public void A_circular_assembly_is_refused_instead_of_looping()
    {
        var path = Archive(("3D/3dmodel.model", Model("""<resources><object id="1"><components><component objectid="1"/></components></object></resources><build><item objectid="1"/></build>""")));

        Assert.Throws<InvalidDataException>(() => ThreeMfReader.Load(path));
    }

    [Fact]
    public void A_missing_main_model_is_reported()
    {
        var path = Archive(("readme.txt", "x"));

        Assert.Throws<InvalidDataException>(() => ThreeMfReader.Load(path));
    }

    [Fact]
    public void The_viewer_loader_reads_3mf_and_reports_unreadable_files_as_invalid_data()
    {
        var good = Archive(("3D/3dmodel.model", Model($"""<resources><object id="1">{CubeMesh}</object></resources><build><item objectid="1"/></build>""")));
        Assert.Equal(12, MeshLoader.Load(good).TriangleCount);

        var notZip = Path.Combine(_directory, "faux.3mf");
        File.WriteAllText(notZip, "ceci n'est pas une archive");
        Assert.Throws<InvalidDataException>(() => MeshLoader.Load(notZip));

        var malicious = Archive(("3D/3dmodel.model", """<?xml version="1.0"?><!DOCTYPE model [<!ENTITY x SYSTEM "file:///c:/windows/win.ini">]><model><resources/></model>"""));
        Assert.Throws<InvalidDataException>(() => MeshLoader.Load(malicious));
    }
}
