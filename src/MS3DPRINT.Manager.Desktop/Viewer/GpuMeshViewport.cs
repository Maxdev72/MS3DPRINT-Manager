using System.Numerics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;

namespace MS3DPRINT.Manager.Desktop.Viewer;

/// <summary>What the viewer page needs from a 3D view, so the GPU view and the CPU fallback are interchangeable.</summary>
public interface IMeshViewport
{
    Control Visual { get; }
    ViewCamera Camera { get; }
    bool Wireframe { get; set; }
    Color ModelColor { get; set; }
    IReadOnlyList<Vector3>? Markers { get; set; }
    void SetMesh(MeshModel? mesh);
    void ResetView();
    void RequestRender(bool interactive);
    /// <summary>Raised on the UI thread when the camera moved.</summary>
    event Action? CameraChanged;
    /// <summary>A left click that was not a drag, in the control's own coordinates.</summary>
    event Action<Point>? Clicked;
}

/// <summary>
/// Hardware-accelerated 3D view (OpenGL ES through ANGLE, or desktop OpenGL): the mesh stays on the graphics card,
/// is drawn with a depth buffer, flat shading derived on the GPU and 4x multisampling, so rotation stays smooth
/// on meshes of millions of triangles. Left drag rotates, right/middle drag pans, wheel zooms.
/// </summary>
public sealed class GpuMeshViewport : OpenGlControlBase, IMeshViewport
{
    private const double FieldOfView = 40 * Math.PI / 180;
    private const int MaxWireTriangles = 400_000;
    private const float BackgroundR = 0x10 / 255f, BackgroundG = 0x19 / 255f, BackgroundB = 0x23 / 255f;

    private GlApi? _gl;
    private int _solidProgram, _pointProgram;
    private int _meshVao, _wireVao, _pointVao;
    private int _positionBuffer, _indexBuffer, _wireBuffer, _pointBuffer;
    private int _msaaFramebuffer, _msaaColor, _msaaDepth, _msaaWidth, _msaaHeight, _samples;
    private int _indexCount, _wireCount, _pointCount;
    private MeshModel? _mesh;
    private bool _meshDirty, _wireDirty, _markersDirty;
    private Point _last;
    private bool _rotating, _panning;

    public GpuMeshViewport()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    public Control Visual => this;
    public ViewCamera Camera { get; } = new();
    public bool Wireframe { get; set; }
    public Color ModelColor { get; set; } = Color.FromRgb(0x58, 0xA6, 0xFF);

    private IReadOnlyList<Vector3>? _markers;
    public IReadOnlyList<Vector3>? Markers
    {
        get => _markers;
        set { _markers = value; _markersDirty = true; }
    }

    /// <summary>True once the graphics context was created and the shaders compiled.</summary>
    public bool IsReady { get; private set; }

    /// <summary>Raised when the graphics context is unavailable or lost, so the page can fall back to the CPU view.</summary>
    public event Action<string>? Failed;

    public event Action? CameraChanged;
    public event Action<Point>? Clicked;
    private Point _pressPoint;
    private bool _moved;

    public void SetMesh(MeshModel? mesh)
    {
        _mesh = mesh;
        _meshDirty = true;
        _wireDirty = true;
        Markers = null;
        Camera.Reset();
        RequestRender(false);
    }

    public void ResetView()
    {
        Camera.Reset();
        RequestRender(false);
    }

    public void RequestRender(bool interactive)
    {
        CameraChanged?.Invoke();
        RequestNextFrameRendering();
    }

