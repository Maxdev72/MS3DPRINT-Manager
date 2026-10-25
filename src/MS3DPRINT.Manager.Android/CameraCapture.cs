using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Media;
using Android.Provider;
using AndroidX.Core.Content;

namespace MS3DPRINT.Manager.Companion;

/// <summary>Takes a photo with the phone's camera app and returns it as a JPEG of reasonable size (long side 1600 px), upright.</summary>
public static class CameraCapture
{
    private const int RequestCode = 4101;
    private const int MaxSide = 1600;
    private static TaskCompletionSource<byte[]?>? _pending;
    private static Java.IO.File? _target;

    public static Task<byte[]?> TakePhotoAsync(Activity activity)
    {
        _pending?.TrySetResult(null);
        var directory = new Java.IO.File(activity.CacheDir, "photos");
        directory.Mkdirs();
        _target = new Java.IO.File(directory, $"ticket-{Guid.NewGuid():N}.jpg");
        var uri = FileProvider.GetUriForFile(activity, "fr.ms3dprint.compagnon.fileprovider", _target)
            ?? throw new InvalidOperationException("Impossible de préparer le fichier de la photo.");
        var intent = new Intent(MediaStore.ActionImageCapture);
        intent.PutExtra(MediaStore.ExtraOutput, uri);
        intent.AddFlags(ActivityFlags.GrantWriteUriPermission | ActivityFlags.GrantReadUriPermission);
        _pending = new TaskCompletionSource<byte[]?>();
        try { activity.StartActivityForResult(intent, RequestCode); }
        catch (ActivityNotFoundException) { _pending.TrySetResult(null); }
        return _pending.Task;
    }

    public static void OnResult(int requestCode, Result resultCode)
    {
        if (requestCode != RequestCode || _pending is null) return;
        var pending = _pending; var file = _target; _pending = null; _target = null;
        if (resultCode != Result.Ok || file is null || !file.Exists()) { pending.TrySetResult(null); return; }
        try { pending.TrySetResult(Compress(file.AbsolutePath)); }
        catch (Exception exception) when (exception is Java.IO.IOException or IOException or OutOfMemoryException) { pending.TrySetResult(null); }
        finally { file.Delete(); }
    }

    private static byte[]? Compress(string path)
    {
        var bounds = new BitmapFactory.Options { InJustDecodeBounds = true };
        BitmapFactory.DecodeFile(path, bounds);
        if (bounds.OutWidth <= 0) return null;
        var sample = 1;
        while (Math.Max(bounds.OutWidth, bounds.OutHeight) / (sample * 2) >= MaxSide) sample *= 2;
        using var decoded = BitmapFactory.DecodeFile(path, new BitmapFactory.Options { InSampleSize = sample });
        if (decoded is null) return null;
        var orientation = new ExifInterface(path).GetAttributeInt(ExifInterface.TagOrientation, (int)Orientation.Normal);
        var degrees = orientation switch { (int)Orientation.Rotate90 => 90f, (int)Orientation.Rotate180 => 180f, (int)Orientation.Rotate270 => 270f, _ => 0f };
        var scale = Math.Min(1f, MaxSide / (float)Math.Max(decoded.Width, decoded.Height));
        using var matrix = new Matrix();
        matrix.PostRotate(degrees); matrix.PostScale(scale, scale);
        using var upright = Bitmap.CreateBitmap(decoded, 0, 0, decoded.Width, decoded.Height, matrix, true);
        using var output = new MemoryStream();
        upright!.Compress(Bitmap.CompressFormat.Jpeg!, 85, output);
        return output.ToArray();
    }
}
