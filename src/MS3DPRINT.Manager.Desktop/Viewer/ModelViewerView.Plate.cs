using System.Numerics;
using Avalonia.Controls;
using Avalonia.Layout;

namespace MS3DPRINT.Manager.Desktop.Viewer;

/// <summary>The build plate: which printer's volume the part is stood on, whether it fits, and how it is drawn.</summary>
public sealed partial class ModelViewerView
{
    private sealed record PlateChoice(string Label, PlateSize? Size, bool Custom = false);

    private readonly Func<IReadOnlyList<ViewerPrinter>>? _loadPrinters;
    private readonly ComboBox _plateChoice = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _plateText = new() { PlaceholderText = "Largeur × profondeur × hauteur, en mm", IsVisible = false };
    private readonly TextBlock _plateStatus = new() { Classes = { "caption" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private PlateSize _plate = PlateSize.Default;

    private void InitializePlate()
    {
        _plateChoice.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<PlateChoice>((choice, _) => new TextBlock { Text = choice?.Label ?? string.Empty });
        FillPlates(selected: null);
        _plateChoice.SelectionChanged += (_, _) => ApplyPlate();
        _plateText.TextChanged += (_, _) => ApplyPlate();
    }

    /// <summary>Generic plate, then every printer whose volume can be read, then free entry; keeps the choice that was made when possible.</summary>
    private void FillPlates(string? selected)
    {
        var choices = new List<PlateChoice> { new($"Plateau générique ({PlateSize.Default.Label})", PlateSize.Default) };
        IReadOnlyList<ViewerPrinter> printers = [];
        try { printers = _loadPrinters?.Invoke() ?? []; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException) { }
        foreach (var printer in printers)
            if (PlateSize.Parse(printer.BuildVolume) is { } size) choices.Add(new PlateChoice($"{printer.Name} ({size.Label})", size));
        choices.Add(new PlateChoice("Saisir les dimensions…", null, Custom: true));
        _plateChoice.ItemsSource = choices;
        _plateChoice.SelectedItem = choices.FirstOrDefault(choice => choice.Label == selected) ?? choices[0];
    }

    /// <summary>Reloads the printers of the workshop (they may have changed since the viewer was last shown).</summary>
    private void ReloadPlates()
    {
        var selected = (_plateChoice.SelectedItem as PlateChoice)?.Label;
        FillPlates(selected);
    }

    /// <summary>Takes the plate from the choice, tells whether the part fits, shows it, and checks the feasibility against it.</summary>
    /// <summary>The custom text could not be read and the generic plate stands in for it.</summary>
    private bool _plateTextUnreadable;

    private void ApplyPlate()
    {
        var choice = _plateChoice.SelectedItem as PlateChoice;
        if (choice?.Custom == true)
        {
            _plateText.IsVisible = true;
            var parsed = PlateSize.Parse(_plateText.Text);
            _plateTextUnreadable = parsed is null;
            _plate = parsed ?? PlateSize.Default;
        }
        else
        {
            _plateText.IsVisible = false;
            _plateTextUnreadable = false;
            _plate = choice?.Size ?? PlateSize.Default;
        }
        PushPlate(_viewport);
        _viewport.RequestRender(false);
        ScheduleAssessment();
    }

    /// <summary>Gives the plate to a view, in the units of the model, and writes whether the part fits.</summary>
    private void PushPlate(IMeshViewport viewport)
    {
        var scale = MeasureScale > 0 ? MeasureScale : 1;
        var unreadable = _plateTextUnreadable ? "Dimensions illisibles : le plateau générique 256 × 256 × 256 mm est utilisé. " : string.Empty;
        viewport.PlateHalfWidth = (float)(_plate.WidthMm / 2 / scale);
        viewport.PlateHalfDepth = (float)(_plate.DepthMm / 2 / scale);
        if (_mesh is not { } mesh)
        {
            viewport.PlateOverflow = false;
            _plateStatus.Text = $"{unreadable}Plateau {_plate.Label}.";
            return;
        }
        var size = mesh.Size * (float)scale;
        var overflow = _plate.Overflow(size);
        viewport.PlateOverflow = overflow != Vector3.Zero;
        if (overflow == Vector3.Zero)
        {
            _plateStatus.Text = $"{unreadable}Plateau {_plate.Label} : la pièce tient.";
            return;
        }
        var parts = new List<string>();
        if (overflow.X > 0) parts.Add($"{overflow.X.ToString("0.#", French)} mm en largeur");
        if (overflow.Y > 0) parts.Add($"{overflow.Y.ToString("0.#", French)} mm en profondeur");
        if (overflow.Z > 0) parts.Add($"{overflow.Z.ToString("0.#", French)} mm en hauteur");
        _plateStatus.Text = $"{unreadable}Plateau {_plate.Label} : la pièce dépasse de {string.Join(", ", parts)}. Essayez une autre orientation.";
    }
}
