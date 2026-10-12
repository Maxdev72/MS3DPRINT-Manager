using Avalonia.Controls;
using Avalonia.Platform;

namespace MS3DPRINT.Manager.Desktop;

public sealed class ChangelogWindow : Window
{
    public ChangelogWindow()
    {
        Title = "Journal des modifications";
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Width = 640; Height = 600; MinWidth = 420;
        var text = new SelectableTextBlock { Text = Load(), TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Avalonia.Thickness(24) };
        Content = new ScrollViewer { Content = text };
    }

    private static string Load()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://MS3DPRINT.Manager.Desktop/Assets/CHANGELOG.md"));
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception exception) when (exception is IOException or FileNotFoundException)
        {
            return "Journal des modifications indisponible.";
        }
    }
}
