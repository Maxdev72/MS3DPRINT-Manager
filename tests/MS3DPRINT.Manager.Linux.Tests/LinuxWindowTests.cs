using Avalonia;
using Avalonia.Headless;
using MS3DPRINT.Manager.Desktop;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace MS3DPRINT.Manager.Linux.Tests;

public sealed class LinuxTestApplication : Application
{
    public override void Initialize()
    {
        Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
        foreach (var file in new[] { "Theme.axaml", "Controls.axaml", "Brand.axaml", "Workspace.axaml" })
            Styles.Add(new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://MS3DPRINT.Manager.Desktop/"))
                { Source = new Uri("avares://MS3DPRINT.Manager.Desktop/Styles/" + file) });
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<LinuxTestApplication>().UseSkia().UseHeadless(new() { UseHeadlessDrawing = false });
}

public sealed class LinuxWindowTests
{
    [Fact]
    public async Task Local_workshop_opens_and_renders_without_Windows_assemblies()
    {
        if (!OperatingSystem.IsLinux()) return;
        var directory = Directory.CreateTempSubdirectory("ms3d-linux-window-").FullName;
        var previousRoot = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE");
        var previousWindow = Environment.GetEnvironmentVariable("MS3DPRINT_WINDOW_FILE");
        Environment.SetEnvironmentVariable("MS3DPRINT_WORKSPACE", directory);
        Environment.SetEnvironmentVariable("MS3DPRINT_WINDOW_FILE", Path.Combine(directory, "window.json"));
        try
        {
            await using var session = HeadlessUnitTestSession.StartNew(typeof(LinuxTestApplication));
            await session.Dispatch(async () =>
            {
                var window = new MainWindow { Width = 1280, Height = 850 };
                try
                {
                    await window.PreloadAsync();
                    window.Show();
                    Assert.False(window.IsServerWorkspace);
                    using var bitmap = window.CaptureRenderedFrame();
                    Assert.NotNull(bitmap);
                    Assert.True(bitmap.PixelSize.Width > 0);
                    var output = Environment.GetEnvironmentVariable("MS3DPRINT_LINUX_QA");
                    if (output is not null) bitmap.Save(Path.Combine(output, "linux-workshop.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                }
                finally { window.Close(); }
                return true;
            }, CancellationToken.None);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MS3DPRINT_WORKSPACE", previousRoot);
            Environment.SetEnvironmentVariable("MS3DPRINT_WINDOW_FILE", previousWindow);
            Directory.Delete(directory, true);
        }
    }
}
