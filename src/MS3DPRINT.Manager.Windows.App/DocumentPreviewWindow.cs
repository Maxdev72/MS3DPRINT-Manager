using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using MS3DPRINT.Manager.Windows;
using MS3DPRINT.Manager.Core.Files;
using MS3DPRINT.Manager.Core.Recognition;
using UglyToad.PdfPig;

namespace MS3DPRINT.Manager.Windows.App;

/// <summary>
/// Avalonia PDF/image viewer: all pages in one scrolling column (only those near the screen are drawn), selectable text,
/// search (Ctrl+F), zoom (Ctrl+wheel, Ctrl +/-/0) and text recognition for scanned pages.
/// </summary>
public sealed class DocumentPreviewWindow : Window
{
    private const double PageGap = 12;
    private const string Hint = "Lecture seule · glisser pour sélectionner · Ctrl+C copier · Ctrl+F chercher · Ctrl+molette zoom";

    /// <summary>One page: the picture, the selectable text over it, sized in page units × zoom.</summary>
    private sealed class PageView : Grid
    {
        public PageView(int index, double width, double height, bool paper)
        {
            Index = index; PageWidth = width; PageHeight = height;
            if (paper) Background = Brushes.White;
            Children.Add(Picture); Children.Add(Layer);
        }
        public int Index { get; }
        public double PageWidth { get; }
        public double PageHeight { get; }
        public Image Picture { get; } = new() { Stretch = Stretch.Fill };
        public PdfTextLayer Layer { get; } = new();
        public int RenderedPixels { get; set; }
    }

    private sealed record Found(int Page, int First, int Last);

    private readonly string _path;
    private readonly PreviewFileKind _kind;
    private readonly DispatcherTimer _scrollTimer = new() { Interval = TimeSpan.FromMilliseconds(90) };
    private readonly DispatcherTimer _resizeTimer = new() { Interval = TimeSpan.FromMilliseconds(160) };
    private readonly ScrollViewer _scroll = new() { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
    private readonly StackPanel _pages = new() { Spacing = PageGap, Margin = new Thickness(PageGap), HorizontalAlignment = HorizontalAlignment.Center };
    private readonly List<PageView> _views = [];
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _pageBox = new() { Width = 56, TextAlignment = TextAlignment.Center, IsVisible = false };
    private readonly TextBlock _pageTotal = new() { VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
    private readonly TextBlock _zoomText = new() { Text = "100 %", VerticalAlignment = VerticalAlignment.Center, MinWidth = 48, TextAlignment = TextAlignment.Center };
    private readonly Button _previous = new() { Content = MS3DPRINT.Manager.Desktop.Icons.Glyph("IconChevronUp"), IsVisible = false };
    private readonly Button _next = new() { Content = MS3DPRINT.Manager.Desktop.Icons.Glyph("IconChevronDown"), IsVisible = false };
    private readonly Button _zoomOut = new() { Content = MS3DPRINT.Manager.Desktop.Icons.Glyph("IconMinus") };
    private readonly Button _zoomIn = new() { Content = MS3DPRINT.Manager.Desktop.Icons.Glyph("IconPlus") };
    private readonly Button _print = new() { Content = "Imprimer…", IsVisible = false };
    private readonly Button _searchToggle = new() { Content = MS3DPRINT.Manager.Desktop.Icons.Label("IconSearch", "Rechercher"), IsVisible = false };
    private readonly Border _searchBar = new() { IsVisible = false };
    private readonly TextBox _searchBox = new() { Width = 280, PlaceholderText = "Rechercher dans le document…" };
    private readonly TextBlock _searchCount = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 90 };
    private readonly object _gate = new();
    private readonly SemaphoreSlim _ocrGate = new(1, 1);
    private readonly Dictionary<int, List<PdfWord>> _words = [];
    private readonly Dictionary<int, (string Text, int[] Starts)> _folded = [];
    private readonly HashSet<int> _ocrDone = [];
    private List<Found> _found = [];
    private int _current = -1;
    private PdfPageRenderer? _pdf;
    private PdfDocument? _text;
    private int _pageIndex;
    private int _renderVersion;
    private bool _rendering, _renderAgain, _closed, _wordsLoaded, _ocrAllStarted;
    private bool _fitWidth = true;
    private double _zoom = 1;
    private double _lastViewportWidth;

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

        _previous.Click += (_, _) => GoToPage(_pageIndex - 1);
        _next.Click += (_, _) => GoToPage(_pageIndex + 1);
        _zoomOut.Click += (_, _) => ChangeZoom(1 / 1.25);
        _zoomIn.Click += (_, _) => ChangeZoom(1.25);
        var fit = new Button { Content = "Ajuster à la largeur" };
        fit.Click += (_, _) => { _fitWidth = true; ApplyZoom(); };
        _print.Click += async (_, _) => await PrintAsync();
        _searchToggle.Click += (_, _) => ToggleSearch();
        _pageBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            if (int.TryParse(_pageBox.Text, out var number)) GoToPage(number - 1);
            _scroll.Focus(); e.Handled = true;
        };
        void Describe(Control control, string name)
        {
            Avalonia.Automation.AutomationProperties.SetName(control, name);
            ToolTip.SetTip(control, name);
        }
        Describe(_previous, "Page précédente"); Describe(_next, "Page suivante"); Describe(_pageBox, "Aller à la page");
        Describe(_zoomOut, "Réduire le document · Ctrl+-"); Describe(_zoomIn, "Agrandir le document · Ctrl++");
        Describe(fit, "Ajuster le document à la largeur · Ctrl+0"); Describe(_print, "Imprimer le document"); Describe(_searchToggle, "Rechercher dans le document · Ctrl+F");
        Avalonia.Automation.AutomationProperties.SetHelpText(_zoomText, "Niveau de zoom actuel");

