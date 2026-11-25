using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace MS3DPRINT.Manager.Desktop.Rendering;

/// <summary>
/// Pause like KeyShot: a button on the image (or Space) stops the preview at once; framing and settings stay editable and are
/// rendered on resume. The engines stay loaded while paused, for an immediate resume, and close when the page is left.
/// </summary>
public sealed partial class RenderStudioView
{
    private readonly Button _pauseButton = new() { Classes = { "tool" }, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12) };
    private readonly Border _pauseBadge = new()
    {
        Classes = { "toolbar" }, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12),
        Padding = new Thickness(12, 4), IsHitTestVisible = false, IsVisible = false,
        Child = new TextBlock { Text = "Pause · Espace pour reprendre", Classes = { "caption" } }
    };
    private bool _paused;
    private bool _suspended;

    private void InitializePause()
    {
        _frame.Children.Add(_pauseBadge);
        _frame.Children.Add(new Border { Classes = { "toolbar" }, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12), Child = _pauseButton });
        _pauseButton.Margin = new Thickness(0);
        _pauseButton.Click += (_, _) => SetPaused(!_paused);
        AddHandler(KeyDownEvent, (_, e) =>
        {
            // Space toggles the pause, except while typing in a field.
            if (e.Key != Key.Space || e.KeyModifiers != KeyModifiers.None || e.Source is TextBox or ComboBox or Avalonia.Controls.Slider) return;
            SetPaused(!_paused);
            e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _paused = _saved.Paused;
        ShowPause();
    }

    private void SetPaused(bool paused)
    {
        if (_paused == paused) return;
        _paused = paused;
        ShowPause();
        SavePreferences();
        if (paused)
        {
            _refineCancel?.Cancel();
            ShowStatus("Aperçu en pause : réglez et cadrez librement, rien n’est calculé. Le rendu final reste disponible.");
        }
        else
        {
            _generation++;
            RunPreviews();
        }
    }

    private void ShowPause()
    {
        _pauseButton.Content = Icons.Glyph(_paused ? "IconPlay" : "IconPause", 18);
        var tip = _paused ? "Reprendre l’aperçu (Espace)" : "Mettre l’aperçu en pause (Espace)";
        ToolTip.SetTip(_pauseButton, tip);
        AutomationProperties.SetName(_pauseButton, tip);
        _pauseBadge.IsVisible = _paused;
    }

    /// <summary>The page is left: both Blenders close, giving back their memory; they start again on the next visit.</summary>
    public void Suspend()
    {
        if (_live is null || _suspended) return;
        _refineCancel?.Cancel();
        _finalRender?.Cancel();
        _live.Dispose();
        _refine?.Dispose();
        _suspended = true;
    }

    /// <summary>Back on the page after <see cref="Suspend"/>: the engines warm up again and the preview catches up.</summary>
    private void ResumeEngines()
    {
        if (!_suspended || _live is null || _refine is null) return;
        _suspended = false;
        _ = _live.WarmUpAsync("eevee");
        WarmRefine(_refine);
        if (!_paused && _stlPath is not null) { _generation++; RunPreviews(); }
    }
}