    protected override void OnOpenGlInit(GlInterface gl)
    {
        try
        {
            _gl = new GlApi(gl);
            var isEs = GlVersion.Type == GlProfileType.OpenGLES;
            var header = isEs ? "#version 300 es\nprecision highp float;\n" : "#version 330 core\n";
            _solidProgram = _gl.BuildProgram(header + SolidVertex, header + SolidFragment);
            _pointProgram = _gl.BuildProgram(header + PointVertex, header + PointFragment);
            _positionBuffer = _gl.GenBuffer();
            _indexBuffer = _gl.GenBuffer();
            _wireBuffer = _gl.GenBuffer();
            _pointBuffer = _gl.GenBuffer();
            _meshVao = _gl.GenVertexArray();
            _wireVao = _gl.GenVertexArray();
            _pointVao = _gl.GenVertexArray();
            _samples = Math.Min(4, Math.Max(0, _gl.GetInteger(GlApi.MaxSamples)));
            _meshDirty = true;
            _wireDirty = true;
            _markersDirty = true;
            IsReady = true;
        }
        catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException or MarshalDirectiveException or DllNotFoundException or EntryPointNotFoundException)
        {
            IsReady = false;
            Failed?.Invoke(exception.Message);
        }
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        if (_gl is null) return;
        _gl.DeleteProgram(_solidProgram);
        _gl.DeleteProgram(_pointProgram);
        _gl.DeleteBuffer(_positionBuffer); _gl.DeleteBuffer(_indexBuffer); _gl.DeleteBuffer(_wireBuffer); _gl.DeleteBuffer(_pointBuffer);
        _gl.DeleteVertexArray(_meshVao); _gl.DeleteVertexArray(_wireVao); _gl.DeleteVertexArray(_pointVao);
        ReleaseMultisample();
        IsReady = false;
        _gl = null;
    }

    protected override void OnOpenGlLost()
    {
        IsReady = false;
        Failed?.Invoke("Le contexte graphique a été perdu.");
    }

    protected override void OnOpenGlRender(GlInterface gl, int framebuffer)
    {
        if (_gl is null || !IsReady) return;
        var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        var width = Math.Max(2, (int)(Bounds.Width * scale));
        var height = Math.Max(2, (int)(Bounds.Height * scale));
        try
        {
            UploadPending();
            var target = EnsureMultisample(width, height) ? _msaaFramebuffer : framebuffer;
            _gl.BindFramebuffer(GlApi.Framebuffer, target);
            _gl.Viewport(width, height);
            _gl.ClearColor(BackgroundR, BackgroundG, BackgroundB);
            _gl.Enable(GlApi.DepthTest);
            _gl.DepthFunc(GlApi.Less);
            _gl.Clear(GlApi.ColorBufferBit | GlApi.DepthBufferBit);
            if (_mesh is not null && _indexCount > 0) Draw(width, height);
            if (target != framebuffer)
            {
                _gl.BindFramebuffer(GlApi.ReadFramebuffer, target);
                _gl.BindFramebuffer(GlApi.DrawFramebuffer, framebuffer);
                _gl.Resolve(width, height);
            }
            _gl.BindFramebuffer(GlApi.Framebuffer, framebuffer);
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or OutOfMemoryException)
        {
            IsReady = false;
            Failed?.Invoke(exception.Message);
        }
    }

    private void Draw(int width, int height)
    {
        var mesh = _mesh!;
        var (view, projection) = BuildMatrices(mesh, width, height);
        var color = ModelColor;
        _gl!.UseProgram(_solidProgram);
        _gl.UniformMatrix(_gl.GetUniformLocation(_solidProgram, "uView"), view);
        _gl.UniformMatrix(_gl.GetUniformLocation(_solidProgram, "uProj"), projection);
        _gl.Uniform3(_gl.GetUniformLocation(_solidProgram, "uColor"), color.R / 255f, color.G / 255f, color.B / 255f);
        _gl.Uniform3(_gl.GetUniformLocation(_solidProgram, "uShaded"), Wireframe ? 0 : 1, 0, 0);
        if (Wireframe)
        {
            _gl.BindVertexArray(_wireVao);
            _gl.DrawElements(GlApi.Lines, _wireCount);
        }
        else
        {
            _gl.BindVertexArray(_meshVao);
            _gl.DrawElements(GlApi.Triangles, _indexCount);
        }

        if (_pointCount > 0)
        {
            _gl.Disable(GlApi.DepthTest);
            _gl.Enable(GlApi.ProgramPointSize);
            _gl.UseProgram(_pointProgram);
            _gl.UniformMatrix(_gl.GetUniformLocation(_pointProgram, "uView"), view);
            _gl.UniformMatrix(_gl.GetUniformLocation(_pointProgram, "uProj"), projection);
            _gl.BindVertexArray(_pointVao);
            _gl.DrawArrays(GlApi.Points, 0, _pointCount);
        }
        _gl.BindVertexArray(0);
    }

    /// <summary>View (centre, then rotation) and perspective matrices, in the layout OpenGL expects.</summary>
    private (float[] View, float[] Projection) BuildMatrices(MeshModel mesh, int width, int height)
    {
        var rotation = Matrix4x4.CreateRotationZ((float)Camera.Yaw) * Matrix4x4.CreateRotationX((float)Camera.Pitch);
        var view = Matrix4x4.CreateTranslation(-mesh.Center) * rotation;
        var radius = mesh.Radius;
        var distance = radius * 2.9 / Math.Max(0.02, Camera.Zoom);
        var focal = height * 0.5 / Math.Tan(FieldOfView / 2);
        var panScale = height / 760.0;
        var kx = (float)(2 * Camera.PanX * panScale / width);
        var ky = (float)(-2 * Camera.PanY * panScale / height);
        var near = Math.Max(radius * 0.002, 1e-6);
        var far = distance + radius * 2;
        var a = (float)((far + near) / (far - near));
        var b = (float)(-2 * far * near / (far - near));
        var d = (float)distance;
        var projection = new Matrix4x4(
            (float)(2 * focal / width), 0, 0, 0,
            kx, ky, a, 1,
            0, (float)(2 * focal / height), 0, 0,
            kx * d, ky * d, a * d + b, d);
        return (ToColumnMajor(view), ToColumnMajor(projection));
    }

    // System.Numerics is row-vector, row-major; OpenGL reads the same 16 floats as column-major, i.e. the transpose,
    // which is exactly the matrix to use with column vectors in the shader.
    private static float[] ToColumnMajor(Matrix4x4 m) =>
    [
        m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24,
        m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44
    ];

    private void UploadPending()
    {
        var gl = _gl!;
        if (_meshDirty)
        {
            _meshDirty = false;
            _indexCount = 0;
            if (_mesh is { } mesh)
            {
                gl.BindVertexArray(_meshVao);
                gl.BindBuffer(GlApi.ArrayBuffer, _positionBuffer);
                gl.BufferData(GlApi.ArrayBuffer, mesh.Positions, mesh.Positions.Length * 12);
                gl.EnableVertexAttribArray(0);
                gl.VertexAttribPointerFloat(0, 3, 12);
                gl.BindBuffer(GlApi.ElementArrayBuffer, _indexBuffer);
                gl.BufferData(GlApi.ElementArrayBuffer, mesh.Indices, mesh.Indices.Length * 4);
                _indexCount = mesh.Indices.Length;
                gl.BindVertexArray(0);
            }
        }

        if (_wireDirty && Wireframe && _mesh is { } wireMesh)
        {
            _wireDirty = false;
            var triangles = wireMesh.TriangleCount;
            var step = Math.Max(1, triangles / MaxWireTriangles);
            var edges = new List<int>(Math.Min(triangles, MaxWireTriangles) * 6 + 6);
            for (var t = 0; t < triangles; t += step)
            {
                int a = wireMesh.Indices[t * 3], b = wireMesh.Indices[t * 3 + 1], c = wireMesh.Indices[t * 3 + 2];
                edges.Add(a); edges.Add(b); edges.Add(b); edges.Add(c); edges.Add(c); edges.Add(a);
            }
            var array = edges.ToArray();
            gl.BindVertexArray(_wireVao);
            gl.BindBuffer(GlApi.ArrayBuffer, _positionBuffer);
            gl.EnableVertexAttribArray(0);
            gl.VertexAttribPointerFloat(0, 3, 12);
            gl.BindBuffer(GlApi.ElementArrayBuffer, _wireBuffer);
            gl.BufferData(GlApi.ElementArrayBuffer, array, array.Length * 4);
            _wireCount = array.Length;
            gl.BindVertexArray(0);
        }

        if (_markersDirty)
        {
            _markersDirty = false;
            _pointCount = 0;
            if (_markers is { Count: > 0 } markers)
            {
                var data = new float[markers.Count * 3];
                for (var i = 0; i < markers.Count; i++) { data[i * 3] = markers[i].X; data[i * 3 + 1] = markers[i].Y; data[i * 3 + 2] = markers[i].Z; }
                gl.BindVertexArray(_pointVao);
                gl.BindBuffer(GlApi.ArrayBuffer, _pointBuffer);
                gl.BufferData(GlApi.ArrayBuffer, data, data.Length * 4);
                gl.EnableVertexAttribArray(0);
                gl.VertexAttribPointerFloat(0, 3, 12);
                _pointCount = markers.Count;
                gl.BindVertexArray(0);
            }
        }
    }

    /// <summary>Creates (or resizes) a multisampled render target; returns false when none is available.</summary>
    private bool EnsureMultisample(int width, int height)
    {
        if (_samples < 2) return false;
        if (_msaaFramebuffer != 0 && _msaaWidth == width && _msaaHeight == height) return true;
        ReleaseMultisample();
        var gl = _gl!;
        _msaaFramebuffer = gl.GenFramebuffer();
        _msaaColor = gl.GenRenderbuffer();
        _msaaDepth = gl.GenRenderbuffer();
        gl.BindRenderbuffer(_msaaColor);
        gl.RenderbufferStorage(_samples, GlApi.Rgba8, width, height);
        gl.BindRenderbuffer(_msaaDepth);
        gl.RenderbufferStorage(_samples, GlApi.DepthComponent24, width, height);
        gl.BindFramebuffer(GlApi.Framebuffer, _msaaFramebuffer);
        gl.AttachRenderbuffer(GlApi.ColorAttachment0, _msaaColor);
        gl.AttachRenderbuffer(GlApi.DepthAttachment, _msaaDepth);
        if (!gl.FramebufferIsComplete())
        {
            ReleaseMultisample();
            _samples = 0;
            return false;
        }
        _msaaWidth = width;
        _msaaHeight = height;
        return true;
    }

    private void ReleaseMultisample()
    {
        if (_gl is null) return;
        _gl.DeleteFramebuffer(_msaaFramebuffer);
        _gl.DeleteRenderbuffer(_msaaColor);
        _gl.DeleteRenderbuffer(_msaaDepth);
        _msaaFramebuffer = _msaaColor = _msaaDepth = 0;
    }

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

    private const string SolidVertex = @"
