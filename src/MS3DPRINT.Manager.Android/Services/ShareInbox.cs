using Android.App;
using Android.Content;
using Android.Provider;

namespace MS3DPRINT.Manager.Companion.Services;

/// <summary>Receives a photo or a PDF handed over by the Android "Share" menu (an invoice opened from an e-mail, a photo from the gallery).</summary>
public static class ShareInbox
{
    private const long MaxBytes = 26L * 1024 * 1024;

    /// <summary>Kept until the main screen is ready to take it (a cold start from the Share menu arrives before the screen exists).</summary>
    public static (byte[] Bytes, string Name)? Pending { get; private set; }

    public static (byte[] Bytes, string Name)? Take() { var pending = Pending; Pending = null; return pending; }

    /// <summary>Why the last shared file could not be used; shown once on the entry screen.</summary>
    public static string? Failure { get; private set; }
    public static string? TakeFailure() { var failure = Failure; Failure = null; return failure; }

    public static void Handle(Activity activity, Intent? intent)
    {
        if (intent?.Action != Intent.ActionSend) return;
#pragma warning disable CA1422 // the typed overload only exists from Android 13
        if (intent.GetParcelableExtra(Intent.ExtraStream) is not Android.Net.Uri uri) { Failure = "Aucun fichier n’a été reçu de l’autre application."; App.Main?.ReceiveShared(); return; }
#pragma warning restore CA1422
        try
        {
            using var input = activity.ContentResolver!.OpenInputStream(uri);
            if (input is null) { Failure = "Le fichier partagé n’a pas pu être ouvert."; }
            else
            {
                using var memory = new MemoryStream();
                input.CopyTo(memory);
                if (memory.Length == 0) Failure = "Le fichier partagé est vide.";
                else if (memory.Length > MaxBytes) Failure = "Le fichier partagé est trop volumineux (25 Mo maximum).";
                else Pending = (memory.ToArray(), DisplayName(activity, uri));
            }
        }
        catch (Exception exception) when (exception is IOException or Java.IO.IOException or Java.Lang.SecurityException or UnauthorizedAccessException)
        { Pending = null; Failure = "Le fichier partagé n’a pas pu être lu (l’autre application n’en a pas autorisé l’accès). Enregistrez-le d’abord sur le téléphone, puis choisissez-le avec « Galerie »."; }
        App.Main?.ReceiveShared();
    }

    private static string DisplayName(Activity activity, Android.Net.Uri uri)
    {
        try
        {
            using var cursor = activity.ContentResolver!.Query(uri, [OpenableColumns.DisplayName], null, null, null);
            if (cursor is not null && cursor.MoveToFirst()) return cursor.GetString(0) ?? "fichier";
        }
        catch (Exception exception) when (exception is Java.Lang.Exception) { }
        return uri.LastPathSegment ?? "fichier";
    }
}
