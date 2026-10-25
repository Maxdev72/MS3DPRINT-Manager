using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.LogicalTree;
using Avalonia.Threading;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>
/// Base of every dialog: same chrome (heading, description, scrolling form, footer with actions),
/// centred on its owner; Escape cancels, Enter confirms non-destructive forms only.
/// </summary>
public abstract class DialogWindow : Window
{
    private Button? _primary;
    private bool _enterConfirms = true;

    /// <summary>The heading of the dialog, for a dialog whose heading follows its content.</summary>
    protected TextBlock? HeadingText { get; private set; }

    protected DialogWindow()
    {
        if (DesktopApplication.ReduceMotion) Classes.Add("reduce-motion");
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
    }

    /// <summary>Builds the standard layout. Destructive actions require explicit activation; other forms accept Enter.</summary>
    protected void Layout(string heading, string? description, IEnumerable<Control> body, Button primary, Button? cancel = null, bool destructive = false, Button? extraAction = null)
    {
        _primary = primary;
        _enterConfirms = !destructive;
        primary.Classes.Add(destructive ? "danger" : "primary-action");
        if (cancel is null)
        {
            cancel = new Button { Content = "Annuler" };
            cancel.Click += (_, _) => Close(false);
        }
        if (destructive) Opened += (_, _) => cancel.Focus();

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right };
        if (extraAction is not null) actions.Children.Add(extraAction);
        actions.Children.Add(cancel);
        actions.Children.Add(primary);

        var headings = new StackPanel { Spacing = 8 };
        HeadingText = new TextBlock { Text = heading, Classes = { "brand-heading" }, FontSize = 26, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
        headings.Children.Add(HeadingText);
        if (!string.IsNullOrWhiteSpace(description))
            headings.Children.Add(new TextBlock { Text = description, LineHeight = 22, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap });
        var close = new Button { Content = Icons.Glyph("IconClose", 18), Classes = { "tool" }, VerticalAlignment = VerticalAlignment.Top };
        Avalonia.Automation.AutomationProperties.SetName(close, "Fermer sans enregistrer");
        ToolTip.SetTip(close, "Fermer sans enregistrer · Échap");
        close.Click += (_, _) => Close(false);
        var headerGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 14, Children = { headings, close } };
        Grid.SetColumn(close, 1);
        var header = new Border { Padding = new Thickness(28, 26, 24, 22), BorderBrush = (IBrush?)Application.Current?.FindResource("SubtleBorderBrush"), BorderThickness = new Thickness(0, 0, 0, 1), Child = headerGrid };
        var form = new StackPanel { Margin = new Thickness(28, 22, 28, 22), Spacing = 20 };
        foreach (var control in body) form.Children.Add(control);

