using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using LibVLCSharp.Shared;

namespace MS3DPRINT.Manager.Desktop.Printers;

/// <summary>
/// Plays an RTSP camera (X1, H2, X2) with LibVLC into a bitmap: VLC decodes into memory and each frame is copied to the
/// UI, so no native video view is needed. Windows ships LibVLC with the app; Linux uses the system "vlc" package.
/// </summary>
public sealed class VlcCamera : IDisposable
{
    private static LibVLC? _vlc;
    private static string? _unavailable;

    private readonly MediaPlayer _player;
    private readonly Action<WriteableBitmap> _frame;
    private readonly object _gate = new();
    // VLC keeps raw pointers to these callbacks: the fields keep them alive.
    private readonly MediaPlayer.LibVLCVideoFormatCb _format;
    private readonly MediaPlayer.LibVLCVideoCleanupCb _cleanup;
    private readonly MediaPlayer.LibVLCVideoLockCb _lock;
    private readonly MediaPlayer.LibVLCVideoDisplayCb _display;
    private IntPtr _buffer;
    private int _width, _height, _length;
    private byte[] _copy = [];
    private bool _posted, _disposed;
    private WriteableBitmap? _bitmap;

    /// <summary>Why the video player cannot start, or null when it can.</summary>
    public static string? Unavailable { get { Shared(); return _unavailable; } }

    private static LibVLC? Shared()
    {
        if (_vlc is not null || _unavailable is not null) return _vlc;
        try { LibVLCSharp.Shared.Core.Initialize(); _vlc = new LibVLC("--no-audio", "--no-osd", "--quiet"); }
        catch (Exception exception) when (exception is VLCException or DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        { _unavailable = OperatingSystem.IsLinux() ? "Installez le paquet vlc pour voir la caméra." : "Lecteur vidéo indisponible : " + exception.Message; }
        return _vlc;
    }

    public VlcCamera(string url, Action<WriteableBitmap> frame)
    {
        _frame = frame;
        _format = Format; _cleanup = Cleanup; _lock = Lock; _display = Display;
        var vlc = Shared() ?? throw new InvalidOperationException(_unavailable);
        _player = new MediaPlayer(vlc);
        _player.SetVideoFormatCallbacks(_format, _cleanup);
        _player.SetVideoCallbacks(_lock, null, _display);
        using var media = new Media(vlc, new Uri(url), ":network-caching=800", ":rtsp-tcp");
        _player.Play(media);
    }

    private uint Format(ref IntPtr opaque, IntPtr chroma, ref uint width, ref uint height, ref uint pitches, ref uint lines)
    {
        Marshal.Copy("RV32"u8.ToArray(), 0, chroma, 4);
        lock (_gate)
        {
            if (_buffer != IntPtr.Zero) Marshal.FreeHGlobal(_buffer);
            _width = (int)width; _height = (int)height; _length = _width * _height * 4;
            _buffer = Marshal.AllocHGlobal(_length);
            _copy = new byte[_length];
        }
        pitches = width * 4;
        lines = height;
        return 1;
    }

    private void Cleanup(ref IntPtr opaque)
    {
        lock (_gate) { if (_buffer != IntPtr.Zero) Marshal.FreeHGlobal(_buffer); _buffer = IntPtr.Zero; }
    }

    private IntPtr Lock(IntPtr opaque, IntPtr planes)
    {
        Marshal.WriteIntPtr(planes, _buffer);
        return IntPtr.Zero;
    }

    private void Display(IntPtr opaque, IntPtr picture)
    {
        lock (_gate)
        {
            // Skip frames while the UI has not drawn the previous one.
            if (_posted || _buffer == IntPtr.Zero) return;
            Marshal.Copy(_buffer, _copy, 0, _length);
            _posted = true;
        }
        Dispatcher.UIThread.Post(Present);
    }

    private void Present()
    {
        lock (_gate)
        {
            _posted = false;
            if (_disposed || _length == 0) return;
            if (_bitmap is null || _bitmap.PixelSize.Width != _width || _bitmap.PixelSize.Height != _height)
                _bitmap = new WriteableBitmap(new PixelSize(_width, _height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            using var target = _bitmap.Lock();
            for (var row = 0; row < _height; row++)
                Marshal.Copy(_copy, row * _width * 4, target.Address + row * target.RowBytes, _width * 4);
        }
        _frame(_bitmap);
    }

    public void Dispose()
    {
        lock (_gate) _disposed = true;
        var player = _player;
        // Stop waits for VLC's threads: never on the UI thread.
        _ = Task.Run(() => { player.Stop(); player.Dispose(); });
    }
}
