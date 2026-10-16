using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace MS3DPRINT.Manager.Desktop.Viewer;

/// <summary>Turning the part on the plate: the loaded model, how it is turned, and the part as shown that follows from both.</summary>
public sealed partial class ModelViewerView
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    private readonly ModelOrientation _orientation = new();
    private MeshModel? _original;
    private readonly StackPanel _orientGroup = new() { Spacing = 8, IsEnabled = false };
    private readonly TextBox _angleX = new() { Text = "0" };
    private readonly TextBox _angleY = new() { Text = "0" };
    private readonly TextBox _angleZ = new() { Text = "0" };
    private readonly Button _resetOrientation = new() { Content = "Réinitialiser l’orientation", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _orientStatus = new() { Classes = { "caption" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private bool _syncingAngles;

    private void InitializeOrientation()
    {
        Button Turn(string text, int axis, double degrees)
        {
            var button = new Button { Content = text, HorizontalAlignment = HorizontalAlignment.Stretch };
            button.Click += (_, _) => { _orientation.RotateAxis(axis, degrees); OrientationChanged(); };
            return button;
        }

        var turns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto"), ColumnSpacing = 6, RowSpacing = 6 };
        var buttons = new[] { Turn("X −90°", 0, -90), Turn("X +90°", 0, 90), Turn("Y −90°", 1, -90), Turn("Y +90°", 1, 90), Turn("Z −90°", 2, -90), Turn("Z +90°", 2, 90) };
        for (var i = 0; i < buttons.Length; i++)
        {
            Grid.SetRow(buttons[i], i / 2);
            Grid.SetColumn(buttons[i], i % 2);
            turns.Children.Add(buttons[i]);
        }

        var angles = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,*,Auto,*"), ColumnSpacing = 6 };
        var fields = new[] { ("X", _angleX), ("Y", _angleY), ("Z", _angleZ) };
        for (var i = 0; i < fields.Length; i++)
        {
            var label = Muted(fields[i].Item1);
            Grid.SetColumn(label, i * 2);
            Grid.SetColumn(fields[i].Item2, i * 2 + 1);
            angles.Children.Add(label);
            angles.Children.Add(fields[i].Item2);
            fields[i].Item2.LostFocus += (_, _) => ApplyAngles();
            fields[i].Item2.KeyDown += (_, e) => { if (e.Key == Key.Enter) { ApplyAngles(); e.Handled = true; } };
        }

        _resetOrientation.Click += (_, _) => { _orientation.Reset(); OrientationChanged(); };
        _orientGroup.Children.Add(FormKit.SectionLabel("Orientation"));
        _orientGroup.Children.Add(new TextBlock { Text = "La pièce reste posée sur le plateau. Les rotations se font autour des axes du plateau.", Classes = { "caption" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        _orientGroup.Children.Add(turns);
        _orientGroup.Children.Add(Muted("Angles en degrés (X, puis Y, puis Z)"));
        _orientGroup.Children.Add(angles);
        _orientGroup.Children.Add(_resetOrientation);
        _orientGroup.Children.Add(_orientStatus);
    }

    /// <summary>Reads the three angle fields; when one is not a number they go back to the current orientation.</summary>
    private void ApplyAngles()
    {
        if (_syncingAngles || _original is null) return;
        if (TryAngle(_angleX.Text, out var x) && TryAngle(_angleY.Text, out var y) && TryAngle(_angleZ.Text, out var z))
        {
            var current = _orientation.EulerDegrees;
            // nothing to do when the fields still say what the orientation already is
            if (Math.Abs(current.X - x) < 0.05 && Math.Abs(current.Y - y) < 0.05 && Math.Abs(current.Z - z) < 0.05) return;
            _orientation.SetEuler(x, y, z);
            OrientationChanged();
        }
        else SyncAngles();
    }

    private static bool TryAngle(string? text, out double value) =>
        double.TryParse(text?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);

    private void SyncAngles()
    {
        _syncingAngles = true;
        var angles = _orientation.EulerDegrees;
        _angleX.Text = angles.X.ToString("0.#", French);
        _angleY.Text = angles.Y.ToString("0.#", French);
        _angleZ.Text = angles.Z.ToString("0.#", French);
        _syncingAngles = false;
        _resetOrientation.IsEnabled = !_orientation.IsIdentity;
    }

    /// <summary>The orientation changed: the part as shown is derived again from the loaded model.</summary>
    private void OrientationChanged()
    {
        if (_original is null) return;
        SyncAngles();
        ShowOrientedMesh(_orientation.Apply(_original));
    }

    /// <summary>
    /// Shows a new position of the same model. What was drawn on the old position (measures, cut, comparison) belongs to another frame
    /// of reference and goes; the camera stays where it was and every figure computed from the part is computed again.
    /// </summary>
    private void ShowOrientedMesh(MeshModel mesh)
    {
        ClearComparison();
        ClearMeasures();
        _cutToggle.IsChecked = false;
        _mesh = mesh;
        _overlay.Mesh = mesh;
        var camera = _viewport.Camera;
        double yaw = camera.Yaw, pitch = camera.Pitch, zoom = camera.Zoom, panX = camera.PanX, panY = camera.PanY;
        _viewport.SetMesh(mesh);
        camera.Yaw = yaw; camera.Pitch = pitch; camera.Zoom = zoom; camera.PanX = panX; camera.PanY = panY;
        _viewport.WindingInverted = _measures?.Inverted == true;
        UpdateCut();
        RefreshDimensions();
        _slice = null;
        ScheduleSlice();
        ClearAssessment();
        ScheduleAssessment();
        RefreshVolume();
        _viewport.RequestRender(false);
    }
}
