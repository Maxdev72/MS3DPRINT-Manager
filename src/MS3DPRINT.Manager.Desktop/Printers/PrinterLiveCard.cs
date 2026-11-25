using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using LibVLCSharp.Shared;
using MS3DPRINT.Manager.Core.Printers;

namespace MS3DPRINT.Manager.Desktop.Printers;

/// <summary>
/// Live card of a Bambu Lab printer on the Surveillance page: camera, state, progress, temperatures, AMS and alerts.
/// Read only. The status link runs for as long as the card exists (end-of-print notifications); the camera only while
/// the page is shown.
/// </summary>
public sealed class PrinterLiveCard : Border, IDisposable
{
    private readonly CancellationTokenSource _cancel = new();
    private readonly Image _camera = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock _cameraNote = new() { Text = "Connexion à la caméra…", Foreground = Brushes.Gainsboro, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center, Margin = new(24) };
    private readonly Border _badge = new() { Classes = { "badge" }, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _badgeText = new() { Text = "Connexion…" };
    private readonly Button _alerts = new() { Padding = new(0), MinHeight = 0, MinWidth = 0, Background = Brushes.Transparent, BorderThickness = new(0), VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
    private readonly TextBlock _alertsText = new();
    private readonly StackPanel _alertsList = new() { Spacing = 12, Width = 380 };
    // Two lines reserved: the file name, or the reason the printer is unreachable.
    private readonly TextBlock _job = new() { TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, Height = 40 };
    private readonly List<Image> _mirrors = [];
    private readonly StackPanel _progressBlock = new() { Spacing = 8 };
    private readonly Run _percent = new(), _percentUnit = new(" %") { FontSize = Ui.Text.Section };
    private readonly TextBlock _finishLabel = new() { Text = "FIN PRÉVUE", Classes = { "overline" }, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly TextBlock _finish = new() { FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly TextBlock _remaining = new() { Classes = { "caption" }, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly Border _track = new() { Height = 12, CornerRadius = new(Ui.Radius.Pill) };
    private readonly Border _fill = new() { HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new(Ui.Radius.Pill) };
    private double _fraction;
    private CancellationTokenSource? _cameraCancel;
    private readonly TextBlock _layers = new() { Classes = { "caption" } };
    private readonly Grid _temperatures = new() { ColumnDefinitions = new("*,*,*"), ColumnSpacing = 8 };
    private readonly StackPanel _amsBlock = new() { Spacing = 8 };
    private readonly WrapPanel _trays = new();
    private VlcCamera? _video;
    private BambuLink.RtspRelay? _relay;

    public PrinterProfile Printer { get; }
    public PrinterConnection Connection { get; }
    /// <summary>Last state received, or null while offline; <see cref="Changed"/> follows it.</summary>
    public BambuStatus? Status { get; private set; }
    public event Action? Changed;

    /// <param name="connect">False only to preview the card without a printer.</param>
    public PrinterLiveCard(PrinterProfile printer, PrinterConnection connection, bool connect = true)
    {
        Printer = printer;
        Connection = connection;
        Classes.Add("card-raised");
        // Follows the theme; the class alone left the card invisible on the page background.
        Bind(BackgroundProperty, this.GetResourceObservable("RaisedSurfaceBrush"));
        Bind(BorderBrushProperty, this.GetResourceObservable("SubtleBorderBrush"));
        BorderThickness = new(1);
        CornerRadius = new(Ui.Radius.Card);
        Padding = new(0);
        ClipToBounds = true;
        Margin = new(0, 0, 16, 16);
        AutomationProperties.SetName(_camera, "Caméra de " + printer.Name);
        _badge.Child = _badgeText;
        _alerts.Content = new Border { Classes = { "badge", "danger" }, Child = _alertsText };
        _alerts.Flyout = new Flyout { Content = new ScrollViewer { MaxHeight = 420, Content = _alertsList } };
        ToolTip.SetTip(_alerts, "Lire les alertes de l’imprimante");
        _track.Bind(BackgroundProperty, _track.GetResourceObservable("NeutralSoftBrush"));
        _track.Child = _fill;
        _track.SizeChanged += (_, _) => SizeFill();
        var name = new TextBlock { Text = printer.Name, Classes = { "card-title" }, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(_alerts, 1);
        Grid.SetColumn(_badge, 2);
        var fullScreen = new Button { Content = Icons.Glyph("IconExpand"), Classes = { "icon-action" }, Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new(8) };
        ToolTip.SetTip(fullScreen, "Plein écran (double-clic sur l’image)");
        AutomationProperties.SetName(fullScreen, "Caméra de " + printer.Name + " en plein écran");
        fullScreen.Click += (_, _) => OpenFullScreen();
        // 16:9 like the printers' cameras, whatever the column width.
        var cameraFrame = new Border { Background = Brushes.Black, Child = new Grid { Children = { _camera, _cameraNote, fullScreen } } };
        cameraFrame.SizeChanged += (_, e) => { if (Math.Abs(e.NewSize.Width * 9 / 16 - cameraFrame.Height) > 0.5) cameraFrame.Height = e.NewSize.Width * 9 / 16; };
        cameraFrame.Height = 248;
        cameraFrame.DoubleTapped += (_, _) => OpenFullScreen();
        var percent = new TextBlock { FontSize = Ui.Text.Page, FontWeight = FontWeight.SemiBold, Inlines = new InlineCollection { _percent, _percentUnit } };
        percent.VerticalAlignment = VerticalAlignment.Bottom;
        var finish = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Children = { _finishLabel, _finish, _remaining } };
        Grid.SetColumn(finish, 1);
        _progressBlock.Children.Add(new Grid { ColumnDefinitions = new("*,Auto"), Children = { percent, finish } });
        _progressBlock.Children.Add(_track);
        _progressBlock.Children.Add(_layers);
        _amsBlock.Children.Add(new TextBlock { Text = "AMS", Classes = { "overline" } });
        _amsBlock.Children.Add(_trays);
        Child = new StackPanel
        {
            Children =
            {
                cameraFrame,
                new StackPanel
                {
                    Margin = new(16, 12, 16, 16), Spacing = 12,
                    Children =
                    {
                        // Every section keeps a fixed height so the rows line up from one card to the next.
                        new StackPanel { Spacing = 8, Children = { new Grid { ColumnDefinitions = new("*,Auto,Auto"), ColumnSpacing = 8, Children = { name, _alerts, _badge } }, _job } },
                        _progressBlock, _temperatures, _amsBlock
                    }
                }
            }
        };
        Render(null, "Connexion…", null);
        if (!connect) return;
        var token = _cancel.Token;
        _ = Task.Run(() => BambuLink.WatchStatusAsync(connection, s => Dispatcher.UIThread.Post(() => Show(s)), m => Dispatcher.UIThread.Post(() => ShowOffline(m)), token));
    }

    /// <summary>Opens or closes the camera stream; the status link is not affected.</summary>
    public void SetCameraActive(bool active)
    {
        if (active == _cameraCancel is not null || (active && _cancel.IsCancellationRequested)) return;
        if (!active)
        {
            _cameraCancel!.Cancel();
            _cameraCancel = null;
            StopVideo();
            var last = _camera.Source;
            _camera.Source = null;
            if (last is Bitmap bitmap and not WriteableBitmap) bitmap.Dispose();
            _cameraNote.Text = "Connexion à la caméra…";
            _cameraNote.IsVisible = true;
            return;
        }
        _cameraCancel = CancellationTokenSource.CreateLinkedTokenSource(_cancel.Token);
        StartCamera(Printer.Model, _cameraCancel.Token);
    }

    private void StartCamera(string model, CancellationToken token)
    {
        if (BambuLink.HasJpegCamera(model))
        {
            _ = Task.Run(() => BambuLink.WatchJpegCameraAsync(Connection, jpeg =>
            {
                Bitmap frame;
                try { using var stream = new MemoryStream(jpeg); frame = new Bitmap(stream); }
                catch (Exception) { return; } // a damaged frame is skipped, the next one follows
                Dispatcher.UIThread.Post(() => ShowFrame(frame));
            }, m => Dispatcher.UIThread.Post(() => ShowCameraNote(m)), token));
            return;
        }
        if (VlcCamera.Unavailable is { } reason) { ShowCameraNote(reason); return; }
        StartVideo();
    }

    private void StartVideo()
    {
        if (_cameraCancel is null) return;
        StopVideo();
        try
        {
            _relay = new BambuLink.RtspRelay(Connection);
            _video = new VlcCamera(_relay.Url, ShowFrame, () =>
            {
                ShowCameraNote("Caméra injoignable, nouvelle tentative…");
                StopVideo();
                DispatcherTimer.RunOnce(StartVideo, TimeSpan.FromSeconds(10));
            });
        }
        catch (Exception exception) when (exception is InvalidOperationException or VLCException or UriFormatException or System.Net.Sockets.SocketException)
        { ShowCameraNote("Caméra indisponible : " + exception.Message); }
    }

    private void StopVideo()
    {
        _video?.Dispose(); _video = null;
        _relay?.Dispose(); _relay = null;
    }

    private void ShowFrame(Bitmap frame)
    {
        if (_cameraCancel is null) { if (frame is not WriteableBitmap) frame.Dispose(); return; }
        _cameraNote.IsVisible = false;
        if (ReferenceEquals(_camera.Source, frame)) { _camera.InvalidateVisual(); foreach (var mirror in _mirrors) mirror.InvalidateVisual(); return; }
        var previous = _camera.Source as Bitmap;
        _camera.Source = frame;
        foreach (var mirror in _mirrors) mirror.Source = frame;
        if (previous is not null and not WriteableBitmap && !ReferenceEquals(previous, frame)) previous.Dispose();
    }

    /// <summary>The camera alone on the whole screen; Échap, a click or a double-click closes it.</summary>
    private void OpenFullScreen()
    {
        var image = new Image { Stretch = Stretch.Uniform, Source = _camera.Source };
        AutomationProperties.SetName(image, "Caméra de " + Printer.Name);
        var caption = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)), CornerRadius = new(Ui.Radius.Control), Padding = new(12, 8), Margin = new(24),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock { Foreground = Brushes.White, Text = Printer.Name + " · Échap pour fermer" }
        };
        var window = new Window
        {
            Title = "Caméra · " + Printer.Name, Background = Brushes.Black, WindowState = WindowState.FullScreen,
            Content = new Grid { Children = { image, caption } }
        };
        _mirrors.Add(image);
        window.Closed += (_, _) => _mirrors.Remove(image);
        window.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Escape) window.Close(); };
        window.PointerPressed += (_, _) => window.Close();
        window.Show();
    }

