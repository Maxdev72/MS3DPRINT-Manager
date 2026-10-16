using System.Numerics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;

namespace MS3DPRINT.Manager.Desktop.Viewer;

/// <summary>
/// Cutting plane perpendicular to a model axis (0 = X, 1 = Y, 2 = Z): what lies beyond <see cref="Offset"/> is cut away,
/// or what lies before it when <see cref="Flip"/> is set.
/// </summary>
public readonly record struct ClipPlane(int Axis, float Offset, bool Flip)
{
    public bool Clips(Vector3 point)
    {
        var coordinate = Axis == 0 ? point.X : Axis == 1 ? point.Y : point.Z;
        return Flip ? coordinate < Offset : coordinate > Offset;
    }
}

/// <summary>A picture of the 3D view: RGBA bytes, top row first.</summary>
public sealed record CapturedImage(int Width, int Height, byte[] Rgba);

/// <summary>What the viewer page needs from a 3D view, so the GPU view and the CPU fallback are interchangeable.</summary>
public interface IMeshViewport
{
    Control Visual { get; }
    ViewCamera Camera { get; }
    bool Wireframe { get; set; }
    Color ModelColor { get; set; }
    IReadOnlyList<Vector3>? Markers { get; set; }
    ClipPlane? CutPlane { get; set; }
    /// <summary>True when the file lists its triangles inside out; the cut section is then coloured on the other face.</summary>
    bool WindingInverted { get; set; }
    /// <summary>A second model drawn in the same space, to compare two versions (the GPU view only).</summary>
    MeshModel? SecondMesh { get; set; }
    Color SecondColor { get; set; }
    bool ShowFirst { get; set; }
    bool ShowSecond { get; set; }
    /// <summary>Translation applied to the second model, for example to put its centre on the first one's.</summary>
    Vector3 SecondOffset { get; set; }
    /// <summary>Reference grid on the base of the model (drawn by the GPU view only).</summary>
    bool ShowGrid { get; set; }
    /// <summary>Size of a grid cell in model units; 0 lets the view choose.</summary>
    float GridSpacing { get; set; }
    /// <summary>Build plate centred on the origin of the floor: half its width (X) and depth (Y) in model units; 0 draws none (GPU view only).</summary>
    float PlateHalfWidth { get; set; }
    float PlateHalfDepth { get; set; }
    /// <summary>The part sticks out of the plate: the outline of the plate is drawn in red.</summary>
    bool PlateOverflow { get; set; }
    /// <summary>The view as it is drawn now (null when nothing could be captured).</summary>
    Task<CapturedImage?> CaptureAsync();
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
public sealed class GpuMeshViewport : OpenGlControlBase
{
    private const double FieldOfView = 40 * Math.PI / 180;
    private const int MaxWireTriangles = 400_000;
    private const float BackgroundR = 0x10 / 255f, BackgroundG = 0x19 / 255f, BackgroundB = 0x23 / 255f;

    private GlApi? _gl;
    private int _solidProgram, _pointProgram, _gridProgram, _gridVao;
    private float[] _inverseViewProjection = new float[16];
    private float _gridSpacing;
    private int _meshVao, _wireVao, _pointVao;
    private int _positionBuffer, _indexBuffer, _wireBuffer, _pointBuffer;
    private int _msaaFramebuffer, _msaaColor, _msaaDepth, _msaaWidth, _msaaHeight, _samples;
    private int _indexCount, _wireCount, _pointCount;
    private MeshModel? _mesh;
    private bool _meshDirty, _wireDirty, _markersDirty;

    public GpuMeshViewport()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    public ViewCamera Camera { get; } = new();
    public bool Wireframe { get; set; }
    public Color ModelColor { get; set; } = Color.FromRgb(0x58, 0xA6, 0xFF);

    public ClipPlane? CutPlane { get; set; }
    public bool WindingInverted { get; set; }
    public Color SecondColor { get; set; } = Color.FromRgb(0xFF, 0x6B, 0x6B);
    public bool ShowFirst { get; set; } = true;
    public bool ShowSecond { get; set; } = true;
    public Vector3 SecondOffset { get; set; }
    public bool ShowGrid { get; set; }
    public float GridSpacing
    {
        get => _gridSpacing;
        set { _gridSpacing = value; }
    }
    public float PlateHalfWidth { get; set; }
    public float PlateHalfDepth { get; set; }
    public bool PlateOverflow { get; set; }

