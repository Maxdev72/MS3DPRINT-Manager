using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Input.Platform;
using Avalonia.Media;

namespace MS3DPRINT.Manager.Desktop;

internal sealed class NotificationEntry(string title, string message, bool error, string? detail)
{
    public string Title { get; } = title;
    public string Message { get; } = message;
    public bool Error { get; } = error;
    public string? Detail { get; } = detail;
    public DateTimeOffset Created { get; } = DateTimeOffset.Now;
    public bool Read { get; set; }
}

internal sealed class NotificationHistory
{
    public List<NotificationEntry> Entries { get; } = [];
    public event Action? Changed;
    public int Unread => Entries.Count(entry => !entry.Read);
    public void Add(NotificationEntry entry)
    {
        Entries.Insert(0, entry);
        if (Entries.Count > 200) Entries.RemoveRange(200, Entries.Count - 200);
        Changed?.Invoke();
    }
    public void MarkRead() { foreach (var entry in Entries) entry.Read = true; Changed?.Invoke(); }
    public void Clear() { Entries.Clear(); Changed?.Invoke(); }
}

public sealed class NotificationCenterWindow : DialogWindow
{
    private readonly NotificationHistory _history;
    private readonly Action _refresh;

    internal NotificationCenterWindow(MainWindow owner, NotificationHistory history, NotificationEntry? selected = null)
    {
        _history = history;
        Title = "Centre de notifications"; Width = 600; Height = 760; MinWidth = 460;
        var filter = new ComboBox { ItemsSource = new[] { "Toutes", "Erreurs", "Non lues" }, SelectedIndex = 0 };
        var count = new TextBlock { Classes = { "muted" }, TextWrapping = TextWrapping.Wrap };
        Avalonia.Automation.AutomationProperties.SetLiveSetting(count, Avalonia.Automation.AutomationLiveSetting.Polite);
        var list = new StackPanel { Spacing = 12 };
        var read = new Button { Content = "Tout marquer comme lu", Classes = { "ghost" } };
        var clear = new Button { Content = "Effacer l’historique", Classes = { "ghost" } };
        var feedback = new TextBlock { TextWrapping = TextWrapping.Wrap, Classes = { "muted" } };
        read.Click += (_, _) => history.MarkRead();
        clear.Click += async (_, _) =>
        {
            if (await new ConfirmRemovalWindow("historique des notifications", "Les notifications de cette session seront retirées. Vos documents et les résultats des opérations sont conservés.").ShowDialog<bool>(owner)) history.Clear();
        };
        _refresh = () =>
        {
            var entries = history.Entries.Where(entry => filter.SelectedIndex switch { 1 => entry.Error, 2 => !entry.Read, _ => true })
                .OrderBy(entry => ReferenceEquals(entry, selected) ? 0 : 1).ToArray();
            count.Text = $"{entries.Length} résultat{(entries.Length > 1 ? "s" : "")} · {history.Unread} non lue{(history.Unread > 1 ? "s" : "")} au total";
            read.IsEnabled = history.Unread > 0; clear.IsEnabled = history.Entries.Count > 0;
            list.Children.Clear();
            foreach (var entry in entries)
            {
                var title = new TextBlock { Text = entry.Title, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold, MaxLines = 3, TextTrimming = TextTrimming.CharacterEllipsis };
                ToolTip.SetTip(title, entry.Title);
                var metadata = new TextBlock { Text = $"{entry.Created:dd/MM HH:mm} · {(entry.Error ? "Erreur" : "Information")} · {(entry.Read ? "Lue" : "Non lue")}", Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
                var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 12 };
                var icon = Icons.Glyph(entry.Error ? "IconAlert" : "IconCheck", 18);
                var labels = new StackPanel { Spacing = 4, Children = { title, metadata } };
                Grid.SetColumn(labels, 1); heading.Children.Add(icon); heading.Children.Add(labels);
                var detail = string.Join(Environment.NewLine + Environment.NewLine, new[] { entry.Message, entry.Detail }.Where(text => !string.IsNullOrWhiteSpace(text)));
                if (detail.Length == 0) detail = entry.Title;
                var copy = new Button { Content = "Copier les détails", Classes = { "ghost" } };
                copy.Click += async (_, _) =>
                {
                    try
                    {
                        if (owner.Clipboard is not { } clipboard) throw new InvalidOperationException("Presse-papiers indisponible.");
                        await clipboard.SetTextAsync(entry.Title + Environment.NewLine + detail);
                        feedback.Text = "Détails copiés.";
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException or System.Runtime.InteropServices.ExternalException)
                    { feedback.Text = "Impossible de copier les détails : " + exception.Message; }
                };
                list.Children.Add(new Expander
                {
                    IsExpanded = ReferenceEquals(entry, selected),
                    Header = heading, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Content = new StackPanel { Spacing = 12, Children = { new SelectableTextBlock { Text = detail, TextWrapping = TextWrapping.Wrap }, copy } }
                });
            }
            if (entries.Length == 0) list.Children.Add(new TextBlock
            {
                Text = filter.SelectedIndex switch { 1 => "Aucune erreur dans cet historique.", 2 => "Toutes les notifications ont été lues.", _ => "Aucune notification pour le moment." },
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16)
            });
        };
        filter.SelectionChanged += (_, _) => _refresh();
        history.Changed += _refresh;
        var actions = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 8, LineSpacing = 8, Children = { read, clear } };
        var close = new Button { Content = "Fermer" }; close.Click += (_, _) => Close(false);
        Layout("Notifications", "Les 200 dernières notifications de cette session restent consultables, même après la disparition des messages temporaires.",
            [FormKit.Field("Afficher", filter), actions, count, list, feedback], close, new Button { IsVisible = false });
        _refresh();
    }

    protected override void OnDialogClosed() => _history.Changed -= _refresh;
}
