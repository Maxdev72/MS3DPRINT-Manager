using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MS3DPRINT.Manager.App.Views;

namespace MS3DPRINT.Manager.Core.Tests.Preview;

[Collection("Responsive layout UI")]
public sealed class DocumentPreviewWindowTests
{
    [Fact]
    public void ImageZoomChangesScrollableSizeAndFitRestoresWidthWithoutChangingSource()
    {
        RunSta(async () =>
        {
            var path = Path.Combine(Path.GetTempPath(), "ms3d-image-zoom-" + Guid.NewGuid().ToString("N") + ".png");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(600, 400, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, new byte[600 * 400 * 4], 600 * 4)));
            using (var stream = File.Create(path)) encoder.Save(stream);
            var window = new DocumentPreviewWindow(path);
            try
            {
                await Invoke(window, "LoadAsync");
                var image = Assert.IsType<Image>(window.FindName("PreviewImage"));
                var originalWidth = image.Width;
                var originalImage = image.Source;
                Assert.IsType<Button>(window.FindName("ZoomInButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(image.Width > originalWidth);
                Assert.Equal(1.5, image.Width / image.Height, precision: 3);
                Assert.Same(originalImage, image.Source);
                Assert.IsType<Button>(window.FindName("ZoomOutButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(originalWidth, image.Width, precision: 3);
                Assert.IsType<Button>(window.FindName("FitWidthButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(originalWidth, image.Width, precision: 3);
            }
            finally { window.Close(); File.Delete(path); }
        });
    }

    [Fact]
    public void ClosingWhilePdfLoads_DoesNotApplyLateRendering()
    {
        RunSta(async () =>
        {
            var path = Path.Combine(Path.GetTempPath(), "ms3d-pdf-close-" + Guid.NewGuid().ToString("N") + ".pdf");
            File.WriteAllBytes(path, PdfPageRendererTests.CreateBlankPdf());
            try
            {
                var window = new DocumentPreviewWindow(path);
                var loading = Invoke(window, "LoadAsync");
                window.Close();
                await loading;
                Assert.Null(Assert.IsType<Image>(window.FindName("PreviewImage")).Source);
            }
            finally { File.Delete(path); }
        });
    }

    [Fact]
    public void PdfZoomAndMaximize_PreserveSecondPageAndRestoreWindowState()
    {
        RunSta(async () =>
        {
            var path = Path.Combine(Path.GetTempPath(), "ms3d-preview-zoom-" + Guid.NewGuid().ToString("N") + ".pdf");
            File.WriteAllBytes(path, PdfPageRendererTests.CreateBlankPdf(pageCount: 2));
            var bytes = File.ReadAllBytes(path);
            var window = new DocumentPreviewWindow(path);
            try
            {
                var enlarge = Assert.IsType<Button>(window.FindName("EnlargeButton"));
                var zoomIn = Assert.IsType<Button>(window.FindName("ZoomInButton"));
                var fit = Assert.IsType<Button>(window.FindName("FitWidthButton"));
                var scroll = Assert.IsType<ScrollViewer>(window.FindName("PreviewScroll"));
                Assert.Equal(ScrollBarVisibility.Auto, scroll.HorizontalScrollBarVisibility);
                await Invoke(window, "LoadAsync");
                await Invoke(window, "ShowPageAsync", 1);
                var image = Assert.IsType<Image>(window.FindName("PreviewImage"));
                var originalWidth = image.Width;
                zoomIn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitUntil(() => image.Width > originalWidth);
                Assert.Equal("Page 2 / 2", Assert.IsType<TextBlock>(window.FindName("PageText")).Text);
                var source = Assert.IsType<BitmapImage>(image.Source);
                Assert.True(source.PixelWidth > originalWidth);
                enlarge.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(WindowState.Maximized, window.WindowState);
                Assert.Equal("Rétablir", enlarge.Content);
                enlarge.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(WindowState.Normal, window.WindowState);
                fit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Invoke(window, "ShowPageAsync", 1);
                Assert.Equal("Page 2 / 2", Assert.IsType<TextBlock>(window.FindName("PageText")).Text);
                Assert.Equal(bytes, File.ReadAllBytes(path));
            }
            finally { window.Close(); File.Delete(path); }
        });
    }

    private static Task Invoke(object target, string name, params object[] args) =>
        (Task)target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args)!;

    private static async Task WaitUntil(Func<bool> condition)
    {
        var stop = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < stop) await Task.Delay(20);
        Assert.True(condition());
    }

    private static void RunSta(Func<Task> action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            async void Run()
            {
                try { await action(); } catch (Exception exception) { failure = exception; }
                finally { Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            }
            Dispatcher.CurrentDispatcher.BeginInvoke((Action)Run);
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)));
        if (failure is not null) throw failure;
    }
}
