using System.Windows;
using MS3DPRINT.Manager.Core.Storage;

namespace MS3DPRINT.Manager.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var dataDirectory = Environment.GetEnvironmentVariable("MS3DPRINT_DATA_DIRECTORY");
        ThemeManager.Apply(new ThemeSettingsStore(string.IsNullOrWhiteSpace(dataDirectory) ? null : dataDirectory).LoadAppearance());
    }
}
