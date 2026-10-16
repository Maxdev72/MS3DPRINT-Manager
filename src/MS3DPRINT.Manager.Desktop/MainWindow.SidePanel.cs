using Avalonia.Input;
using Avalonia.Threading;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>Side panel: forms and confirmations slide in over the page, on the right, instead of opening a separate window.</summary>
public partial class MainWindow
{
    private DialogWindow? _panel;

    /// <summary>False while a panel is already open: a second dialog then gets its own window.</summary>
    internal bool CanShowPanel => _panel is null;

    internal Task<bool> ShowPanelAsync(DialogWindow dialog)
    {
        var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _panel = dialog;
        SidePanel.Width = dialog.PanelWidth;
        SidePanelBody.Child = dialog.BeginPanel(accepted =>
        {
            _panel = null;
            SidePanel.Classes.Remove("open");
            SidePanelOverlay.IsVisible = false;
            SidePanelBody.Child = null;
            result.TrySetResult(accepted);
        });
        SidePanelOverlay.IsVisible = true;
        Dispatcher.UIThread.Post(() =>
        {
            if (_panel != dialog) return;
            SidePanel.Classes.Add("open");
            dialog.RaisePanelOpened();
        }, DispatcherPriority.Loaded);
        return result.Task;
    }

    private void SidePanel_KeyDown(object? sender, KeyEventArgs e) => _panel?.HandleKey(e);
}
