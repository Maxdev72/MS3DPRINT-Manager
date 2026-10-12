using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MS3DPRINT.Manager.Core.Files;
using MS3DPRINT.Manager.Windows;

namespace MS3DPRINT.Manager.Windows.App;

/// <summary>Print options for a PDF: printer, number of copies and pages. Nothing is sent until "Imprimer" is pressed.</summary>
public sealed class PdfPrintWindow : Window
{
    private readonly ComboBox _printer = new() { MinWidth = 320 };
    private readonly TextBox _copies = new() { Text = "1", Width = 80 };
    private readonly TextBox _pages = new() { PlaceholderText = "Toutes (ex. 1-3,5)" };
    private readonly TextBlock _error = new() { Foreground = Brushes.IndianRed, IsVisible = false, TextWrapping = TextWrapping.Wrap };
    private readonly int _pageCount;

    public PdfPrintRequest? Request { get; private set; }

    public PdfPrintWindow(IReadOnlyList<string> printers, string? defaultPrinter, int pageCount, int currentPage, string documentName)
    {
        _pageCount = pageCount;
        Title = "Imprimer";
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Width = 500; Height = 440; MinWidth = 420;
        _printer.ItemsSource = printers;
        _printer.SelectedItem = defaultPrinter is not null && printers.Contains(defaultPrinter) ? defaultPrinter : printers.FirstOrDefault();

        var all = new Button { Content = "Toutes" };
        all.Click += (_, _) => _pages.Text = string.Empty;
        var current = new Button { Content = $"Page affichée ({currentPage + 1})" };
        current.Click += (_, _) => _pages.Text = (currentPage + 1).ToString();

        var print = new Button { Content = "Imprimer", Classes = { "primary-action" } };
        print.Click += (_, _) => Validate();
        var cancel = new Button { Content = "Annuler" };
        cancel.Click += (_, _) => Close(false);

        var muted = Application.Current?.TryFindResource("MutedTextBrush", Application.Current.ActualThemeVariant, out var brush) == true ? brush as IBrush : null;
        var document = new TextBlock { Text = $"{documentName} · {pageCount} page(s)", TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = FontWeight.SemiBold };
        Content = new StackPanel
        {
            Margin = new Thickness(24), Spacing = 10,
            Children =
            {
                new TextBlock { Text = "Imprimer le PDF", FontSize = 20, FontWeight = FontWeight.SemiBold },
                document,
                Label("Imprimante"), _printer,
                Label("Exemplaires"), _copies,
                Label("Pages"), _pages,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { all, current } },
                new TextBlock { Text = "Chaque page est ajustée à la feuille, en portrait ou paysage selon le format du document.", FontSize = 12, Foreground = muted, TextWrapping = TextWrapping.Wrap },
                _error,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, print } }
            }
        };
    }

    private static TextBlock Label(string text) => new() { Text = text, FontSize = 12 };

    private void Validate()
    {
        try
        {
            if (_printer.SelectedItem is not string printer) throw new FormatException("Aucune imprimante n’est installée.");
            if (!int.TryParse(_copies.Text, out var copies) || copies is < 1 or > 99) throw new FormatException("Le nombre d’exemplaires doit être compris entre 1 et 99.");
            Request = new PdfPrintRequest(printer, copies, PrintPageSelection.Parse(_pages.Text, _pageCount));
            Close(true);
        }
        catch (FormatException exception)
        {
            _error.Text = exception.Message;
            _error.IsVisible = true;
        }
    }
}
