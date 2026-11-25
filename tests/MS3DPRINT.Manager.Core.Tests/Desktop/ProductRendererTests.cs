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

        store.Save(RenderPreferences.Default with { CustomColors = [new("Bleu nuit soie", "#1A2B4C", "silk")], Scene = 2, Rotation = 90 });
        var loaded = store.Load();
        Assert.Equal(new RenderColor("Bleu nuit soie", "#1A2B4C", "silk"), Assert.Single(loaded.CustomColors));
        Assert.Equal((2, 90d), (loaded.Scene, loaded.Rotation));

        File.WriteAllText(path, "{ pas du json");
        Assert.Equal(RenderPreferences.Default, store.Load());
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
