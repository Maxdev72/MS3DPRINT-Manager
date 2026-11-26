using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MS3DPRINT.Manager.Desktop.Viewer;

namespace MS3DPRINT.Manager.Desktop.Rendering;

/// <summary>
/// Rendu tab: quality, processor or graphics card, output format and size, and series (cameras × colours, with a contact sheet).
/// </summary>
public sealed partial class RenderStudioView
{
    /// <summary>Output quality; Seconds is the time measured for one image on an RTX 5070, for the series estimate.</summary>
    private static readonly (string Label, string Hint, Pass Pass, double Seconds)[] Qualities =
    [
        ("Rapide", "EEVEE temps réel, 2048 px : environ 1 s.", new(2048, 64, "eevee"), 1),
        ("Standard", "Cycles 1024 px : environ 2 s.", new(1024, 128, "cycles"), 2),
        ("HD", "Cycles 2048 px : environ 11 s.", new(2048, 256, "cycles"), 11),
        ("Très haute", "Cycles 4096 px : environ 1 min 30.", new(4096, 512, "cycles"), 90)
    ];
    private static readonly (string Label, string Key)[] Devices = [("Automatique", "auto"), ("Carte graphique (GPU)", "gpu"), ("Processeur (CPU)", "cpu")];
    private static readonly (string Label, string Format)[] Outputs = [("PNG (transparence, sans perte)", "png"), ("JPEG (léger, web et e-mail)", "jpeg"), ("WebP (très léger, transparence)", "webp"), ("TIFF 16 bits (retouche)", "tiff")];
    private static readonly (string Label, int Side)[] Sizes = [("Taille du rendu", 0), ("2048 px", 2048), ("1600 px", 1600), ("1200 px", 1200), ("1000 px", 1000), ("800 px", 800)];

