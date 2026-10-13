using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace MS3DPRINT.Manager.Desktop;

public partial class App : Application
{
    /// <summary>The splash screen stays long enough to be read, then closes as soon as the workspace is read.</summary>
    private const int SplashMinimumMs = 900;

    public override void OnFrameworkInitializationCompleted()
    {
        Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            e.Handled = true;
            if ((ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow is MainWindow window)
                window.ReportUnexpectedError(e.Exception);
            else
                ErrorLog.Write(e.Exception);
        };
        TaskScheduler.UnobservedTaskException += (_, e) => { ErrorLog.Write(e.Exception); e.SetObserved(); };

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
        while (clock.ElapsedMilliseconds < SplashMinimumMs || !preload.IsCompleted)
        {
            var percent = clock.ElapsedMilliseconds * 100.0 / SplashMinimumMs;
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
