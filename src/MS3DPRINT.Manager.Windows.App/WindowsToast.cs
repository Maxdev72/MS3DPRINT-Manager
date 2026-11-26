namespace MS3DPRINT.Manager.Windows.App;

/// <summary>
/// The "ms3dprint://surveillance" link once used by Windows notifications. Notifications now stay inside the
/// application; the link is still handled so a notification left in the Windows centre opens Surveillance.
/// </summary>
internal static class WindowsToast
{
    public const string SurveillanceLink = "ms3dprint://surveillance";
}