    private TaskCompletionSource<CapturedImage?>? _capture;

    /// <summary>Asks for the next frame to be read back from the graphics card.</summary>
    public Task<CapturedImage?> CaptureAsync()
    {
        var source = new TaskCompletionSource<CapturedImage?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _capture = source;
        RequestNextFrameRendering();
        // a view that is hidden or whose context was lost never renders: do not wait for ever
        _ = Task.Delay(4000).ContinueWith(_ => source.TrySetResult(null), TaskScheduler.Default);
        return source.Task;
    }

    private CapturedImage ReadBack(int width, int height)
    {
        var rows = _gl!.ReadRgba(width, height);
        var flipped = new byte[rows.Length];
        var stride = width * 4;
        for (var y = 0; y < height; y++)
            Buffer.BlockCopy(rows, (height - 1 - y) * stride, flipped, y * stride, stride);
        for (var i = 3; i < flipped.Length; i += 4) flipped[i] = 255;
        return new CapturedImage(width, height, flipped);
    }

    private MeshModel? _second;
    private int _secondVao, _secondWireVao, _secondPosition, _secondIndex, _secondWire, _secondIndexCount, _secondWireCount;
    private bool _secondDirty, _secondWireDirty;

    public MeshModel? SecondMesh
    {
        get => _second;
        set { _second = value; _secondDirty = true; _secondWireDirty = true; }
    }

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
            _gridProgram = _gl.BuildProgram(header + GridVertex, header + GridFragment);
            _gridVao = _gl.GenVertexArray();
            _positionBuffer = _gl.GenBuffer();
            _indexBuffer = _gl.GenBuffer();
            _wireBuffer = _gl.GenBuffer();
            _pointBuffer = _gl.GenBuffer();
            _secondPosition = _gl.GenBuffer(); _secondIndex = _gl.GenBuffer(); _secondWire = _gl.GenBuffer();
            _secondVao = _gl.GenVertexArray(); _secondWireVao = _gl.GenVertexArray();
            _secondDirty = true; _secondWireDirty = true;
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
        _gl.DeleteProgram(_gridProgram);
        _gl.DeleteVertexArray(_gridVao);
        _gl.DeleteBuffer(_positionBuffer); _gl.DeleteBuffer(_indexBuffer); _gl.DeleteBuffer(_wireBuffer); _gl.DeleteBuffer(_pointBuffer);
        _gl.DeleteVertexArray(_meshVao); _gl.DeleteVertexArray(_wireVao); _gl.DeleteVertexArray(_pointVao);
        _gl.DeleteBuffer(_secondPosition); _gl.DeleteBuffer(_secondIndex); _gl.DeleteBuffer(_secondWire);
        _gl.DeleteVertexArray(_secondVao); _gl.DeleteVertexArray(_secondWireVao);
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
            if (_capture is { } pending) { _capture = null; pending.TrySetResult(ReadBack(width, height)); }
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
        if (ShowGrid)
        {
            // one triangle over the whole view: every pixel finds where its ray meets the floor, so the grid has no edge and no far plane.
            // It is drawn first and without depth, the model then covers it.
            _gl!.Disable(GlApi.DepthTest);
            _gl.UseProgram(_gridProgram);
            _gl.UniformMatrix(_gl.GetUniformLocation(_gridProgram, "uInverseViewProjection"), _inverseViewProjection);
            // a hair under the base so that a flat bottom does not flicker against the grid
            _gl.Uniform1(_gl.GetUniformLocation(_gridProgram, "uPlaneZ"), mesh.Min.Z - mesh.Radius * 0.0005f);
            _gl.Uniform1(_gl.GetUniformLocation(_gridProgram, "uSpacing"), GroundGrid.Spacing(_gridSpacing, mesh.Radius));
            _gl.Uniform3(_gl.GetUniformLocation(_gridProgram, "uBackground"), BackgroundR, BackgroundG, BackgroundB);
            _gl.Uniform3(_gl.GetUniformLocation(_gridProgram, "uMinor"), GroundGrid.Minor.X, GroundGrid.Minor.Y, GroundGrid.Minor.Z);
            _gl.Uniform3(_gl.GetUniformLocation(_gridProgram, "uMajor"), GroundGrid.Major.X, GroundGrid.Major.Y, GroundGrid.Major.Z);
            _gl.Uniform3(_gl.GetUniformLocation(_gridProgram, "uPlate"), PlateHalfWidth, PlateHalfDepth, PlateOverflow ? 1f : 0f);
            _gl.Uniform3(_gl.GetUniformLocation(_gridProgram, "uPlateFill"), 0.10f, 0.15f, 0.21f);
            _gl.Uniform3(_gl.GetUniformLocation(_gridProgram, "uPlateEdge"), 0.45f, 0.62f, 0.85f);
            _gl.Uniform3(_gl.GetUniformLocation(_gridProgram, "uPlateOver"), 0.96f, 0.50f, 0.50f);
            _gl.BindVertexArray(_gridVao);
            _gl.DrawArrays(GlApi.Triangles, 0, 3);
            _gl.Enable(GlApi.DepthTest);
        }
        _gl!.UseProgram(_solidProgram);
        _gl.UniformMatrix(_gl.GetUniformLocation(_solidProgram, "uView"), view);
        _gl.UniformMatrix(_gl.GetUniformLocation(_solidProgram, "uProj"), projection);
        _gl.Uniform3(_gl.GetUniformLocation(_solidProgram, "uColor"), color.R / 255f, color.G / 255f, color.B / 255f);
        _gl.Uniform3(_gl.GetUniformLocation(_solidProgram, "uShaded"), Wireframe ? 0 : 1, CutPlane is null ? 0 : 1, WindingInverted ? 1 : 0);
        // plane as dot(position, normal) > limit: the side to cut away; a huge limit when there is no cut
        if (CutPlane is { } clip)
        {
            var sign = clip.Flip ? -1f : 1f;
            _gl.Uniform4(_gl.GetUniformLocation(_solidProgram, "uClip"), clip.Axis == 0 ? sign : 0, clip.Axis == 1 ? sign : 0, clip.Axis == 2 ? sign : 0, clip.Offset * sign);
        }
        else _gl.Uniform4(_gl.GetUniformLocation(_solidProgram, "uClip"), 0, 0, 1, 1e30f);
        var uOffset = _gl.GetUniformLocation(_solidProgram, "uOffset");
        var uColor = _gl.GetUniformLocation(_solidProgram, "uColor");
        _gl.Uniform3(uOffset, 0, 0, 0);
        if (ShowFirst) DrawMesh(_meshVao, _wireVao, _indexCount, _wireCount);
        if (_second is not null && ShowSecond)
        {
            var other = SecondColor;
            var move = SecondOffset;
            _gl.Uniform3(uColor, other.R / 255f, other.G / 255f, other.B / 255f);
            _gl.Uniform3(uOffset, move.X, move.Y, move.Z);
            // where both versions coincide, the first one wins instead of flickering
            _gl.Enable(GlApi.PolygonOffsetFill);
            _gl.PolygonOffset(1f, 1f);
            DrawMesh(_secondVao, _secondWireVao, _secondIndexCount, _secondWireCount);
            _gl.Disable(GlApi.PolygonOffsetFill);
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

    private void DrawMesh(int vao, int wireVao, int indexCount, int wireCount)
    {
        if (Wireframe) { _gl!.BindVertexArray(wireVao); _gl.DrawElements(GlApi.Lines, wireCount); }
        else { _gl!.BindVertexArray(vao); _gl.DrawElements(GlApi.Triangles, indexCount); }
    }

    /// <summary>View (centre, then rotation) and perspective matrices, in the layout OpenGL expects.</summary>
    private (float[] View, float[] Projection) BuildMatrices(MeshModel mesh, int width, int height)
    {
        var rotation = Matrix4x4.CreateRotationZ((float)Camera.Yaw) * Matrix4x4.CreateRotationX((float)Camera.Pitch);
        var view = Matrix4x4.CreateTranslation(-mesh.Center) * rotation;
        var radius = mesh.Radius;
        var distance = radius * 2.9 / Math.Max(ViewCamera.MinZoom, Camera.Zoom);
        var focal = height * 0.5 / Math.Tan(FieldOfView / 2);
        var panScale = height / 760.0;
        var kx = (float)(2 * Camera.PanX * panScale / width);
        var ky = (float)(-2 * Camera.PanY * panScale / height);
        // the clipping planes follow the camera, so the model is neither cut when the view dives into it nor flickers when it is far away:
        // from outside, the near plane sits halfway to the closest point of the model; from close or inside, it stays a sliver in front of the eye
        var near = distance > radius * 1.5 ? (distance - radius) * 0.5 : Math.Max(distance * 0.02, radius * 1e-9);
        var far = distance + radius * 1.5;
        var a = (float)((far + near) / (far - near));
        var b = (float)(-2 * far * near / (far - near));
        var d = (float)distance;
        var projection = new Matrix4x4(
            (float)(2 * focal / width), 0, 0, 0,
            kx, ky, a, 1,
            0, (float)(2 * focal / height), 0, 0,
            kx * d, ky * d, a * d + b, d);
        // the grid unprojects pixels: (view × projection) inverted, laid out like the other matrices
        _inverseViewProjection = Matrix4x4.Invert(view * projection, out var inverse) ? ToColumnMajor(inverse) : ToColumnMajor(Matrix4x4.Identity);
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
            _indexCount = _mesh is { } mesh ? UploadMesh(gl, mesh, _meshVao, _positionBuffer, _indexBuffer) : 0;
        }
        if (_wireDirty && Wireframe && _mesh is { } wireMesh)
        {
            _wireDirty = false;
            _wireCount = UploadWire(gl, wireMesh, _wireVao, _positionBuffer, _wireBuffer);
        }
        if (_secondDirty)
        {
            _secondDirty = false;
            _secondIndexCount = _second is { } second ? UploadMesh(gl, second, _secondVao, _secondPosition, _secondIndex) : 0;
        }
        if (_secondWireDirty && Wireframe && _second is { } secondWire)
        {
            _secondWireDirty = false;
            _secondWireCount = UploadWire(gl, secondWire, _secondWireVao, _secondPosition, _secondWire);
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

    private static int UploadMesh(GlApi gl, MeshModel mesh, int vao, int positions, int indices)
    {
        gl.BindVertexArray(vao);
        gl.BindBuffer(GlApi.ArrayBuffer, positions);
        gl.BufferData(GlApi.ArrayBuffer, mesh.Positions, mesh.Positions.Length * 12);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointerFloat(0, 3, 12);
        gl.BindBuffer(GlApi.ElementArrayBuffer, indices);
        gl.BufferData(GlApi.ElementArrayBuffer, mesh.Indices, mesh.Indices.Length * 4);
        gl.BindVertexArray(0);
        return mesh.Indices.Length;
    }

    /// <summary>Edge list for the wireframe, thinned on very large meshes.</summary>
    private static int UploadWire(GlApi gl, MeshModel mesh, int vao, int positions, int wire)
    {
        var triangles = mesh.TriangleCount;
        var step = Math.Max(1, triangles / MaxWireTriangles);
        var edges = new List<int>(Math.Min(triangles, MaxWireTriangles) * 6 + 6);
        for (var t = 0; t < triangles; t += step)
        {
            int a = mesh.Indices[t * 3], b = mesh.Indices[t * 3 + 1], c = mesh.Indices[t * 3 + 2];
            edges.Add(a); edges.Add(b); edges.Add(b); edges.Add(c); edges.Add(c); edges.Add(a);
        }
        var array = edges.ToArray();
        gl.BindVertexArray(vao);
        gl.BindBuffer(GlApi.ArrayBuffer, positions);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointerFloat(0, 3, 12);
        gl.BindBuffer(GlApi.ElementArrayBuffer, wire);
        gl.BufferData(GlApi.ElementArrayBuffer, array, array.Length * 4);
        gl.BindVertexArray(0);
        return array.Length;
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

    private const string SolidVertex = @"
layout(location = 0) in vec3 aPos;
uniform mat4 uView;
uniform mat4 uProj;
uniform vec3 uOffset;
out vec3 vView;
out vec3 vModel;
void main() {
    vec3 p = aPos + uOffset;
    vec4 v = uView * vec4(p, 1.0);
    vView = v.xyz;
    vModel = p;
    gl_Position = uProj * v;
}";

    // Flat shading from screen-space derivatives: no per-face normals to store or upload.
    private const string SolidFragment = @"
in vec3 vView;
in vec3 vModel;
uniform vec3 uColor;
uniform vec3 uShaded;
uniform vec4 uClip;
out vec4 outColor;
void main() {
    if (dot(vModel, uClip.xyz) > uClip.w) discard;
    // inside faces seen through the cut are drawn flat orange: it reads as the cut section
    if (uShaded.y > 0.5 && uShaded.x > 0.5 && (gl_FrontFacing == (uShaded.z > 0.5))) {
        outColor = vec4(1.0, 0.62, 0.26, 1.0);
        return;
    }
    float shade = 1.0;
    if (uShaded.x > 0.5) {
        vec3 n = normalize(cross(dFdx(vView), dFdy(vView)));
        shade = 0.28 + 0.72 * abs(dot(n, normalize(vec3(-0.35, -1.0, 0.55))));
    }
    outColor = vec4(uColor * shade, 1.0);
}";

    private const string GridVertex = @"
out vec2 vNdc;
void main() {
    // a triangle that covers the screen, made from the vertex number alone
    vec2 corner = vec2(float((gl_VertexID << 1) & 2), float(gl_VertexID & 2));
    vNdc = corner * 2.0 - 1.0;
    gl_Position = vec4(vNdc, 0.0, 1.0);
}";

    private const string GridFragment = @"
in vec2 vNdc;
uniform mat4 uInverseViewProjection;
uniform float uPlaneZ;
uniform float uSpacing;
uniform vec3 uBackground;
uniform vec3 uMinor;
uniform vec3 uMajor;
uniform vec3 uPlate;       // half width, half depth, 1 when the part sticks out; a half width of 0 means no plate
uniform vec3 uPlateFill;
uniform vec3 uPlateEdge;
uniform vec3 uPlateOver;
out vec4 outColor;

// Lines of one cell size, one pixel wide whatever the distance. When they come closer than a few pixels they would blur into
// a haze, so they fade out and the next, coarser, level takes over.
float level(vec2 p, float cell) {
    vec2 coord = p / cell;
    vec2 width = max(fwidth(coord), vec2(1e-7));
    vec2 g = abs(fract(coord - 0.5) - 0.5) / width;
    float line = 1.0 - min(min(g.x, g.y), 1.0);
    return line * (1.0 - smoothstep(0.12, 0.5, max(width.x, width.y)));
}

void main() {
    vec4 nearPoint = uInverseViewProjection * vec4(vNdc, -1.0, 1.0);
    vec4 farPoint = uInverseViewProjection * vec4(vNdc, 1.0, 1.0);
    vec3 origin = nearPoint.xyz / nearPoint.w;
    vec3 direction = farPoint.xyz / farPoint.w - origin;
    if (abs(direction.z) < 1e-12) discard;
    float t = (uPlaneZ - origin.z) / direction.z;
    if (t <= 0.0) discard;
    vec2 p = (origin + direction * t).xy;

    // the plate: a lighter rectangle with an outline, the grid goes on around it a little dimmer
    float onPlate = 0.0;
    float edge = 0.0;
    if (uPlate.x > 0.0) {
        vec2 q = abs(p) - uPlate.xy;
        float d = max(q.x, q.y);
        float w = max(fwidth(d), 1e-7);
        onPlate = 1.0 - smoothstep(-w, w, d);
        edge = 1.0 - smoothstep(0.0, 1.5 * w, abs(d));
    }
    float strength = uPlate.x > 0.0 ? mix(0.55, 1.0, onPlate) : 1.0;

    float minor = level(p, uSpacing);
    float major = 0.0;
    float cell = uSpacing * 5.0;
    for (int k = 0; k < 7; k++) {
        major = max(major, level(p, cell));
        cell *= 5.0;
    }
    vec3 color = mix(uBackground, uPlateFill, onPlate);
    color = mix(color, uMinor, minor * strength);
    color = mix(color, uMajor, major * strength);
    color = mix(color, uPlate.z > 0.5 ? uPlateOver : uPlateEdge, edge);
    // towards the horizon the floor dissolves into the background
    float elevation = abs(direction.z) / length(direction);
    outColor = vec4(mix(uBackground, color, smoothstep(0.0, 0.06, elevation)), 1.0);
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