        var footer = new Border
        {
            Background = (IBrush?)Application.Current?.FindResource("SidebarBrush"),
            BorderBrush = (IBrush?)Application.Current?.FindResource("SubtleBorderBrush"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(26, 14),
            Child = actions
        };
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Classes = { "dialog-form" } };
        root.Children.Add(header);
        var scroll = new ScrollViewer { Content = form, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); root.Children.Add(scroll);
        Grid.SetRow(footer, 2);
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
        else if (_enterConfirms && e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None && _primary is { IsEnabled: true } &&
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
        var caption = new TextBlock { Text = label, FontSize = 14, FontWeight = FontWeight.Medium, TextWrapping = TextWrapping.Wrap, Classes = { "form-label" } };
        Avalonia.Automation.AutomationProperties.SetName(input, label);
        Avalonia.Automation.AutomationProperties.SetLabeledBy(input, caption);
        var help = string.Join(" ", new[] { string.IsNullOrWhiteSpace(unit) ? null : "Unité : " + unit + ".", hint }.Where(text => !string.IsNullOrWhiteSpace(text)));
        if (help.Length > 0) Avalonia.Automation.AutomationProperties.SetHelpText(input, help);
        Control row = input;
        if (unit is not null)
        {
            var suffix = new TextBlock { Text = unit, Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center, MinWidth = 36 };
            Grid.SetColumn(suffix, 1);
            row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8, Children = { input, suffix } };
        }
        var field = new StackPanel { Spacing = 7, Children = { caption, row } };
        // A field whose input is hidden (e.g. company name for an individual) hides its label too.
        field.Bind(Visual.IsVisibleProperty, input.GetObservable(Visual.IsVisibleProperty));
        var inlineError = new TextBlock { IsVisible = false, TextWrapping = TextWrapping.Wrap, FontSize = 13, Foreground = (IBrush?)Application.Current?.FindResource("DangerBrush") ?? Brushes.IndianRed };
        field.Children.Add(inlineError);
        FieldErrors.Remove(input); FieldErrors.Add(input, inlineError);
        if (hint is not null) field.Children.Add(new TextBlock { Text = hint, Classes = { "muted" }, FontSize = 13, LineHeight = 20, TextWrapping = TextWrapping.Wrap });
        return field;
    }

    public static TextBlock ErrorText()
    {
        var error = new TextBlock
        {
            Foreground = (IBrush?)Application.Current?.FindResource("DangerBrush") ?? Brushes.IndianRed,
            IsVisible = false,
            TextWrapping = TextWrapping.Wrap
        };
        Avalonia.Automation.AutomationProperties.SetLiveSetting(error, Avalonia.Automation.AutomationLiveSetting.Polite);
        return error;
    }

    private sealed record ErrorLink(Control Input, string? Help, string? Status, object? Tip, EventHandler<AvaloniaPropertyChangedEventArgs> Changed);
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TextBlock, ErrorLink> ErrorLinks = new();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, TextBlock> FieldErrors = new();

    /// <summary>A submitted error belongs to one field; editing that field clears its stale error without revalidating each keystroke.</summary>
    public static void ShowError(TextBlock error, Control input, string message)
    {
        ClearError(error);
        var help = Avalonia.Automation.AutomationProperties.GetHelpText(input);
        var status = Avalonia.Automation.AutomationProperties.GetItemStatus(input);
        EventHandler<AvaloniaPropertyChangedEventArgs> changed = (_, args) =>
        {
            if (args.Property == TextBox.TextProperty || args.Property == ComboBox.SelectedItemProperty || args.Property == Avalonia.Controls.Primitives.ToggleButton.IsCheckedProperty) ClearError(error);
        };
        ErrorLinks.Add(error, new(input, help, status, ToolTip.GetTip(input), changed));
        input.PropertyChanged += changed;
        input.Classes.Add("form-invalid"); ToolTip.SetTip(input, message);
        if (FieldErrors.TryGetValue(input, out var inline)) { inline.Text = message; inline.IsVisible = true; }
        Avalonia.Automation.AutomationProperties.SetHelpText(input, string.Join(" ", new[] { help, "Erreur : " + message }.Where(text => !string.IsNullOrWhiteSpace(text))));
        Avalonia.Automation.AutomationProperties.SetItemStatus(input, "Erreur : " + message);
        error.Text = message; error.IsVisible = true;
        foreach (var section in input.GetLogicalAncestors().OfType<Expander>()) section.IsExpanded = true;
        Dispatcher.UIThread.Post(() =>
        {
            if (error.IsVisible && ErrorLinks.TryGetValue(error, out var current) && ReferenceEquals(current.Input, input)
                && input.IsEffectivelyVisible && input.IsEffectivelyEnabled && TopLevel.GetTopLevel(input) is not null)
            { input.BringIntoView(); input.Focus(); }
        }, DispatcherPriority.Loaded);
    }

    public static void ClearError(TextBlock error)
    {
        if (ErrorLinks.TryGetValue(error, out var link))
        {
            link.Input.PropertyChanged -= link.Changed;
            Avalonia.Automation.AutomationProperties.SetHelpText(link.Input, link.Help);
            Avalonia.Automation.AutomationProperties.SetItemStatus(link.Input, link.Status);
            ToolTip.SetTip(link.Input, link.Tip); link.Input.Classes.Remove("form-invalid");
            if (FieldErrors.TryGetValue(link.Input, out var inline)) { inline.IsVisible = false; inline.Text = string.Empty; }
            ErrorLinks.Remove(error);
        }
        error.IsVisible = false; error.Text = string.Empty;
    }

    public static TextBlock SectionLabel(string text) => new() { Text = text.ToUpperInvariant(), Classes = { "overline" }, Margin = new Thickness(0, 6, 0, 0) };
}
