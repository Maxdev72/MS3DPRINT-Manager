using System.Threading;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Reflection;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf.SharpDX;
using Xunit.Abstractions;
using MS3DPRINT.Manager.App.Views;

namespace MS3DPRINT.Manager.Core.Tests.Preview;

public sealed class PreviewWindowSmokeTests
{
    private readonly ITestOutputHelper _output;

    public PreviewWindowSmokeTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void GpuViewer_DenseModelRotationBenchmark()
    {
        const int triangleCount = 100_000;
        var file = Path.Combine(Path.GetTempPath(), $"ms3dprint-render-{Guid.NewGuid():N}.stl");
        using (var stream = File.Create(file))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(new byte[80]);
            writer.Write((uint)triangleCount);
            var triangle = new float[] { 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0 };
            for (var index = 0; index < triangleCount; index++)
            {
                foreach (var value in triangle) writer.Write(value);
                writer.Write((ushort)0);
            }
        }
        try
        {
            Exception? failure = null;
            var loaded = false;
            var frameRate = 0.0;
            var thread = new Thread(() =>
            {
                try
                {
                    var window = new GpuModelPreviewWindow(file);
                    var viewport = (Viewport3DX)window.FindName("Viewport3D");
                    viewport.ShowFrameRate = true;
                    var dimensions = (TextBlock)window.FindName("DimensionsText");
                    var started = DateTime.UtcNow;
                    var angle = 0.0;
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
                    timer.Tick += (_, _) =>
                    {
                        loaded |= dimensions.Text.StartsWith("Dimensions :", StringComparison.Ordinal);
                        if (loaded)
                        {
                            angle += 0.05;
                            var camera = Assert.IsType<HelixToolkit.Wpf.SharpDX.PerspectiveCamera>(viewport.Camera);
                            camera.Position = new Point3D(2 + Math.Cos(angle), 2 + Math.Sin(angle), 3);
                            camera.LookDirection = new Vector3D(0.5 - camera.Position.X, 0.5 - camera.Position.Y, -camera.Position.Z);
                            frameRate = viewport.FrameRate;
                        }
                        if (DateTime.UtcNow - started > TimeSpan.FromSeconds(3))
                        {
                            timer.Stop();
                            window.Close();
                        }
                    };
                    timer.Start();
                    window.ShowDialog();
                }
                catch (Exception exception) { failure = exception; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Le benchmark GPU n’a pas terminé.");
            Assert.Null(failure);
            Assert.True(loaded, "Le STL dense n’a pas été affiché.");
            _output.WriteLine($"Visualiseur GPU : {triangleCount:N0} triangles, fréquence observée {frameRate:N1} images/s (test synthétique).");
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void EmptyGpuViewer_CanBeConstructedOnStaThread()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new GpuModelPreviewWindow();
                window.Close();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "La création de la fenêtre GPU n’a pas terminé.");
        Assert.Null(failure);
    }

    [Fact]
    public void GpuViewer_CanRenderAndCloseOnStaThread()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new GpuModelPreviewWindow();
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                timer.Tick += (_, _) => { timer.Stop(); window.Close(); };
                timer.Start();
                window.ShowDialog();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "L’ouverture du moteur GPU n’a pas terminé.");
        Assert.Null(failure);
    }

    [Fact]
    public void GpuViewer_CanDisplayABinaryStl()
    {
        var file = Path.Combine(Path.GetTempPath(), $"ms3dprint-preview-{Guid.NewGuid():N}.stl");
        using (var stream = File.Create(file))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(new byte[80]);
            writer.Write(1u);
            foreach (var value in new float[] { 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0 }) writer.Write(value);
            writer.Write((ushort)0);
        }
        try
        {
            Exception? failure = null;
            var displayed = false;
            var thread = new Thread(() =>
            {
                try
                {
                    var window = new GpuModelPreviewWindow(file);
                    var started = DateTime.UtcNow;
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                    timer.Tick += (_, _) =>
                    {
                        var dimensions = (TextBlock)window.FindName("DimensionsText");
                        displayed = dimensions.Text.StartsWith("Dimensions :", StringComparison.Ordinal);
                        if (displayed)
                        {
                            try
                            {
                                var group = (GroupModel3D)window.FindName("ModelsGroup");
                                var model = Assert.IsType<MeshGeometryModel3D>(Assert.Single(group.Children));
                                var geometry = model.Geometry;
                                ((ComboBox)window.FindName("RenderModeBox")).SelectedIndex = 1;
                                ((TextBox)window.FindName("ColorHexBox")).Text = "#FF6600";
                                var applyColor = typeof(GpuModelPreviewWindow).GetMethod("TryApplyColor", BindingFlags.Instance | BindingFlags.NonPublic);
                                Assert.NotNull(applyColor);
                                applyColor.Invoke(window, null);
                                Assert.Equal(SharpDX.Direct3D11.FillMode.Wireframe, model.FillMode);
                                Assert.Equal(System.Windows.Media.Color.FromRgb(0xFF, 0x66, 0), model.WireframeColor);
                                Assert.Same(geometry, model.Geometry);
                            }
                            catch (Exception exception) { failure = exception; }
                        }
                        if (displayed || DateTime.UtcNow - started > TimeSpan.FromSeconds(5))
                        {
                            timer.Stop();
                            window.Close();
                        }
                    };
                    timer.Start();
                    window.ShowDialog();
                }
                catch (Exception exception) { failure = exception; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "L’affichage du STL n’a pas terminé.");
            Assert.Null(failure);
            Assert.True(displayed, "Le STL n’a pas été chargé dans le visualiseur GPU.");
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void GpuViewer_FramesSubUnitModelsRelativeToTheirBounds()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new GpuModelPreviewWindow();
                var setCamera = typeof(GpuModelPreviewWindow).GetMethod("SetCamera", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(setCamera);
                setCamera.Invoke(window, [new Rect3D(0, 0, 0, 0.001, 0.001, 0)]);
                var viewport = (Viewport3DX)window.FindName("Viewport3D");
                var camera = Assert.IsType<HelixToolkit.Wpf.SharpDX.PerspectiveCamera>(viewport.Camera);
                Assert.True(camera.Position.X < 0.01, $"Caméra trop éloignée : {camera.Position.X}");
                window.Close();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        Assert.Null(failure);
    }

    [Fact]
    public void EmptyModelViewer_CanBeConstructedOnStaThread()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new ModelPreviewWindow();
                window.Close();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "La création de la fenêtre n’a pas terminé.");
        Assert.Null(failure);
    }

    [Fact]
    public void ModelViewer_WithFileRemovedAfterListing_DoesNotThrowDuringConstruction()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".stl");
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new ModelPreviewWindow(missingPath);
                window.Close();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "La création de la fenêtre n’a pas terminé.");
        Assert.Null(failure);
    }
}
