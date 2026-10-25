using Android.App;
using Android.Content;
using Android.Provider;
using AndroidX.Core.Content;
using MS3DPRINT.Manager.Companion.Services;

namespace MS3DPRINT.Manager.Companion;

/// <summary>
/// Takes a photo with the phone's camera app and returns it as a JPEG of reasonable size (long side 1600 px), upright.
/// Android may close this app while the camera is open: the target file is then remembered on disk and the photo is recovered when the app comes back.
/// </summary>
public static class CameraCapture
{
    private const int RequestCode = 4101;
    private static TaskCompletionSource<byte[]?>? _pending;
    private static Java.IO.File? _target;

    /// <summary>A photo taken before the app was closed by Android, waiting to be attached to the form.</summary>
    public static byte[]? Recovered { get; private set; }
    public static byte[]? TakeRecovered() { var photo = Recovered; Recovered = null; return photo; }

    private static string MarkerPath => Path.Combine(Path.GetTempPath(), "photo-en-cours.txt");

    public static Task<byte[]?> TakePhotoAsync(Activity activity)
    {
        _pending?.TrySetResult(null);
        var directory = new Java.IO.File(activity.CacheDir, "photos");
        directory.Mkdirs();
        _target = new Java.IO.File(directory, $"ticket-{Guid.NewGuid():N}.jpg");
        try { File.WriteAllText(MarkerPath, _target.AbsolutePath); } catch (IOException) { }
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
        if (requestCode != RequestCode) return;
        var pending = _pending; var file = _target; _pending = null; _target = null;
        // The process was restarted while the camera was open: find the file again from the marker.
        if (pending is null)
        {
            try { if (File.Exists(MarkerPath)) file = new Java.IO.File(File.ReadAllText(MarkerPath).Trim()); } catch (IOException) { }
        }
        try { File.Delete(MarkerPath); } catch (IOException) { }
        byte[]? photo = null;
        if (resultCode == Result.Ok && file is not null && file.Exists())
        {
            try { photo = PhotoTools.NormalizeFile(file.AbsolutePath); }
            catch (Exception exception) when (exception is Java.IO.IOException or IOException or OutOfMemoryException) { photo = null; }
        }
        if (file is not null && file.Exists()) file.Delete();
        if (pending is not null) pending.TrySetResult(photo);
        else if (photo is not null) { Recovered = photo; App.Main?.PhotoRecovered(); }
    }
}
