using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace MS3DPRINT.Manager.Desktop.Viewer;

/// <summary>One image owner: cancel hidden/detached rows and dispose results which arrive too late.</summary>
public sealed class ThumbnailBinding : IDisposable
{
    private readonly Control _owner;
    private readonly Image _image;
    private readonly Func<CancellationToken, Task<Bitmap?>> _load;
    private readonly Action<bool?>? _stateChanged;
    private readonly bool _automatic;
    private readonly List<Visual> _ancestors = [];
    private CancellationTokenSource? _request;
    private Bitmap? _bitmap;
    private int _version;
    private bool _attached, _disposed;

    public ThumbnailBinding(Control owner, Image image, Func<CancellationToken, Task<Bitmap?>> load, Action<bool?>? stateChanged = null, bool automatic = true)
    {
        _owner = owner; _image = image; _load = load; _stateChanged = stateChanged; _automatic = automatic;
        owner.AttachedToVisualTree += Attached;
        owner.DetachedFromVisualTree += Detached;
        if (TopLevel.GetTopLevel(owner) is not null) Attach();
    }

    private void Attached(object? sender, VisualTreeAttachmentEventArgs args) => Attach();
    private void Attach()
    {
        if (_disposed || _attached) return;
        _attached = true;
        foreach (var visual in _owner.GetVisualAncestors().Prepend(_owner))
        { _ancestors.Add(visual); visual.PropertyChanged += VisibilityChanged; }
        if (_automatic) _ = StartAsync();
    }

    private void VisibilityChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property != Visual.IsVisibleProperty) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (!_attached || _disposed) return;
            if (!_owner.IsEffectivelyVisible) Reset();
            else if (_automatic) _ = StartAsync();
        });
    }

    public async Task StartAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_disposed || !_attached || !_owner.IsEffectivelyVisible || _bitmap is not null || _request is not null) return;
        var cancellation = _request = new CancellationTokenSource();
        var token = cancellation.Token; var version = _version;
        _stateChanged?.Invoke(null);
        try
        {
            var bitmap = await _load(token);
            if (token.IsCancellationRequested || version != _version || !_attached || !_owner.IsEffectivelyVisible || _disposed)
            { bitmap?.Dispose(); return; }
            _bitmap = bitmap; _image.Source = bitmap;
            _stateChanged?.Invoke(bitmap is not null);
        }
        catch (Exception error) when (error is OperationCanceledException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or FormatException)
        { if (version == _version && _attached && !_disposed) _stateChanged?.Invoke(false); }
        finally
        {
            if (ReferenceEquals(_request, cancellation)) _request = null;
            cancellation.Dispose();
        }
    }

    private void Reset()
    {
        ++_version;
        _request?.Cancel(); _request?.Dispose(); _request = null;
        _image.Source = null;
        _bitmap?.Dispose(); _bitmap = null;
        _stateChanged?.Invoke(null);
    }

    private void Detached(object? sender, VisualTreeAttachmentEventArgs args) => Detach();
    private void Detach()
    {
        _attached = false;
        foreach (var ancestor in _ancestors) ancestor.PropertyChanged -= VisibilityChanged;
        _ancestors.Clear(); Reset();
    }

    public void Dispose()
    {
        if (_disposed) return;
        Dispatcher.UIThread.VerifyAccess();
        _disposed = true; Detach();
        _owner.AttachedToVisualTree -= Attached; _owner.DetachedFromVisualTree -= Detached;
    }
}