    private readonly ComboBox _quality = Choice(Qualities.Select(q => q.Label));
    private readonly TextBlock _qualityHint = new() { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox _device = Choice(Devices.Select(d => d.Label));
    private readonly TextBlock _deviceNote = new() { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox _outputFormat = Choice(Outputs.Select(o => o.Label));
    private readonly Slider _outputQuality = new() { Minimum = 50, Maximum = 100, TickFrequency = 5, IsSnapToTickEnabled = true };
    private readonly StackPanel _outputQualityField = new() { Spacing = 4 };
    private readonly CheckBox _maxCompression = new() { Content = "Compression maximale (sans perte, un peu plus long à enregistrer)" };
    private readonly ComboBox _maxSide = Choice(Sizes.Select(s => s.Label));
    private readonly TextBlock _sizeEstimate = new() { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _outputWarning = new() { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private readonly StackPanel _seriesCameras = new() { Spacing = 4 };
    private readonly WrapPanel _seriesColors = new();
    private readonly WrapPanel _seriesGroups = new();
    private readonly HashSet<string> _seriesColorKeys = [];
    private readonly HashSet<string> _seriesCameraNames = ["Vue actuelle"];
    private readonly CheckBox _contactSheet = new() { Content = "Planche couleurs : une image avec toutes les variantes et leur nom" };
    private readonly TextBlock _seriesSummary = new() { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
    private readonly Button _runSeries = new() { Content = Icons.Label("IconPlay", "Lancer la série"), HorizontalAlignment = HorizontalAlignment.Stretch };

    private OutputOptions CurrentOutput => new(Outputs[Math.Max(0, _outputFormat.SelectedIndex)].Format, (int)_outputQuality.Value, _maxCompression.IsChecked == true,
        Sizes[Math.Max(0, _maxSide.SelectedIndex)].Side);

    private Control BuildOutputTab()
    {
        _outputQualityField.Children.Add(FormKit.Field("Qualité JPEG / WebP", _outputQuality));
        return new StackPanel
        {
            Spacing = 12,
            Children =
            {
                FormKit.Field("Qualité du rendu", _quality), _qualityHint,
                FormKit.Field("Calcul sur", _device), _deviceNote,
                FormKit.SectionLabel("Fichier"), FormKit.Field("Format", _outputFormat), _outputQualityField, _maxCompression,
                FormKit.Field("Taille de l’image enregistrée", _maxSide, hint: "Réduit l’image à l’enregistrement, sans refaire le rendu."),
                _sizeEstimate, _outputWarning,
                // By default « Rendu final » renders one image of the scene; the series stays folded until asked for.
                new Expander
                {
                    Header = "Série de rendus (plusieurs vues ou couleurs)", IsExpanded = false, HorizontalAlignment = HorizontalAlignment.Stretch, Classes = { "estimate-section" },
                    Content = new StackPanel
                    {
                        Spacing = 12, Children =
                        {
                            FormKit.Field("Caméras", _seriesCameras),
                            FormKit.Field("Couleurs", new StackPanel { Spacing = 8, Children = { _seriesGroups, _seriesColors } },
                                hint: "Aucune couleur cochée : la couleur actuelle seulement."),
                            _contactSheet, _seriesSummary, _runSeries
                        }
                    }
                }
            }
        };
    }

    private void InitializeOutput()
    {
        _quality.SelectedIndex = Math.Clamp(_saved.Quality, 0, Qualities.Length - 1);
        _device.SelectedIndex = Math.Max(0, Array.FindIndex(Devices, d => d.Key == _saved.Device));
        _outputFormat.SelectedIndex = Math.Max(0, Array.FindIndex(Outputs, o => o.Format == _saved.OutputFormat));
        _outputQuality.Value = _saved.OutputQuality;
        _maxCompression.IsChecked = _saved.MaxCompression;
        _maxSide.SelectedIndex = Math.Max(0, Array.FindIndex(Sizes, s => s.Side == _saved.MaxSide));
        _contactSheet.IsChecked = _saved.ContactSheet;
        _quality.SelectionChanged += (_, _) => { ShowOutputHints(); SavePreferences(); };
        _device.SelectionChanged += (_, _) => { ShowOutputHints(); SavePreferences(); };
        _outputFormat.SelectionChanged += async (_, _) => { ShowOutputHints(); SavePreferences(); await EstimateSizeAsync(); };
        _maxSide.SelectionChanged += async (_, _) => { SavePreferences(); await EstimateSizeAsync(); };
        _maxCompression.IsCheckedChanged += async (_, _) => { SavePreferences(); await EstimateSizeAsync(); };
        _outputQuality.PropertyChanged += async (_, e) => { if (e.Property == Avalonia.Controls.Primitives.RangeBase.ValueProperty) { SavePreferences(); await EstimateSizeAsync(); } };
        _scene.SelectionChanged += (_, _) => ShowOutputHints();
        _contactSheet.IsCheckedChanged += (_, _) => { SavePreferences(); ShowSeriesSummary(); };
        _runSeries.Click += async (_, _) => await RunSeriesAsync();
        ShowOutputHints();
        RebuildSeries();
    }

    private void ShowOutputHints()
    {
        _qualityHint.Text = Qualities[Math.Max(0, _quality.SelectedIndex)].Hint;
        var card = _refine?.DeviceName;
        _deviceNote.Text = Devices[Math.Max(0, _device.SelectedIndex)].Key == "cpu"
            ? "Le processeur est bien plus lent : sur ce type de PC, environ 10 fois plus long qu’avec la carte graphique. L’aperçu en direct reste sur la carte."
            : string.IsNullOrEmpty(card) ? (card is null ? "Carte graphique : détection au démarrage de Blender." : "Aucune carte graphique utilisable : le rendu se fait sur le processeur.")
            : $"Carte détectée : {card}.";
        var format = Outputs[Math.Max(0, _outputFormat.SelectedIndex)].Format;
        _outputQualityField.IsVisible = format is "jpeg" or "webp";
        _maxCompression.IsVisible = format == "png";
        _maxSide.IsEnabled = format != "tiff";
        var transparent = Scenes[_scene.SelectedIndex].Key == "transparent";
        _outputWarning.IsVisible = transparent && format == "jpeg";
        _outputWarning.Text = "Le JPEG n’a pas de transparence : le fond sera blanc. Choisissez PNG ou WebP pour la garder.";
        ShowSeriesSummary();
    }

    /// <summary>Encodes the image on screen with the current options to show the size of the file before saving.</summary>
    private async Task EstimateSizeAsync()
    {
        // The finished render in the window when there is one, else the preview of the page.
        if ((_resultWindow?.Png ?? _shown) is not { } shown) { _sizeEstimate.Text = string.Empty; return; }
        var options = CurrentOutput;
        if (options.Format == "tiff")
        {
            _sizeEstimate.Text = _shownTiff is { } tiff && File.Exists(tiff) ? $"Fichier TIFF 16 bits : {Weight(new FileInfo(tiff).Length)}." : "Le TIFF 16 bits est créé par le rendu final.";
            return;
        }
        try
        {
            var (bytes, size) = await Task.Run(() =>
            {
                var data = RenderOutput.Encode(shown, options);
                using var probe = SkiaSharp.SKBitmap.Decode(data);
                return (data.Length, probe is null ? "" : $"{probe.Width} × {probe.Height}");
            });
            _sizeEstimate.Text = $"Fichier estimé pour l’image affichée : {Weight(bytes)} · {size}.";
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException) { _sizeEstimate.Text = string.Empty; }
    }

    private static string Weight(long bytes)
        => bytes >= 1024 * 1024 ? (bytes / 1048576.0).ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")) + " Mo" : Math.Max(1, bytes / 1024) + " Ko";

    /// <summary>The cameras and colours that can go into a series (called when they change elsewhere on the page).</summary>
    private void RebuildSeries()
    {
        _seriesCameras.Children.Clear();
        foreach (var name in new[] { "Vue actuelle" }.Concat(Presets.Select(p => p.Name)).Concat(SavedCameras.Select(c => c.Name)))
        {
            var box = new CheckBox { Content = name, IsChecked = _seriesCameraNames.Contains(name) };
            box.IsCheckedChanged += (_, _) => { if (box.IsChecked == true) _seriesCameraNames.Add(name); else _seriesCameraNames.Remove(name); ShowSeriesSummary(); };
            _seriesCameras.Children.Add(box);
        }
        _seriesGroups.Children.Clear();
        var current = new Button { Content = "Couleur actuelle seulement", Classes = { "filter-chip" }, Margin = new Thickness(0, 0, 8, 8) };
        current.Click += (_, _) => { _seriesColorKeys.Clear(); MarkSeriesColors(); };
        _seriesGroups.Children.Add(current);
        foreach (var palette in RenderPalettes.All)
        {
            var all = new Button { Content = "Tout " + palette.Name.ToLowerInvariant(), Classes = { "filter-chip" }, Margin = new Thickness(0, 0, 8, 8) };
            all.Click += (_, _) => { foreach (var color in palette.Colors.Where(c => !_hiddenColors.Contains(c.Key))) _seriesColorKeys.Add(color.Key); MarkSeriesColors(); };
            _seriesGroups.Children.Add(all);
        }
        _seriesColors.Children.Clear();
        foreach (var color in SeriesColorChoices())
        {
            var dot = new Button
            {
                Tag = color, Width = 36, Height = 36, MinWidth = 36, MinHeight = 36, Padding = new Thickness(4), Margin = new Thickness(0, 0, 4, 4),
                CornerRadius = new CornerRadius(Ui.Radius.Pill), Background = Brushes.Transparent, BorderThickness = new Thickness(2),
                Content = new Border
                {
                    Width = 24, Height = 24, CornerRadius = new CornerRadius(Ui.Radius.Pill), Background = new SolidColorBrush(Color.Parse(color.Hex)),
                    BorderThickness = new Thickness(1), BorderBrush = (IBrush?)Application.Current?.FindResource("ControlBorderBrush")
                }
            };
            ToolTip.SetTip(dot, color.Label);
            AutomationProperties.SetName(dot, "Série : " + color.Label);
            dot.Click += (_, _) => { if (!_seriesColorKeys.Remove(color.Key)) _seriesColorKeys.Add(color.Key); MarkSeriesColors(); };
            _seriesColors.Children.Add(dot);
        }
        MarkSeriesColors();
    }

    private IEnumerable<RenderColor> SeriesColorChoices()
        => _customColors.Concat(_workshopColors).Concat(RenderPalettes.All.SelectMany(p => p.Colors))
            .Where(c => !_hiddenColors.Contains(c.Key) || _customColors.Contains(c)).DistinctBy(c => c.Key);

    private void MarkSeriesColors()
    {
        var accent = (IBrush?)Application.Current?.FindResource("FocusBorderBrush") ?? Brushes.White;
        foreach (var dot in _seriesColors.Children.OfType<Button>())
            dot.BorderBrush = _seriesColorKeys.Contains(((RenderColor)dot.Tag!).Key) ? accent : Brushes.Transparent;
        ShowSeriesSummary();
    }

    private (List<SavedCamera> Views, List<RenderColor> Colors) SeriesPlan()
    {
        var views = new[] { Current("Vue actuelle") }.Concat(Presets).Concat(SavedCameras).Where(v => _seriesCameraNames.Contains(v.Name)).DistinctBy(v => v.Name).ToList();
        var colors = SeriesColorChoices().Where(c => _seriesColorKeys.Contains(c.Key)).ToList();
        if (colors.Count == 0) colors.Add(new RenderColor("Couleur actuelle", CurrentHex, Finishes[Math.Max(0, _finish.SelectedIndex)].Key));
        return (views, colors);
    }

    private void ShowSeriesSummary()
    {
        if (_quality.SelectedIndex < 0) return;
        var (views, colors) = SeriesPlan();
        var images = views.Count * colors.Count;
        var quality = Qualities[_quality.SelectedIndex];
        var time = TimeSpan.FromSeconds(images * quality.Seconds * (Devices[Math.Max(0, _device.SelectedIndex)].Key == "cpu" && quality.Pass.Engine == "cycles" ? 10 : 1));
        _seriesSummary.Text = images == 0 ? "Cochez au moins une caméra."
            : $"{views.Count} vue{(views.Count > 1 ? "s" : "")} × {colors.Count} couleur{(colors.Count > 1 ? "s" : "")} = {images} image{(images > 1 ? "s" : "")} · environ "
              + (time.TotalMinutes >= 1 ? $"{(int)time.TotalMinutes} min {time.Seconds:00}" : $"{Math.Max(1, (int)time.TotalSeconds)} s") + $" en {quality.Label}"
              + (_contactSheet.IsChecked == true && colors.Count > 1 ? $", plus {views.Count} planche{(views.Count > 1 ? "s" : "")}." : ".");
        _runSeries.IsEnabled = images > 0 && _finalRender is null;
    }

    /// <summary>Renders every chosen camera in every chosen colour, then files the images (and contact sheets) in one go.</summary>
    private async Task RunSeriesAsync()
    {
        if (_modelPath is not { } model || _stlPath is not { } stl) return;
        var (views, colors) = SeriesPlan();
        if (views.Count == 0) { ShowStatus("Cochez au moins une caméra dans la série."); return; }
        var quality = Qualities[_quality.SelectedIndex];
        var options = CurrentOutput;
        var keep = Directory.CreateDirectory(Path.Combine(_work, "serie-" + Guid.NewGuid().ToString("N"))).FullName;
        var rendered = new List<(string Png, string? Tiff, SavedCamera View, RenderColor Color)>();
        var total = views.Count * colors.Count;
        var window = _resultWindow;
        window?.Start(RenderSummary() + $" · série de {total} image{(total > 1 ? "s" : "")}", SuggestedName());
        var finished = await RunExclusiveAsync(async (cancel, progress) =>
        {
            foreach (var view in views)
                foreach (var color in colors)
                {
                    var index = rendered.Count + 1;
                    var output = Path.Combine(_work, $"render-{++_counter}.png");
                    var tiff = options.Format == "tiff" ? Path.Combine(keep, $"{index}.tif") : null;
                    if (ReadSettings(quality.Pass, _mesh!, color, view, tiff) is not { } settings) return false;
                    await SessionFor(quality.Pass).RenderAsync(stl, settings, output, cancel, new Progress<double>(f => progress(f, index, total)));
                    var copy = Path.Combine(keep, $"{index}.png");
                    File.Copy(output, copy);
                    ShowImage(output);
                    window?.ShowPreview(output);
                    window?.Samples($"Image {index}/{total}", quality.Pass.Samples, quality.Pass.Samples);
                    rendered.Add((copy, tiff, view, color));
                    progress(1, index, total);
                }
            return true;
        }, "Série " + quality.Label, (text, fraction) => window?.Progress(text, fraction));
        if (!finished || rendered.Count == 0) { window?.Stopped(_status.Text ?? "Série arrêtée."); return; }
        ShowStatus("Enregistrement de la série…", busy: true);
        try
        {
            var sheet = _contactSheet.IsChecked == true;
            var summary = SeriesSummary(model, quality.Label, options, views, colors);
            var zip = await Task.Run(() => WriteSeries(model, rendered, views.Count, colors.Count, options, keep, sheet, summary));
            var where = SaveFiles is { } save ? await save([zip], model, false) : null;
            ShowStatus(where is null ? "Série terminée, non enregistrée." : $"Série terminée : {rendered.Count} image{(rendered.Count > 1 ? "s" : "")} dans {Path.GetFileName(zip)}.");
            window?.Stopped(_status.Text!);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException) { ShowStatus(exception.Message); window?.Stopped(exception.Message); }
    }

    /// <summary>What the series was made of, written next to the images in the ZIP.</summary>
    private string SeriesSummary(string model, string quality, OutputOptions options, List<SavedCamera> views, List<RenderColor> colors)
    {
        var lines = new List<string>
        {
            "Série de rendus MS3DPRINT Manager", $"Pièce : {Path.GetFileName(model)}", $"Date : {DateTime.Now:dd/MM/yyyy HH:mm}",
            $"Qualité : {quality} · format : {options.Format.ToUpperInvariant()}" + (options.MaxSide > 0 ? $" · {options.MaxSide} px" : ""),
            $"Ambiance : {Scenes[_scene.SelectedIndex].Label} · cadre : {Formats[_format.SelectedIndex].Label}", "", "Vues :"
        };
        lines.AddRange(views.Select(v => "  - " + v.Name));
        lines.Add("");
        lines.Add("Couleurs :");
        lines.AddRange(colors.Select(c => $"  - {c.Label} ({c.Hex}, {Finishes.FirstOrDefault(f => f.Key == c.Finish).Label ?? c.Finish})"));
        if (_zones.Count > 0)
        {
            lines.Add("");
            lines.Add("Zones peintes (identiques sur toutes les images) :");
            lines.AddRange(_zones.Select(z => $"  - {z.Label} ({z.Hex})"));
        }
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Packs the series in one ZIP, sorted: a folder per view when there are several (one image per colour inside), the contact
    /// sheets and a summary of the settings at the top.
    /// </summary>
    private static string WriteSeries(string model, List<(string Png, string? Tiff, SavedCamera View, RenderColor Color)> rendered, int viewCount, int colorCount,
        OutputOptions options, string folder, bool contactSheet, string summary)
    {
        static string Clean(string text) => string.Concat(text.Split(Path.GetInvalidFileNameChars())).Trim();
        var stem = Clean(Path.GetFileNameWithoutExtension(model));
        var zip = Path.Combine(folder, $"{stem} - série {DateTime.Now:yyyy-MM-dd HHmm}.zip");
        using (var archive = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create))
        {
            void Add(string entry, byte[] bytes)
            {
                using var target = archive.CreateEntry(entry, System.IO.Compression.CompressionLevel.NoCompression).Open();
                target.Write(bytes);
            }
            foreach (var (png, tiff, view, color) in rendered)
            {
                var name = colorCount > 1 ? Clean(color.Label) : stem + (viewCount > 1 ? "" : " - rendu");
                var folderName = viewCount > 1 ? Clean(view.Name) + "/" : "";
                if (tiff is not null && File.Exists(tiff)) Add(folderName + name + ".tif", File.ReadAllBytes(tiff));
                else Add(folderName + name + (options.Format == "tiff" ? ".png" : options.Extension), RenderOutput.Encode(png, options.Format == "tiff" ? options with { Format = "png" } : options));
            }
            if (contactSheet && colorCount > 1)
                foreach (var group in rendered.GroupBy(r => r.View.Name))
                {
                    using var sheet = RenderOutput.ContactSheet(group.Select(r => (r.Png, r.Color.Label)).ToList());
                    var sheetOptions = options.Format == "tiff" ? options with { Format = "png" } : options with { MaxSide = 0 };
                    Add("planche couleurs" + (viewCount > 1 ? " - " + Clean(group.Key) : "") + (sheetOptions.Format == "png" ? ".png" : sheetOptions.Extension), RenderOutput.Encode(sheet, sheetOptions));
                }
            Add("récapitulatif.txt", System.Text.Encoding.UTF8.GetBytes(summary));
        }
        return zip;
    }
}
