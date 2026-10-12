using System.Runtime.InteropServices;
using System.Text;
using Avalonia.OpenGL;

namespace MS3DPRINT.Manager.Desktop.Viewer;

/// <summary>
/// The few OpenGL (ES 3.0 / core 3.3) entry points the viewer needs, loaded through the context Avalonia gives us.
/// Loading them ourselves keeps the viewer independent of which subset the framework wrapper exposes.
/// </summary>
internal sealed class GlApi
{
    public const int ArrayBuffer = 0x8892, ElementArrayBuffer = 0x8893, StaticDraw = 0x88E4;
    public const int Float = 0x1406, UnsignedInt = 0x1405;
    public const int Points = 0, Lines = 1, Triangles = 4;
    public const int DepthTest = 0x0B71, Less = 0x0201, ProgramPointSize = 0x8642;
    public const int ColorBufferBit = 0x4000, DepthBufferBit = 0x100;
    public const int VertexShader = 0x8B31, FragmentShader = 0x8B30, CompileStatus = 0x8B81, LinkStatus = 0x8B82;
    public const int Framebuffer = 0x8D40, ReadFramebuffer = 0x8CA8, DrawFramebuffer = 0x8CA9, Renderbuffer = 0x8D41;
    public const int ColorAttachment0 = 0x8CE0, DepthAttachment = 0x8D00, DepthComponent24 = 0x81A6, Rgba8 = 0x8058;
    public const int PolygonOffsetFill = 0x8037;
    public const int FramebufferComplete = 0x8CD5, MaxSamples = 0x8D57, Nearest = 0x2600;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void GenObjects(int count, out int id);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void DeleteObjects(int count, ref int id);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void BindObject(int target, int id);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void BindVertexArrayFn(int id);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void BufferDataFn(int target, IntPtr size, IntPtr data, int usage);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CreateShaderFn(int type);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void ShaderSourceFn(int shader, int count, [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPStr)] string[] source, int[] length);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void IdFn(int id);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void GetIvFn(int id, int name, out int value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void GetInfoLogFn(int id, int max, out int length, StringBuilder log);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CreateProgramFn();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void AttachShaderFn(int program, int shader);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetUniformLocationFn(int program, [MarshalAs(UnmanagedType.LPStr)] string name);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void UniformMatrix4Fn(int location, int count, byte transpose, float[] value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void Uniform3Fn(int location, float x, float y, float z);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void Uniform1Fn(int location, float x);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void Uniform4Fn(int location, float x, float y, float z, float w);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void VertexAttribPointerFn(int index, int size, int type, byte normalized, int stride, IntPtr pointer);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void DrawArraysFn(int mode, int first, int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void DrawElementsFn(int mode, int count, int type, IntPtr indices);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void CapFn(int cap);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void DepthFuncFn(int func);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void ClearColorFn(float r, float g, float b, float a);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void ClearFn(int mask);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void ViewportFn(int x, int y, int width, int height);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void GetIntegervFn(int name, out int value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void FramebufferRenderbufferFn(int target, int attachment, int renderbufferTarget, int renderbuffer);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void RenderbufferStorageMultisampleFn(int target, int samples, int format, int width, int height);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CheckFramebufferStatusFn(int target);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void BlitFramebufferFn(int sx0, int sy0, int sx1, int sy1, int dx0, int dy0, int dx1, int dy1, int mask, int filter);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetErrorFn();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void PolygonOffsetFn(float factor, float units);

    private readonly GenObjects _genBuffers, _genVertexArrays, _genFramebuffers, _genRenderbuffers;
    private readonly DeleteObjects _deleteBuffers, _deleteVertexArrays, _deleteFramebuffers, _deleteRenderbuffers;
    private readonly BindObject _bindBuffer, _bindFramebuffer, _bindRenderbuffer;
    private readonly BindVertexArrayFn _bindVertexArray;
    private readonly BufferDataFn _bufferData;
    private readonly CreateShaderFn _createShader;
    private readonly ShaderSourceFn _shaderSource;
    private readonly IdFn _compileShader, _linkProgram, _useProgram, _enableVertexAttribArray, _deleteShader, _deleteProgram;
    private readonly GetIvFn _getShaderiv, _getProgramiv;
    private readonly GetInfoLogFn _getShaderInfoLog, _getProgramInfoLog;
    private readonly CreateProgramFn _createProgram;
    private readonly AttachShaderFn _attachShader;
    private readonly GetUniformLocationFn _getUniformLocation;
    private readonly UniformMatrix4Fn _uniformMatrix4;
    private readonly Uniform3Fn _uniform3;
    private readonly Uniform1Fn _uniform1;
    private readonly Uniform4Fn _uniform4;
    private readonly VertexAttribPointerFn _vertexAttribPointer;
    private readonly DrawArraysFn _drawArrays;
    private readonly DrawElementsFn _drawElements;
    private readonly CapFn _enable, _disable;
    private readonly DepthFuncFn _depthFunc;
    private readonly ClearColorFn _clearColor;
    private readonly ClearFn _clear;
    private readonly ViewportFn _viewport;
    private readonly GetIntegervFn _getIntegerv;
    private readonly FramebufferRenderbufferFn _framebufferRenderbuffer;
    private readonly RenderbufferStorageMultisampleFn _renderbufferStorageMultisample;
    private readonly CheckFramebufferStatusFn _checkFramebufferStatus;
    private readonly BlitFramebufferFn _blitFramebuffer;
    private readonly GetErrorFn _getError;
    private readonly PolygonOffsetFn _polygonOffset;

    public GlApi(GlInterface gl)
    {
        T Load<T>(string name) where T : Delegate
        {
            var address = gl.GetProcAddress(name);
            if (address == IntPtr.Zero) throw new NotSupportedException("OpenGL : fonction indisponible (" + name + ").");
            return Marshal.GetDelegateForFunctionPointer<T>(address);
        }

        _genBuffers = Load<GenObjects>("glGenBuffers"); _genVertexArrays = Load<GenObjects>("glGenVertexArrays");
        _genFramebuffers = Load<GenObjects>("glGenFramebuffers"); _genRenderbuffers = Load<GenObjects>("glGenRenderbuffers");
        _deleteBuffers = Load<DeleteObjects>("glDeleteBuffers"); _deleteVertexArrays = Load<DeleteObjects>("glDeleteVertexArrays");
        _deleteFramebuffers = Load<DeleteObjects>("glDeleteFramebuffers"); _deleteRenderbuffers = Load<DeleteObjects>("glDeleteRenderbuffers");
        _bindBuffer = Load<BindObject>("glBindBuffer"); _bindFramebuffer = Load<BindObject>("glBindFramebuffer"); _bindRenderbuffer = Load<BindObject>("glBindRenderbuffer");
        _bindVertexArray = Load<BindVertexArrayFn>("glBindVertexArray");
        _bufferData = Load<BufferDataFn>("glBufferData");
        _createShader = Load<CreateShaderFn>("glCreateShader");
        _shaderSource = Load<ShaderSourceFn>("glShaderSource");
        _compileShader = Load<IdFn>("glCompileShader"); _linkProgram = Load<IdFn>("glLinkProgram"); _useProgram = Load<IdFn>("glUseProgram");
        _enableVertexAttribArray = Load<IdFn>("glEnableVertexAttribArray"); _deleteShader = Load<IdFn>("glDeleteShader"); _deleteProgram = Load<IdFn>("glDeleteProgram");
        _getShaderiv = Load<GetIvFn>("glGetShaderiv"); _getProgramiv = Load<GetIvFn>("glGetProgramiv");
        _getShaderInfoLog = Load<GetInfoLogFn>("glGetShaderInfoLog"); _getProgramInfoLog = Load<GetInfoLogFn>("glGetProgramInfoLog");
        _createProgram = Load<CreateProgramFn>("glCreateProgram");
        _attachShader = Load<AttachShaderFn>("glAttachShader");
        _getUniformLocation = Load<GetUniformLocationFn>("glGetUniformLocation");
        _uniformMatrix4 = Load<UniformMatrix4Fn>("glUniformMatrix4fv");
        _uniform3 = Load<Uniform3Fn>("glUniform3f"); _uniform1 = Load<Uniform1Fn>("glUniform1f"); _uniform4 = Load<Uniform4Fn>("glUniform4f");
        _vertexAttribPointer = Load<VertexAttribPointerFn>("glVertexAttribPointer");
        _drawArrays = Load<DrawArraysFn>("glDrawArrays"); _drawElements = Load<DrawElementsFn>("glDrawElements");
        _enable = Load<CapFn>("glEnable"); _disable = Load<CapFn>("glDisable");
        _depthFunc = Load<DepthFuncFn>("glDepthFunc");
        _clearColor = Load<ClearColorFn>("glClearColor"); _clear = Load<ClearFn>("glClear");
        _viewport = Load<ViewportFn>("glViewport");
        _getIntegerv = Load<GetIntegervFn>("glGetIntegerv");
        _framebufferRenderbuffer = Load<FramebufferRenderbufferFn>("glFramebufferRenderbuffer");
        _renderbufferStorageMultisample = Load<RenderbufferStorageMultisampleFn>("glRenderbufferStorageMultisample");
        _checkFramebufferStatus = Load<CheckFramebufferStatusFn>("glCheckFramebufferStatus");
        _blitFramebuffer = Load<BlitFramebufferFn>("glBlitFramebuffer");
        _getError = Load<GetErrorFn>("glGetError");
        _polygonOffset = Load<PolygonOffsetFn>("glPolygonOffset");
    }

    public int GenBuffer() { _genBuffers(1, out var id); return id; }
    public int GenVertexArray() { _genVertexArrays(1, out var id); return id; }
    public int GenFramebuffer() { _genFramebuffers(1, out var id); return id; }
    public int GenRenderbuffer() { _genRenderbuffers(1, out var id); return id; }
    public void DeleteBuffer(int id) { if (id != 0) _deleteBuffers(1, ref id); }
    public void DeleteVertexArray(int id) { if (id != 0) _deleteVertexArrays(1, ref id); }
    public void DeleteFramebuffer(int id) { if (id != 0) _deleteFramebuffers(1, ref id); }
    public void DeleteRenderbuffer(int id) { if (id != 0) _deleteRenderbuffers(1, ref id); }
    public void BindBuffer(int target, int id) => _bindBuffer(target, id);
    public void BindFramebuffer(int target, int id) => _bindFramebuffer(target, id);
    public void BindRenderbuffer(int id) => _bindRenderbuffer(Renderbuffer, id);
    public void BindVertexArray(int id) => _bindVertexArray(id);
    public void UseProgram(int id) => _useProgram(id);
    public void EnableVertexAttribArray(int index) => _enableVertexAttribArray(index);
    public void VertexAttribPointerFloat(int index, int size, int stride) => _vertexAttribPointer(index, size, Float, 0, stride, IntPtr.Zero);
    public void DrawArrays(int mode, int first, int count) => _drawArrays(mode, first, count);
    public void DrawElements(int mode, int count) => _drawElements(mode, count, UnsignedInt, IntPtr.Zero);
    public void Enable(int cap) => _enable(cap);
    public void Disable(int cap) => _disable(cap);
    public void DepthFunc(int func) => _depthFunc(func);
    public void ClearColor(float r, float g, float b) => _clearColor(r, g, b, 1);
    public void Clear(int mask) => _clear(mask);
    public void Viewport(int width, int height) => _viewport(0, 0, width, height);
    public int GetInteger(int name) { _getIntegerv(name, out var value); return value; }
    public int GetError() => _getError();
    public void PolygonOffset(float factor, float units) => _polygonOffset(factor, units);
    public void AttachRenderbuffer(int attachment, int renderbuffer) => _framebufferRenderbuffer(Framebuffer, attachment, Renderbuffer, renderbuffer);
    public void RenderbufferStorage(int samples, int format, int width, int height) => _renderbufferStorageMultisample(Renderbuffer, samples, format, width, height);
    public bool FramebufferIsComplete() => _checkFramebufferStatus(Framebuffer) == FramebufferComplete;
    public void Resolve(int width, int height) => _blitFramebuffer(0, 0, width, height, 0, 0, width, height, ColorBufferBit, Nearest);
    public int GetUniformLocation(int program, string name) => _getUniformLocation(program, name);
    public void Uniform3(int location, float x, float y, float z) => _uniform3(location, x, y, z);
    public void Uniform4(int location, float x, float y, float z, float w) => _uniform4(location, x, y, z, w);
    public void UniformMatrix(int location, float[] columnMajor) => _uniformMatrix4(location, 1, 0, columnMajor);

    /// <summary>Uploads raw bytes to the buffer currently bound to <paramref name="target"/>.</summary>
    public void BufferData(int target, Array data, int byteLength)
    {
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try { _bufferData(target, (IntPtr)byteLength, handle.AddrOfPinnedObject(), StaticDraw); }
        finally { handle.Free(); }
    }

    public int BuildProgram(string vertexSource, string fragmentSource)
    {
        var vertex = Compile(VertexShader, vertexSource);
        var fragment = Compile(FragmentShader, fragmentSource);
        var program = _createProgram();
        _attachShader(program, vertex);
        _attachShader(program, fragment);
        _linkProgram(program);
        _getProgramiv(program, LinkStatus, out var linked);
        if (linked == 0)
        {
            var log = new StringBuilder(2048);
            _getProgramInfoLog(program, log.Capacity, out _, log);
            throw new InvalidOperationException("Édition de liens des shaders impossible : " + log);
        }
        _deleteShader(vertex);
        _deleteShader(fragment);
        return program;
    }

    public void DeleteProgram(int program) { if (program != 0) _deleteProgram(program); }

    private int Compile(int type, string source)
    {
        var shader = _createShader(type);
        _shaderSource(shader, 1, [source], null!);
        _compileShader(shader);
        _getShaderiv(shader, CompileStatus, out var compiled);
        if (compiled == 0)
        {
            var log = new StringBuilder(2048);
            _getShaderInfoLog(shader, log.Capacity, out _, log);
            throw new InvalidOperationException("Compilation d’un shader impossible : " + log);
        }
        return shader;
    }
}
