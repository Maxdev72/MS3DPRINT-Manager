using System.Numerics;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MS3DPRINT.Manager.Desktop;
using MS3DPRINT.Manager.Desktop.Rendering;
using MS3DPRINT.Manager.Desktop.Viewer;

namespace MS3DPRINT.Manager.Core.Tests.Estimates;

public sealed partial class EstimateUiTests
{
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
            var shownBefore = Get<string>("_shown");
            typeof(RenderStudioView).GetMethod("SettingsChanged", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(studio, null);
            await Task.Delay(300);
            Assert.Equal(shownBefore, Get<string>("_shown"));
            Assert.StartsWith("Aperçu en pause", Get<TextBlock>("_status").Text);
            setPaused.Invoke(studio, [false]);
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
            Assert.True(((StackPanel)typeof(RenderResultWindow).GetField("_savePanel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(result)!).IsVisible);
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
