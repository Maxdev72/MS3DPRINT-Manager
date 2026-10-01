using System.Threading;
using MS3DPRINT.Manager.App.Views;

namespace MS3DPRINT.Manager.Core.Tests.Preview;

public sealed class PreviewWindowSmokeTests
{
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
