using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using UglyToad.PdfPig;

namespace MS3DPRINT.Manager.Windows.App;

/// <summary>One word of a PDF page: its text, its box in page units (origin top left) and the reading line it belongs to.</summary>
internal readonly record struct PdfWord(string Text, Rect Box, int Line);

/// <summary>Transparent layer over a rendered PDF page: select words with the mouse, copy them, and show search results.</summary>
internal sealed class PdfTextLayer : Control
{
    private static readonly IBrush Selection = new SolidColorBrush(Color.FromArgb(96, 56, 189, 248));
    private static readonly IBrush Match = new SolidColorBrush(Color.FromArgb(110, 250, 204, 21));
    private static readonly IBrush CurrentMatch = new SolidColorBrush(Color.FromArgb(190, 251, 146, 60));
    private IReadOnlyList<PdfWord> _words = [];
    private IReadOnlyList<(int First, int Last)> _matches = [];
    private int _currentMatch = -1;
    private double _scale = 1;
    private int _anchor = -1, _caret = -1;

    /// <summary>Raised when a new selection starts here, so the other pages can drop theirs.</summary>
    public event Action<PdfTextLayer>? SelectionStarted;

    public PdfTextLayer()
    {
        Cursor = new Cursor(StandardCursorType.Ibeam);
        var copy = new MenuItem { Header = "Copier" };
        copy.Click += async (_, _) => await CopyAsync();
        var all = new MenuItem { Header = "Tout sélectionner" };
        all.Click += (_, _) => SelectAll();
        ContextMenu = new ContextMenu { ItemsSource = new[] { copy, all } };
    }

    public bool HasSelection => _anchor >= 0 && _caret >= 0;
    public IReadOnlyList<PdfWord> Words => _words;

    public double Scale
    {
        get => _scale;
        set { _scale = value; InvalidateVisual(); }
    }

    public void SetWords(IReadOnlyList<PdfWord> words)
    {
        _words = words; _anchor = _caret = -1; _matches = []; _currentMatch = -1;
        IsVisible = words.Count > 0;
        InvalidateVisual();
    }

    public void SetMatches(IReadOnlyList<(int First, int Last)> matches, int current)
    {
        _matches = matches; _currentMatch = current; InvalidateVisual();
    }

    public void ClearSelection() { if (_anchor < 0) return; _anchor = _caret = -1; InvalidateVisual(); }

    public void SelectAll()
    {
        if (_words.Count == 0) return;
        SelectionStarted?.Invoke(this);
        _anchor = 0; _caret = _words.Count - 1; InvalidateVisual();
    }

    public string SelectedText()
    {
        if (!HasSelection) return "";
        var (from, to) = (Math.Min(_anchor, _caret), Math.Max(_anchor, _caret));
        var text = new System.Text.StringBuilder();
        for (var index = from; index <= to; index++)
        {
            if (index > from) text.Append(_words[index].Line == _words[index - 1].Line ? ' ' : '\n');
            text.Append(_words[index].Text);
        }
        return text.ToString();
    }

    public async Task CopyAsync()
    {
        var text = SelectedText();
        if (text.Length > 0 && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(text);
    }

    private Rect Scaled(Rect box) => new(box.X * _scale, box.Y * _scale, box.Width * _scale, box.Height * _scale);

    /// <summary>The word under the pointer, or the nearest one when the pointer is between words.</summary>
    private int Hit(Point point)
    {
        var best = -1; var bestDistance = double.MaxValue;
        for (var index = 0; index < _words.Count; index++)
        {
            var box = Scaled(_words[index].Box);
            var dx = Math.Max(Math.Max(box.Left - point.X, 0), point.X - box.Right);
            var dy = Math.Max(Math.Max(box.Top - point.Y, 0), point.Y - box.Bottom);
            var distance = dx * dx + dy * dy;
            if (distance < bestDistance) { bestDistance = distance; best = index; }
        }
        return best;
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size)); // gives the layer its hit area
        for (var match = 0; match < _matches.Count; match++)
            for (var index = _matches[match].First; index <= _matches[match].Last && index < _words.Count; index++)
                context.FillRectangle(match == _currentMatch ? CurrentMatch : Match, Scaled(_words[index].Box));
        if (!HasSelection) return;
        for (var index = Math.Min(_anchor, _caret); index <= Math.Max(_anchor, _caret) && index < _words.Count; index++)
            context.FillRectangle(Selection, Scaled(_words[index].Box));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_words.Count == 0 || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        SelectionStarted?.Invoke(this);
        _anchor = _caret = Hit(e.GetPosition(this));
        e.Pointer.Capture(this);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!ReferenceEquals(e.Pointer.Captured, this) || _anchor < 0) return;
        var index = Hit(e.GetPosition(this));
        if (index == _caret) return;
        _caret = index; InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (ReferenceEquals(e.Pointer.Captured, this)) e.Pointer.Capture(null);
    }

    /// <summary>Puts words (from a PDF or from text recognition) in reading order and numbers their lines.</summary>
    public static List<PdfWord> InReadingOrder(IEnumerable<(string Text, Rect Box)> words)
    {
        var placed = words.Where(word => !string.IsNullOrWhiteSpace(word.Text)).OrderBy(word => word.Box.Center.Y).ToList();
        var result = new List<PdfWord>(placed.Count);
        var row = new List<(string Text, Rect Box)>();
        var line = 0;
        void Flush()
        {
            foreach (var item in row.OrderBy(item => item.Box.X)) result.Add(new PdfWord(item.Text, item.Box, line));
            row.Clear(); line++;
        }
        foreach (var item in placed)
        {
            // A word joins the current row when its centre sits within half a word height of the row's.
            if (row.Count > 0 && Math.Abs(item.Box.Center.Y - row.Average(r => r.Box.Center.Y)) > Math.Max(item.Box.Height, 1) * 0.5) Flush();
            row.Add(item);
        }
        if (row.Count > 0) Flush();
        return result;
    }

    /// <summary>Words of a page of a digital PDF, in the page units used by the rendered image.</summary>
    public static List<PdfWord> Read(PdfDocument document, int pageIndex, double pageWidth, double pageHeight)
    {
        var page = document.GetPage(pageIndex + 1);
        // A rotated page would need its words turned too; text recognition on the picture covers it instead.
        if (page.Rotation.Value != 0) return [];
        var crop = page.CropBox.Bounds;
        if (crop.Width <= 0 || crop.Height <= 0) return [];
        double sx = pageWidth / crop.Width, sy = pageHeight / crop.Height;
        return InReadingOrder(page.GetWords().Select(word => (word.Text,
            new Rect((word.BoundingBox.Left - crop.Left) * sx, (crop.Top - word.BoundingBox.Top) * sy, word.BoundingBox.Width * sx, word.BoundingBox.Height * sy))));
    }
}
