using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using MS3DPRINT.Manager.Core.Tests.Estimates;
using MS3DPRINT.Manager.Desktop.Viewer;

namespace MS3DPRINT.Manager.Core.Tests.Preview;

[Collection("WorkspaceEnvironment")]
public sealed class PreviewLifecycleTests
{
    private static async Task Run(Func<Task> action)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EstimateTestApplication));
        await session.Dispatch(async () => { await action(); return true; }, CancellationToken.None);
    }
    private static WriteableBitmap? Bitmap(MeshViewport view) => (WriteableBitmap?)typeof(MeshViewport).GetField("_bitmap", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(view);
    private static void Frame(MeshViewport view, int size) => typeof(MeshViewport).GetMethod("ShowFrame", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(view, [new uint[size * size], size, size]);

    [Fact]
    public Task Hidden_thumbnail_rows_cancel_requests_and_dispose_late_results() => Run(async () =>
    {
        var picture = new Image(); var row = new StackPanel { Children = { picture } };
        var page = new Border { Child = row };
        var window = new Window { Content = page, Width = 400, Height = 300 };
        var completion = new TaskCompletionSource<Avalonia.Media.Imaging.Bitmap?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = default(CancellationToken);
        using var binding = new ThumbnailBinding(row, picture, cancellation => { token = cancellation; return completion.Task; }, automatic: false);
        window.Show(); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        try
        {
            var pending = binding.StartAsync();
            page.IsVisible = false; Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.True(token.IsCancellationRequested);
            var late = new WriteableBitmap(new PixelSize(2, 2), new Vector(96, 96), PixelFormat.Bgra8888);
            completion.SetResult(late); await pending;
            Assert.Null(picture.Source);
            using var output = new MemoryStream(); Assert.ThrowsAny<Exception>(() => late.Save(output, PngBitmapEncoderOptions.Default));
        }
        finally { completion.TrySetResult(null); window.Close(); }
    });

    [Fact]
    public Task Thumbnail_reattachment_loads_a_fresh_owned_image() => Run(() =>
    {
        var picture = new Image(); var row = new StackPanel { Children = { picture } };
        using var binding = new ThumbnailBinding(row, picture, _ => Task.FromResult<Avalonia.Media.Imaging.Bitmap?>(new WriteableBitmap(new PixelSize(2, 2), new Vector(96, 96), PixelFormat.Bgra8888)));
        var window = new Window { Width = 400, Height = 300, Content = row }; window.Show(); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        try
        {
            var first = Assert.IsAssignableFrom<Avalonia.Media.Imaging.Bitmap>(picture.Source);
            window.Content = null; Assert.Null(picture.Source);
            window.Content = row; Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.NotNull(picture.Source); Assert.NotSame(first, picture.Source);
            using var output = new MemoryStream(); Assert.ThrowsAny<Exception>(() => first.Save(output, PngBitmapEncoderOptions.Default));
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task Released_software_view_ignores_pending_frames_and_resumes_with_its_camera() => Run(async () =>
    {
        var view = new MeshViewport(); var window = new Window { Content = view, Width = 400, Height = 300 }; window.Show();
        try
        {
            var mesh = new MeshModel([new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)], [0, 1, 2]);
            view.SetMesh(mesh); view.Camera.Yaw = 1.25; view.RequestRender(true);
            var pending = (Task)typeof(MeshViewport).GetField("_renderTask", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!;
            view.Release(); await pending; Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Null(Bitmap(view));
            Assert.False(((Avalonia.Threading.DispatcherTimer)typeof(MeshViewport).GetField("_settle", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(view)!).IsEnabled);
            view.Resume(); await (Task)typeof(MeshViewport).GetField("_renderTask", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!;
            Assert.NotNull(Bitmap(view)); Assert.Equal(1.25, view.Camera.Yaw);
            view.SetMesh(null); Assert.Null(Bitmap(view));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Resizing_software_frames_releases_the_previous_bitmap() => Run(() =>
    {
        var view = new MeshViewport(); Frame(view, 4); var old = Bitmap(view)!;
        Frame(view, 8);
        Assert.NotSame(old, Bitmap(view));
        using var output = new MemoryStream();
        Assert.ThrowsAny<Exception>(() => old.Save(output, PngBitmapEncoderOptions.Default));
        view.Release();
        return Task.CompletedTask;
    });

    [Fact]
    public Task Detaching_a_software_view_releases_its_frame() => Run(() =>
    {
        var view = new MeshViewport(); var window = new Window { Width = 400, Height = 300, Content = view }; window.Show();
        try { Frame(view, 8); window.Content = null; Assert.Null(Bitmap(view)); }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task Warm_thumbnail_cache_obeys_cancellation_and_recovers_a_corrupt_png(bool canceled) => Run(async () =>
    {
        var folder = Path.Combine(Path.GetTempPath(), "ms3d-preview-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        var previous = ModelThumbnails.CacheDirectory; ModelThumbnails.CacheDirectory = folder;
        try
        {
            var model = Path.Combine(folder, "part.stl");
            File.WriteAllText(model, "solid p\nfacet normal 0 0 1\nouter loop\nvertex 0 0 0\nvertex 1 0 0\nvertex 0 1 0\nendloop\nendfacet\nendsolid p\n");
            var info = new FileInfo(model); var cache = ModelThumbnails.CachePath(model, info.Length, info.LastWriteTimeUtc);
            if (canceled) { using var image = new WriteableBitmap(new PixelSize(2, 2), new Vector(96, 96), PixelFormat.Bgra8888); image.Save(cache, PngBitmapEncoderOptions.Default); }
            else File.WriteAllText(cache, "PNG corrompu");
            using var result = await ModelThumbnails.GetAsync(model, canceled ? new CancellationToken(true) : default);
            if (canceled) Assert.Null(result); else Assert.NotNull(result);
        }
        finally { ModelThumbnails.CacheDirectory = previous; Directory.Delete(folder, true); }
    });
}
