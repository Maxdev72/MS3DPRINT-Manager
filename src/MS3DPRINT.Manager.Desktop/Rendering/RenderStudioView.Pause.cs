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
    private readonly Button _pauseButton = new() { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12), Padding = new Thickness(12, 8) };
    // Paused must be obvious at a glance: a coloured frame around the image and a banner on it.
    private readonly Border _pauseBadge = new()
    {
        Background = (Avalonia.Media.IBrush?)Application.Current?.FindResource("WarningBrush"), CornerRadius = new CornerRadius(Ui.Radius.Pill),
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12),
        Padding = new Thickness(16, 8), IsHitTestVisible = false, IsVisible = false,
        Child = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            Children = { Icons.Glyph("IconPause", 16), new TextBlock { Text = "APERÇU EN PAUSE · Espace pour reprendre", FontWeight = Avalonia.Media.FontWeight.SemiBold, Foreground = Avalonia.Media.Brushes.Black } }
        }
    };
    private readonly Border _pauseFrame = new()
    {
        BorderBrush = (Avalonia.Media.IBrush?)Application.Current?.FindResource("WarningBrush"), BorderThickness = new Thickness(4), IsHitTestVisible = false, IsVisible = false
    };
    private bool _paused;
    private bool _suspended;

    private void InitializePause()
    {
        _frame.Children.Add(_pauseFrame);
        _frame.Children.Add(_pauseBadge);
        _frame.Children.Add(_pauseButton);
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
            ShowStatus("Aperçu en pause : la pièce est figée, la caméra et les réglages restent libres ; rien n’est calculé. Le rendu final reste disponible.");
        }
        else
        {
            _generation++;
            RunPreviews();
        }
    }

    private void ShowPause()
    {
        _pauseButton.Content = Icons.Label(_paused ? "IconPlay" : "IconPause", _paused ? "Reprendre" : "Pause", 16);
        if (_paused) _pauseButton.Classes.Add("primary-action"); else _pauseButton.Classes.Remove("primary-action");
        _pauseFrame.IsVisible = _paused;
        LockPartWhilePaused();
        var tip = _paused ? "Reprendre l’aperçu (Espace)" : "Mettre l’aperçu en pause (Espace)";
        ToolTip.SetTip(_pauseButton, tip);
        AutomationProperties.SetName(_pauseButton, tip);
        _pauseBadge.IsVisible = _paused;
    }

    /// <summary>Paused, the parts stay where they are: moving, turning and laying them is off until the preview resumes.</summary>
    private void LockPartWhilePaused()
    {
        var editable = !_paused && _parts.Count > 0;
        if (!editable) { _transformMode.IsChecked = false; _faceMode.IsChecked = false; }
        _partTools.IsEnabled = _transformMode.IsEnabled = _splitParts.IsEnabled = editable;
        ToolTip.SetShowOnDisabled(_transformMode, true);
        ToolTip.SetTip(_transformMode, _paused ? "Reprenez l’aperçu pour déplacer ou tourner la pièce" : "Flèches : déplacer · Anneaux : tourner · Maj : pas de déplacement · Échap : quitter");
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
