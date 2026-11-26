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
/// The final render in a window of its own, as in KeyShot: the image refines in it while it renders (a quick pass first, then
/// the full one) with the samples counted live; once done, a bar at the top saves it and zooms in it. The panel on the right
/// keeps the file choices (name, format, quality, size). Closing an unsaved image asks first.
/// </summary>
public sealed class RenderResultWindow : Window
{
    private static readonly (string Label, string Format)[] Outputs = [("PNG", "png"), ("JPEG", "jpeg"), ("WebP", "webp"), ("TIFF 16 bits", "tiff")];
    private static readonly (string Label, int Side)[] Sizes = [("Taille du rendu", 0), ("2048 px", 2048), ("1600 px", 1600), ("1200 px", 1200), ("1000 px", 1000), ("800 px", 800)];
    private static readonly System.Globalization.CultureInfo French = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");

    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private readonly ScrollViewer _scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly TextBlock _samples = new() { Foreground = Brushes.White, FontWeight = FontWeight.SemiBold };
    private readonly Border _samplesBadge;
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 100, MinWidth = 160, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _summary = new() { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
    private readonly Button _cancel = new() { Content = "Annuler le rendu" };
    private readonly TextBlock _zoomLabel = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 56, TextAlignment = TextAlignment.Center };
    private readonly TextBox _name = new();
    private readonly ComboBox _format = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Slider _quality = new() { Minimum = 50, Maximum = 100, TickFrequency = 5, IsSnapToTickEnabled = true, Value = 90 };
    private readonly StackPanel _qualityField;
    private readonly ComboBox _size = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _weight = new() { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _savePanel = new() { Spacing = 12, IsEnabled = false };
    private readonly StackPanel _runBar = new() { Orientation = Orientation.Horizontal, Spacing = 12 };
    private readonly StackPanel _doneBar = new() { Orientation = Orientation.Horizontal, Spacing = 4, IsVisible = false };
    private string? _png;
    private string? _tiff;
    private bool _saved;
    private bool _closeConfirmed;
    /// <summary>Pixels of screen per pixel of image; 0 fits the image in the window.</summary>
    private double _zoom;

    public event Action? CancelRequested;
    public event Action? RedoRequested;
    /// <summary>Save the image: its PNG, its TIFF if any, the options, the file name, and whether to choose the folder.</summary>
    public event Action<string, string?, OutputOptions, string, bool>? SaveRequested;

    public RenderResultWindow(string title, OutputOptions defaults)
    {
        Title = title;
        Width = 1240;
        Height = 820;
        MinWidth = 720;
        MinHeight = 480;
        Background = (IBrush?)Application.Current?.FindResource("CanvasBrush");
        _format.ItemsSource = Outputs.Select(o => o.Label).ToList();
        _format.SelectedIndex = Math.Max(0, Array.FindIndex(Outputs, o => o.Format == defaults.Format));
        _size.ItemsSource = Sizes.Select(s => s.Label).ToList();
        _size.SelectedIndex = Math.Max(0, Array.FindIndex(Sizes, s => s.Side == defaults.MaxSide));
        _quality.Value = defaults.Quality;
        _qualityField = FormKit.Field("Qualité JPEG / WebP", _quality);
        _format.SelectionChanged += async (_, _) => { ShowFormatFields(); await EstimateAsync(); };
        _size.SelectionChanged += async (_, _) => await EstimateAsync();
        _quality.PropertyChanged += async (_, e) => { if (e.Property == RangeBase.ValueProperty) await EstimateAsync(); };
        _cancel.Click += (_, _) => CancelRequested?.Invoke();

        // Top bar while rendering: what is happening, the progress and a way to stop.
        _runBar.Children.Add(_status);
        _runBar.Children.Add(_progress);
        _runBar.Children.Add(_cancel);
        // Top bar once done: save first, then zoom.
        _doneBar.Children.Add(Tool("IconSave", "Enregistrer dans le projet", () => Save(chooseFolder: false), primary: true));
        _doneBar.Children.Add(Tool("IconOpen", "Enregistrer sous…", () => Save(chooseFolder: true)));
        _doneBar.Children.Add(new Border { Width = 1, Margin = new Thickness(8, 4), Background = (IBrush?)Application.Current?.FindResource("SubtleBorderBrush") });
        _doneBar.Children.Add(Tool("IconMinus", "Zoom arrière (Ctrl + molette)", () => ZoomBy(1 / 1.25)));
        _doneBar.Children.Add(_zoomLabel);
        _doneBar.Children.Add(Tool("IconPlus", "Zoom avant (Ctrl + molette)", () => ZoomBy(1.25)));
        _doneBar.Children.Add(Tool("IconExpand", "Ajuster à la fenêtre", () => SetZoom(0)));
        _doneBar.Children.Add(new Button { Content = "100 %", Padding = new Thickness(8, 4), VerticalAlignment = VerticalAlignment.Center });
        ((Button)_doneBar.Children[^1]).Click += (_, _) => SetZoom(1);
        ToolTip.SetTip(_doneBar.Children[^1], "Taille réelle : un pixel de l’image par pixel de l’écran");
        _doneBar.Children.Add(new Border { Width = 1, Margin = new Thickness(8, 4), Background = (IBrush?)Application.Current?.FindResource("SubtleBorderBrush") });
        _doneBar.Children.Add(Tool("IconRefresh", "Refaire le rendu", () => RedoRequested?.Invoke()));
        var bar = new Border { Classes = { "toolbar" }, Padding = new Thickness(12, 8), Child = new Panel { Children = { _runBar, _doneBar } } };

        _savePanel.Children.Add(new TextBlock { Text = "Fichier", FontSize = Ui.Text.Section, FontWeight = FontWeight.SemiBold });
        _savePanel.Children.Add(FormKit.Field("Nom du fichier", _name));
        _savePanel.Children.Add(FormKit.Field("Format", _format));
        _savePanel.Children.Add(_qualityField);
        _savePanel.Children.Add(FormKit.Field("Taille", _size, hint: "Réduit l’image à l’enregistrement, sans refaire le rendu."));
        _savePanel.Children.Add(_weight);

        var side = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = new StackPanel { Spacing = 16, Margin = new Thickness(0, 0, 12, 0), Children = { _summary, _savePanel } }
        };
        _scroll.Content = _image;
        _scroll.AddHandler(PointerWheelChangedEvent, (_, e) =>
        {
            if (_png is null || !e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
            ZoomBy(e.Delta.Y > 0 ? 1.25 : 1 / 1.25);
            e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        // Live sample count over the image, as in KeyShot.
        _samplesBadge = new Border
        {
            Background = new SolidColorBrush(Colors.Black, 0.6), CornerRadius = new CornerRadius(Ui.Radius.Control), Padding = new Thickness(12, 4), Margin = new Thickness(12),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false, Child = _samples
        };
        var stage = new Border
        {
            CornerRadius = new CornerRadius(Ui.Radius.Card), ClipToBounds = true, BorderThickness = new Thickness(1),
            BorderBrush = (IBrush?)Application.Current?.FindResource("SubtleBorderBrush"), Child = new Grid { Children = { _scroll, _samplesBadge } }
        };
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,300"), ColumnSpacing = 24 };
        body.Children.Add(stage);
        Grid.SetColumn(side, 1);
        body.Children.Add(side);
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 16, Margin = new Thickness(24, 16, 24, 24) };
        root.Children.Add(bar);
        Grid.SetRow(body, 1);
        root.Children.Add(body);
        Content = root;
        ShowFormatFields();
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

    private static Button Tool(string icon, string tip, Action click, bool primary = false)
    {
        var button = new Button { Content = Icons.Glyph(icon, 18), Padding = new Thickness(12, 8), VerticalAlignment = VerticalAlignment.Center };
        if (primary) button.Classes.Add("primary-action");
        ToolTip.SetTip(button, tip);
        AutomationProperties.SetName(button, tip);
        button.Click += (_, _) => click();
        return button;
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
        _cancel.IsVisible = true;
        _runBar.IsVisible = true;
        _doneBar.IsVisible = false;
        _savePanel.IsEnabled = false;
        _weight.Text = string.Empty;
    }

    public void Progress(string text, double fraction)
    {
        _status.Text = text;
        _progress.Value = Math.Clamp(fraction, 0, 1) * 100;
    }

    /// <summary>Samples done in the pass being rendered, shown over the image.</summary>
    public void Samples(string pass, int done, int total)
        => _samples.Text = $"{pass} · {done.ToString("N0", French)} / {total.ToString("N0", French)} échantillons";

    /// <summary>A quicker pass of the same image, shown while the full one renders.</summary>
    public void ShowPreview(string png) => ShowBitmap(png);

    public async void ShowResult(string png, string? tiff)
    {
        _png = png;
        _tiff = tiff;
        ShowBitmap(png);
        _samplesBadge.IsVisible = false;
        _runBar.IsVisible = false;
        _doneBar.IsVisible = true;
        _savePanel.IsEnabled = true;
        await EstimateAsync();
    }

    public void Stopped(string message)
    {
        _status.Text = message;
        _samplesBadge.IsVisible = false;
        _cancel.IsVisible = false;
        _closeConfirmed = true;
    }

    /// <summary>Marks the image as saved, so closing does not ask.</summary>
    public void MarkSaved(string where)
    {
        _saved = true;
        _weight.Text = "Enregistrée : " + where;
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

    private OutputOptions Options => new(Outputs[Math.Max(0, _format.SelectedIndex)].Format, (int)_quality.Value, true, Sizes[Math.Max(0, _size.SelectedIndex)].Side);

    private void Save(bool chooseFolder)
    {
        if (_png is null) return;
        var name = string.Concat((_name.Text ?? "rendu").Split(Path.GetInvalidFileNameChars())).Trim();
        SaveRequested?.Invoke(_png, _tiff, Options, name.Length == 0 ? "rendu" : name, chooseFolder);
    }

    private void ShowFormatFields()
    {
        var format = Outputs[Math.Max(0, _format.SelectedIndex)].Format;
        _qualityField.IsVisible = format is "jpeg" or "webp";
        _size.IsEnabled = format != "tiff";
    }

    private async Task EstimateAsync()
    {
        if (_png is not { } png || _saved) return;
        var options = Options;
        if (options.Format == "tiff") { _weight.Text = _tiff is { } t && File.Exists(t) ? $"Fichier TIFF 16 bits : {new FileInfo(t).Length / 1024} Ko." : "Pas de TIFF pour ce rendu : choisissez TIFF dans l’onglet Rendu avant de lancer."; return; }
        try
        {
            var bytes = await Task.Run(() => RenderOutput.Encode(png, options).Length);
            _weight.Text = "Fichier estimé : " + (bytes >= 1048576 ? (bytes / 1048576.0).ToString("0.0", French) + " Mo" : Math.Max(1, bytes / 1024) + " Ko");
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException) { _weight.Text = string.Empty; }
    }
}
