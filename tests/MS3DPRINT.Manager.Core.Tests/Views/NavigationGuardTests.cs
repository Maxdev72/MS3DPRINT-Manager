using System.Reflection;
using System.Windows;
using MS3DPRINT.Manager.App;

namespace MS3DPRINT.Manager.Core.Tests.Views;

public sealed class NavigationGuardTests
{
    [Theory]
    [InlineData(false, MessageBoxResult.Cancel, false, true, 0)]
    [InlineData(true, MessageBoxResult.Cancel, false, false, 0)]
    [InlineData(true, MessageBoxResult.No, false, true, 0)]
    [InlineData(true, MessageBoxResult.Yes, true, true, 1)]
    [InlineData(true, MessageBoxResult.Yes, false, false, 1)]
    public void LeaveDecision_RespectsSaveDiscardAndStay(bool changed, MessageBoxResult choice, bool saveSucceeds, bool expected, int saves)
    {
        var guard = typeof(MainWindow).Assembly.GetType("MS3DPRINT.Manager.App.NavigationGuard");
        Assert.NotNull(guard);
        var method = guard.GetMethod("TryLeave", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var saveCount = 0;
        var result = (bool)method.Invoke(null, [changed, (Func<MessageBoxResult>)(() => choice), (Func<bool>)(() => { saveCount++; return saveSucceeds; })])!;
        Assert.Equal(expected, result);
        Assert.Equal(saves, saveCount);
    }
}
