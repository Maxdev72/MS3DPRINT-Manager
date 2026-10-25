using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;

namespace MS3DPRINT.Manager.Companion;

[Application]
public class AndroidApp : AvaloniaAndroidApplication<App>
{
    public AndroidApp(IntPtr handle, JniHandleOwnership transfer) : base(handle, transfer) { }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) => base.CustomizeAppBuilder(builder);
}
