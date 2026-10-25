using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;

namespace MS3DPRINT.Manager.Companion;

[Application(Icon = "@mipmap/ic_launcher", RoundIcon = "@mipmap/ic_launcher_round", Label = "MS3DPRINT Compagnon")]
public class AndroidApp : AvaloniaAndroidApplication<App>
{
    public AndroidApp(IntPtr handle, JniHandleOwnership transfer) : base(handle, transfer) { }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) => base.CustomizeAppBuilder(builder);
}