        var navigation = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 12, 8), Children = { _previous, _pageBox, _pageTotal, _next } };
        navigation.Bind(Visual.IsVisibleProperty, _previous.GetObservable(Visual.IsVisibleProperty));
        var zoom = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 12, 8), Children = { _zoomOut, _zoomText, _zoomIn } };
        fit.Margin = new Thickness(0, 0, 8, 8); _searchToggle.Margin = new Thickness(0, 0, 8, 8); _print.Margin = new Thickness(0, 0, 0, 8);
        var toolbar = new WrapPanel { Orientation = Orientation.Horizontal, Children = { navigation, zoom, fit, _searchToggle, _print } };

        var previousMatch = new Button { Content = MS3DPRINT.Manager.Desktop.Icons.Glyph("IconChevronUp") };
        var nextMatch = new Button { Content = MS3DPRINT.Manager.Desktop.Icons.Glyph("IconChevronDown") };
        var closeSearch = new Button { Content = MS3DPRINT.Manager.Desktop.Icons.Glyph("IconClose") };
        Describe(previousMatch, "Résultat précédent · Maj+Entrée"); Describe(nextMatch, "Résultat suivant · Entrée"); Describe(closeSearch, "Fermer la recherche · Échap");
        Describe(_searchBox, "Rechercher dans le document");
        previousMatch.Click += (_, _) => StepMatch(-1);
        nextMatch.Click += (_, _) => StepMatch(1);
        closeSearch.Click += (_, _) => ToggleSearch(false);
        _searchBox.TextChanged += (_, _) => RunSearch(jump: true);
        _searchBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            StepMatch(e.KeyModifiers == KeyModifiers.Shift ? -1 : 1); e.Handled = true;
        };
        _searchBar.Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 4), Children = { _searchBox, previousMatch, nextMatch, _searchCount, closeSearch } };

        _scroll.Content = _pages;
        _scroll.ScrollChanged += (_, _) =>
        {
            UpdateCurrentPage();
            _scrollTimer.Stop(); _scrollTimer.Start();
            if (_fitWidth && _views.Count > 0 && Math.Abs(_scroll.Viewport.Width - _lastViewportWidth) > 1) { _resizeTimer.Stop(); _resizeTimer.Start(); }
        };
        _scrollTimer.Tick += async (_, _) => { _scrollTimer.Stop(); await RenderVisibleAsync(); };
        _resizeTimer.Tick += (_, _) => { _resizeTimer.Stop(); if (_fitWidth) ApplyZoom(); };
        _scroll.AddHandler(PointerWheelChangedEvent, (_, e) =>
        {
            if (e.KeyModifiers != KeyModifiers.Control) return;
            ChangeZoom(Math.Pow(1.1, e.Delta.Y)); e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        KeyDown += OnWindowKeyDown;

        var header = new StackPanel { Spacing = 8, Children = { new StackPanel { Spacing = 2, Children = { title, location } }, toolbar, _searchBar } };
        var viewer = new Border { Background = Resource("SurfaceBrush"), BorderBrush = Resource("BorderBrush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Child = _scroll };
        var root = new Grid { Margin = new Thickness(18), RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 12 };
        root.Children.Add(header);
        Grid.SetRow(viewer, 1); root.Children.Add(viewer);
        Grid.SetRow(_message, 2); root.Children.Add(_message);
        Content = root;

        Opened += async (_, _) => await LoadAsync();
        Closed += (_, _) =>
        {
            _closed = true; _renderVersion++; _scrollTimer.Stop(); _resizeTimer.Stop();
            foreach (var view in _views) ReplaceImage(view, null);
            _pdf = null;
            lock (_gate) { _text?.Dispose(); _text = null; }
        };
    }

    private static IBrush? Resource(string key)
        => Application.Current?.TryFindResource(key, Application.Current.ActualThemeVariant, out var value) == true ? value as IBrush : null;

    private static void ReplaceImage(PageView view, Bitmap? bitmap)
    {
        var previous = view.Picture.Source as Bitmap;
        view.Picture.Source = bitmap;
        if (!ReferenceEquals(previous, bitmap)) previous?.Dispose();
    }

    private double Top(PageView view) => view.Bounds.Top + _pages.Margin.Top;

    // ---- loading

    private async Task LoadAsync()
    {
        _message.Text = "Chargement de l’aperçu…";
        try
        {
            if (_kind == PreviewFileKind.Image)
            {
                var bitmap = await Task.Run(() => new Bitmap(_path));
                if (_closed) { bitmap.Dispose(); return; }
                var view = new PageView(0, bitmap.Size.Width, bitmap.Size.Height, paper: false);
                view.Picture.Source = bitmap;
                _views.Add(view); _pages.Children.Add(view);
                ApplyZoom();
                _message.Text = "Image affichée en lecture seule.";
                return;
            }

            var pdf = await PdfPageRenderer.LoadAsync(_path);
            if (_closed) return;
            _pdf = pdf;
            if (pdf.PageCount == 0) throw new InvalidDataException("Le PDF ne contient aucune page.");
            for (var index = 0; index < pdf.PageCount; index++)
            {
                var size = pdf.GetPageSize(index);
                var view = new PageView(index, size.Width, size.Height, paper: true);
                view.Layer.SelectionStarted += started => { foreach (var other in _views) if (!ReferenceEquals(other.Layer, started)) other.Layer.ClearSelection(); };
                _views.Add(view); _pages.Children.Add(view);
            }
            _previous.IsVisible = _next.IsVisible = _pageBox.IsVisible = _pageTotal.IsVisible = _print.IsVisible = _searchToggle.IsVisible = true;
            _pageTotal.Text = "/ " + pdf.PageCount;
            ApplyZoom();
            _message.Text = Hint;
            _ = LoadWordsAsync();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (!_closed) _message.Text = "Impossible d’afficher ce fichier : " + exception.Message;
        }
    }

    // ---- zoom and navigation

    private void CalculateZoom()
    {
        if (_views.Count == 0) return;
        if (_fitWidth)
        {
            var available = _scroll.Viewport.Width > 0 ? _scroll.Viewport.Width : Math.Max(1, Bounds.Width - 62);
            _zoom = Math.Clamp((available - PageGap * 2) / _views.Max(view => view.PageWidth), 0.1, 8);
        }
        _zoomText.Text = $"{_zoom * 100:0} %";
        _zoomOut.IsEnabled = _zoom > 0.1;
        _zoomIn.IsEnabled = _zoom < 8;
    }

    private void ApplyZoom()
    {
        var ratio = _scroll.Extent.Height > 0 ? _scroll.Offset.Y / _scroll.Extent.Height : 0;
        CalculateZoom();
        _lastViewportWidth = _scroll.Viewport.Width;
        _renderVersion++;
        foreach (var view in _views)
        {
            view.Width = view.PageWidth * _zoom; view.Height = view.PageHeight * _zoom;
            view.Layer.Scale = _zoom;
            if (_pdf is not null) view.RenderedPixels = 0; // the shown picture is stretched until the sharp one is ready
        }
        // Keep the same place of the document in view once the layout has taken the new sizes.
        Dispatcher.UIThread.Post(() =>
        {
            if (_closed) return;
            _scroll.Offset = new Vector(_scroll.Offset.X, ratio * _scroll.Extent.Height);
            _scrollTimer.Stop(); _scrollTimer.Start();
        }, DispatcherPriority.Loaded);
    }

    private void ChangeZoom(double factor)
    {
        _fitWidth = false;
        _zoom = Math.Clamp(_zoom * factor, 0.1, 8);
        ApplyZoom();
    }

    private void GoToPage(int index)
    {
        if (_views.Count == 0) return;
        index = Math.Clamp(index, 0, _views.Count - 1);
        _scroll.Offset = new Vector(_scroll.Offset.X, Math.Max(0, Top(_views[index]) - PageGap));
    }

    private void UpdateCurrentPage()
    {
        if (_views.Count == 0) return;
        var probe = _scroll.Offset.Y + _scroll.Viewport.Height * 0.3;
        var index = 0;
        for (var i = 0; i < _views.Count; i++) { if (Top(_views[i]) <= probe) index = i; else break; }
        _pageIndex = index;
        if (!_pageBox.IsFocused) _pageBox.Text = (index + 1).ToString(CultureInfo.InvariantCulture);
        _previous.IsEnabled = index > 0;
        _next.IsEnabled = index < _views.Count - 1;
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        var typing = e.Source is TextBox;
        if (e.KeyModifiers == KeyModifiers.Control)
        {
            switch (e.Key)
            {
                case Key.F when _pdf is not null: ToggleSearch(true); e.Handled = true; break;
                case Key.C when !typing && _views.FirstOrDefault(view => view.Layer.HasSelection) is { } selected:
                    _ = selected.Layer.CopyAsync(); e.Handled = true; break;
                case Key.A when !typing && _views.Count > 0: _views[_pageIndex].Layer.SelectAll(); e.Handled = true; break;
                case Key.D0 or Key.NumPad0: _fitWidth = true; ApplyZoom(); e.Handled = true; break;
                case Key.OemPlus or Key.Add: ChangeZoom(1.25); e.Handled = true; break;
                case Key.OemMinus or Key.Subtract: ChangeZoom(1 / 1.25); e.Handled = true; break;
            }
            return;
        }
        if (e.Key == Key.Escape && _searchBar.IsVisible) { ToggleSearch(false); e.Handled = true; return; }
        if (typing || e.KeyModifiers != KeyModifiers.None) return;
        var page = _scroll.Viewport.Height * 0.9;
        switch (e.Key)
        {
            case Key.PageDown: _scroll.Offset = new Vector(_scroll.Offset.X, _scroll.Offset.Y + page); e.Handled = true; break;
            case Key.PageUp: _scroll.Offset = new Vector(_scroll.Offset.X, Math.Max(0, _scroll.Offset.Y - page)); e.Handled = true; break;
            case Key.Home: _scroll.Offset = new Vector(_scroll.Offset.X, 0); e.Handled = true; break;
            case Key.End: _scroll.Offset = new Vector(_scroll.Offset.X, _scroll.Extent.Height); e.Handled = true; break;
            case Key.F3: StepMatch(1); e.Handled = true; break;
        }
    }

    // ---- drawing only the pages near the screen

    private async Task RenderVisibleAsync()
    {
        if (_pdf is null || _closed) return;
        if (_rendering) { _renderAgain = true; return; }
        _rendering = true;
        try
        {
            do { _renderAgain = false; await RenderPassAsync(); }
            while (_renderAgain && !_closed);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (!_closed) _message.Text = "Impossible d’afficher une page : " + exception.Message;
        }
        finally { _rendering = false; }
    }

    private async Task RenderPassAsync()
    {
        var pdf = _pdf;
        if (pdf is null) return;
        var version = _renderVersion;
        var top = _scroll.Offset.Y; var height = Math.Max(1, _scroll.Viewport.Height);
        var scaling = RenderScaling > 0 ? RenderScaling : 1;
        // Free the pictures of pages far from the screen.
        foreach (var view in _views)
            if (view.Picture.Source is not null && (Top(view) + view.Bounds.Height < top - height * 3 || Top(view) > top + height * 4))
            { ReplaceImage(view, null); view.RenderedPixels = 0; }
        var near = _views.Where(view => Top(view) + view.Bounds.Height >= top - height && Top(view) <= top + height * 2)
            .OrderBy(view => Math.Abs(Top(view) + view.Bounds.Height / 2 - (top + height / 2))).ToList();
        foreach (var view in near)
        {
            if (_closed || version != _renderVersion) return;
            var pixels = Math.Max(1, (int)Math.Ceiling(view.Width * scaling));
            if (view.RenderedPixels == pixels) continue;
            var bytes = await pdf.RenderPageAsync(view.Index, pixels);
            var bitmap = await Task.Run(() => new Bitmap(new MemoryStream(bytes, writable: false)));
            if (_closed || version != _renderVersion) { bitmap.Dispose(); return; }
            ReplaceImage(view, bitmap);
            view.RenderedPixels = pixels;
        }
        // A scanned page on screen gets its text recognised, so it can be selected and searched.
        foreach (var view in near.Where(v => Top(v) + v.Bounds.Height >= top && Top(v) <= top + height))
            if (_wordsLoaded && !_closed && _words.TryGetValue(view.Index, out var words) && words.Count == 0 && !_ocrDone.Contains(view.Index))
                await RecognizeAsync(view);
    }

    // ---- text: digital PDF first, text recognition for scans

    private async Task LoadWordsAsync()
    {
        foreach (var view in _views)
        {
            if (_closed) return;
            var (width, height, index) = (view.PageWidth, view.PageHeight, view.Index);
            var words = await Task.Run(() =>
            {
                try
                {
                    lock (_gate)
                    {
                        if (_closed) return new List<PdfWord>();
                        _text ??= PdfDocument.Open(_path);
                        return PdfTextLayer.Read(_text, index, width, height);
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or UglyToad.PdfPig.Core.PdfDocumentFormatException)
                { return new List<PdfWord>(); }
            });
            if (_closed) return;
            SetWords(view, words);
        }
        _wordsLoaded = true;
        _scrollTimer.Stop(); _scrollTimer.Start(); // scanned pages already on screen
        if (_searchBar.IsVisible) _ = RecognizeAllAsync();
    }

    private void SetWords(PageView view, List<PdfWord> words)
    {
        _words[view.Index] = words; _folded.Remove(view.Index);
        view.Layer.SetWords(words);
    }

    private async Task RecognizeAsync(PageView view)
    {
        _ocrDone.Add(view.Index);
        if (DocumentReader.Ocr is not WindowsOcrEngine { IsAvailable: true } engine || _pdf is null) return;
        await _ocrGate.WaitAsync();
        try
        {
            if (_closed) return;
            _message.Text = $"Reconnaissance du texte de la page {view.Index + 1}…";
            var bytes = await _pdf.RenderPageAsync(view.Index, 1800);
            var (found, width, height) = await engine.RecognizeWordsAsync(bytes);
            if (_closed || width <= 0) return;
            double sx = view.PageWidth / width, sy = view.PageHeight / height;
            SetWords(view, PdfTextLayer.InReadingOrder(found.Select(word => (word.Text, new Rect(word.X * sx, word.Y * sy, word.Width * sx, word.Height * sy)))));
            if (_searchBar.IsVisible) RunSearch(jump: false);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or ArgumentException or System.Runtime.InteropServices.COMException)
        { /* this page stays an image */ }
        finally { _ocrGate.Release(); if (!_closed) _message.Text = Hint; }
    }

    /// <summary>Reads every scanned page (once), so a search covers the whole document.</summary>
    private async Task RecognizeAllAsync()
    {
        if (_ocrAllStarted || !_wordsLoaded) return;
        _ocrAllStarted = true;
        foreach (var view in _views.ToList())
            if (!_closed && _words.TryGetValue(view.Index, out var words) && words.Count == 0 && !_ocrDone.Contains(view.Index))
                await RecognizeAsync(view);
    }

    // ---- search

    private void ToggleSearch(bool? show = null)
    {
        var visible = show ?? !_searchBar.IsVisible;
        if (visible && _pdf is null) return;
        _searchBar.IsVisible = visible;
        if (visible)
        {
            _searchBox.Focus(); _searchBox.SelectAll();
            _ = RecognizeAllAsync();
        }
        else
        {
            _found = []; _current = -1; ApplyMatches(); _scroll.Focus();
        }
    }

    private static string Fold(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) builder.Append(char.ToLowerInvariant(c));
        return builder.ToString();
    }

    private (string Text, int[] Starts) FoldedPage(int index)
    {
        if (_folded.TryGetValue(index, out var known)) return known;
        var words = _words[index];
        var starts = new int[words.Count]; var text = new StringBuilder();
        for (var i = 0; i < words.Count; i++) { starts[i] = text.Length; text.Append(Fold(words[i].Text)).Append(' '); }
        return _folded[index] = (text.ToString(), starts);
    }

    private void RunSearch(bool jump)
    {
        var query = string.Join(' ', Fold(_searchBox.Text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var found = new List<Found>();
        if (query.Length > 0)
            foreach (var view in _views)
            {
                if (!_words.TryGetValue(view.Index, out var words) || words.Count == 0) continue;
                var (text, starts) = FoldedPage(view.Index);
                int WordAt(int character) { var k = Array.BinarySearch(starts, character); return k >= 0 ? k : ~k - 1; }
                for (var at = text.IndexOf(query, StringComparison.Ordinal); at >= 0; at = text.IndexOf(query, at + query.Length, StringComparison.Ordinal))
                    found.Add(new Found(view.Index, WordAt(at), WordAt(at + query.Length - 1)));
            }
        _found = found;
        _current = found.Count == 0 ? -1 : Math.Max(0, found.FindIndex(match => match.Page >= _pageIndex));
        ApplyMatches();
        if (jump && _current >= 0) ScrollToMatch();
    }

    private void StepMatch(int direction)
    {
        if (_found.Count == 0) return;
        _current = (_current + direction + _found.Count) % _found.Count;
        ApplyMatches(); ScrollToMatch();
    }

    private void ApplyMatches()
    {
        foreach (var view in _views)
        {
            var onPage = _found.Where(match => match.Page == view.Index).ToList();
            var local = _current >= 0 && _found[_current].Page == view.Index ? onPage.IndexOf(_found[_current]) : -1;
            view.Layer.SetMatches(onPage.Select(match => (match.First, match.Last)).ToList(), local);
        }
        var query = (_searchBox.Text ?? "").Trim();
        _searchCount.Text = !_searchBar.IsVisible || query.Length == 0 ? "" : _found.Count == 0 ? "Aucun résultat" : $"{_current + 1} / {_found.Count}";
    }

    private void ScrollToMatch()
    {
        if (_current < 0) return;
        var match = _found[_current];
        var view = _views[match.Page];
        var y = Top(view) + _words[match.Page][match.First].Box.Y * _zoom - _scroll.Viewport.Height * 0.3;
        _scroll.Offset = new Vector(_scroll.Offset.X, Math.Max(0, y));
    }

    // ---- printing

    private async Task PrintAsync()
    {
        var pdf = _pdf;
        if (pdf is null || _closed) return;
        PdfPrintRequest? request;
        try
        {
            request = SystemPrintDialog.Show(TryGetPlatformHandle()?.Handle ?? IntPtr.Zero, pdf.PageCount, _pageIndex);
        }
        catch (InvalidOperationException exception)
        {
            _message.Text = exception.Message;
            return;
        }
        if (request is null) return;

        _print.IsEnabled = false;
        _message.Text = $"Envoi de {request.Pages.Count} page(s) à « {request.PrinterName} »…";
        try
        {
            await Task.Run(() => PdfPrinter.Print(pdf, Path.GetFileName(_path), request));
            if (!_closed) _message.Text = $"Impression envoyée à « {request.PrinterName} » ({request.Pages.Count} page(s) × {request.Copies}).";
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException or System.Drawing.Printing.InvalidPrinterException or IOException)
        {
            if (!_closed) _message.Text = "Impression impossible : " + exception.Message;
        }
        finally
        {
            if (!_closed) _print.IsEnabled = true;
        }
    }
}
