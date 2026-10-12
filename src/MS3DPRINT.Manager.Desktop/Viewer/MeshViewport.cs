using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace MS3DPRINT.Manager.Desktop.Viewer;

/// <summary>Interactive 3D view: left drag rotates, right/middle drag pans, wheel zooms.</summary>
public sealed class MeshViewport : Control, IMeshViewport
{
    private const uint BackgroundColor = 0xFF101923;
    private readonly DispatcherTimer _settle = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private MeshModel? _mesh;
    private WriteableBitmap? _bitmap;
    private CancellationTokenSource? _render;
    private int _version;
    private Point _last;
    private bool _rotating;
    private bool _panning;

    public MeshViewport()
    {
        ClipToBounds = true;
        Focusable = true;
        _settle.Tick += (_, _) => { _settle.Stop(); RequestRender(interactive: false); };
    }

    public Control Visual => this;
    public ViewCamera Camera { get; } = new();
    public bool Wireframe { get; set; }
    public Color ModelColor { get; set; } = Color.FromRgb(0x58, 0xA6, 0xFF);
    public IReadOnlyList<Vector3>? Markers { get; set; }

    public void SetMesh(MeshModel? mesh)
    {
        _mesh = mesh;
        Markers = null;
        Camera.Reset();
        RequestRender(interactive: false);
    }

    public void ResetView()
    {
        Camera.Reset();
        RequestRender(interactive: false);
    }

    public void RequestRender(bool interactive)
    {
        if (interactive) { _settle.Stop(); _settle.Start(); }
        var mesh = _mesh;
        if (mesh is null || Bounds.Width < 2 || Bounds.Height < 2) { InvalidateVisual(); return; }
        _render?.Cancel();
        var cancellation = _render = new CancellationTokenSource();
        var version = ++_version;
        var scale = (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1) * (interactive ? 0.5 : 1);
        var width = Math.Max(2, (int)(Bounds.Width * scale));
        var height = Math.Max(2, (int)(Bounds.Height * scale));
        var options = new RenderOptions(width, height, Wireframe, 0xFF000000u | ((uint)ModelColor.R << 16) | ((uint)ModelColor.G << 8) | ModelColor.B, BackgroundColor);
        var camera = new ViewCamera { Yaw = Camera.Yaw, Pitch = Camera.Pitch, Zoom = Camera.Zoom, PanX = Camera.PanX, PanY = Camera.PanY };
        var markers = Markers;
        _ = Task.Run(() =>
        {
            try { return SoftwareRenderer.Render(mesh, camera, options, markers, cancellation.Token); }
            catch (OperationCanceledException) { return null; }
        }).ContinueWith(task =>
        {
            if (task.Result is not { } pixels || version != _version) return;
            Dispatcher.UIThread.Post(() => ShowFrame(pixels, width, height));
        }, TaskScheduler.Default);
    }

    private void ShowFrame(uint[] pixels, int width, int height)
    {
        if (_bitmap is null || _bitmap.PixelSize.Width != width || _bitmap.PixelSize.Height != height)
            _bitmap = new WriteableBitmap(new PixelSize(width, height), new Avalonia.Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using (var buffer = _bitmap.Lock())
        {
            var ints = (int[])(object)pixels;
            if (buffer.RowBytes == width * 4) Marshal.Copy(ints, 0, buffer.Address, ints.Length);
            else
                for (var row = 0; row < height; row++)
                    Marshal.Copy(ints, row * width, buffer.Address + row * buffer.RowBytes, width);
        }
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var area = new Rect(Bounds.Size);
        context.FillRectangle(new SolidColorBrush(Color.FromUInt32(BackgroundColor)), area);
        if (_bitmap is not null && _mesh is not null)
            context.DrawImage(_bitmap, new Rect(_bitmap.Size), area);
        if (_mesh is not null) DrawAxes(context);
    }

    private void DrawAxes(DrawingContext context)
    {
        var origin = new Point(Bounds.Width - 54, Bounds.Height - 54);
        (string Label, Vector3 Direction, Color Color)[] axes =
        [
            ("X", Vector3.UnitX, Color.FromRgb(0xFF, 0x5C, 0x5C)),
            ("Y", Vector3.UnitY, Color.FromRgb(0x5C, 0xD6, 0x7A)),
            ("Z", Vector3.UnitZ, Color.FromRgb(0x5C, 0x9C, 0xFF))
        ];
        foreach (var (label, direction, color) in axes)
        {
            var (x, y) = SoftwareRenderer.ProjectDirection(Camera, direction);
            var end = new Point(origin.X + x * 32, origin.Y + y * 32);
            context.DrawLine(new Pen(new SolidColorBrush(color), 2), origin, end);
            var text = new FormattedText(label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface.Default, 12, new SolidColorBrush(color));
            context.DrawText(text, new Point(end.X + (x >= 0 ? 3 : -11), end.Y - 7));
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        _last = point.Position;
        _rotating = point.Properties.IsLeftButtonPressed;
        _panning = point.Properties.IsRightButtonPressed || point.Properties.IsMiddleButtonPressed;
        if (_rotating || _panning) { e.Pointer.Capture(this); e.Handled = true; }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_rotating && !_panning) return;
        var position = e.GetPosition(this);
        var dx = position.X - _last.X; var dy = position.Y - _last.Y;
        _last = position;
        if (_rotating)
        {
            Camera.Yaw -= dx * 0.01;
            Camera.Pitch = Math.Clamp(Camera.Pitch + dy * 0.01, -Math.PI / 2 + 0.02, Math.PI / 2 - 0.02);
        }
        else
        {
            var scale = 760 / Math.Max(1, Bounds.Height);
            Camera.PanX += dx * scale;
            Camera.PanY += dy * scale;
        }
        RequestRender(interactive: true);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _rotating = _panning = false;
        e.Pointer.Capture(null);
        RequestRender(interactive: false);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        Camera.Zoom = Math.Clamp(Camera.Zoom * Math.Pow(1.12, e.Delta.Y), 0.05, 40);
        RequestRender(interactive: true);
        e.Handled = true;
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        RequestRender(interactive: false);
    }
}
