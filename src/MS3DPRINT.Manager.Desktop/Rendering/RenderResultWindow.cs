using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace MS3DPRINT.Manager.Desktop.Rendering;

/// <summary>
/// The final render in a window of its own, as in KeyShot: the progress while it renders, then the image with what to do with
/// it — save it (name, format, quality, size; in the model's project or elsewhere), render it again, or close (an unsaved image
/// asks first).
/// </summary>
public sealed class RenderResultWindow : Window
{
    private static readonly (string Label, string Format)[] Outputs = [("PNG", "png"), ("JPEG", "jpeg"), ("WebP", "webp"), ("TIFF 16 bits", "tiff")];
    private static readonly (string Label, int Side)[] Sizes = [("Taille du rendu", 0), ("2048 px", 2048), ("1600 px", 1600), ("1200 px", 1200), ("1000 px", 1000), ("800 px", 800)];

    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 100 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _summary = new() { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
    private readonly Button _cancel = new() { Content = "Annuler le rendu", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _name = new();
    private readonly ComboBox _format = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Slider _quality = new() { Minimum = 50, Maximum = 100, TickFrequency = 5, IsSnapToTickEnabled = true, Value = 90 };
    private readonly StackPanel _qualityField;
    private readonly ComboBox _size = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _weight = new() { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _savePanel = new() { Spacing = 12, IsVisible = false };
    private readonly StackPanel _runPanel = new() { Spacing = 12 };
    private string? _png;
    private string? _tiff;
    private bool _saved;
    private bool _closeConfirmed;

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
        _quality.PropertyChanged += async (_, e) => { if (e.Property == Avalonia.Controls.Primitives.RangeBase.ValueProperty) await EstimateAsync(); };
        _cancel.Click += (_, _) => CancelRequested?.Invoke();

        var saveProject = new Button { Content = Icons.Label("IconSave", "Enregistrer dans le projet"), Classes = { "primary-action" }, HorizontalAlignment = HorizontalAlignment.Stretch };
        saveProject.Click += (_, _) => Save(chooseFolder: false);
        var saveAs = new Button { Content = Icons.Label("IconOpen", "Enregistrer sous…"), HorizontalAlignment = HorizontalAlignment.Stretch };
        saveAs.Click += (_, _) => Save(chooseFolder: true);
        var redo = new Button { Content = Icons.Label("IconRefresh", "Refaire le rendu"), HorizontalAlignment = HorizontalAlignment.Stretch };
        redo.Click += (_, _) => RedoRequested?.Invoke();
        var close = new Button { Content = "Fermer", HorizontalAlignment = HorizontalAlignment.Stretch };
        close.Click += (_, _) => Close();

        _runPanel.Children.Add(new TextBlock { Text = "Rendu en cours", FontSize = Ui.Text.Section, FontWeight = FontWeight.SemiBold });
        _runPanel.Children.Add(_progress);
        _runPanel.Children.Add(_status);
        _runPanel.Children.Add(_cancel);
        _savePanel.Children.Add(new TextBlock { Text = "Rendu terminé : que faire de l’image ?", FontSize = Ui.Text.Section, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        _savePanel.Children.Add(FormKit.Field("Nom du fichier", _name));
        _savePanel.Children.Add(FormKit.Field("Format", _format));
        _savePanel.Children.Add(_qualityField);
        _savePanel.Children.Add(FormKit.Field("Taille", _size, hint: "Réduit l’image à l’enregistrement, sans refaire le rendu."));
        _savePanel.Children.Add(_weight);
        _savePanel.Children.Add(saveProject);
        _savePanel.Children.Add(saveAs);
        _savePanel.Children.Add(redo);
        _savePanel.Children.Add(close);

        var side = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = new StackPanel { Spacing = 16, Margin = new Thickness(0, 0, 12, 0), Children = { _summary, _runPanel, _savePanel } }
        };
        var stage = new Border
        {
            CornerRadius = new CornerRadius(Ui.Radius.Card), ClipToBounds = true, BorderThickness = new Thickness(1),
            BorderBrush = (IBrush?)Application.Current?.FindResource("SubtleBorderBrush"), Child = _image
        };
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("*,320"), ColumnSpacing = 24, Margin = new Thickness(24) };
        root.Children.Add(stage);
        Grid.SetColumn(side, 1);
        root.Children.Add(side);
        Content = root;
        ShowFormatFields();
        Closing += async (_, e) =>
        {
            if (_closeConfirmed || _png is null || _saved) return;
            e.Cancel = true;
            if (!await ConfirmRemovalWindow.AskAsync(this, "l’image rendue", "L’image n’est pas enregistrée : elle sera perdue. Le rendu devra être refait pour la retrouver.")) return;
            _closeConfirmed = true;
            Close();
        };
    }

    /// <summary>A new render starts in this window (also after « Refaire le rendu »).</summary>
    public void Start(string summary, string suggestedName)
    {
        _summary.Text = summary;
        _name.Text = suggestedName;
        _png = null;
        _tiff = null;
        _saved = false;
        _progress.Value = 0;
        _status.Text = "Préparation…";
        _runPanel.IsVisible = true;
        _savePanel.IsVisible = false;
    }

    public void Progress(string text, double fraction)
    {
        _status.Text = text;
        _progress.Value = Math.Clamp(fraction, 0, 1) * 100;
    }

    public async void ShowResult(string png, string? tiff)
    {
        _png = png;
        _tiff = tiff;
        using (var stream = File.OpenRead(png)) { (_image.Source as IDisposable)?.Dispose(); _image.Source = new Bitmap(stream); }
        _runPanel.IsVisible = false;
        _savePanel.IsVisible = true;
        await EstimateAsync();
    }

    public void Stopped(string message)
    {
        _status.Text = message;
        _cancel.IsVisible = false;
        _closeConfirmed = true;
    }

    /// <summary>Marks the image as saved, so closing does not ask.</summary>
    public void MarkSaved(string where)
    {
        _saved = true;
        _weight.Text = "Enregistrée : " + where;
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
            _weight.Text = "Fichier estimé : " + (bytes >= 1048576 ? (bytes / 1048576.0).ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")) + " Mo" : Math.Max(1, bytes / 1024) + " Ko");
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException) { _weight.Text = string.Empty; }
    }
}
