using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace MS3DPRINT.Manager.Desktop;

public partial class App : Application
{
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
        splash.Report(15, "Chargement de l’interface…");
        await Task.Yield();
        var main = new MainWindow();
        splash.Report(55, "Lecture de l’espace de travail…");
        await main.PreloadAsync();
        splash.Report(100, "Prêt");
        await Task.Delay(250);
        desktop.MainWindow = main;
        main.Show();
        splash.Close();
    }
}
