using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;
using MS3DPRINT.Manager.Desktop;
using MS3DPRINT.Manager.Desktop.Rendering;
using MS3DPRINT.Manager.Desktop.Viewer;

namespace MS3DPRINT.Manager.Core.Tests.Desktop;

public sealed class ProductRendererTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ms3d-render-" + Guid.NewGuid().ToString("N"));

    public ProductRendererTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static readonly RenderSettings Small = new("#D93A3A", "matte", 0.2, "light", "#2F6FD6", 30, 20, 64, 48, 4);

    [Theory]
    [InlineData(0d, -50d)]
    [InlineData(null, 0d)]
    public async Task Blender_keeps_a_stable_ground_for_a_raised_part_and_preserves_legacy_defaults(double? ground, double expected)
    {
        if (BlenderLocator.Detect() is not { } blender) return;
        var stl = Path.Combine(_directory, "raised.stl");
        StlWriter.Write(stl, new MeshModel([new(0, 0, 50), new(20, 0, 50), new(0, 20, 50), new(0, 0, 70)], [0, 2, 1, 0, 1, 3, 0, 3, 2, 1, 2, 3]));
        var settings = System.Text.Json.Nodes.JsonNode.Parse(ProductRenderer.SettingsJson(stl, Path.Combine(_directory, "raised.png"), Small with { Engine = "eevee" }))!;
        if (ground is not null) settings["groundZ"] = ground.Value;
        var settingsPath = Path.Combine(_directory, "raised.json");
        File.WriteAllText(settingsPath, settings.ToJsonString());
        var driver = Path.Combine(_directory, "inspect-floor.py");
        var resultPath = Path.Combine(_directory, "floor.json");
        var scriptPath = Path.Combine(_directory, "product_render.py");
        using (var source = typeof(ProductRenderer).Assembly.GetManifestResourceStream("MS3DPRINT.Manager.Desktop.Rendering.product_render.py")!)
        using (var destination = File.Create(scriptPath)) source.CopyTo(destination);
        File.WriteAllText(driver, "import runpy, bpy, json\nrunpy.run_path(" + JsonSerializer.Serialize(scriptPath) + ")\n"
            + "value = bpy.data.objects['Backdrop'].location.z / bpy.data.objects['Turntable'].scale.z\n"
            + "json.dump(value, open(" + JsonSerializer.Serialize(resultPath) + ", 'w'))\n");
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(blender)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            ArgumentList = { "-b", "--factory-startup", "--python-exit-code", "1", "-P", driver, "--", settingsPath }
        })!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, await errors + await output);
        Assert.Equal(expected, JsonSerializer.Deserialize<double>(File.ReadAllText(resultPath)), 3);
    }

    private string FakeBlender(string folder)
    {
        var path = Path.Combine(_directory, "Blender Foundation", folder, "blender.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
        return path;
    }

    [Fact]
    public void Locator_takes_the_newest_installed_version_by_number()
    {
        FakeBlender("Blender 4.4");
        var newest = FakeBlender("Blender 10.1");
        FakeBlender("Blender 5.3");

        Assert.Equal(newest, BlenderLocator.Detect(Path.Combine(_directory, "Blender Foundation"), Path.Combine(_directory, "steam.exe")));
    }

    [Fact]
    public void Locator_falls_back_to_steam_then_to_nothing()
    {
        var steam = Path.Combine(_directory, "steam-blender.exe");
        Assert.Null(BlenderLocator.Detect(Path.Combine(_directory, "none"), steam));

        File.WriteAllText(steam, "");
        Assert.Equal(steam, BlenderLocator.Detect(Path.Combine(_directory, "none"), steam));
    }

    [Fact]
    public void Environments_come_from_the_blender_install_with_french_names()
    {
        var exe = FakeBlender("Blender 5.3");
        var world = Path.Combine(Path.GetDirectoryName(exe)!, "5.3", "datafiles", "studiolights", "world");
        Directory.CreateDirectory(world);
        foreach (var name in new[] { "forest.exr", "mine.hdr", "license.txt" }) File.WriteAllText(Path.Combine(world, name), "");

        var found = BlenderLocator.Environments(exe);

        Assert.Equal(["Forêt", "mine"], found.Select(e => e.Label));
        Assert.All(found, e => Assert.True(BlenderLocator.IsEnvironmentFile(e.Path)));
    }

    [Fact]
    public void Settings_file_carries_every_key_the_bundled_script_reads()
    {
        using var source = typeof(ProductRenderer).Assembly.GetManifestResourceStream("MS3DPRINT.Manager.Desktop.Rendering.product_render.py");
        Assert.NotNull(source);
        var script = new StreamReader(source).ReadToEnd();
        using var json = JsonDocument.Parse(ProductRenderer.SettingsJson(@"C:\a b\model.stl", @"C:\a b\out.png", Small));

        var keys = Regex.Matches(script, @"settings\[""(\w+)""\]").Select(match => match.Groups[1].Value).Distinct().ToList();
        Assert.Contains("layerHeight", keys);
        foreach (var key in keys) Assert.True(json.RootElement.TryGetProperty(key, out _), "clé absente : " + key);
        Assert.Equal(@"C:\a b\model.stl", json.RootElement.GetProperty("model").GetString());
    }

    [Theory]
    [InlineData("PLA Silk Or", "silk")]
    [InlineData("PLA Soie cuivre", "silk")]
    [InlineData("PETG HF Bleu", "glossy")]
    [InlineData("PLA Basic", "matte")]
    public void Finish_follows_the_filament_name(string text, string finish)
        => Assert.Equal(finish, MainWindow.RenderFinish(text));

    [Fact]
    public void Preferences_keep_custom_colours_and_survive_a_damaged_file()
    {
        var path = Path.Combine(_directory, "render.json");
        var store = new RenderPreferencesStore(path);
        Assert.Empty(store.Load().CustomColors);

        store.Save(RenderPreferences.Default with { CustomColors = [new("Bleu nuit soie", "#1A2B4C", "silk")], Scene = 2, Yaw = 1.5 });
        var loaded = store.Load();
        Assert.Equal(new RenderColor("Bleu nuit soie", "#1A2B4C", "silk"), Assert.Single(loaded.CustomColors));
        Assert.Equal((2, 1.5), (loaded.Scene, loaded.Yaw));

        File.WriteAllText(path, "{ pas du json");
        Assert.Equal(RenderPreferences.Default, store.Load());
    }

    [Fact]
    public void A_part_drawn_lying_along_y_stands_up_after_a_quarter_turn_forward()
    {
        var lying = new MeshModel([new(0, 0, 0), new(10, 0, 0), new(0, 80, 0), new(0, 0, 10)], [0, 1, 2, 0, 1, 3]);

        var standing = ProductRenderer.Turned(lying, Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2));

        Assert.Equal(80, standing.Size.Z, 3);
        Assert.Equal(lying.TriangleCount, standing.TriangleCount);
        Assert.Same(lying, ProductRenderer.Turned(lying, Quaternion.Identity));
    }

    private sealed class Recorder : IProgress<double>
    {
        public double Highest;
        public void Report(double value) { lock (this) Highest = Math.Max(Highest, value); }
    }

    [Fact]
    public void Output_is_re_encoded_resized_and_flattened_for_jpeg()
    {
        var png = Path.Combine(_directory, "source.png");
        using (var bitmap = new SkiaSharp.SKBitmap(800, 400))
        {
            using (var canvas = new SkiaSharp.SKCanvas(bitmap)) { canvas.Clear(SkiaSharp.SKColors.Transparent); canvas.DrawCircle(400, 200, 150, new SkiaSharp.SKPaint { Color = SkiaSharp.SKColors.Red }); }
            File.WriteAllBytes(png, bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100).ToArray());
        }

        var jpeg = RenderOutput.Encode(png, new OutputOptions("jpeg", 80, MaxSide: 400));
        var webp = RenderOutput.Encode(png, new OutputOptions("webp", 80));
        var small = RenderOutput.Encode(png, new OutputOptions("png", MaxSide: 200));

        using var decodedJpeg = SkiaSharp.SKBitmap.Decode(jpeg);
        Assert.Equal((400, 200), (decodedJpeg.Width, decodedJpeg.Height));
        Assert.Equal(SkiaSharp.SKColors.White, decodedJpeg.GetPixel(2, 2)); // transparent corner on white
        using var decodedWebp = SkiaSharp.SKBitmap.Decode(webp);
        Assert.Equal(800, decodedWebp.Width);
        using var decodedPng = SkiaSharp.SKBitmap.Decode(small);
        Assert.Equal(0, decodedPng.GetPixel(2, 2).Alpha); // PNG keeps the transparency
        using var sheet = RenderOutput.ContactSheet([(png, "Rouge mat"), (png, "Noir mat"), (png, "Blanc mat")], 200);
        Assert.Equal(2 * 224 + 24, sheet.Width);
    }

    [Fact]
    public void Palettes_are_bundled_with_their_finish()
    {
        Assert.Equal(["Mat", "Brillant", "Soie", "Phosphorescent"], RenderPalettes.All.Select(g => g.Name));
        Assert.Equal(30, RenderPalettes.All.Sum(g => g.Colors.Count));
        Assert.All(RenderPalettes.All, g => Assert.All(g.Colors, c => Assert.Equal(g.Finish, c.Finish)));
        Assert.Contains(RenderPalettes.All.SelectMany(g => g.Colors), c => c.Label == "Rouge mat" && c.Hex == "#DE4343");
    }

    [Fact]
    public void A_plate_drawn_standing_is_laid_on_its_largest_face_and_a_flat_one_is_left_alone()
    {
        static MeshModel Box(float x, float y, float z)
        {
            Vector3[] corners = [new(0, 0, 0), new(x, 0, 0), new(x, y, 0), new(0, y, 0), new(0, 0, z), new(x, 0, z), new(x, y, z), new(0, y, z)];
            return new MeshModel(corners, [0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 2, 3, 7, 2, 7, 6, 0, 4, 7, 0, 7, 3, 1, 2, 6, 1, 6, 5]);
        }
        var standing = Box(40, 2, 60);

        var laid = ProductRenderer.Turned(standing, RenderOrientation.AutoStraighten(standing));

        Assert.Equal(2, laid.Size.Z, 2);
        Assert.True(RenderOrientation.AutoStraighten(Box(40, 60, 2)).IsIdentity);
    }

    [Fact]
    public void A_round_pot_drawn_lying_stands_up_on_its_base()
    {
        // 64-sided prism of radius 45 and length 60 along Y (a file drawn Y up): its flat sides are thin strips, its ends full discs.
        var positions = new List<Vector3>();
        var indices = new List<int>();
        for (var i = 0; i < 64; i++)
        {
            var a = i * MathF.Tau / 64;
            positions.Add(new(45 * MathF.Cos(a), 0, 45 * MathF.Sin(a)));
            positions.Add(new(45 * MathF.Cos(a), 60, 45 * MathF.Sin(a)));
        }
        positions.Add(new(0, 0, 0));
        positions.Add(new(0, 60, 0));
        for (var i = 0; i < 64; i++)
        {
            int a0 = 2 * i, a1 = 2 * i + 1, b0 = 2 * ((i + 1) % 64), b1 = b0 + 1;
            indices.AddRange([a0, b0, a1, a1, b0, b1, 128, a0, b0, 129, b1, a1]);
        }
        var lying = new MeshModel([.. positions], [.. indices]);

        var standing = ProductRenderer.Turned(lying, RenderOrientation.AutoStraighten(lying));

        Assert.Equal(60, standing.Size.Z, 1);
    }

    /// <summary>A body with a foot under it, as an extruded profile (sides only): 8 edges of the profile, 2 triangles each.</summary>
    private static MeshModel BodyWithFoot()
    {
        (float X, float Z)[] profile = [(0, 4), (5, 4), (5, 0), (10, 0), (10, 4), (40, 4), (40, 20), (0, 20)];
        var positions = new List<Vector3>();
        var indices = new List<int>();
        for (var i = 0; i < profile.Length; i++)
        {
            var (a, b) = (profile[i], profile[(i + 1) % profile.Length]);
            var start = positions.Count;
            positions.AddRange([new(a.X, 0, a.Z), new(b.X, 0, b.Z), new(a.X, 10, a.Z), new(b.X, 10, b.Z)]);
            indices.AddRange([start, start + 3, start + 1, start, start + 2, start + 3]); // outward normals
        }
        return new MeshModel([.. positions], [.. indices]);
    }

    [Fact]
    public void A_click_paints_the_whole_foot_alt_click_only_its_face_and_the_body_stops_at_the_foot()
    {
        var mesh = BodyWithFoot();
        Assert.True(mesh.Normals[4].Z < -0.9f, "le dessous du pied doit regarder vers le bas");
        var selector = new PaintSelector(mesh);

        Assert.Equal([2, 3, 4, 5, 6, 7], selector.Select(4, wholeElement: true, 30).Order());
        Assert.Equal([4, 5], selector.Select(4, wholeElement: false, 30).Order());
        Assert.Equal([0, 1, 8, 9, 10, 11, 12, 13, 14, 15], selector.Select(12, wholeElement: true, 30).Order());
    }

    [Fact]
    public void Painted_zones_are_written_as_their_own_files_and_the_body_keeps_the_rest()
    {
        var mesh = BodyWithFoot();
        var (body, zones) = PaintParts.Write(mesh, [new PaintZone("Noir mat", "#000000", "matte", [2, 3, 4, 5, 6, 7]), new PaintZone("Vide", "#FFFFFF", "matte", [])], _directory, "part");

        Assert.Equal(10, MeshLoader.Load(body).TriangleCount);
        var zone = Assert.Single(zones);
        Assert.Equal(6, MeshLoader.Load(zone.File).TriangleCount);
    }

    [Fact]
    public void Camera_orbit_turns_the_part_and_keeps_height_zoom_and_pan()
    {
        var mesh = new MeshModel([new(0, 0, 0), new(20, 0, 0), new(0, 20, 0), new(0, 0, 20)], [0, 1, 2, 0, 1, 3]);
        var view = new ViewCamera { Yaw = Math.PI / 2, Pitch = 0, Zoom = 2, PanX = 76, PanY = 0 };

        var (camera, rotation) = RenderCamera.From(mesh, view, 800, 400);

        Assert.Equal(90, rotation, 6);
        var distance = mesh.Radius * 2.9 / 2;
        Assert.Equal(mesh.Center.Y - distance, camera.Location[1], 3);
        Assert.Equal(mesh.Center.Z, camera.Location[2], 3);
        Assert.Equal(-0.1 * 0.5, camera.ShiftX, 6);
    }

    /// <summary>What the 3D view frames is what Blender renders: the part covers the same pixels in both (camera turned, raised, zoomed and panned).</summary>
    [Fact]
    public async Task Blender_frames_the_part_like_the_3d_view_when_installed()
    {
        if (BlenderLocator.Detect() is not { } blender) return;
        Vector3[] corners = [new(0, 0, 0), new(40, 0, 0), new(40, 10, 0), new(0, 10, 0), new(0, 0, 25), new(40, 0, 25), new(40, 10, 25), new(0, 10, 25),
            new(0, 10, 0), new(10, 10, 0), new(10, 30, 0), new(0, 30, 0), new(0, 10, 25), new(10, 10, 25), new(10, 30, 25), new(0, 30, 25)];
        int[] box = [0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 2, 3, 7, 2, 7, 6, 0, 4, 7, 0, 7, 3, 1, 2, 6, 1, 6, 5];
        var mesh = new MeshModel(corners, [.. box, .. box.Select(i => i + 8)]); // an L, so a wrong turn shows
        var stl = Path.Combine(_directory, "l.stl");
        StlWriter.Write(stl, mesh);
        var view = new ViewCamera { Yaw = -1.0, Pitch = 0.55, Zoom = 1.3, PanX = 60, PanY = -30 };
        const int width = 320, height = 240;
        var (camera, rotation) = RenderCamera.From(mesh, view, width, height);
        var png = Path.Combine(_directory, "l.png");

        using (var session = new BlenderSession(blender))
            await session.RenderAsync(stl, Small with { Scene = "transparent", LayerHeight = 0, Width = width, Height = height, Rotation = rotation, Camera = camera }, png, CancellationToken.None);

        const uint background = 0xFF000000;
        var live = SoftwareRenderer.Render(mesh, view, new RenderOptions(width, height, false, 0xFFFFFFFF, background));
        using var rendered = SkiaSharp.SKBitmap.Decode(png);
        int both = 0, either = 0;
        for (var i = 0; i < width * height; i++)
        {
            var inLive = live[i] != background;
            var inBlender = rendered.GetPixel(i % width, i / width).Alpha > 240; // the part is opaque, its shadow only partly
            if (inLive && inBlender) both++;
            if (inLive || inBlender) either++;
        }
        Assert.True(either > width * height / 20, "la pièce doit être visible");
        Assert.True(both > either * 0.9, $"cadrages différents : {both} pixels communs sur {either}");
    }

    /// <summary>Blender kept open renders several images, the later ones without starting again.</summary>
    [Fact]
    public async Task Session_renders_one_job_after_another_when_installed()
    {
        if (BlenderLocator.Detect() is not { } blender) return;
        var stl = Path.Combine(_directory, "cube.stl");
        StlWriter.Write(stl, new MeshModel([new(0, 0, 0), new(20, 0, 0), new(0, 20, 0), new(0, 0, 20)], [0, 2, 1, 0, 1, 3, 0, 3, 2, 1, 2, 3]));
        using var session = new BlenderSession(blender);
        foreach (var name in new[] { "one.png", "two.png" })
        {
            var png = Path.Combine(_directory, name);
            await session.RenderAsync(stl, Small, png, CancellationToken.None);
            Assert.True(new FileInfo(png).Length > 0);
        }
        // Cycles reports its samples (shown as a percentage) and can also write a 16-bit TIFF of the same render.
        var progress = new Recorder();
        var tiff = Path.Combine(_directory, "three.tif");
        await session.RenderAsync(stl, Small with { Samples = 16, Tiff = tiff }, Path.Combine(_directory, "three.png"), CancellationToken.None, progress);
        Assert.True(progress.Highest > 0.5, "avancement reçu : " + progress.Highest);
        Assert.True(new FileInfo(tiff).Length > 0);
        Assert.False(string.IsNullOrWhiteSpace(session.DeviceName) && session.DeviceName is null);
    }

    /// <summary>A real render when Blender is installed on this computer (a few seconds); nothing to check otherwise.</summary>
    [Fact]
    public async Task Blender_renders_a_cube_when_installed()
    {
        if (BlenderLocator.Detect() is not { } blender) return;
        Vector3[] corners = [new(0, 0, 0), new(20, 0, 0), new(20, 20, 0), new(0, 20, 0), new(0, 0, 20), new(20, 0, 20), new(20, 20, 20), new(0, 20, 20)];
        int[] faces = [0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 2, 3, 7, 2, 7, 6, 0, 4, 7, 0, 7, 3, 1, 2, 6, 1, 6, 5];
        var stl = Path.Combine(_directory, "cube.stl");
        StlWriter.Write(stl, new MeshModel(corners, faces));
        var png = Path.Combine(_directory, "cube.png");

        await ProductRenderer.RenderAsync(blender, stl, Small with { Scene = "transparent" }, png, CancellationToken.None);

        Assert.True(new FileInfo(png).Length > 0);
    }
}
