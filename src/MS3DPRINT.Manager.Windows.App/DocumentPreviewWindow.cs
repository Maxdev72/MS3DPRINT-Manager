using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using MS3DPRINT.Manager.Windows;
using MS3DPRINT.Manager.Core.Files;

namespace MS3DPRINT.Manager.Windows.App;

/// <summary>Avalonia PDF/image viewer, so documents open in the same look as the rest of the shell.</summary>
public sealed class DocumentPreviewWindow : Window
{
    private readonly string _path;
    private readonly PreviewFileKind _kind;
    private readonly DispatcherTimer _resizeTimer = new() { Interval = TimeSpan.FromMilliseconds(160) };
    private readonly ScrollViewer _scroll = new() { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
    private readonly Image _image = new() { Stretch = Stretch.Fill, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12) };
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _pageText = new() { VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
    private readonly TextBlock _zoomText = new() { Text = "100 %", VerticalAlignment = VerticalAlignment.Center, MinWidth = 48, TextAlignment = TextAlignment.Center };
    private readonly Button _previous = new() { Content = "←  Précédent", IsEnabled = false, IsVisible = false };
    private readonly Button _next = new() { Content = "Suivant  →", IsEnabled = false, IsVisible = false };
    private readonly Button _zoomOut = new() { Content = "−" };
    private readonly Button _zoomIn = new() { Content = "+" };
    private PdfPageRenderer? _pdf;
    private int _pageIndex;
    private int _version;
    private bool _closed;
    private bool _fitWidth = true;
    private double _zoom = 1;
    private double _pageWidth;
    private double _pageHeight;

    public DocumentPreviewWindow(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
        _kind = PreviewFileSupport.GetKind(_path);
        if (_kind == PreviewFileKind.None) throw new NotSupportedException("L’aperçu accepte les images et les PDF.");

        Title = Path.GetFileName(_path);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Width = 960; Height = 760; MinWidth = 520; MinHeight = 400;

        var muted = Resource("MutedTextBrush") ?? Brushes.Gray;
        var title = new TextBlock { Text = Path.GetFileName(_path), FontSize = 18, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        var location = new TextBlock { Text = _path, Foreground = muted, TextTrimming = TextTrimming.CharacterEllipsis };
        _message.Foreground = muted;

        _previous.Click += async (_, _) => await ShowPageAsync(_pageIndex - 1);
        _next.Click += async (_, _) => await ShowPageAsync(_pageIndex + 1);
        _zoomOut.Click += async (_, _) => await ChangeZoomAsync(1 / 1.25);
        _zoomIn.Click += async (_, _) => await ChangeZoomAsync(1.25);
        var fit = new Button { Content = "Ajuster à la largeur" };
        fit.Click += async (_, _) => { _fitWidth = true; await RefreshAsync(); };
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _previous, _pageText, _next, _zoomOut, _zoomText, _zoomIn, fit } };

        _scroll.Content = _image;
        _scroll.PropertyChanged += (_, e) =>
        {
            if (e.Property == ScrollViewer.ViewportProperty && _fitWidth && _pageWidth > 0 && !_closed) { _resizeTimer.Stop(); _resizeTimer.Start(); }
        };
        _resizeTimer.Tick += async (_, _) => { _resizeTimer.Stop(); await RefreshAsync(); };

        var header = new StackPanel { Spacing = 8, Children = { new StackPanel { Spacing = 2, Children = { title, location } }, toolbar } };
        var viewer = new Border { Background = Resource("SurfaceBrush"), BorderBrush = Resource("BorderBrush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Child = _scroll };
        var root = new Grid { Margin = new Thickness(18), RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 12 };
        root.Children.Add(header);
        Grid.SetRow(viewer, 1); root.Children.Add(viewer);
        Grid.SetRow(_message, 2); root.Children.Add(_message);
        Content = root;

        Opened += async (_, _) => await LoadAsync();
        Closed += (_, _) => { _closed = true; _version++; _resizeTimer.Stop(); _image.Source = null; _pdf = null; };
    }

    private static IBrush? Resource(string key)
        => Application.Current?.TryFindResource(key, Application.Current.ActualThemeVariant, out var value) == true ? value as IBrush : null;

    private async Task LoadAsync()
    {
        _message.Text = "Chargement de l’aperçu…";
        try
        {
            if (_kind == PreviewFileKind.Image)
            {
                var bitmap = await Task.Run(() => new Bitmap(_path));
                if (_closed) return;
                _image.Source = bitmap;
                _pageWidth = bitmap.Size.Width; _pageHeight = bitmap.Size.Height;
                ApplySize();
                _message.Text = "Image affichée en lecture seule.";
                return;
            }

            var pdf = await PdfPageRenderer.LoadAsync(_path);
            if (_closed) return;
            _pdf = pdf;
            if (pdf.PageCount == 0) throw new InvalidDataException("Le PDF ne contient aucune page.");
            _previous.IsVisible = _next.IsVisible = _pageText.IsVisible = true;
            await ShowPageAsync(0);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (!_closed) _message.Text = "Impossible d’afficher ce fichier : " + exception.Message;
        }
    }

    private async Task ShowPageAsync(int pageIndex)
    {
        var pdf = _pdf;
        if (_closed || pdf is null || pageIndex < 0 || pageIndex >= pdf.PageCount) return;
        var version = ++_version;
        _pageIndex = pageIndex;
        try
        {
            var size = pdf.GetPageSize(pageIndex);
            _pageWidth = size.Width; _pageHeight = size.Height;
            CalculateZoom();
            var width = _pageWidth * _zoom;
            var pixels = Math.Max(1, (int)Math.Ceiling(width * (RenderScaling > 0 ? RenderScaling : 1)));
            _previous.IsEnabled = _next.IsEnabled = false;
            _message.Text = "Rendu de la page…";
            var bytes = await pdf.RenderPageAsync(pageIndex, pixels);
            var bitmap = await Task.Run(() => new Bitmap(new MemoryStream(bytes, writable: false)));
            if (_closed || version != _version) return;
            _image.Source = bitmap;
            _image.Width = width; _image.Height = _pageHeight * _zoom;
            _pageText.Text = $"Page {_pageIndex + 1} / {pdf.PageCount}";
            _message.Text = "PDF affiché en lecture seule.";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (!_closed && version == _version) _message.Text = "Impossible d’afficher cette page : " + exception.Message;
        }
        finally
        {
            if (!_closed && version == _version)
            {
                _previous.IsEnabled = _pageIndex > 0;
                _next.IsEnabled = _pageIndex < pdf.PageCount - 1;
            }
        }
    }

    private void CalculateZoom()
    {
        if (_pageWidth <= 0) return;
        if (_fitWidth)
        {
            var available = _scroll.Viewport.Width > 0 ? _scroll.Viewport.Width : Math.Max(1, Bounds.Width - 62);
            _zoom = Math.Max(0.01, (available - 24) / _pageWidth);
        }
        _zoomText.Text = $"{_zoom * 100:0} %";
        _zoomOut.IsEnabled = _zoom > 0.1;
        _zoomIn.IsEnabled = _zoom < 8;
    }

    private void ApplySize()
    {
        CalculateZoom();
        if (_pageWidth <= 0) return;
        _image.Width = _pageWidth * _zoom;
        _image.Height = _pageHeight * _zoom;
    }

    private Task RefreshAsync()
    {
        if (_closed) return Task.CompletedTask;
        if (_kind == PreviewFileKind.Pdf) return ShowPageAsync(_pageIndex);
        ApplySize();
        return Task.CompletedTask;
    }

    private async Task ChangeZoomAsync(double factor)
    {
        _fitWidth = false;
        _zoom = Math.Clamp(_zoom * factor, 0.1, 8);
        await RefreshAsync();
    }
}