layout(location = 0) in vec3 aPos;
uniform mat4 uView;
uniform mat4 uProj;
out vec3 vView;
void main() {
    vec4 v = uView * vec4(aPos, 1.0);
    vView = v.xyz;
    gl_Position = uProj * v;
}";

    // Flat shading from screen-space derivatives: no per-face normals to store or upload.
    private const string SolidFragment = @"
in vec3 vView;
uniform vec3 uColor;
uniform vec3 uShaded;
out vec4 outColor;
void main() {
    float shade = 1.0;
    if (uShaded.x > 0.5) {
        vec3 n = normalize(cross(dFdx(vView), dFdy(vView)));
        shade = 0.28 + 0.72 * abs(dot(n, normalize(vec3(-0.35, -1.0, 0.55))));
    }
    outColor = vec4(uColor * shade, 1.0);
}";

    private const string PointVertex = @"
layout(location = 0) in vec3 aPos;
uniform mat4 uView;
uniform mat4 uProj;
void main() {
    gl_Position = uProj * (uView * vec4(aPos, 1.0));
    gl_PointSize = 9.0;
}";

    private const string PointFragment = @"
out vec4 outColor;
void main() {
    vec2 c = gl_PointCoord - vec2(0.5);
    if (dot(c, c) > 0.25) discard;
    outColor = vec4(1.0, 0.416, 0.0, 1.0);
}";
}
