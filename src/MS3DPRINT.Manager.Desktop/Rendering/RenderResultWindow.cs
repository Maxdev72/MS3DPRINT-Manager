using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace MS3DPRINT.Manager.Desktop.Rendering;

/// <summary>
/// The render window, as in KeyShot: every render setting on the right (quality, device, file, series), « Lancer le rendu » at
/// the top. While it renders, the image refines in it (a quick pass, then the full one) with the samples counted live; once
/// done, the top bar saves it and zooms in it. Closing an unsaved image asks first.
/// </summary>
public sealed class RenderResultWindow : Window
{
    private static readonly System.Globalization.CultureInfo French = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");

    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private readonly ScrollViewer _scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly TextBlock _samples = new() { Foreground = Brushes.White, FontWeight = FontWeight.SemiBold };
    private readonly Border _samplesBadge;
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 100, MinWidth = 160, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _setupNote = new() { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _summary = new() { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
    private readonly Button _cancel = new() { Content = "Annuler le rendu" };
    private readonly TextBlock _zoomLabel = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 56, TextAlignment = TextAlignment.Center };
    private readonly TextBox _name = new();
    private readonly TextBlock _savedNote = new() { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
    private readonly Border _settingsHost = new();
    private readonly StackPanel _setupBar = new() { Orientation = Orientation.Horizontal, Spacing = 12 };
    private readonly StackPanel _runBar = new() { Orientation = Orientation.Horizontal, Spacing = 12, IsVisible = false };
    private readonly StackPanel _doneBar = new() { Orientation = Orientation.Horizontal, Spacing = 4, IsVisible = false };
    private string? _png;
    private string? _tiff;
    private bool _saved;
    private bool _closeConfirmed;
    /// <summary>Pixels of screen per pixel of image; 0 fits the image in the window.</summary>
    private double _zoom;

    public event Action? CancelRequested;
    public event Action? RenderRequested;
    /// <summary>Save the image: its PNG, its TIFF if any, the file name, and whether to choose the folder.</summary>
    public event Action<string, string?, string, bool>? SaveRequested;

    /// <summary>The finished image, while the window shows one.</summary>
    public string? Png => _png;

    /// <param name="settings">The render settings of the page (quality, device, file, series), shown on the right.</param>
    public RenderResultWindow(string title, Control settings)
    {
        Title = title;
        Width = 1280;
        Height = 860;
        MinWidth = 760;
        MinHeight = 520;
        Background = (IBrush?)Application.Current?.FindResource("CanvasBrush");
        _settingsHost.Child = settings;
        // Settings belong to the page: they leave with the window, so the next one can show them.
        Closed += (_, _) => _settingsHost.Child = null;
        _cancel.Click += (_, _) => CancelRequested?.Invoke();

        // Top bar before rendering: launch.
        var launch = new Button { Content = Icons.Label("IconPlay", "Lancer le rendu"), Classes = { "primary-action" }, VerticalAlignment = VerticalAlignment.Center };
        launch.Click += (_, _) => RenderRequested?.Invoke();
        _setupBar.Children.Add(launch);
        _setupBar.Children.Add(_setupNote);
        // While rendering: what is happening, the progress and a way to stop.
        _runBar.Children.Add(_status);
        _runBar.Children.Add(_progress);
        _runBar.Children.Add(_cancel);
        // Once done: save first, then zoom, then render again.
        _doneBar.Children.Add(Tool("IconSave", "Enregistrer dans le projet", () => Save(chooseFolder: false), primary: true));
        _doneBar.Children.Add(Tool("IconOpen", "Enregistrer sous…", () => Save(chooseFolder: true)));
        _doneBar.Children.Add(Divider());
        _doneBar.Children.Add(Tool("IconMinus", "Zoom arrière (Ctrl + molette)", () => ZoomBy(1 / 1.25)));
        _doneBar.Children.Add(_zoomLabel);
        _doneBar.Children.Add(Tool("IconPlus", "Zoom avant (Ctrl + molette)", () => ZoomBy(1.25)));
        _doneBar.Children.Add(Tool("IconExpand", "Ajuster à la fenêtre", () => SetZoom(0)));
        var actual = new Button { Content = "100 %", Padding = new Thickness(8, 4), VerticalAlignment = VerticalAlignment.Center };
        actual.Click += (_, _) => SetZoom(1);
        ToolTip.SetTip(actual, "Taille réelle : un pixel de l’image par pixel de l’écran");
        _doneBar.Children.Add(actual);
        _doneBar.Children.Add(Divider());
        _doneBar.Children.Add(Tool("IconRefresh", "Refaire le rendu avec les réglages de droite", () => RenderRequested?.Invoke()));
        var bar = new Border { Classes = { "toolbar" }, Padding = new Thickness(12, 8), Child = new Panel { Children = { _setupBar, _runBar, _doneBar } } };

        var side = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = new StackPanel
            {
                Spacing = 16, Margin = new Thickness(0, 0, 12, 0),
                Children = { _summary, FormKit.Field("Nom du fichier", _name), _savedNote, _settingsHost }
            }
        };
        _scroll.Content = _image;
        _scroll.AddHandler(PointerWheelChangedEvent, (_, e) =>
        {
            if (_image.Source is null || !e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
            ZoomBy(e.Delta.Y > 0 ? 1.25 : 1 / 1.25);
            e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        // Live sample count over the image, as in KeyShot.
        _samplesBadge = new Border
        {
            Background = new SolidColorBrush(Colors.Black, 0.6), CornerRadius = new CornerRadius(Ui.Radius.Control), Padding = new Thickness(12, 4), Margin = new Thickness(12),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false, IsVisible = false, Child = _samples
        };
        var stage = new Border
        {
            CornerRadius = new CornerRadius(Ui.Radius.Card), ClipToBounds = true, BorderThickness = new Thickness(1),
            BorderBrush = (IBrush?)Application.Current?.FindResource("SubtleBorderBrush"), Child = new Grid { Children = { _scroll, _samplesBadge } }
        };
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,360"), ColumnSpacing = 24 };
        body.Children.Add(stage);
        Grid.SetColumn(side, 1);
        body.Children.Add(side);
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 16, Margin = new Thickness(24, 16, 24, 24) };
        root.Children.Add(bar);
        Grid.SetRow(body, 1);
        root.Children.Add(body);
        Content = root;
        SetZoom(0);
        Closing += async (_, e) =>
        {
            if (_closeConfirmed || _png is null || _saved) return;
            e.Cancel = true;
            if (!await ConfirmRemovalWindow.AskAsync(this, "l’image rendue", "L’image n’est pas enregistrée : elle sera perdue. Le rendu devra être refait pour la retrouver.")) return;
            _closeConfirmed = true;
            Close();
        };
    }

    private static Border Divider() => new() { Width = 1, Margin = new Thickness(8, 4), Background = (IBrush?)Application.Current?.FindResource("SubtleBorderBrush") };

    private static Button Tool(string icon, string tip, Action click, bool primary = false)
    {
        var button = new Button { Content = Icons.Glyph(icon, 18), Padding = new Thickness(12, 8), VerticalAlignment = VerticalAlignment.Center };
        if (primary) button.Classes.Add("primary-action");
        ToolTip.SetTip(button, tip);
        AutomationProperties.SetName(button, tip);
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>Ready to render: the settings on the right, the current preview as a reminder of the framing.</summary>
    public void Setup(string summary, string suggestedName, string? preview, string? note = null)
    {
        _summary.Text = summary;
        if (_png is null) _name.Text = suggestedName;
        if (_png is null && preview is not null && File.Exists(preview)) ShowBitmap(preview);
        _setupNote.Text = note ?? "Choisissez la qualité et le fichier à droite, puis lancez le rendu.";
        _setupBar.IsVisible = _png is null;
        _runBar.IsVisible = false;
        _doneBar.IsVisible = _png is not null;
    }

    /// <summary>A new render starts in this window (also after « Refaire le rendu »); the last image stays until the first pass replaces it.</summary>
    public void Start(string summary, string suggestedName)
    {
        _summary.Text = summary;
        _name.Text = suggestedName;
        _png = null;
        _tiff = null;
        _saved = false;
        _closeConfirmed = false;
        _progress.Value = 0;
        _status.Text = "Préparation…";
        _samples.Text = "Préparation du rendu…";
        _samplesBadge.IsVisible = true;
        _savedNote.Text = string.Empty;
        _setupBar.IsVisible = false;
        _runBar.IsVisible = true;
        _doneBar.IsVisible = false;
    }

    public void Progress(string text, double fraction)
    {
        _status.Text = text;
        _progress.Value = Math.Clamp(fraction, 0, 1) * 100;
    }

    /// <summary>Samples done in the pass being rendered, shown over the image.</summary>
    public void Samples(string pass, int done, int total)
        => _samples.Text = $"{pass} · {done.ToString("N0", French)} / {total.ToString("N0", French)} échantillons";

    /// <summary>A quicker pass of the same image (or the last image of a series), shown while rendering goes on.</summary>
    public void ShowPreview(string png) => ShowBitmap(png);

    public void ShowResult(string png, string? tiff)
    {
        _png = png;
        _tiff = tiff;
        ShowBitmap(png);
        _samplesBadge.IsVisible = false;
        _runBar.IsVisible = false;
        _doneBar.IsVisible = true;
    }

    /// <summary>The render stopped (cancelled, failed, or a series that saved itself): back to the settings, with the message.</summary>
    public void Stopped(string message)
    {
        _samplesBadge.IsVisible = false;
        _closeConfirmed = true;
        _runBar.IsVisible = false;
        _setupBar.IsVisible = true;
        _setupNote.Text = message;
    }

    /// <summary>Marks the image as saved, so closing does not ask.</summary>
    public void MarkSaved(string where)
    {
        _saved = true;
        _savedNote.Text = "Enregistrée : " + where;
    }

    private void ShowBitmap(string png)
    {
        Bitmap bitmap;
        using (var stream = File.OpenRead(png)) bitmap = new Bitmap(stream);
        (_image.Source as IDisposable)?.Dispose();
        _image.Source = bitmap;
        SetZoom(_zoom);
    }

    private void ZoomBy(double factor)
    {
        if (_image.Source is not Bitmap bitmap) return;
        var current = _zoom > 0 ? _zoom : Math.Min(_scroll.Bounds.Width / bitmap.PixelSize.Width, _scroll.Bounds.Height / bitmap.PixelSize.Height);
        SetZoom(Math.Clamp(current * factor, 0.05, 8));
    }

    private void SetZoom(double zoom)
    {
        _zoom = zoom;
        var fit = zoom <= 0 || _image.Source is not Bitmap;
        var visibility = fit ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        (_scroll.HorizontalScrollBarVisibility, _scroll.VerticalScrollBarVisibility) = (visibility, visibility);
        _image.Stretch = fit ? Stretch.Uniform : Stretch.Fill;
        if (!fit && _image.Source is Bitmap bitmap) (_image.Width, _image.Height) = (bitmap.PixelSize.Width * zoom, bitmap.PixelSize.Height * zoom);
        else (_image.Width, _image.Height) = (double.NaN, double.NaN);
        _zoomLabel.Text = fit ? "Ajusté" : zoom.ToString("0 %", French);
    }

    private void Save(bool chooseFolder)
    {
        if (_png is null) return;
        var name = string.Concat((_name.Text ?? "rendu").Split(Path.GetInvalidFileNameChars())).Trim();
        SaveRequested?.Invoke(_png, _tiff, name.Length == 0 ? "rendu" : name, chooseFolder);
    }
}
