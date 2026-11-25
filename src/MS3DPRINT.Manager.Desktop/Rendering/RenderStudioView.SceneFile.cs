using System.Numerics;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using MS3DPRINT.Manager.Desktop.Viewer;

namespace MS3DPRINT.Manager.Desktop.Rendering;

/// <summary>A product of a saved scene: its file and how it was set (orientation as X, Y, Z, W; shift on the floor in mm).</summary>
public sealed record SceneFilePart(string Path, float[] Turn, string Hex, string Finish, float[] Shift, IReadOnlyList<PaintZone> Zones);

/// <summary>A scene saved to a file (.ms3dscene): its products and how it is staged, lit and framed.</summary>
public sealed record SceneFile(int Version, IReadOnlyList<SceneFilePart> Parts, int Active, double Spacing, int Scene, string Background, string? Environment,
    double EnvironmentRotation, bool Shadow, double ShadowStrength, double ShadowSoftness, double LightAngle, double LightHeight, int Format, int Layer,
    double Yaw, double Pitch, double Zoom, double PanX, double PanY);

/// <summary>Saving a scene to a file and opening it again, to come back to a staging later or share it.</summary>
public sealed partial class RenderStudioView
{
    private static readonly FilePickerFileType SceneType = new("Scène de rendu MS3DPRINT") { Patterns = ["*.ms3dscene"] };

    private Control BuildSceneFileButtons()
    {
        var save = new Button { Content = Icons.Label("IconSave", "Enregistrer la scène…"), HorizontalAlignment = HorizontalAlignment.Stretch };
        save.Click += async (_, _) => await SaveSceneAsync();
        var open = new Button { Content = Icons.Label("IconOpen", "Ouvrir une scène…"), HorizontalAlignment = HorizontalAlignment.Stretch };
        open.Click += async (_, _) => await OpenSceneAsync();
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 8 };
        row.Children.Add(save);
        Grid.SetColumn(open, 1);
        row.Children.Add(open);
        return row;
    }

    private SceneFile CurrentScene()
    {
        StoreActive();
        var c = _viewport.Camera;
        return new SceneFile(1, _parts.Select(p => new SceneFilePart(p.Path, [p.Turn.X, p.Turn.Y, p.Turn.Z, p.Turn.W], p.Hex, p.Finish, [p.Shift.X, p.Shift.Y], p.Zones)).ToList(),
            _active, _spacing.Value, _scene.SelectedIndex, _background.Text?.Trim() ?? "#2F6FD6", (_environment.SelectedItem as RenderEnvironment)?.Path,
            _environmentRotation.Value, _shadow.IsChecked == true, _shadowStrength.Value, _shadowSoftness.Value, _lightAngle.Value, _lightHeight.Value,
            _format.SelectedIndex, _layer.SelectedIndex, c.Yaw, c.Pitch, c.Zoom, c.PanX, c.PanY);
    }

    private async Task SaveSceneAsync()
    {
        if (_parts.Count == 0) { ShowStatus("Ouvrez d’abord un modèle : la scène est vide."); return; }
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var target = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Enregistrer la scène",
            SuggestedFileName = Path.GetFileNameWithoutExtension(_parts[0].Path) + (_parts.Count > 1 ? " et autres" : "") + ".ms3dscene",
            DefaultExtension = "ms3dscene",
            FileTypeChoices = [SceneType]
        });
        if (target?.TryGetLocalPath() is not { } path) return;
        try
        {
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(CurrentScene(), new JsonSerializerOptions { WriteIndented = true }));
            ShowStatus("Scène enregistrée : " + Path.GetFileName(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { ShowStatus("Scène non enregistrée : " + exception.Message); }
    }

    private async Task OpenSceneAsync()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Ouvrir une scène", AllowMultiple = false, FileTypeFilter = [SceneType] });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { Length: > 0 } path) await OpenSceneAsync(path);
    }

    /// <summary>Opens a saved scene; products whose file is gone are left out and named.</summary>
    public async Task OpenSceneAsync(string path)
    {
        SceneFile? scene;
        try { scene = JsonSerializer.Deserialize<SceneFile>(await File.ReadAllTextAsync(path)); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            ShowStatus("Scène illisible : " + exception.Message);
            return;
        }
        if (scene?.Parts is not { Count: > 0 } saved) { ShowStatus("Cette scène ne contient aucune pièce."); return; }
        ShowStatus("Ouverture de la scène…", busy: true);
        var loaded = await Task.Run(() => saved.Select(p =>
        {
            MeshModel? mesh;
            try { mesh = File.Exists(p.Path) ? MeshLoader.Load(p.Path) : null; }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException or ArgumentException) { mesh = null; }
            return (Part: p, Mesh: mesh);
        }).ToList());
        var missing = loaded.Where(l => l.Mesh is null).Select(l => Path.GetFileName(l.Part.Path)).ToList();
        if (missing.Count == saved.Count) { ShowStatus("Aucune pièce de la scène n’a été trouvée : " + string.Join(", ", missing)); return; }
        foreach (var part in _parts) DeleteFiles(part);
        _parts.Clear();
        foreach (var (part, mesh) in loaded.Where(l => l.Mesh is not null))
            _parts.Add(new ScenePart
            {
                Path = part.Path, Loaded = mesh!, Hex = part.Hex, Finish = part.Finish, Zones = [.. part.Zones ?? []],
                Turn = part.Turn is { Length: 4 } t ? new Quaternion(t[0], t[1], t[2], t[3]) : Quaternion.Identity,
                Shift = part.Shift is { Length: 2 } s ? new Vector2(s[0], s[1]) : Vector2.Zero
            });
        _active = Math.Clamp(scene.Active, 0, _parts.Count - 1);
        _spacing.Value = scene.Spacing;
        _scene.SelectedIndex = Math.Clamp(scene.Scene, 0, Scenes.Length - 1);
        _background.Text = scene.Background;
        if (scene.Environment is { } environment && File.Exists(environment)) SelectEnvironment(environment);
        _environmentRotation.Value = scene.EnvironmentRotation;
        _shadow.IsChecked = scene.Shadow;
        _shadowStrength.Value = scene.ShadowStrength;
        _shadowSoftness.Value = scene.ShadowSoftness;
        _lightAngle.Value = scene.LightAngle;
        _lightHeight.Value = scene.LightHeight;
        _format.SelectedIndex = Math.Clamp(scene.Format, 0, Formats.Length - 1);
        _layer.SelectedIndex = Math.Clamp(scene.Layer, 0, Layers.Length - 1);
        LoadActive();
        _image.Source = null;
        var c = _viewport.Camera;
        (c.Yaw, c.Pitch, c.Zoom, c.PanX, c.PanY) = (scene.Yaw, scene.Pitch, scene.Zoom, scene.PanX, scene.PanY);
        _orientationNote.Text = missing.Count == 0 ? "Scène ouverte." : "Scène ouverte sans : " + string.Join(", ", missing) + " (fichier introuvable).";
        await UseOrientationAsync();
    }
}
