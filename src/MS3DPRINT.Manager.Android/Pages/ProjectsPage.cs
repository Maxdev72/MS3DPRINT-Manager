using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MS3DPRINT.Manager.Companion.Services;
using MS3DPRINT.Manager.Companion.Ui;
using MS3DPRINT.Manager.Core.Mobile;
using MS3DPRINT.Manager.Core.Revenues;

namespace MS3DPRINT.Manager.Companion.Pages;

/// <summary>The projects of the PC, searchable, with the money side of each one. Read-only: the phone never changes a project.</summary>
public sealed class ProjectsPage : UserControl
{
    private static readonly (string Label, Func<SnapshotProject, bool> Match)[] Filters =
    [
        ("Actifs", p => !p.Archived && p.Status != "Completed"),
        ("Devis", p => !p.Archived && p.Status == "Quote"),
        ("En cours", p => !p.Archived && p.Status == "InProgress"),
        ("Terminés", p => !p.Archived && p.Status == "Completed"),
        ("Archivés", p => p.Archived),
        ("Tous", _ => true)
    ];

    private readonly AppServices _app = AppServices.Current;
    private readonly Action<bool, SnapshotProject> _openEntry;
    private readonly TextBox _search = Ui.Ui.Input(new TextBox { Watermark = "Rechercher un projet, un client, une référence" });
    private readonly StackPanel _list = new() { Spacing = 10 };
    private readonly TextBlock _count = Ui.Ui.Muted("");
    private readonly Grid _tiles = new() { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = 8 };
    private readonly List<Button> _chips = [];
    private readonly ContentControl _host = new();
    private readonly Control _listView;
    private int _filter;

