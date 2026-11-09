using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>Borderless start-up window showing the logo, the product name and a progress bar.</summary>
public sealed class SplashWindow : Window
{
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 100, Height = 6, Value = 0 };
    private readonly TextBlock _status = new() { Text = "Démarrage…", FontSize = Ui.Text.Micro, Foreground = Token("MutedTextBrush", "#94A7C4"), HorizontalAlignment = HorizontalAlignment.Center };

    public SplashWindow()
    {
        Title = "MS3DPRINT Manager";
        Width = 460; Height = 300;
        CanResize = false;
        ShowInTaskbar = false;
        WindowDecorations = WindowDecorations.None;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Token("CanvasBrush", "#080C14");

        var stack = new StackPanel { Spacing = 16, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(48, 0) };
        if (LoadLogo() is { } logo)
            stack.Children.Add(new Image { Source = logo, Width = 96, Height = 96, HorizontalAlignment = HorizontalAlignment.Center });
        stack.Children.Add(new TextBlock { Text = "MS3DPRINT", FontSize = Ui.Text.Page, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Token("TextBrush", "#EAF0FC") });
        stack.Children.Add(new TextBlock { Text = "MANAGER", FontSize = Ui.Text.Micro, LetterSpacing = 4, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Token("AccentBrush", "#6B9BFF") });
        stack.Children.Add(new Border { Height = 8 });
        stack.Children.Add(_progress);
        stack.Children.Add(_status);
        Content = new Border { BorderBrush = Token("BorderBrush", "#243247"), BorderThickness = new Thickness(1), Child = stack };
    }

    private static IBrush Token(string key, string fallback)
        => Application.Current?.TryFindResource(key, Application.Current.ActualThemeVariant, out var value) == true && value is IBrush brush
            ? brush : new SolidColorBrush(Color.Parse(fallback));

    public void Report(double percent, string status)
    {
        _progress.Value = percent;
        _status.Text = status;
    }

    private static Bitmap? LoadLogo()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://MS3DPRINT.Manager.Desktop/Assets/app-icon.png"));
            return new Bitmap(stream);
        }
        catch (Exception exception) when (exception is IOException or FileNotFoundException)
        {
            return null;
        }
    }
}
