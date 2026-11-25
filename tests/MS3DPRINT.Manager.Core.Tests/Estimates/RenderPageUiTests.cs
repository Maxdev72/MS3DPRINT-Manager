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
            Assert.Contains(studio.GetVisualDescendants().OfType<Button>(), b => AutomationProperties.GetName(b) == "Couleur Rouge");
            if (BlenderLocator.Detect() is null) return;

            Vector3[] corners = [new(0, 0, 0), new(30, 0, 0), new(30, 30, 0), new(0, 30, 0), new(0, 0, 30), new(30, 0, 30), new(30, 30, 30), new(0, 30, 30)];
            int[] faces = [0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 2, 3, 7, 2, 7, 6, 0, 4, 7, 0, 7, 3, 1, 2, 6, 1, 6, 5];
            var model = Path.Combine(_root, "cube.stl");
            StlWriter.Write(model, new MeshModel(corners, faces));
            await studio.LoadModelAsync(model);
            Dispatcher.UIThread.RunJobs();

            Assert.NotNull(studio.GetVisualDescendants().OfType<Image>().Single().Source);
            CaptureDraftUi(owner, "render-page");

            // The HDRI environments shipped with Blender light and fill the background.
            T Get<T>(string name) => (T)typeof(RenderStudioView).GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(studio)!;
            var environments = Get<ComboBox>("_environment");
            Assert.NotEmpty(environments.Items);
            Get<ComboBox>("_scene").SelectedIndex = 4;
            environments.SelectedIndex = 0;
            Assert.True(Get<StackPanel>("_environmentField").IsVisible);
            await studio.LoadModelAsync(model);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Aperçu à jour. Lancez le rendu HD pour l’image finale.", Get<TextBlock>("_status").Text);
            CaptureDraftUi(owner, "render-page-environment");
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
