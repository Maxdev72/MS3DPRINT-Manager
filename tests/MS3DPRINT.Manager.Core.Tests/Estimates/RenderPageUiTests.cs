using System.Numerics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MS3DPRINT.Manager.Desktop;
using MS3DPRINT.Manager.Desktop.Rendering;
using MS3DPRINT.Manager.Desktop.Viewer;

namespace MS3DPRINT.Manager.Core.Tests.Estimates;

public sealed partial class EstimateUiTests
{
    [Theory]
    [InlineData(-3f)]
    [InlineData(20f)]
    public Task Transform_gpu_clipping_follows_geometry_without_recentering_the_camera(float distance) => Run(() =>
    {
        var original = new MeshModel([new(-1, -1, -1), new(1, -1, -1), new(0, 1, 1)], [0, 1, 2]);
        var geometry = new MeshModel(original.Positions.Select(p => p + Vector3.UnitY * distance).ToArray(), original.Indices);
        var framed = new MeshModel(geometry, original);
        var viewport = new GpuMeshViewport();
        viewport.Camera.Yaw = viewport.Camera.Pitch = 0;
        var matrices = ((float[] View, float[] Projection))typeof(GpuMeshViewport).GetMethod("BuildMatrices",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(viewport, [framed, 800, 600])!;
        static Matrix4x4 Matrix(float[] a) => new(a[0], a[1], a[2], a[3], a[4], a[5], a[6], a[7], a[8], a[9], a[10], a[11], a[12], a[13], a[14], a[15]);
        foreach (var point in geometry.Positions)
        {
            var clip = Vector4.Transform(new Vector4(point, 1), Matrix(matrices.View) * Matrix(matrices.Projection));
            Assert.True(clip.W > 0);
            Assert.InRange(clip.Z / clip.W, -1, 1);
        }
        Assert.Equal(original.Center, framed.Center);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Transform_gizmo_snaps_with_shift_and_can_cancel_a_drag() => Run(() =>
    {
        var gizmo = new OrientationGizmo
        {
            Mesh = new MeshModel([new(0, 0, 0), new(30, 0, 0), new(0, 30, 0), new(0, 0, 30)], [0, 2, 1, 0, 1, 3, 0, 3, 2, 1, 2, 3]),
            Camera = new ViewCamera { Yaw = -0.7, Pitch = 0.55 }, MoveStep = 5
        };
        var window = new Window { Width = 800, Height = 600, Content = gizmo };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var points = (Point[])typeof(OrientationGizmo).GetMethod("ProjectAxis", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(gizmo, [0])!;
            var from = points[0] + (points[1] - points[0]) * 0.8;
            var to = from + (points[1] - points[0]) * 0.37;
            var movement = Vector3.Zero;
            gizmo.Moved += delta => movement += delta;
            window.MouseDown(from, MouseButton.Left);
            window.MouseMove(to, RawInputModifiers.LeftMouseButton | RawInputModifiers.Shift);
            Assert.True(movement.X > 0);
            Assert.InRange(Math.Abs(movement.X / 5 - MathF.Round(movement.X / 5)), 0, 0.001f);
            gizmo.Cancel();
            Assert.False(gizmo.IsDragging);
            Assert.InRange(movement.Length(), 0, 0.001f);
            window.MouseUp(to, MouseButton.Left);
            Assert.InRange(movement.Length(), 0, 0.001f);
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public Task Transform_gizmo_moves_along_an_axis_or_plane_during_the_drag(int handle) => Run(() =>
    {
        var mesh = new MeshModel([new(0, 0, 0), new(30, 0, 0), new(0, 30, 0), new(0, 0, 30)], [0, 2, 1, 0, 1, 3, 0, 3, 2, 1, 2, 3]);
        var gizmo = new OrientationGizmo { Mesh = mesh, Camera = new ViewCamera { Yaw = -0.7, Pitch = 0.55 } };
        var window = new Window { Width = 800, Height = 600, Content = gizmo };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var projection = typeof(OrientationGizmo).GetMethod(handle < 3 ? "ProjectAxis" : "ProjectPlane", flags);
            Assert.NotNull(projection);
            var points = (Point[])projection.Invoke(gizmo, [handle % 3])!;
            var from = handle < 3 ? points[0] + (points[1] - points[0]) * 0.8 : new Point(points.Average(p => p.X), points.Average(p => p.Y));
            var to = handle < 3 ? from + (points[1] - points[0]) * 0.25 : from + new Avalonia.Vector(24, -18);
            var movement = Vector3.Zero;
            var moved = typeof(OrientationGizmo).GetEvent("Moved");
            Assert.NotNull(moved);
            moved.AddEventHandler(gizmo, (Action<Vector3>)(delta => movement += delta));
            window.MouseDown(from, MouseButton.Left);
            window.MouseMove(to, RawInputModifiers.LeftMouseButton);
            Assert.True(movement.Length() > 0.01f);
            if (handle < 3)
            {
                Assert.True(Vector3.Dot(movement, handle == 0 ? Vector3.UnitX : handle == 1 ? Vector3.UnitY : Vector3.UnitZ) > 0);
                Assert.InRange(handle == 0 ? Math.Abs(movement.Y) + Math.Abs(movement.Z) : handle == 1 ? Math.Abs(movement.X) + Math.Abs(movement.Z) : Math.Abs(movement.X) + Math.Abs(movement.Y), 0, 0.001f);
            }
            else Assert.InRange(Math.Abs(handle == 3 ? movement.Z : handle == 4 ? movement.X : movement.Y), 0, 0.001f);
            var beforeRelease = movement;
            window.MouseUp(to, MouseButton.Left);
            Assert.Equal(beforeRelease, movement);
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task Transforming_one_part_keeps_its_pivot_neighbours_and_camera_stable() => Run(async () =>
    {
        var store = new RenderPreferencesStore(Path.Combine(_root, "transform-preferences.json"));
        store.Save(RenderPreferences.Default with { Paused = true });
        var studio = new RenderStudioView(() => [], store);
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        object? Call(string method, params object[] args) => typeof(RenderStudioView).GetMethod(method, flags)!.Invoke(studio, args);
        T Get<T>(string name) => (T)typeof(RenderStudioView).GetField(name, flags)!.GetValue(studio)!;
        var model = Path.Combine(_root, "transform.stl");
        StlWriter.Write(model, new MeshModel([new(0, 0, 0), new(40, 0, 0), new(0, 20, 0), new(0, 0, 15)], [0, 2, 1, 0, 1, 3, 0, 3, 2, 1, 2, 3]));
        await studio.LoadModelAsync(model);
        await studio.AddPartAsync(model);
        var parts = Get<System.Collections.IList>("_parts");
        MeshModel Placed(int index) => (MeshModel)parts[index]!.GetType().GetField("Placed")!.GetValue(parts[index])!;
        var neighbour = Placed(0).Center;
        var pivot = (Vector3)parts[1]!.GetType().GetField("Anchor")!.GetValue(parts[1])!;
        var beforeVertex = Placed(1).Positions[0];
        var frame = Get<MeshModel>("_mesh");
        await (Task)Call("TurnAsync", Vector3.UnitY, 45d)!;
        var expectedVertex = Vector3.Transform(beforeVertex - pivot, Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4)) + pivot;
        Assert.InRange(Vector3.Distance(expectedVertex, Placed(1).Positions[0]), 0, 0.001f);
        Assert.Equal(neighbour, Placed(0).Center);
        var move = typeof(RenderStudioView).GetMethod("MoveSelectedAsync", flags);
        Assert.NotNull(move);
        var delta = new Vector3(12, -8, 7);
        var centreBeforeMove = Placed(1).Center;
        await (Task)move.Invoke(studio, [delta])!;
        Assert.InRange(Vector3.Distance(centreBeforeMove + delta, Placed(1).Center), 0, 0.001f);
        Assert.Equal(neighbour, Placed(0).Center);
        Assert.Equal(frame.Center, Get<MeshModel>("_mesh").Center);
        Assert.Equal(frame.Radius, Get<MeshModel>("_mesh").Radius);
        var file = Path.Combine(_root, "transform.ms3dscene");
        File.WriteAllText(file, System.Text.Json.JsonSerializer.Serialize((SceneFile)Call("CurrentScene")!));
        await studio.OpenSceneAsync(file);
        Assert.InRange(Vector3.Distance(centreBeforeMove + delta, Placed(1).Center), 0, 0.001f);
        Assert.Equal(frame.Center, Get<MeshModel>("_mesh").Center);
        var legacy = (SceneFile)Call("CurrentScene")!;
        legacy = legacy with { Frame = null, Parts = legacy.Parts.Select(p => p with { Anchor = null, Shift = [3f, 4f] }).ToList(), Spacing = 25 };
        File.WriteAllText(file, System.Text.Json.JsonSerializer.Serialize(legacy));
        await studio.OpenSceneAsync(file);
        Assert.Equal([3f, 4f], ((SceneFile)Call("CurrentScene")!).Parts[1].Shift);
    });

    [Fact]
    public Task Render_part_tab_keeps_transforms_compact_and_secondary_settings_collapsed() => Run(async () =>
    {
        var store = new RenderPreferencesStore(Path.Combine(_root, "compact-part-preferences.json"));
        store.Save(RenderPreferences.Default with { Paused = false }); // the part can only be moved while the preview runs
        var studio = new RenderStudioView(() => [], store);
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        T Get<T>(string name) => (T)typeof(RenderStudioView).GetField(name, flags)!.GetValue(studio)!;
        typeof(RenderStudioView).GetMethod("SwapViewport", flags)!.Invoke(studio, [new MeshViewport()]);
        var first = Path.Combine(_root, "Cache lave phare.stl");
        var second = Path.Combine(_root, "Croix 200mm - MPO France - nom de pièce long.stl");
        Vector3[] vertices = [new(0, 0, 0), new(20, 0, 0), new(20, 20, 0), new(0, 20, 0), new(0, 0, 30), new(20, 0, 30), new(20, 20, 30), new(0, 20, 30)];
        int[] triangles = [0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 2, 3, 7, 2, 7, 6, 0, 4, 7, 0, 7, 3, 1, 2, 6, 1, 6, 5];
        StlWriter.Write(first, new MeshModel(vertices, triangles));
        StlWriter.Write(second, new MeshModel(vertices.Select(p => p * 0.7f).ToArray(), triangles));
        await studio.LoadModelAsync(first);
        Assert.True(Get<StackPanel>("_partList").IsEffectivelyVisible);
        await studio.AddPartAsync(second);
        var window = new Window { Width = 1200, Height = 850, Content = studio };
        window.Show();
        try
        {
            var descendants = Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(studio).ToList();
            var extras = Assert.Single(descendants.OfType<Expander>(), e => e.Header as string == "Autres orientations");
            var layout = Assert.Single(descendants.OfType<Expander>(), e => e.Header as string == "Disposition de la scène");
            Assert.False(extras.IsExpanded);
            Assert.False(layout.IsExpanded);
            var rows = Get<StackPanel>("_partList").Children.OfType<Grid>().ToList();
            Assert.Equal(2, rows.Count);
            Assert.All(rows, row => Assert.Equal(2, row.Children.Count));
            var selectFirst = Assert.IsType<Button>(rows[0].Children[0]);
            selectFirst.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(0, Get<int>("_active"));
            Assert.Equal(2, Get<StackPanel>("_partList").Children.Count);
            var actions = Assert.IsType<MenuFlyout>(Assert.IsType<Button>(rows[0].Children[1]).Flyout);
            Assert.False(actions.Items.OfType<MenuItem>().Single(item => item.Header as string == "Placer avant").IsEnabled);
            Assert.True(actions.Items.OfType<MenuItem>().Single(item => item.Header as string == "Placer après").IsEnabled);
            await (Task)typeof(RenderStudioView).GetMethod("MovePartAsync", flags)!.Invoke(studio, [0, 1])!;
            Assert.Equal(1, Get<int>("_active"));
            Assert.Equal(first, Get<string>("_modelPath"));
            var sceneFiles = Assert.Single(descendants.OfType<Button>(), b => AutomationProperties.GetName(b) == "Fichiers de scène");
            Assert.IsType<MenuFlyout>(sceneFiles.Flyout);
            Get<ComboBox>("_shiftStep").SelectedIndex = 3;
            foreach (var (width, height) in new[] { (1200d, 850d), (960d, 720d) })
            {
                window.Width = width; window.Height = height;
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                Assert.True(sceneFiles.IsEffectivelyVisible);
                var rotation = Get<ToggleButton>("_transformMode");
                rotation.IsChecked = true;
                var position = rotation.TranslatePoint(new Point(), window)!.Value;
                Assert.InRange(position.Y, 0, window.Height - 160);
                CaptureDraftUi(window, "render-part-compact-" + width);
            }
            Get<ToggleButton>("_transformMode").Focus();
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.False(Get<ToggleButton>("_transformMode").IsChecked);
            Assert.False(Get<OrientationGizmo>("_gizmo").IsVisible);
            var location = sceneFiles.TranslatePoint(new Point(20, 20), window)!.Value;
            window.MouseDown(location, MouseButton.Left); window.MouseUp(location, MouseButton.Left);
            Assert.True(sceneFiles.Flyout!.IsOpen);
            sceneFiles.Flyout.Hide();
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Render_light_defaults_can_be_restored_and_add_part_stays_visible() => Run(() =>
    {
        var store = new RenderPreferencesStore(Path.Combine(_root, "light-preferences.json"));
        store.Save(RenderPreferences.Default with { Shadow = false, ShadowStrength = 0.8, ShadowSoftness = 0.9, LightAngle = 180, LightHeight = 70, Hex = "#0055AA" });
        var studio = new RenderStudioView(() => [], store);
        var window = new Window { Width = 1200, Height = 850, Content = studio };
        window.Show();
        try
        {
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            T Get<T>(string name) => (T)typeof(RenderStudioView).GetField(name, flags)!.GetValue(studio)!;
            var buttons = Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(studio).OfType<Button>().ToList();
            var resetStrength = Assert.Single(buttons, b => AutomationProperties.GetName(b) == "Rétablir : Intensité de l’ombre");
            resetStrength.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(RenderPreferences.Default.ShadowStrength, Get<Slider>("_shadowStrength").Value);
            Assert.Equal(0.9, Get<Slider>("_shadowSoftness").Value);
            Assert.False(Get<CheckBox>("_shadow").IsChecked);
            var resetAll = Assert.Single(buttons, b => b.Content as string == "Réinitialiser les ombres et la lumière");
            resetAll.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.True(Get<CheckBox>("_shadow").IsChecked);
            Assert.Equal(RenderPreferences.Default.ShadowSoftness, Get<Slider>("_shadowSoftness").Value);
            Assert.Equal(RenderPreferences.Default.LightAngle, Get<Slider>("_lightAngle").Value);
            Assert.Equal(RenderPreferences.Default.LightHeight, Get<Slider>("_lightHeight").Value);
            typeof(RenderStudioView).GetMethod("SavePreferences", flags)!.Invoke(studio, null);
            Assert.Equal("#0055AA", store.Load().Hex);

            var add = Assert.Single(buttons, b => AutomationProperties.GetName(b) == "Ajouter une pièce à la scène");
            for (var tab = 0; tab < 5; tab++)
            {
                typeof(RenderStudioView).GetMethod("ShowTab", flags)!.Invoke(studio, [tab]);
                Dispatcher.UIThread.RunJobs();
                Assert.True(add.IsEffectivelyVisible);
            }
            typeof(RenderStudioView).GetMethod("ShowTab", flags)!.Invoke(studio, [2]);
            Dispatcher.UIThread.RunJobs();
            CaptureDraftUi(window, "render-light-defaults");
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task Rotation_ring_moves_the_part_before_the_mouse_is_released() => Run(() =>
    {
        var gizmo = new OrientationGizmo
        {
            Mesh = new MeshModel([new(0, 0, 0), new(20, 0, 0), new(0, 20, 0), new(0, 0, 20)],
                [0, 2, 1, 0, 1, 3, 0, 3, 2, 1, 2, 3]),
            Camera = new ViewCamera(), Step = 15
        };
        var window = new Window { Width = 800, Height = 600, Content = gizmo };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var points = (List<Point>)typeof(OrientationGizmo).GetMethod("Project",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(gizmo, [0])!;
            var turns = new List<double>();
            var completed = 0;
            gizmo.Turned += (_, degrees) => turns.Add(degrees);
            gizmo.TurnCompleted += () => completed++;
            window.MouseDown(points[9], MouseButton.Left);
            window.MouseMove(points[18], RawInputModifiers.LeftMouseButton);
            Assert.NotEmpty(turns);
            var afterFirstMove = turns.Sum();
            Assert.InRange(Math.Abs(afterFirstMove - 45), 0, 0.001);
            window.MouseMove(points[18], RawInputModifiers.LeftMouseButton);
            Assert.Equal(afterFirstMove, turns.Sum());
            window.MouseMove(points[27], RawInputModifiers.LeftMouseButton);
            var beforeRelease = turns.Sum();
            Assert.NotEqual(afterFirstMove, beforeRelease);
            Assert.Equal(0, completed);
            window.MouseUp(points[27], MouseButton.Left);
            Assert.Equal(beforeRelease, turns.Sum());
            Assert.Equal(1, completed);
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task Render_rotation_rings_are_only_shown_on_request() => Run(async () =>
    {
        var store = new RenderPreferencesStore(Path.Combine(_root, "rotation-preferences.json"));
        store.Save(RenderPreferences.Default with { Paused = false }); // the part can only be moved while the preview runs
        var studio = new RenderStudioView(() => [], store);
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        object? Call(string method, params object[] args) => typeof(RenderStudioView).GetMethod(method, flags)!.Invoke(studio, args);
        var gizmo = (OrientationGizmo)typeof(RenderStudioView).GetField("_gizmo", flags)!.GetValue(studio)!;
        var model = Path.Combine(_root, "rotation.stl");
        StlWriter.Write(model, new MeshModel([new(0, 0, 0), new(20, 0, 0), new(0, 20, 0), new(0, 0, 20)],
            [0, 2, 1, 0, 1, 3, 0, 3, 2, 1, 2, 3]));
        await studio.LoadModelAsync(model);
        Call("ShowTab", 0);
        Assert.False(gizmo.IsVisible);

        var toggle = Assert.Single(Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(studio)
            .OfType<ToggleButton>(), button => AutomationProperties.GetName(button) == "Déplacer / tourner la pièce");
        toggle.IsChecked = true;
        Assert.True(gizmo.IsVisible);
        // Dragging updates the 3D mesh without rewriting Blender files or changing the camera.
        var viewport = new MeshViewport();
        Call("SwapViewport", viewport);
        viewport.Camera.Yaw = 0.8;
        viewport.Camera.Zoom = 1.3;
        var work = (string)typeof(RenderStudioView).GetField("_work", flags)!.GetValue(studio)!;
        var files = Directory.GetFiles(work).Order().ToArray();
        var oldMesh = typeof(MeshViewport).GetField("_mesh", flags)!.GetValue(viewport);
        var beforeTurn = (Quaternion)typeof(RenderStudioView).GetField("_turn", flags)!.GetValue(studio)!;
        typeof(OrientationGizmo).GetField("_dragging", flags)!.SetValue(gizmo, 0);
        try { await (Task)Call("TurnAsync", Vector3.UnitX, 15d)!; }
        finally { typeof(OrientationGizmo).GetField("_dragging", flags)!.SetValue(gizmo, -1); }
        Assert.NotSame(oldMesh, typeof(MeshViewport).GetField("_mesh", flags)!.GetValue(viewport));
        Assert.Equal(files, Directory.GetFiles(work).Order().ToArray());
        Assert.Equal((0.8, 1.3), (viewport.Camera.Yaw, viewport.Camera.Zoom));
        var afterTurn = (Quaternion)typeof(RenderStudioView).GetField("_turn", flags)!.GetValue(studio)!;
        Assert.NotEqual(beforeTurn, afterTurn);
        Assert.Equal(0, ((Image)typeof(RenderStudioView).GetField("_image", flags)!.GetValue(studio)!).Opacity);
        await (Task)Call("UseOrientationAsync")!;
        Assert.Equal(afterTurn, (Quaternion)typeof(RenderStudioView).GetField("_turn", flags)!.GetValue(studio)!);
        Assert.True(gizmo.IsVisible);
        toggle.IsChecked = false;
        await (Task)Call("UseOrientationAsync")!;
        Assert.False(gizmo.IsVisible);

        toggle.IsChecked = true;
        Call("ShowTab", 1);
        Assert.False(gizmo.IsVisible);
        Call("ShowTab", 0);
        Assert.False(gizmo.IsVisible);
        await studio.LoadModelAsync(model);
        Assert.False(gizmo.IsVisible);
    });

    [Fact]
    public Task Render_page_offers_colours_and_previews_a_model_with_blender() => Run(async () =>
    {
        var owner = await Open(); owner.Width = 1440; owner.Height = 900; owner.Show();
        try
        {
            await (Task)Invoke(owner, "NavigateToWorkspaceAsync", "Rendu produit")!;
            var panel = owner.FindControl<Border>("RenderWorkspacePanel")!;
            Assert.True(panel.IsVisible);
            var studio = Assert.IsType<RenderStudioView>(panel.Child);
            Assert.Contains(Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(studio).OfType<Button>(), b => AutomationProperties.GetName(b) == "Couleur Rouge mat");
            if (BlenderLocator.Detect() is null) return;

            Vector3[] corners = [new(0, 0, 0), new(30, 0, 0), new(30, 30, 0), new(0, 30, 0), new(0, 0, 30), new(30, 0, 30), new(30, 30, 30), new(0, 30, 30)];
            int[] faces = [0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 2, 3, 7, 2, 7, 6, 0, 4, 7, 0, 7, 3, 1, 2, 6, 1, 6, 5];
            var model = Path.Combine(_root, "cube.stl");
            StlWriter.Write(model, new MeshModel(corners, faces));
            await studio.LoadModelAsync(model);
            Dispatcher.UIThread.RunJobs();

            T Get<T>(string name) => (T)typeof(RenderStudioView).GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(studio)!;
            Assert.True(Get<Image>("_image").Source is not null, Get<TextBlock>("_status").Text);
            Assert.StartsWith("Aperçu affiné", Get<TextBlock>("_status").Text);
            CaptureDraftUi(owner, "render-page");
            foreach (var (tab, name) in new[] { (0, "piece"), (1, "matiere"), (2, "ambiance"), (3, "camera"), (4, "rendu") })
            {
                typeof(RenderStudioView).GetMethod("ShowTab", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(studio, [tab]);
                Get<OrientationGizmo>("_gizmo").IsVisible = tab == 0;
                await Task.Delay(50);
                CaptureDraftUi(owner, "render-tab-" + name);
            }
            Get<OrientationGizmo>("_gizmo").IsVisible = false;

            // A moved camera (turn, height, zoom, pan) is rendered by Blender with the same framing as the live 3D view.
            var viewport = Get<IMeshViewport>("_viewport");
            (viewport.Camera.Yaw, viewport.Camera.Pitch, viewport.Camera.Zoom, viewport.Camera.PanX, viewport.Camera.PanY) = (-1.0, 0.6, 1.6, 90, -50);
            viewport.RequestRender(false);
            typeof(RenderStudioView).GetMethod("CameraMoved", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(studio, null);
            await (Task)typeof(RenderStudioView).GetMethod("RunPreviewsAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(studio, null)!;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, Get<Image>("_image").Opacity);
            CaptureDraftUi(owner, "render-match-blender");
            Get<Image>("_image").Opacity = 0;
            await Task.Delay(100);
            CaptureDraftUi(owner, "render-match-live");

            // The HDRI environments shipped with Blender light and fill the background.
            var environments = Get<ComboBox>("_environment");
            Assert.NotEmpty(environments.Items);
            Get<ComboBox>("_scene").SelectedIndex = 4;
            environments.SelectedIndex = 0;
            Assert.True(Get<StackPanel>("_environmentField").IsVisible);
            await studio.LoadModelAsync(model);
            Dispatcher.UIThread.RunJobs();
            Assert.StartsWith("Aperçu affiné", Get<TextBlock>("_status").Text);
            CaptureDraftUi(owner, "render-page-environment");

            // The top of the cube painted black: rendered as a part of its own.
            Get<ComboBox>("_scene").SelectedIndex = 0;
            var framing = Get<IMeshViewport>("_viewport").Camera;
            (framing.Yaw, framing.Pitch, framing.Zoom, framing.PanX, framing.PanY) = (0.8, 0.5, 1.3, 40, 10);
            typeof(RenderStudioView).GetField("_zones", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(studio, new List<PaintZone> { new("Noir brillant", "#000000", "glossy", [2, 3]) });
            await (Task)typeof(RenderStudioView).GetMethod("ZonesChangedAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(studio, null)!;
            Dispatcher.UIThread.RunJobs();
            Assert.Single(Get<System.Collections.IList>("_zoneFiles"));
            Assert.Equal((0.8, 0.5, 1.3, 40d, 10d), (framing.Yaw, framing.Pitch, framing.Zoom, framing.PanX, framing.PanY)); // painting keeps the framing
            Assert.StartsWith("Aperçu affiné", Get<TextBlock>("_status").Text);
            CaptureDraftUi(owner, "render-paint");

            // Pause: nothing is rendered until the preview resumes.
            var setPaused = typeof(RenderStudioView).GetMethod("SetPaused", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            setPaused.Invoke(studio, [true]);
            Assert.False(Get<ToggleButton>("_transformMode").IsEnabled); // paused, the part cannot be moved
            var shownBefore = Get<string>("_shown");
            typeof(RenderStudioView).GetMethod("SettingsChanged", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(studio, null);
            await Task.Delay(300);
            Assert.Equal(shownBefore, Get<string>("_shown"));
            Assert.StartsWith("Aperçu en pause", Get<TextBlock>("_status").Text);
            setPaused.Invoke(studio, [false]);
            Assert.True(Get<ToggleButton>("_transformMode").IsEnabled);
            await (Task)typeof(RenderStudioView).GetMethod("RunPreviewsAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(studio, null)!;

            // A series: one view in two colours, as JPEG, with a contact sheet.
            Get<ComboBox>("_scene").SelectedIndex = 0;
            Get<ComboBox>("_quality").SelectedIndex = 0;
            Get<ComboBox>("_outputFormat").SelectedIndex = 1;
            Get<CheckBox>("_contactSheet").IsChecked = true;
            var keys = Get<HashSet<string>>("_seriesColorKeys");
            keys.Add("#DE4343|matte");
            keys.Add("#000000|matte");
            var saved = new List<string>();
            studio.SaveFiles = (files, _, _) => { saved.AddRange(files); return Task.FromResult<string?>("test"); };
            await (Task)typeof(RenderStudioView).GetMethod("RunSeriesAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(studio, null)!;
            var zip = Assert.Single(saved);
            Assert.EndsWith(".zip", zip);
            using (var archive = System.IO.Compression.ZipFile.OpenRead(zip))
                Assert.Equal(new HashSet<string> { "Noir mat.jpg", "Rouge mat.jpg", "planche couleurs.jpg", "récapitulatif.txt" }, archive.Entries.Select(e => e.FullName).ToHashSet());

            // A second product next to the first, in its own colour: both are rendered side by side.
            var other = Path.Combine(_root, "petit cube.stl");
            StlWriter.Write(other, new MeshModel([.. corners.Select(c => c * 0.5f)], faces));
            await studio.AddPartAsync(other);
            Dispatcher.UIThread.RunJobs();
            var parts = Get<System.Collections.IList>("_parts");
            Assert.Equal(2, parts.Count);
            Assert.Equal(24, Get<MeshModel>("_mesh").TriangleCount);
            typeof(RenderStudioView).GetMethod("SetColor", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(studio, ["#0055B8"]);
            await (Task)typeof(RenderStudioView).GetMethod("RunPreviewsAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(studio, null)!;
            Assert.Contains(Get<System.Collections.IList>("_zoneFiles").Cast<object>(), z => z.ToString()!.Contains("#000000")); // the first cube keeps its painted top
            Get<IMeshViewport>("_viewport").Camera.Zoom = 0.8;
            typeof(RenderStudioView).GetMethod("CameraMoved", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(studio, null);
            await (Task)typeof(RenderStudioView).GetMethod("RunPreviewsAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(studio, null)!;
            typeof(RenderStudioView).GetMethod("ShowTab", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(studio, [0]);
            await Task.Delay(100);
            CaptureDraftUi(owner, "render-scene");

            // The small cube moved back 10 mm, then the scene saved and opened again: same products, shift and colours.
            await (Task)typeof(RenderStudioView).GetMethod("ShiftActiveAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(studio, [new System.Numerics.Vector2(0, 10)])!;
            var sceneFile = Path.Combine(_root, "scène.ms3dscene");
            var saved1 = (SceneFile)typeof(RenderStudioView).GetMethod("CurrentScene", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(studio, null)!;
            File.WriteAllText(sceneFile, System.Text.Json.JsonSerializer.Serialize(saved1));
            await studio.LoadModelAsync(model);
            Assert.Single(Get<System.Collections.IList>("_parts"));
            await studio.OpenSceneAsync(sceneFile);
            var reopened = (SceneFile)typeof(RenderStudioView).GetMethod("CurrentScene", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(studio, null)!;
            Assert.Equal(2, reopened.Parts.Count);
            Assert.Equal([0f, 10f], reopened.Parts[1].Shift);
            Assert.Equal("#0055B8", reopened.Parts[1].Hex);
            Assert.Single(reopened.Parts[0].Zones);

            // The final render opens its own window, then offers to save the image.
            Get<ComboBox>("_quality").SelectedIndex = 0;
            await (Task)typeof(RenderStudioView).GetMethod("RenderFinalAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(studio, null)!;
            Dispatcher.UIThread.RunJobs();
            var result = Assert.IsType<RenderResultWindow>(typeof(RenderStudioView).GetField("_resultWindow", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(studio));
            await Task.Delay(200);
            CaptureDraftUi(result, "render-window");
            Assert.True(((StackPanel)typeof(RenderResultWindow).GetField("_doneBar", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(result)!).IsVisible);
            result.Close();
        }
        finally { owner.Close(); }
    });

    [Fact]
    public Task Surveillance_window_does_not_open_empty_without_printers() => Run(async () =>
    {
        var owner = await Open(); owner.Show();
        try
        {
            await (Task)Invoke(owner, "NavigateToWorkspaceAsync", "Surveillance")!;
            Assert.False(owner.FindControl<Button>("MonitoringDetachButton")!.IsVisible);
            Invoke(owner, "OpenMonitoringWindow");
            await Task.Delay(200); Dispatcher.UIThread.RunJobs();
            Assert.Null(Field<Window?>(owner, "_monitoringWindow"));
            Assert.False(owner.FindControl<Border>("MonitoringDetachedNote")!.IsVisible);
        }
        finally { owner.Close(); }
    });
}
