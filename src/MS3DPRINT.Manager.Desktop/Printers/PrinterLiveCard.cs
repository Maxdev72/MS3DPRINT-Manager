using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using LibVLCSharp.Shared;
using MS3DPRINT.Manager.Core.Printers;

namespace MS3DPRINT.Manager.Desktop.Printers;

/// <summary>Live card of a Bambu Lab printer on the Printers page: state, progress, temperatures, AMS and camera. Read only.</summary>
public sealed class PrinterLiveCard : Border, IDisposable
{
    private readonly CancellationTokenSource _cancel = new();
    private readonly Image _camera = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock _cameraNote = new() { Text = "Connexion à la caméra…", Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center, Margin = new(12) };
    private readonly TextBlock _state = new() { Text = "Connexion…", FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _job = new() { TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 100, IsVisible = false };
    private readonly TextBlock _detail = new() { Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private readonly TextBlock _temperatures = new() { Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private readonly WrapPanel _trays = new() { IsVisible = false };
    private VlcCamera? _video;
    private BambuLink.RtspRelay? _relay;

    public PrinterProfile Printer { get; }
    public PrinterConnection Connection { get; }

    public PrinterLiveCard(PrinterProfile printer, PrinterConnection connection)
    {
        Printer = printer;
        Connection = connection;
        Classes.Add("card");
        Width = 440;
        Margin = new(0, 0, 12, 12);
        AutomationProperties.SetName(_camera, "Caméra de " + printer.Name);
        Child = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = printer.Name, Classes = { "card-title" }, TextWrapping = TextWrapping.Wrap },
                new Border { Height = 248, Background = Brushes.Black, CornerRadius = new(6), ClipToBounds = true, Child = new Grid { Children = { _camera, _cameraNote } } },
                _state, _job, _progress, _detail, _temperatures, _trays
            }
        };
        var token = _cancel.Token;
        _ = Task.Run(() => BambuLink.WatchStatusAsync(connection, s => Dispatcher.UIThread.Post(() => Show(s)), m => Dispatcher.UIThread.Post(() => ShowOffline(m)), token));
        StartCamera(printer.Model, token);
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
        if (_cancel.IsCancellationRequested) return;
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
        if (_cancel.IsCancellationRequested) { if (frame is not WriteableBitmap) frame.Dispose(); return; }
        _cameraNote.IsVisible = false;
        if (ReferenceEquals(_camera.Source, frame)) { _camera.InvalidateVisual(); return; }
        var previous = _camera.Source as Bitmap;
        _camera.Source = frame;
        if (previous is not null and not WriteableBitmap) previous.Dispose();
    }

    private void ShowCameraNote(string message)
    {
        _cameraNote.Text = BambuLink.HasJpegCamera(Printer.Model) ? message : message + Environment.NewLine + "Vérifiez que « LAN Mode Liveview » est activé sur l’imprimante.";
        _cameraNote.IsVisible = true;
    }

    private void ShowOffline(string message)
    {
        _state.Text = message;
        _job.IsVisible = _progress.IsVisible = _detail.IsVisible = _temperatures.IsVisible = _trays.IsVisible = false;
    }

    private void Show(BambuStatus s)
    {
        _state.Text = s.ErrorCount > 0 ? $"{s.StateLabel} · {s.ErrorCount} alerte{(s.ErrorCount > 1 ? "s" : "")} sur l’écran de l’imprimante" : s.StateLabel;
        _job.Text = s.JobName ?? "";
        _job.IsVisible = s.IsPrinting && s.JobName is not null;
        _progress.Value = s.Percent ?? 0;
        _progress.IsVisible = s.IsPrinting;
        var parts = new List<string>();
        if (s.Percent is { } percent) parts.Add(percent + " %");
        if (s.Layer is { } layer && s.TotalLayers is > 0) parts.Add($"couche {layer}/{s.TotalLayers}");
        if (s.RemainingMinutes is > 0 and var minutes) parts.Add("reste " + (minutes >= 60 ? $"{minutes / 60} h {minutes % 60:00}" : $"{minutes} min"));
        _detail.Text = string.Join(" · ", parts);
        _detail.IsVisible = s.IsPrinting && parts.Count > 0;
        var temperatures = new List<string>();
        if (s.Nozzle is { } nozzle) temperatures.Add("Buse " + Degrees(nozzle, s.NozzleTarget));
        if (s.Bed is { } bed) temperatures.Add("Plateau " + Degrees(bed, s.BedTarget));
        if (s.Chamber is > 0 and var chamber) temperatures.Add("Enceinte " + Degrees(chamber, null));
        _temperatures.Text = string.Join(" · ", temperatures);
        _temperatures.IsVisible = temperatures.Count > 0;
        _trays.Children.Clear();
        foreach (var tray in s.Trays)
        {
            var color = Color.TryParse("#" + tray.ColorHex, out var parsed) ? parsed : Colors.Gray;
            var chip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new(0, 0, 10, 4), Children =
            {
                new Border { Width = 12, Height = 12, CornerRadius = new(6), Background = new SolidColorBrush(color), BorderBrush = Brushes.Gray, BorderThickness = new(1), VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = tray.Type, Classes = { "muted" } }
            } };
            AutomationProperties.SetName(chip, $"{tray.Type} #{tray.ColorHex}");
            _trays.Children.Add(chip);
        }
        _trays.IsVisible = s.Trays.Count > 0;
    }

    private static string Degrees(double current, double? target) =>
        target is > 0 ? $"{current.ToString("0", CultureInfo.CurrentCulture)}/{target.Value.ToString("0", CultureInfo.CurrentCulture)} °C" : $"{current.ToString("0", CultureInfo.CurrentCulture)} °C";

    public void Dispose()
    {
        _cancel.Cancel();
        StopVideo();
    }
}
