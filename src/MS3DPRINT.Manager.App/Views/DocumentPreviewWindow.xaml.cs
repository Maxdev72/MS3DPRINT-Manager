using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MS3DPRINT.Manager.App.Preview;
using MS3DPRINT.Manager.Core.Files;

namespace MS3DPRINT.Manager.App.Views;

public partial class DocumentPreviewWindow : Window
{
    private readonly string _path;
    private readonly PreviewFileKind _kind;
    private readonly SemaphoreSlim _renderGate = new(1, 1);
    private readonly DispatcherTimer _resizeTimer;
    private PdfPageRenderer? _pdf;
    private int _pageIndex;
    private int _renderVersion;
    private bool _closed;
    private bool _fitWidth = true;
    private double _zoom = 1;
    private double _pageWidth;
    private double _pageHeight;
    private WindowState _previousState = WindowState.Normal;

    public DocumentPreviewWindow(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
        _kind = PreviewFileSupport.GetKind(_path);
        if (_kind == PreviewFileKind.None) throw new NotSupportedException("L’aperçu accepte les images et les PDF.");
        _resizeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
        _resizeTimer.Tick += async (_, _) => { _resizeTimer.Stop(); await RefreshPreviewAsync(); };
        InitializeComponent();
        TitleText.Text = Path.GetFileName(_path);
        PathText.Text = _path;
        Loaded += async (_, _) => await LoadAsync();
        StateChanged += (_, _) => EnlargeButton.Content = WindowState == WindowState.Maximized ? "Rétablir" : "Agrandir";
        Closed += (_, _) => { _closed = true; ++_renderVersion; _resizeTimer.Stop(); PreviewImage.Source = null; _pdf = null; };
    }

    private async Task LoadAsync()
    {
        MessageText.Text = "Chargement de l’aperçu…";
        try
        {
            if (_kind == PreviewFileKind.Image)
            {
                var image = await Task.Run(() => PreviewImageLoader.Load(_path));
                if (_closed) return;
                PreviewImage.Source = image;
                _pageWidth = image.Width;
                _pageHeight = image.Height;
                ApplyImageSize();
                MessageText.Text = "Image affichée en lecture seule.";
                return;
            }
            var pdf = await PdfPageRenderer.LoadAsync(_path);
            if (_closed) return;
            _pdf = pdf;
            if (_pdf.PageCount == 0) throw new InvalidDataException("Le PDF ne contient aucune page.");
            PreviousButton.Visibility = NextButton.Visibility = PageText.Visibility = Visibility.Visible;
            await ShowPageAsync(0);
        }
        catch (Exception exception)
        {
            if (!_closed) MessageText.Text = UiErrorMessages.For(exception);
        }
    }

    private async Task ShowPageAsync(int pageIndex)
    {
        var pdf = _pdf;
        if (_closed || pdf is null || pageIndex < 0 || pageIndex >= pdf.PageCount) return;
        var version = ++_renderVersion;
        _pageIndex = pageIndex;
        (double Width, double Height) size;
        try { size = pdf.GetPageSize(pageIndex); }
        catch (Exception exception)
        {
            if (!_closed) MessageText.Text = UiErrorMessages.For(exception);
            return;
        }
        _pageWidth = size.Width;
        _pageHeight = size.Height;
        CalculateZoom();
        var width = _pageWidth * _zoom;
        var height = _pageHeight * _zoom;
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var requestedWidth = Math.Max(1, (int)Math.Min(int.MaxValue, Math.Ceiling(width * dpi)));
        PreviousButton.IsEnabled = NextButton.IsEnabled = false;
        MessageText.Text = "Rendu de la page…";
        await _renderGate.WaitAsync();
        try
        {
            if (_closed || version != _renderVersion) return;
            var bytes = await pdf.RenderPageAsync(pageIndex, requestedWidth);
            var image = await Task.Run(() => DecodePdf(bytes));
            if (_closed || version != _renderVersion) return;
            PreviewImage.Source = image;
            PreviewImage.Width = width;
            PreviewImage.Height = height;
            PageText.Text = $"Page {_pageIndex + 1} / {pdf.PageCount}";
            MessageText.Text = "PDF affiché en lecture seule.";
        }
        catch (Exception exception)
        {
            if (!_closed && version == _renderVersion) MessageText.Text = UiErrorMessages.For(exception);
        }
        finally
        {
            _renderGate.Release();
            if (!_closed && version == _renderVersion)
            {
                PreviousButton.IsEnabled = _pageIndex > 0;
                NextButton.IsEnabled = _pageIndex < pdf.PageCount - 1;
            }
        }
    }

    // PDF bytes are already bounded by the renderer; preserve their resolution above the image loader's 2000px cap.
    private static BitmapImage DecodePdf(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private void CalculateZoom()
    {
        if (_pageWidth <= 0) return;
        if (_fitWidth)
        {
            var available = PreviewScroll.ViewportWidth > 0 ? PreviewScroll.ViewportWidth : Math.Max(1, ActualWidth > 0 ? ActualWidth - 62 : Width - 62);
            _zoom = Math.Max(0.01, (available - 24) / _pageWidth);
        }
        ZoomText.Text = $"{_zoom * 100:0} %";
        ZoomOutButton.IsEnabled = _zoom > 0.1;
        ZoomInButton.IsEnabled = _zoom < 8;
    }

    private void ApplyImageSize()
    {
        CalculateZoom();
        if (_pageWidth <= 0) return;
        PreviewImage.Width = _pageWidth * _zoom;
        PreviewImage.Height = _pageHeight * _zoom;
    }

    private Task RefreshPreviewAsync()
    {
        if (_closed) return Task.CompletedTask;
        if (_kind == PreviewFileKind.Pdf) return ShowPageAsync(_pageIndex);
        ApplyImageSize();
        return Task.CompletedTask;
    }

    private async Task ChangeZoomAsync(double factor)
    {
        _fitWidth = false;
        _zoom = Math.Clamp(_zoom * factor, 0.1, 8);
        await RefreshPreviewAsync();
    }

    private async void ZoomIn_Click(object sender, RoutedEventArgs e) => await ChangeZoomAsync(1.25);
    private async void ZoomOut_Click(object sender, RoutedEventArgs e) => await ChangeZoomAsync(1 / 1.25);
    private async void FitWidth_Click(object sender, RoutedEventArgs e) { _fitWidth = true; await RefreshPreviewAsync(); }
    private void Enlarge_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized) WindowState = _previousState;
        else { _previousState = WindowState; WindowState = WindowState.Maximized; }
        EnlargeButton.Content = WindowState == WindowState.Maximized ? "Rétablir" : "Agrandir";
    }
    private void PreviewScroll_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_closed || !_fitWidth || _pageWidth <= 0) return;
        _resizeTimer.Stop();
        _resizeTimer.Start();
    }
    private async void Previous_Click(object sender, RoutedEventArgs e) => await ShowPageAsync(_pageIndex - 1);
    private async void Next_Click(object sender, RoutedEventArgs e) => await ShowPageAsync(_pageIndex + 1);
}
