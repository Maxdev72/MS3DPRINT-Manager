using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Views;
using AndroidX.Activity;
using Avalonia;
using Avalonia.Android;

namespace MS3DPRINT.Manager.Companion;


[Activity(Label = "MS3DPRINT Compagnon", Icon = "@mipmap/ic_launcher", RoundIcon = "@mipmap/ic_launcher_round", Theme = "@style/MyTheme.NoActionBar", MainLauncher = true, LaunchMode = LaunchMode.SingleTask, WindowSoftInputMode = SoftInput.AdjustResize,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden)]
[IntentFilter(new[] { Intent.ActionSend }, Categories = new[] { Intent.CategoryDefault }, DataMimeTypes = new[] { "image/*", "application/pdf" })]
public class MainActivity : AvaloniaMainActivity
{
    public static MainActivity? Instance { get; private set; }

    protected override void OnCreate(Android.OS.Bundle? savedInstanceState)
    {
        Instance = this;
        base.OnCreate(savedInstanceState);
        OnBackPressedDispatcher.AddCallback(this, new BackCallback(this));
        Services.ShareInbox.Handle(this, Intent);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        Services.ShareInbox.Handle(this, intent);
    }

    protected override void OnResume()
    {
        base.OnResume();
        App.Main?.Resumed();
    }

    /// <summary>Android 13+ no longer calls OnBackPressed: the app registers its own callback and goes to the background when it has nothing left to close.</summary>
    private sealed class BackCallback(MainActivity activity) : OnBackPressedCallback(true)
    {
        public override void HandleOnBackPressed()
        {
            if (App.Main?.HandleBack() == true) return;
            activity.MoveTaskToBack(true);
        }
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        CameraCapture.OnResult(requestCode, resultCode);
    }
}
