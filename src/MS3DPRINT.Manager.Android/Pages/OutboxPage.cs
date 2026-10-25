using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MS3DPRINT.Manager.Companion.Services;
using MS3DPRINT.Manager.Companion.Ui;
using MS3DPRINT.Manager.Core.Revenues;

namespace MS3DPRINT.Manager.Companion.Pages;

/// <summary>What the phone sent, and what still waits for the exchange folder.</summary>
public sealed class OutboxPage : UserControl
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");
    private readonly AppServices _app = AppServices.Current;
    private readonly Action<string, string, Action> _confirm;
    private readonly StackPanel _stack = new() { Spacing = 12, Margin = new Thickness(16, 12, 16, 28) };
    private bool _sending;

    public OutboxPage(Action<string, string, Action> confirm)
    {
        _confirm = confirm;
        Content = new ScrollViewer { Content = _stack, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        _app.Changed += Rebuild;
        Rebuild();
    }

    private void Rebuild()
    {
        _stack.Children.Clear();
        var pending = _app.Data.History.Where(h => h.Status != "Sent").ToList();
        if (pending.Count > 0)
        {
            var failed = pending.Any(h => h.Status == "Failed");
            _stack.Children.Add(Ui.Ui.Banner($"{pending.Count} saisie{(pending.Count > 1 ? "s" : "")} pas encore arrivée{(pending.Count > 1 ? "s" : "")} dans le dossier d’échange" + (_app.Exchange.IsConfigured ? "." : " : choisissez d’abord le dossier dans les Réglages."), failed ? BannerKind.Warning : BannerKind.Info));
            var retry = Ui.Ui.Primary(_sending ? "Envoi en cours…" : "Réessayer l’envoi", Glyph.Refresh);
            retry.IsEnabled = !_sending && _app.Exchange.IsConfigured;
            retry.Click += async (_, _) => { _sending = true; Rebuild(); await _app.SendPendingAsync(); _sending = false; Rebuild(); };
            _stack.Children.Add(retry);
        }
        if (_app.Data.History.Count == 0)
        {
            _stack.Children.Add(Ui.Ui.EmptyState("Rien d’envoyé", "Les achats et recettes que vous notez apparaissent ici, avec leur état."));
            return;
        }
        foreach (var day in _app.Data.History.GroupBy(h => DateOnly.FromDateTime(h.At.ToLocalTime().DateTime)).OrderByDescending(g => g.Key))
        {
            _stack.Children.Add(new TextBlock { Text = Look.DayLabel(day.Key).ToUpperInvariant(), FontSize = 12, FontWeight = FontWeight.Bold, Foreground = Pal.Faint, LetterSpacing = 1.2, Margin = new Thickness(4, 8, 0, 0) });
            foreach (var item in day.OrderByDescending(h => h.At)) _stack.Children.Add(Card(item));
        }
        if (_app.Data.History.Any(h => h.Status == "Sent"))
        {
            var clear = Ui.Ui.Quiet("Effacer les envois confirmés de cette liste", Glyph.Trash);
            clear.Click += (_, _) => _confirm("Effacer l’historique ?", "Seules les lignes déjà envoyées disparaissent de cette liste. Les achats et recettes restent sur le PC.", _app.ClearSent);
            _stack.Children.Add(clear);
        }
    }

    private Control Card(HistoryItem item)
    {
        var (statusText, back, fore) = item.Status switch
        {
            "Sent" => ("Envoyé", Pal.SuccessSoft, Pal.Success),
            "Failed" => ("À renvoyer", Pal.WarningSoft, Pal.Warning),
            _ => ("En attente", Pal.WarningSoft, Pal.Warning)
        };
        var income = item.Kind == "recette";
        var kind = Ui.Ui.Pill(income ? "Recette" : "Achat", income ? Pal.SuccessSoft : Pal.AccentSoft, income ? Pal.Success : Pal.Accent);
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        var amount = new TextBlock { Text = (income ? "+ " : "− ") + RevenueExporter.Money(item.Amount), FontSize = 19, FontWeight = FontWeight.SemiBold, Foreground = income ? Pal.Success : Pal.Text, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(amount, 1);
        head.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { kind, Ui.Ui.Pill(statusText, back, fore) } }); head.Children.Add(amount);
        var stack = new StackPanel { Spacing = 6, Children =
        {
            head,
            Ui.Ui.Text(item.Title, 17, Pal.Text, bold: true),
            Ui.Ui.Muted(item.Label + (string.IsNullOrWhiteSpace(item.ProjectRef) ? "" : " · projet " + item.ProjectRef)),
            Ui.Ui.Text($"{item.Date:dd/MM/yyyy}" + (item.HasPhoto ? (item.AttachmentExt == ".pdf" ? " · facture PDF" : " · photo") : "") + $" · noté le {item.At.ToLocalTime():dd/MM à HH:mm}", 12, Pal.Faint)
        } };
        if (item.Status == "Failed" && item.Error is { Length: > 0 }) stack.Children.Add(Ui.Ui.Text(item.Error, 13, Pal.Danger));
        if (item.Status != "Sent")
        {
            var discard = Ui.Ui.Quiet("Supprimer cette saisie", Glyph.Trash); discard.HorizontalAlignment = HorizontalAlignment.Left;
            discard.Click += (_, _) => _confirm("Supprimer cette saisie ?", $"« {item.Title} », {RevenueExporter.Money(item.Amount)}, n’a pas encore été envoyée. Elle sera définitivement supprimée du téléphone.", () => _app.Discard(item));
            stack.Children.Add(discard);
        }
        return Ui.Ui.Card(stack);
    }
}
