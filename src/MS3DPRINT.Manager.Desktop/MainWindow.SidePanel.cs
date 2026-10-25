using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.Controls;
using Avalonia.VisualTree;

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
        var opener = FocusManager?.GetFocusedElement() as Control;
        var background = RootGrid.Children.Where(control => control != SidePanelOverlay).Select(control => (Control: control, Enabled: control.IsEnabled)).ToArray();
        foreach (var item in background) item.Control.IsEnabled = false;
        KeyboardNavigation.SetTabNavigation(SidePanelOverlay, KeyboardNavigationMode.Cycle);
        _panel = dialog;
        SidePanel.Classes.Set("motion-reduced", MotionReduced);
        SidePanel.Width = dialog.PanelWidth;
        SidePanelBody.Child = dialog.BeginPanel(accepted =>
        {
            _panel = null;
            SidePanel.Classes.Remove("open");
            SidePanelOverlay.IsVisible = false;
            SidePanelBody.Child = null;
            foreach (var item in background) item.Control.IsEnabled = item.Enabled;
            if (opener is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true }) opener.Focus();
            result.TrySetResult(accepted);
        });
        SidePanelOverlay.IsVisible = true;
        BrandMotion.DimBackground(SidePanelScrim, MotionReduced);
        Dispatcher.UIThread.Post(() =>
        {
            if (_panel != dialog) return;
            SidePanel.Classes.Add("open");
            dialog.RaisePanelOpened();
            if (FocusManager?.GetFocusedElement() is not Control focus || !SidePanelBody.GetVisualDescendants().Contains(focus))
                (SidePanelBody.GetVisualDescendants().OfType<Control>().FirstOrDefault(control => (control is TextBox or ComboBox) && control.IsEffectivelyVisible && control.IsEffectivelyEnabled && control.Focusable)
                    ?? SidePanelBody.GetVisualDescendants().OfType<Button>().FirstOrDefault(button => button.IsEffectivelyVisible && button.IsEffectivelyEnabled))?.Focus();
        }, DispatcherPriority.Loaded);
        return result.Task;
    }

    private void SidePanel_KeyDown(object? sender, KeyEventArgs e) => _panel?.HandleKey(e);
}
