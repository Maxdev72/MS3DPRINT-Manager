using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace MS3DPRINT.Manager.Desktop;

public sealed class ImagePreviewWindow : Window
{
    public ImagePreviewWindow(string path)
    {
        Title = Path.GetFileName(path);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Width = 1000;
        Height = 760;
        Background = Avalonia.Media.Brushes.Black;
        Content = new ScrollViewer
        {
            Content = new Image
            {
                Source = new Bitmap(path),
                Stretch = Avalonia.Media.Stretch.Uniform
            }
        };
    }
}
