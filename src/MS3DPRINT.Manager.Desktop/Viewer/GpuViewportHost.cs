using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace MS3DPRINT.Manager.Desktop.Viewer;

/// <summary>
/// Hosts the OpenGL view and owns the mouse input: a GL surface is not hit-testable by itself, so the transparent
/// host receives the pointer. Left drag rotates, right/middle drag pans, wheel zooms, a short left click is reported.
/// </summary>
public sealed class GpuViewportHost : Panel, IMeshViewport
{
    private readonly GpuMeshViewport _gpu = new();
    private Point _last, _pressPoint;
    private bool _rotating, _panning, _moved;

    public GpuViewportHost()
    {
        Background = Brushes.Transparent;
        Focusable = true;
        ClipToBounds = true;
        Children.Add(_gpu);
        _gpu.Failed += message => Failed?.Invoke(message);
        _gpu.CameraChanged += () => CameraChanged?.Invoke();
    }

    public Control Visual => this;
    public ViewCamera Camera => _gpu.Camera;
    public bool Wireframe { get => _gpu.Wireframe; set => _gpu.Wireframe = value; }
    public Color ModelColor { get => _gpu.ModelColor; set => _gpu.ModelColor = value; }
    public IReadOnlyList<Vector3>? Markers { get => _gpu.Markers; set => _gpu.Markers = value; }
    public ClipPlane? CutPlane { get => _gpu.CutPlane; set => _gpu.CutPlane = value; }
    public bool WindingInverted { get => _gpu.WindingInverted; set => _gpu.WindingInverted = value; }
    public MeshModel? SecondMesh { get => _gpu.SecondMesh; set => _gpu.SecondMesh = value; }
    public Color SecondColor { get => _gpu.SecondColor; set => _gpu.SecondColor = value; }
    public bool ShowFirst { get => _gpu.ShowFirst; set => _gpu.ShowFirst = value; }
    public bool ShowSecond { get => _gpu.ShowSecond; set => _gpu.ShowSecond = value; }
    public Vector3 SecondOffset { get => _gpu.SecondOffset; set => _gpu.SecondOffset = value; }
    public bool IsReady => _gpu.IsReady;
    public event Action<string>? Failed;
    public event Action? CameraChanged;
    public event Action<Point>? Clicked;

    public Task<CapturedImage?> CaptureAsync() => _gpu.CaptureAsync();
    public void SetMesh(MeshModel? mesh) => _gpu.SetMesh(mesh);
    public void ResetView() => _gpu.ResetView();
    public void RequestRender(bool interactive) => _gpu.RequestRender(interactive);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        _last = _pressPoint = point.Position;
        _moved = false;
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
        if (Math.Abs(position.X - _pressPoint.X) + Math.Abs(position.Y - _pressPoint.Y) > 4) _moved = true;
        var camera = Camera;
        if (_rotating)
        {
            camera.Yaw += dx * 0.01;
            camera.Pitch = Math.Clamp(camera.Pitch + dy * 0.01, -Math.PI / 2 + 0.02, Math.PI / 2 - 0.02);
        }
        else
        {
            var scale = 760 / Math.Max(1, Bounds.Height);
            camera.PanX += dx * scale;
            camera.PanY += dy * scale;
        }
        RequestRender(true);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var wasClick = _rotating && !_moved;
        _rotating = _panning = false;
        e.Pointer.Capture(null);
        if (wasClick) Clicked?.Invoke(e.GetPosition(this));
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        Camera.Zoom = Math.Clamp(Camera.Zoom * Math.Pow(1.12, e.Delta.Y), 0.05, 40);
        RequestRender(true);
        e.Handled = true;
    }
}
