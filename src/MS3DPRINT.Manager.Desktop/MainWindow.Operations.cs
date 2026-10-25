using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Automation;

namespace MS3DPRINT.Manager.Desktop;

public partial class MainWindow
{
    private readonly HashSet<string> _pendingOperations = new(StringComparer.OrdinalIgnoreCase);

    private sealed class PendingOperation(Action finish) : IDisposable
    {
        private Action? _finish = finish;
        public void Dispose() { var finish = _finish; _finish = null; finish?.Invoke(); }
    }

    private IDisposable? TryBeginOperation(string key, string label, Button? button = null, string? buttonLabel = null)
    {
        if (!_pendingOperations.Add(key)) return null;
        var content = button?.Content;
        var enabled = button?.IsEnabled ?? true;
        var status = button is null ? null : AutomationProperties.GetItemStatus(button);
        if (button is not null)
        {
            button.IsEnabled = false;
            if (buttonLabel is not null) button.Content = buttonLabel;
            AutomationProperties.SetItemStatus(button, "Opération en cours");
        }
        var row = new TextBlock { Text = label, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetLiveSetting(row, AutomationLiveSetting.Polite);
        OperationsList.Children.Add(row); OperationsPanel.IsVisible = true;
        BeginBusy();
        return new PendingOperation(() =>
        {
            _pendingOperations.Remove(key);
            OperationsList.Children.Remove(row);
            OperationsPanel.IsVisible = OperationsList.Children.Count > 0;
            if (button is not null)
            {
                button.Content = content; button.IsEnabled = enabled;
                AutomationProperties.SetItemStatus(button, status);
            }
            EndBusy();
        });
    }

    private static string RestoreOperationKey(string root, Guid id) => root + "|restore|" + id;
}