    private void ShowCameraNote(string message)
    {
        _cameraNote.Text = BambuLink.HasJpegCamera(Printer.Model) ? message : message + Environment.NewLine + "Vérifiez que « LAN Mode Liveview » est activé sur l’imprimante.";
        _cameraNote.IsVisible = true;
    }

    internal void ShowOffline(string message)
    {
        Render(null, "Hors ligne", message);
        Changed?.Invoke();
    }

    internal void Show(BambuStatus s)
    {
        Render(s, s.StateLabel, null);
        Changed?.Invoke();
    }

    /// <summary>Every state keeps the same layout; values that are not known show a dash.</summary>
    private void Render(BambuStatus? s, string badge, string? message)
    {
        Status = s;
        SetBadge(badge, s?.State switch { "RUNNING" or "PREPARE" or "SLICING" => "info", "PAUSE" => "warning", "FAILED" => "danger", "FINISH" => "success", _ => null });
        var alerts = s?.Alerts ?? [];
        _alertsText.Text = $"{alerts.Count} alerte{(alerts.Count > 1 ? "s" : "")}";
        _alerts.IsVisible = alerts.Count > 0;
        AutomationProperties.SetName(_alerts, _alertsText.Text + " : lire le détail");
        _alertsList.Children.Clear();
        foreach (var alert in alerts.OrderByDescending(a => a.Since))
            _alertsList.Children.Add(new StackPanel { Spacing = 4, Children =
            {
                new TextBlock { Text = alert.Severity.ToUpperInvariant() + (alert.Since is { } since ? " · depuis le " + since.ToString("d MMMM à HH:mm", French) : ""), Classes = { "overline" } },
                new TextBlock { Text = BambuHmsCatalog.Describe(alert, BambuHmsTexts.Current), TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = alert.DisplayCode, Classes = { "caption" } }
            } });

        var printing = s?.IsPrinting == true;
        var finished = s?.State == "FINISH";
        var showJob = (printing || finished) && s?.JobName is not null;
        _job.Text = message ?? (showJob ? s!.JobName : "Aucune impression en cours");
        _job.Classes.Set("muted", !printing);

        var percent = printing ? s!.Percent ?? 0 : finished ? 100 : (int?)null;
        _percent.Text = percent?.ToString(CultureInfo.CurrentCulture) ?? "—";
        _percentUnit.Text = percent is null ? "" : " %";
        _fraction = (percent ?? 0) / 100.0;
        SetFillColors(s?.State);
        SizeFill();
        var minutes = printing ? s!.RemainingMinutes ?? 0 : 0;
        // Empty lines keep their height (non-breaking space) so every card's bar sits at the same level.
        _finishLabel.Text = minutes > 0 ? "FIN PRÉVUE" : " ";
        _finish.Text = minutes > 0 ? FinishTime(DateTime.Now.AddMinutes(minutes)) : finished ? "Terminée" : " ";
        _remaining.Text = minutes > 0 ? "dans " + (minutes >= 60 ? $"{minutes / 60} h {minutes % 60:00}" : $"{minutes} min") : " ";
        _layers.Text = (printing || finished) && s!.Layer is { } layer && s.TotalLayers is > 0 ? $"Couche {layer} sur {s.TotalLayers}" : "Couche —";

        _temperatures.Children.Clear();
        var tiles = new[] { Temperature("BUSE", s?.Nozzle, s?.NozzleTarget), Temperature("PLATEAU", s?.Bed, s?.BedTarget), Temperature("ENCEINTE", s?.Chamber is > 0 ? s.Chamber : null, null) };
        for (var i = 0; i < tiles.Length; i++) { Grid.SetColumn(tiles[i], i); _temperatures.Children.Add(tiles[i]); }

        _trays.Children.Clear();
        foreach (var tray in s?.Trays ?? [])
        {
            var color = Color.TryParse("#" + tray.ColorHex, out var parsed) ? parsed : Colors.Gray;
            var chip = new StackPanel { Spacing = 4, Width = 52, Margin = new(0, 0, 8, 0), Children =
            {
                new Border { Width = 28, Height = 28, CornerRadius = new(Ui.Radius.Pill), Background = new SolidColorBrush(color), BorderBrush = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)), BorderThickness = new(1), HorizontalAlignment = HorizontalAlignment.Center },
                new TextBlock { Text = tray.Type, Classes = { "caption" }, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }
            } };
            AutomationProperties.SetName(chip, $"{tray.Type} #{tray.ColorHex}");
            _trays.Children.Add(chip);
        }
        // Same height as a row of spools, so empty cards keep the layout of the others.
        if (_trays.Children.Count == 0)
            _trays.Children.Add(new TextBlock { Text = s is null ? "—" : "Aucune bobine détectée", Classes = { "caption" }, Height = 46, VerticalAlignment = VerticalAlignment.Center });
    }

    private void SetBadge(string text, string? tone)
    {
        _badgeText.Text = text;
        foreach (var t in new[] { "info", "warning", "danger", "success" }) _badge.Classes.Set(t, t == tone);
    }

    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>"aujourd’hui à 14:32", "demain à 07:46" or "vendredi 28 novembre à 09:10".</summary>
    public static string FinishTime(DateTime end, DateTime? now = null)
    {
        var today = (now ?? DateTime.Now).Date;
        var time = end.ToString("HH:mm", French);
        return end.Date == today ? "aujourd’hui à " + time : end.Date == today.AddDays(1) ? "demain à " + time : end.ToString("dddd d MMMM", French) + " à " + time;
    }

    private void SizeFill()
    {
        var width = _track.Bounds.Width * _fraction;
        // Below the bar's height, a rounded fill would draw as a dot: start it at one full height.
        _fill.Width = _fraction <= 0 ? 0 : Math.Max(width, _track.Height);
    }

    /// <summary>Blue to cyan while printing, amber in pause, red on failure, green when done.</summary>
    private void SetFillColors(string? state)
    {
        var (from, to) = state switch
        {
            "PAUSE" => ("WarningBrush", "WarningBrush"), "FAILED" => ("DangerBrush", "DangerBrush"), "FINISH" => ("SuccessBrush", "SuccessBrush"),
            _ => ("AccentBrush", "CyanBrush")
        };
        Color Of(string key) => Application.Current?.FindResource(key) is ISolidColorBrush brush ? brush.Color : Colors.DodgerBlue;
        _fill.Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Of(from), 0), new GradientStop(Of(to), 1) }
        };
        var glow = Of(to);
        _fill.BoxShadow = new BoxShadows(new BoxShadow { Blur = 12, Color = Color.FromArgb(110, glow.R, glow.G, glow.B) });
    }

    private static Control Temperature(string label, double? current, double? target)
    {
        var value = new TextBlock { FontWeight = FontWeight.SemiBold, Inlines = new InlineCollection
        {
            new Run(current is { } c ? c.ToString("0", CultureInfo.CurrentCulture) + " °C" : "—"),
            new Run(current is not null && target is > 0 ? " / " + target.Value.ToString("0", CultureInfo.CurrentCulture) : "") { FontWeight = FontWeight.Normal, Foreground = Application.Current?.FindResource("MutedTextBrush") as IBrush }
        } };
        var tile = new Border
        {
            CornerRadius = new(Ui.Radius.Control), Padding = new(12, 8),
            Child = new StackPanel { Spacing = 2, Children = { new TextBlock { Text = label, Classes = { "overline" } }, value } }
        };
        tile.Bind(BackgroundProperty, tile.GetResourceObservable("NeutralSoftBrush"));
        return tile;
    }

    public void Dispose()
    {
        SetCameraActive(false);
        _cancel.Cancel();
    }
}
