using System.Windows;
using MS3DPRINT.Manager.App.Preview;
using MS3DPRINT.Manager.Core.Files;

namespace MS3DPRINT.Manager.App.Views;

public partial class DocumentPreviewWindow : Window
{
    private readonly string _path;
    private readonly PreviewFileKind _kind;
    private PdfPageRenderer? _pdf;
    private int _pageIndex;
    private int _renderVersion;

    public DocumentPreviewWindow(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
        _kind = PreviewFileSupport.GetKind(_path);
        if (_kind == PreviewFileKind.None) throw new NotSupportedException("L’aperçu accepte les images et les PDF.");
        InitializeComponent();
        TitleText.Text = Path.GetFileName(_path);
        PathText.Text = _path;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        MessageText.Text = "Chargement de l’aperçu…";
        try
        {
            if (_kind == PreviewFileKind.Image)
            {
                PreviewImage.Source = await Task.Run(() => PreviewImageLoader.Load(_path));
                MessageText.Text = "Image affichée en lecture seule.";
                return;
            }

            _pdf = await PdfPageRenderer.LoadAsync(_path);
            if (_pdf.PageCount == 0) throw new InvalidDataException("Le PDF ne contient aucune page.");
            PreviousButton.Visibility = Visibility.Visible;
            NextButton.Visibility = Visibility.Visible;
            PageText.Visibility = Visibility.Visible;
            await ShowPageAsync(0);
        }
        catch (Exception exception)
        {
            MessageText.Text = UiErrorMessages.For(exception);
        }
    }

    private async Task ShowPageAsync(int pageIndex)
    {
        if (_pdf is null || pageIndex < 0 || pageIndex >= _pdf.PageCount) return;
        var version = ++_renderVersion;
        PreviousButton.IsEnabled = false;
        NextButton.IsEnabled = false;
        MessageText.Text = "Rendu de la page…";
        try
        {
            var bytes = await _pdf.RenderPageAsync(pageIndex);
            var image = await Task.Run(() => PreviewImageLoader.Load(bytes));
            if (version != _renderVersion) return;
            PreviewImage.Source = image;
            _pageIndex = pageIndex;
            PageText.Text = $"Page {_pageIndex + 1} / {_pdf.PageCount}";
            MessageText.Text = "PDF affiché en lecture seule.";
        }
        catch (Exception exception)
        {
            if (version == _renderVersion) MessageText.Text = UiErrorMessages.For(exception);
        }
        finally
        {
            if (version == _renderVersion)
            {
                PreviousButton.IsEnabled = _pageIndex > 0;
                NextButton.IsEnabled = _pageIndex < _pdf.PageCount - 1;
            }
        }
    }

    private async void Previous_Click(object sender, RoutedEventArgs e) => await ShowPageAsync(_pageIndex - 1);
    private async void Next_Click(object sender, RoutedEventArgs e) => await ShowPageAsync(_pageIndex + 1);
}
