using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace MS3DPRINT.Manager.Desktop;

public partial class App : Application
{
    private const int SplashDurationMs = 5000;

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var splash = new SplashWindow();
            desktop.MainWindow = splash;
            splash.Opened += async (_, _) => await StartAsync(desktop, splash);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task StartAsync(IClassicDesktopStyleApplicationLifetime desktop, SplashWindow splash)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        splash.Report(0, "Chargement de l’interface…");
        await Task.Yield();
        var main = new MainWindow();
        var preload = main.PreloadAsync();
        while (clock.ElapsedMilliseconds < SplashDurationMs)
        {
            var percent = clock.ElapsedMilliseconds * 100.0 / SplashDurationMs;
            splash.Report(Math.Min(percent, 99), percent < 40 ? "Chargement de l’interface…" : percent < 80 ? "Lecture de l’espace de travail…" : "Finalisation…");
            await Task.Delay(40);
        }
        await preload;
        splash.Report(100, "Prêt");
        await Task.Delay(150);
        desktop.MainWindow = main;
        main.Show();
        splash.Close();
    }
}
