using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>
/// Base of every dialog: same chrome (heading, description, scrolling form, footer with actions),
/// centred on its owner, Escape cancels and Enter confirms.
/// </summary>
public abstract class DialogWindow : Window
{
    private Button? _primary;

    /// <summary>The heading of the dialog, for a dialog whose heading follows its content.</summary>
    protected TextBlock? HeadingText { get; private set; }

    protected DialogWindow()
    {
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
    }

    /// <summary>Builds the standard layout. <paramref name="primary"/> becomes the accent button and the Enter key target.</summary>
    protected void Layout(string heading, string? description, IEnumerable<Control> body, Button primary, Button? cancel = null, bool destructive = false, Button? extraAction = null)
    {
        _primary = primary;
        primary.Classes.Add(destructive ? "danger" : "primary-action");
        if (cancel is null)
        {
            cancel = new Button { Content = "Annuler" };
            cancel.Click += (_, _) => Close(false);
        }

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right };
        if (extraAction is not null) actions.Children.Add(extraAction);
        actions.Children.Add(cancel);
        actions.Children.Add(primary);

        var form = new StackPanel { Margin = new Thickness(26, 24, 26, 14), Spacing = 14 };
        HeadingText = new TextBlock { Text = heading, Classes = { "section-title" }, TextWrapping = TextWrapping.Wrap };
        form.Children.Add(HeadingText);
        if (!string.IsNullOrWhiteSpace(description))
            form.Children.Add(new TextBlock { Text = description, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap });
        foreach (var control in body) form.Children.Add(control);

        var footer = new Border
        {
            Background = (IBrush?)Application.Current?.FindResource("SidebarBrush"),
            BorderBrush = (IBrush?)Application.Current?.FindResource("SubtleBorderBrush"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(26, 14),
            Child = actions
        };
        var root = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        root.Children.Add(new ScrollViewer { Content = form, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        Grid.SetRow(footer, 1);
        root.Children.Add(footer);
        Content = root;
    }

    // ---- side panel hosting: the same dialog can be shown in the main window instead of its own window ----

    private Action<bool>? _panelDone;

    /// <summary>Width the dialog takes as a side panel.</summary>
    internal double PanelWidth => double.IsNaN(Width) ? 460 : Math.Clamp(Width, 440, 600);

    /// <summary>Hands the content over to the panel; <paramref name="done"/> is called once, with the result, when the dialog closes.</summary>
    internal Control BeginPanel(Action<bool> done)
    {
        _panelDone = done;
        var content = (Control)Content!;
        Content = null;
        return content;
    }

    internal void RaisePanelOpened() => OnOpened(EventArgs.Empty);

    /// <summary>Called when the dialog closes, in its own window or in the panel.</summary>
    protected virtual void OnDialogClosed() { }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        OnDialogClosed();
    }

    /// <summary>Closes the dialog with its result (true confirms), whether it sits in a window or in the side panel.</summary>
    public new void Close(object? dialogResult)
    {
        if (_panelDone is { } done)
        {
            _panelDone = null;
            OnDialogClosed();
            done(dialogResult is true);
        }
        else base.Close(dialogResult);
    }

    /// <summary>Shows the dialog in the side panel of the main window; any other owner gets a regular window.</summary>
    public new async Task<TResult> ShowDialog<TResult>(Window owner)
    {
        if (typeof(TResult) == typeof(bool) && owner is MainWindow { CanShowPanel: true } main)
            return (TResult)(object)await main.ShowPanelAsync(this);
        return await base.ShowDialog<TResult>(owner);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        HandleKey(e);
    }

    internal void HandleKey(KeyEventArgs e)
    {
        if (e.Handled) return;
        if (e.Key == Key.Escape)
        {
            Close(false);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None && _primary is { IsEnabled: true } &&
                 e.Source is not Button && e.Source is not TextBox { AcceptsReturn: true })
        {
            _primary.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            e.Handled = true;
        }
    }
}

/// <summary>Small building blocks shared by the forms.</summary>
public static class FormKit
{
    /// <summary>Label above the control; an optional unit sits to its right and an optional hint below it.</summary>
    public static StackPanel Field(string label, Control input, string? unit = null, string? hint = null)
    {
        if (input is ComboBox or TextBox) input.HorizontalAlignment = HorizontalAlignment.Stretch;
        var caption = new TextBlock { Text = label, Classes = { "caption" } };
        Control row = input;
        if (unit is not null)
        {
            var suffix = new TextBlock { Text = unit, Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center, MinWidth = 36 };
            Grid.SetColumn(suffix, 1);
            row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8, Children = { input, suffix } };
        }
        var field = new StackPanel { Spacing = 5, Children = { caption, row } };
        // A field whose input is hidden (e.g. company name for an individual) hides its label too.
        field.Bind(Visual.IsVisibleProperty, input.GetObservable(Visual.IsVisibleProperty));
        if (hint is not null) field.Children.Add(new TextBlock { Text = hint, Classes = { "faint" }, FontSize = 12, TextWrapping = TextWrapping.Wrap });
        return field;
    }

    public static TextBlock ErrorText() => new()
    {
        Foreground = (IBrush?)Application.Current?.FindResource("DangerBrush") ?? Brushes.IndianRed,
        IsVisible = false,
        TextWrapping = TextWrapping.Wrap
    };

    public static TextBlock SectionLabel(string text) => new() { Text = text.ToUpperInvariant(), Classes = { "overline" }, Margin = new Thickness(0, 6, 0, 0) };
}
