using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;

namespace MS3DPRINT.Manager.Desktop;

public sealed class BrandResourcesWindow : DialogWindow
{
    public BrandResourcesWindow()
    {
        Title = "Identité visuelle et licences"; Width = 620; Height = 760; MinWidth = 440;
        using var stream = AssetLoader.Open(new Uri("avares://MS3DPRINT.Manager.Desktop/Assets/Fonts/OFL-BricolageGrotesque.txt"));
        using var reader = new StreamReader(stream);
        var close = new Button { Content = "Fermer" }; close.Click += (_, _) => Close(true);
        Layout(Title, "Logo et direction visuelle issus de ms3dprint.fr. Police Bricolage Grotesque distribuée sous licence SIL Open Font License.",
            [new SelectableTextBlock { Text = reader.ReadToEnd(), TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 20 }], close, cancel: new Button { IsVisible = false });
    }
}
