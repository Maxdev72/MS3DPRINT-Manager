using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Globalization;

namespace MS3DPRINT.Manager.Desktop;

public partial class MainWindow
{
    private readonly List<TextBlock> _settingsMatches = [];
    private int _settingsMatchIndex = -1, _settingsMatchGeneration;
    private TextDecorationCollection? _settingsOriginalDecorations;

    private void RestoreCurrentSettingsMatch()
    {
        if (_settingsMatchIndex < 0) return;
        var target = _settingsMatches[_settingsMatchIndex];
        target.TextDecorations = _settingsOriginalDecorations;
        target.Classes.Remove("settings-search-current");
        _settingsOriginalDecorations = null;
    }

    private static bool MatchesSettingText(string? text, string query)
        => !string.IsNullOrEmpty(text) && CultureInfo.GetCultureInfo("fr-FR").CompareInfo.IndexOf(text, query, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;

    private void QueueSettingsMatches(string query)
    {
        var generation = ++_settingsMatchGeneration;
        RestoreCurrentSettingsMatch();
        foreach (var text in _settingsMatches) { text.Classes.Remove("settings-search-hit"); text.Classes.Remove("settings-search-current"); }
        _settingsMatches.Clear(); _settingsMatchIndex = -1;
        if (query.Length == 0) return;
        // Filtered sections must be measured before locating a label inside them.
        Dispatcher.UIThread.Post(() =>
        {
            if (generation != _settingsMatchGeneration || !SettingsWorkspacePanel.IsVisible) return;
            SettingsScroll.UpdateLayout();
            _settingsMatches.AddRange(StorageList.Children.OfType<Border>().Where(card => card.IsVisible)
                .SelectMany(card => card.GetVisualDescendants().OfType<TextBlock>())
                .Where(text => text.IsEffectivelyVisible && MatchesSettingText(text.Text, query)));
            foreach (var text in _settingsMatches) text.Classes.Add("settings-search-hit");
            if (_settingsMatches.Count == 0) return; // A category name can match without an individual label.
            SettingsScroll.UpdateLayout();
            ShowSettingsMatch(0);
        }, DispatcherPriority.Render);
    }

    private void ShowSettingsMatch(int index)
    {
        if (_settingsMatches.Count == 0) return;
        RestoreCurrentSettingsMatch();
        _settingsMatchIndex = (index + _settingsMatches.Count) % _settingsMatches.Count;
        var target = _settingsMatches[_settingsMatchIndex]; target.Classes.Add("settings-search-current");
        _settingsOriginalDecorations = target.TextDecorations;
        // Decorations belong to this window's UI thread, like the labels they adorn.
        target.TextDecorations = new TextDecorationCollection
        {
            new TextDecoration { Location = TextDecorationLocation.Underline }
        };
        SettingsScroll.UpdateLayout();
        if (SettingsScroll.Content is Visual content && target.TranslatePoint(default, content) is { } position)
            SettingsScroll.Offset = new Vector(0, Math.Max(0, position.Y - 16));
        var sections = StorageList.Children.Count(card => card.IsVisible);
        SettingsDescription.Text = $"Correspondance {_settingsMatchIndex + 1} sur {_settingsMatches.Count} · {sections} section{(sections > 1 ? "s" : "")} · Entrée : suivante · Maj+Entrée : précédente";
    }
}
