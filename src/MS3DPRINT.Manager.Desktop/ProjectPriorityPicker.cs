using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MS3DPRINT.Manager.Core.Projects;

namespace MS3DPRINT.Manager.Desktop;

public static class ProjectPriorityVisual
{
    public static readonly ProjectPriority[] Choices = [ProjectPriority.High, ProjectPriority.Medium, ProjectPriority.Low, ProjectPriority.None];
    public static string Text(ProjectPriority value) => value switch
    {
        ProjectPriority.High => "Haute priorité", ProjectPriority.Medium => "Priorité moyenne",
        ProjectPriority.Low => "Basse priorité", _ => "Aucune priorité"
    };
    public static Control Flag(ProjectPriority value)
    {
        var color = (IBrush?)Application.Current?.FindResource(value switch { ProjectPriority.High => "DangerBrush", ProjectPriority.Medium => "WarningBrush", ProjectPriority.Low => "AccentBrush", _ => "MutedTextBrush" }) ?? Brushes.Gray;
        var icon = (Viewbox)Icons.Glyph("IconFlag", 18);
        var path = (Avalonia.Controls.Shapes.Path)icon.Child!;
        path.Stroke = color; path.Fill = value == ProjectPriority.None ? Brushes.Transparent : color;
        return icon;
    }
    public static Control Label(ProjectPriority value) => new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { Flag(value), new TextBlock { Text = Text(value), VerticalAlignment = VerticalAlignment.Center } } };
}

public sealed class ProjectPriorityPicker : StackPanel
{
    private readonly List<(Button Button, ProjectPriority Priority)> _choices = [];
    private readonly TextBlock _hint = new() { Classes = { "muted" }, FontSize = 13 };
    private ProjectPriority _value;
    public ProjectPriority Value
    {
        get => _value;
        set
        {
            if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value));
            _value = value;
            foreach (var choice in _choices)
            {
                choice.Button.Classes.Set("selected", choice.Priority == value);
                Avalonia.Automation.AutomationProperties.SetName(choice.Button, ProjectPriorityVisual.Text(choice.Priority) + (choice.Priority == value ? ", sélectionnée" : ""));
            }
            _hint.Text = ProjectPriorityVisual.Text(value);
        }
    }
    public ProjectPriorityPicker(ProjectPriority initial = ProjectPriority.None)
    {
        Spacing = 8;
        var options = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var priority in ProjectPriorityVisual.Choices)
        {
            var button = new Button { Content = ProjectPriorityVisual.Flag(priority), Classes = { "priority-choice" }, Width = 44, MinHeight = 42 };
            ToolTip.SetTip(button, ProjectPriorityVisual.Text(priority));
            Avalonia.Automation.AutomationProperties.SetName(button, ProjectPriorityVisual.Text(priority));
            button.Click += (_, _) => Value = priority;
            _choices.Add((button, priority)); options.Children.Add(button);
        }
        Children.Add(options); Children.Add(_hint);
        Value = Enum.IsDefined(initial) ? initial : ProjectPriority.None;
    }
}