    public ProjectsPage(Action<bool, SnapshotProject> openEntry)
    {
        _openEntry = openEntry;
        var chipRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        for (var i = 0; i < Filters.Length; i++)
        {
            var index = i;
            var chip = new Button { Content = new TextBlock { Text = Filters[i].Label, FontSize = 14, FontWeight = FontWeight.SemiBold }, MinHeight = 40, CornerRadius = new CornerRadius(20), Padding = new Thickness(16, 6), BorderThickness = new Thickness(1) };
            chip.Click += (_, _) => { _filter = index; Paint(); Rebuild(); };
            _chips.Add(chip); chipRow.Children.Add(chip);
        }
        _search.TextChanged += (_, _) => Rebuild();
        var chips = new ScrollViewer { Content = chipRow, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        var top = new StackPanel { Spacing = 12, Margin = new Thickness(16, 12, 16, 4), Children = { _tiles, _search, chips, _count } };
        var scroll = new ScrollViewer { Content = new StackPanel { Children = { top, new Border { Margin = new Thickness(16, 4, 16, 24), Child = _list } } }, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        _listView = scroll;
        _host.Content = _listView;
        Content = _host;
        _app.Changed += Rebuild;
        Paint(); Rebuild();
    }

    public bool CanGoBack => !ReferenceEquals(_host.Content, _listView);
    public void GoBack() => _host.Content = _listView;

    private void Paint()
    {
        for (var i = 0; i < _chips.Count; i++)
        {
            var on = i == _filter;
            _chips[i].Background = on ? Pal.Brand : Pal.Raised;
            _chips[i].BorderBrush = on ? Pal.Brand : Pal.Border;
            ((TextBlock)_chips[i].Content!).Foreground = on ? Brushes.White : Pal.Muted;
        }
    }

    private void Rebuild()
    {
        _list.Children.Clear();
        var snapshot = _app.Snapshot;
        _tiles.Children.Clear();
        _tiles.IsVisible = snapshot is not null;
        if (snapshot is not null)
        {
            var live = snapshot.Projects.Where(p => !p.Archived && p.Status != "Completed").ToList();
            var late = live.Count(p => p.DueDate is { } d && d < DateOnly.FromDateTime(DateTime.Today));
            var left = snapshot.Projects.Where(p => !p.Archived && p.Quoted is { } q && q > p.Collected).Sum(p => p.Quoted!.Value - p.Collected);
            Control[] tiles = [Look.Tile("projets actifs", live.Count.ToString(CultureInfo.InvariantCulture)), Look.Tile("en retard", late.ToString(CultureInfo.InvariantCulture), late > 0 ? Pal.Danger : Pal.Text), Look.Tile("à encaisser", RevenueExporter.Money(left), left > 0 ? Pal.Warning : Pal.Text)];
            for (var i = 0; i < tiles.Length; i++) { Grid.SetColumn(tiles[i], i); _tiles.Children.Add(tiles[i]); }
        }
        if (snapshot is null)
        {
            _count.Text = "";
            _list.Children.Add(Ui.Ui.EmptyState("Aucun état de l’atelier reçu", _app.SnapshotProblem ?? "Ouvrez MS3DPRINT Manager sur le PC : il publie ses projets dans le dossier d’échange, puis actualisez ici."));
            return;
        }
        var words = (_search.Text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var found = snapshot.Projects.Where(Filters[_filter].Match)
            .Where(p => words.All(w => p.Reference.Contains(w, StringComparison.CurrentCultureIgnoreCase) || p.Title.Contains(w, StringComparison.CurrentCultureIgnoreCase) || p.ClientName.Contains(w, StringComparison.CurrentCultureIgnoreCase)))
            .ToList();
        _count.Text = $"{found.Count} projet{(found.Count > 1 ? "s" : "")}";
        if (found.Count == 0) { _list.Children.Add(Ui.Ui.EmptyState("Aucun projet", "Aucun projet ne correspond à cette recherche ou à ce filtre.")); return; }
        foreach (var project in found.Take(200)) _list.Children.Add(Card(project));
        if (found.Count > 200) _list.Children.Add(Ui.Ui.Muted("Les 200 premiers projets sont affichés : affinez la recherche."));
    }

    public static (string Text, IBrush Back, IBrush Fore) StatusStyle(string status) => status switch
    {
        "Quote" => ("Devis", Pal.AccentSoft, Pal.Accent),
        "InProgress" => ("En cours", Pal.WarningSoft, Pal.Warning),
        "Completed" => ("Terminé", Pal.SuccessSoft, Pal.Success),
        _ => (status, Pal.Neutral, Pal.Muted)
    };

    private Control Card(SnapshotProject project)
    {
        var (statusText, back, fore) = StatusStyle(project.Status);
        var overdue = project.DueDate is { } due && due < DateOnly.FromDateTime(DateTime.Today) && project.Status != "Completed" && !project.Archived;
        var left = new StackPanel { Spacing = 3, Children = { Ui.Ui.Text(project.Title, 17, Pal.Text, bold: true), Ui.Ui.Muted(project.ClientName), Ui.Ui.Text(project.Reference, 12, Pal.Faint) } };
        var right = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right, Children = { Ui.Ui.Pill(project.Archived ? "Archivé" : statusText, project.Archived ? Pal.Neutral : back, project.Archived ? Pal.Muted : fore) } };
        right.Children[0].HorizontalAlignment = HorizontalAlignment.Right;
        if (project.DueDate is { } date) right.Children.Add(new TextBlock { Text = date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), FontSize = 13, Foreground = overdue ? Pal.Danger : Pal.Muted, HorizontalAlignment = HorizontalAlignment.Right, FontWeight = overdue ? FontWeight.SemiBold : FontWeight.Normal });
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12, Children = { left, right } };
        Grid.SetColumn(right, 1);
        var body = new StackPanel { Spacing = 10, Children = { row } };
        if (project.Quoted is { } quoted && quoted > 0)
        {
            body.Children.Add(new ProgressBar { Minimum = 0, Maximum = 1, Value = Math.Min(1, (double)(project.Collected / quoted)), Height = 6, Foreground = project.Collected >= quoted ? Pal.Success : Pal.Accent, Background = Pal.Raised });
            body.Children.Add(new TextBlock { Text = $"{RevenueExporter.Money(project.Collected)} encaissés sur {RevenueExporter.Money(quoted)}", FontSize = 12, Foreground = Pal.Faint });
        }
        var stripe = new Border { Width = 5, Background = project.Archived ? Pal.Neutral : fore, CornerRadius = new CornerRadius(14, 0, 0, 14) };
        var inner = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Children = { stripe, new Border { Padding = new Thickness(14, 14, 16, 14), Child = body } } };
        Grid.SetColumn(inner.Children[1], 1);
        var card = new Button { Content = inner, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(0), Background = Pal.Surface, BorderBrush = Pal.Subtle, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), ClipToBounds = true };
        card.Click += (_, _) => _host.Content = Detail(project);
        return card;
    }

    private Control Detail(SnapshotProject project)
    {
        var (statusText, back, fore) = StatusStyle(project.Status);
        var stack = new StackPanel { Spacing = 14, Margin = new Thickness(16, 12, 16, 28) };
        var backButton = Ui.Ui.Quiet("Retour aux projets", Glyph.Back); backButton.HorizontalAlignment = HorizontalAlignment.Left; backButton.Click += (_, _) => GoBack();
        stack.Children.Add(backButton);
        stack.Children.Add(Ui.Ui.Title(project.Title));
        stack.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Ui.Ui.Pill(project.Archived ? "Archivé" : statusText, project.Archived ? Pal.Neutral : back, project.Archived ? Pal.Muted : fore), Ui.Ui.Pill(project.Reference, Pal.Neutral, Pal.Muted) } });
        stack.Children.Add(Ui.Ui.Card(new StackPanel { Spacing = 0, Children =
        {
            Row("Client", project.ClientName),
            Row("Échéance", project.DueDate is { } d ? d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : "Non définie"),
            Row("Priorité", project.Priority switch { "High" => "Haute", "Medium" => "Moyenne", "Low" => "Basse", _ => "Aucune" })
        } }));
        stack.Children.Add(Ui.Ui.Section("Bilan du projet"));
        var balance = new StackPanel { Spacing = 0 };
        balance.Children.Add(Row("Prix estimé (dernier devis)", project.Quoted is { } q ? RevenueExporter.Money(q) : "Pas de devis"));
        balance.Children.Add(Row("Encaissé", RevenueExporter.Money(project.Collected)));
        if (project.Quoted is { } quoted && quoted > project.Collected) balance.Children.Add(Row("Reste à encaisser", RevenueExporter.Money(quoted - project.Collected), Pal.Warning));
        balance.Children.Add(Row("Achats liés", RevenueExporter.Money(project.Purchases)));
        balance.Children.Add(Row("Encaissé − achats", RevenueExporter.Money(project.Collected - project.Purchases), project.Collected - project.Purchases < 0 ? Pal.Danger : Pal.Success));
        stack.Children.Add(Ui.Ui.Card(balance));
        stack.Children.Add(Ui.Ui.Muted("Cotisations, électricité et usure des machines ne sont pas déduites.", 12));
        var revenue = Ui.Ui.Primary("Noter un encaissement", Glyph.Plus); revenue.Click += (_, _) => _openEntry(true, project);
        var purchase = Ui.Ui.Secondary("Noter un achat pour ce projet", Glyph.Plus); purchase.Click += (_, _) => _openEntry(false, project);
        stack.Children.Add(revenue); stack.Children.Add(purchase);
        return new ScrollViewer { Content = stack, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    }

    private static Control Row(string label, string value, IBrush? valueBrush = null)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12, MinHeight = 44 };
        var name = new TextBlock { Text = label, FontSize = 15, Foreground = Pal.Muted, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        var amount = new TextBlock { Text = value, FontSize = 16, FontWeight = FontWeight.SemiBold, Foreground = valueBrush ?? Pal.Text, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(amount, 1); grid.Children.Add(name); grid.Children.Add(amount);
        return new Border { BorderBrush = Pal.Subtle, BorderThickness = new Thickness(0, 0, 0, 1), Child = grid };
    }
}
