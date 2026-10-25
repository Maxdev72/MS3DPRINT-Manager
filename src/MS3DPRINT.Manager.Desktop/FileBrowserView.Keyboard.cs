using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using MS3DPRINT.Manager.Core.Files;

namespace MS3DPRINT.Manager.Desktop;

public sealed partial class FileBrowserView
{
    public Func<bool>? FileCommandsAvailable { get; set; }
    public Func<string, Task>? RenameSelectionRequested { get; set; }
    private bool _keyboardCommandActive;

    private void InitializeFileKeyboard()
    {
        AutomationProperties.SetHelpText(Rows, "Ctrl+A : sélectionner les fichiers filtrés ; Échap : désélectionner ; F2 : renommer un fichier sélectionné ; Suppr : corbeille avec confirmation.");
        Rows.KeyDown += async (_, e) =>
        {
            if (e.Handled || _keyboardCommandActive || _loading || !Rows.IsEffectivelyVisible || !Rows.IsEffectivelyEnabled
                || !SearchInput.IsEnabled || !(FileCommandsAvailable?.Invoke() ?? true)) return;
            // Future row editors must retain their native text and popup shortcuts too.
            if (e.Source is Control source && source.GetVisualAncestors().Prepend(source)
                .Any(c => c is TextBox or ComboBox or AutoCompleteBox or NumericUpDown)) return;

            if (e.Key == Key.A && e.KeyModifiers == KeyModifiers.Control)
            {
                var family = FamilyInput.SelectedItem is FileFamily selected ? selected : FileFamily.All;
                var files = FileCatalogFilter.Filter(_entries, SearchInput.Text, family).Where(entry => !entry.IsDirectory).ToArray();
                _selectedFiles.Clear(); foreach (var file in files) _selectedFiles.Add(file.FullPath);
                _selectionAnchor = files.FirstOrDefault()?.FullPath;
                UpdateSelection(); e.Handled = true; return;
            }
            if (e.KeyModifiers != KeyModifiers.None) return;
            if (e.Key == Key.Escape && _selectedFiles.Count > 0)
            { ClearSelection(); e.Handled = true; return; }

            Func<Task>? command = null;
            if (e.Key == Key.F2 && _selectedFiles.Count == 1 && RenameSelectionRequested is { } rename)
            { var path = _selectedFiles.Single(); command = () => rename(path); }
            else if (e.Key == Key.Delete && _selectedFiles.Count > 0 && TrashSelectionRequested is { } trash)
            { var paths = _selectedFiles.ToArray(); command = () => trash(paths); }
            if (command is null) return;
            e.Handled = true; _keyboardCommandActive = true;
            try { await command(); }
            finally { _keyboardCommandActive = false; }
        };
    }
}
