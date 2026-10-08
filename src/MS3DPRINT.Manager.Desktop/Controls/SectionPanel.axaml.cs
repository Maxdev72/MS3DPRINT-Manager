using Avalonia;
using Avalonia.Controls;

namespace MS3DPRINT.Manager.Desktop.Controls;

public partial class SectionPanel : UserControl
{
    public static readonly StyledProperty<object?> HeaderProperty =
        AvaloniaProperty.Register<SectionPanel, object?>(nameof(Header));

    public static readonly StyledProperty<object?> BodyProperty =
        AvaloniaProperty.Register<SectionPanel, object?>(nameof(Body));

    public static readonly StyledProperty<object?> FooterProperty =
        AvaloniaProperty.Register<SectionPanel, object?>(nameof(Footer));

    public object? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public object? Body
    {
        get => GetValue(BodyProperty);
        set => SetValue(BodyProperty, value);
    }

    public object? Footer
    {
        get => GetValue(FooterProperty);
        set => SetValue(FooterProperty, value);
    }

    public SectionPanel() => InitializeComponent();
}
