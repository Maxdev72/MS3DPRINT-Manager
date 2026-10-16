using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using MS3DPRINT.Estimator;

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
    private readonly Button _autoOrient = new() { Content = "Orientation automatique", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _autoGoal = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Moins de supports", "Plus rapide", "Plus stable" }, SelectedIndex = 0 };
    private readonly Button _saveOriented = new() { Content = "Enregistrer le modèle orienté…", HorizontalAlignment = HorizontalAlignment.Stretch };
    private CancellationTokenSource? _autoOrientCancel;
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
        _autoOrient.Click += async (_, _) => await GuardedAsync(AutoOrientAsync);
        _orientGroup.Children.Add(Muted("Orientation automatique : objectif"));
        _orientGroup.Children.Add(_autoGoal);
        _orientGroup.Children.Add(_autoOrient);
        _saveOriented.Click += async (_, _) => await GuardedAsync(SaveOrientedAsync);
        _orientGroup.Children.Add(_saveOriented);
        _orientGroup.Children.Add(_resetOrientation);
        _orientGroup.Children.Add(_orientStatus);
    }

    /// <summary>Runs a flow started from an async void handler: nothing may escape, or the process would crash.</summary>
    private async Task GuardedAsync(Func<Task> flow)
    {
        try { await flow(); }
        catch (Exception exception) { _orientStatus.Text = "Opération impossible : " + exception.Message; }
    }

    /// <summary>
    /// Asks the engine for the best way to stand the part and applies it. A second click while it works cancels. The candidates are measured
    /// with supports, whatever the supports box says: that is what tells two positions apart.
    /// </summary>
    private async Task AutoOrientAsync()
    {
        if (_autoOrientCancel is not null) { _autoOrientCancel.Cancel(); return; }
        if (_mesh is not { } mesh) return;
        var (settings, machine) = CurrentEstimateInputs();
        if (settings is null)
        {
            _orientStatus.Text = "Saisissez un remplissage et une épaisseur de parois valides pour évaluer les orientations.";
            return;
        }
        var scale = TryNumber(_scale.Text, out var s) ? s : 1;
        var goal = (AutoOrientGoal)Math.Max(0, _autoGoal.SelectedIndex);
        var plate = _plate.ToVector3();
        var version = _loadVersion;
        using var cancellation = new CancellationTokenSource();
        _autoOrientCancel = cancellation;
        _autoOrient.Content = "Annuler";
        _orientStatus.Text = "Évaluation des orientations…";
        var progress = new Progress<double>(value => { if (_autoOrientCancel == cancellation) _orientStatus.Text = $"Évaluation des orientations… {value:P0}"; });
        try
        {
            var proposal = await Task.Run(() => AutoOrient.Propose(ModelAssessment.ToPrintMesh(mesh, scale), settings, machine, goal, plate, progress, cancellation.Token), cancellation.Token);
            if (version != _loadVersion || !ReferenceEquals(_mesh, mesh)) { _orientStatus.Text = string.Empty; return; }
            if (cancellation.IsCancellationRequested) { _orientStatus.Text = "Évaluation annulée."; return; }
            // a mesh with holes gives estimates that cannot be trusted
            var unreliable = _feasibility?.Issues.Any(issue => issue.Code is FeasibilityCode.OpenMesh or FeasibilityCode.NonManifold) == true
                ? " Le maillage n'est pas étanche : ce résultat est peu fiable."
                : string.Empty;
            if (!proposal.IsImprovement)
            {
                _orientStatus.Text = (proposal.Current.FitsPlate
                    ? "L’orientation actuelle est déjà la meilleure parmi celles essayées."
                    : "Aucune orientation essayée ne tient sur le plateau : la pièce est trop grande.") + unreliable;
                return;
            }
            _orientation.Compose(proposal.Best.Rotation);
            OrientationChanged();
            var supports = (proposal.Best.SupportVolumeMm3 - proposal.Current.SupportVolumeMm3) / 1000;
            var minutes = (proposal.Best.TimeSeconds - proposal.Current.TimeSeconds) / 60;
            _orientStatus.Text = $"Orientation appliquée : supports {supports:+0.##;−0.##;0} cm³, durée {minutes:+0;−0;0} min"
                + (proposal.Best.FitsPlate ? "." : ", mais la pièce dépasse encore du plateau.")
                + (proposal.Limited ? " Modèle très volumineux : seuls les six axes ont été essayés." : string.Empty)
                + unreliable;
        }
        catch (OperationCanceledException) { _orientStatus.Text = "Évaluation annulée."; }
        catch (ArgumentException exception) { _orientStatus.Text = "Évaluation impossible : " + exception.Message; }
        catch (Exception exception) { _orientStatus.Text = "Évaluation impossible : " + exception.Message; }
        finally
        {
            _autoOrientCancel = null;
            _autoOrient.Content = "Orientation automatique";
        }
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
        double.TryParse(text?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value) && Math.Abs(value) <= 3600;

    private void SyncAngles()
    {
        _syncingAngles = true;
        var angles = _orientation.EulerDegrees;
        _angleX.Text = ModelOrientation.FormatAngle(angles.X);
        _angleY.Text = ModelOrientation.FormatAngle(angles.Y);
        _angleZ.Text = ModelOrientation.FormatAngle(angles.Z);
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
        // a wall analysis in flight or finished belongs to the old position: stop it and clear its text (the viewport drops the markers)
        _activeAnalysis?.Cancel();
        _analysisSummary.IsVisible = false;
        _analysis.Text = "Analyse indicative sur échantillons, sans certification d’imprimabilité. Aucun fichier n’est modifié.";
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
        _sliceCancel?.Cancel();
        ScheduleSlice();
        ClearAssessment();
        ScheduleAssessment();
        RefreshVolume();
        PushPlate(_viewport);
        _viewport.RequestRender(false);
    }

    private void UpdateFaceMode()
    {
        var on = _faceToggle.IsChecked == true;
        _viewport.Visual.Cursor = on || _measureToggle.IsChecked == true ? new Cursor(StandardCursorType.Cross) : null;
        if (on) _message.Text = "Cliquez la face à poser sur le plateau. Échap annule.";
        else UpdateModeMessage();
    }

    /// <summary>Lays the face under the click on the plate.</summary>
    private async Task FaceClickAsync(Point click)
    {
        if (_faceToggle.IsChecked != true || _mesh is not { } mesh) return;
        var bounds = _viewport.Visual.Bounds;
        var camera = new ViewCamera { Yaw = _viewport.Camera.Yaw, Pitch = _viewport.Camera.Pitch, Zoom = _viewport.Camera.Zoom, PanX = _viewport.Camera.PanX, PanY = _viewport.Camera.PanY };
        var cutPlane = _viewport.CutPlane;
        var picked = await Task.Run(() =>
        {
            var (origin, direction) = ViewMath.Ray(mesh, camera, bounds.Width, bounds.Height, click.X, click.Y);
            if (!MeshPicker.TryPick(mesh, origin, direction, out _, out var triangle, cutPlane)) return (System.Numerics.Vector3?)null;
            var normal = mesh.Normals[triangle];
            if (normal.Length() < 1e-6f) return null; // degenerate triangle: no usable face
            // the face that looks at the camera is the outer one, whichever way the triangle is wound
            if (System.Numerics.Vector3.Dot(normal, direction) > 0) normal = -normal;
            return normal;
        });
        if (_faceToggle.IsChecked != true || !ReferenceEquals(_mesh, mesh)) return;
        if (picked is not { } face)
        {
            _message.Text = "Aucune face à cet endroit.";
            return;
        }
        _orientation.FaceDown(face);
        _faceToggle.IsChecked = false;
        OrientationChanged();
        _orientStatus.Text = "Face posée sur le plateau.";
    }

    /// <summary>Writes the part as shown to a temporary STL in millimetres, for the slicer. A partial file is removed when writing fails.</summary>
    private async Task<string> WriteOrientedCopyAsync(MeshModel mesh)
    {
        var path = Path.Combine(Path.GetTempPath(), "ms3d-oriented-" + Guid.NewGuid().ToString("N") + ".stl");
        var scale = MeasureScale;
        try
        {
            await Task.Run(() => StlWriter.Write(path, mesh, scale));
        }
        catch
        {
            try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
        return path;
    }

    /// <summary>Saves the part as shown to an STL in millimetres, next to the model by default. The original file is not touched.</summary>
    private async Task SaveOrientedAsync()
    {
        if (_mesh is not { } mesh || _modelPath is null) return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        try
        {
            var folder = Path.GetDirectoryName(_modelPath);
            var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Enregistrer le modèle orienté",
                SuggestedFileName = Path.GetFileNameWithoutExtension(_modelPath) + "__orienté",
                DefaultExtension = "stl",
                FileTypeChoices = [new FilePickerFileType("STL (millimètres)") { Patterns = ["*.stl"] }],
                SuggestedStartLocation = folder is null ? null : await storage.TryGetFolderFromPathAsync(folder)
            });
            var local = file?.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(local)) return;
            if (string.Equals(Path.GetFullPath(local), Path.GetFullPath(_modelPath), StringComparison.OrdinalIgnoreCase))
            {
                _orientStatus.Text = "Choisissez un autre nom : le fichier d’origine n’est jamais remplacé.";
                return;
            }
            var scale = MeasureScale;
            await Task.Run(() => StlWriter.Write(local, mesh, scale));
            _orientStatus.Text = "Modèle orienté enregistré : " + Path.GetFileName(local) + ".";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            _orientStatus.Text = "Enregistrement impossible : " + exception.Message;
        }
        catch (Exception exception)
        {
            _orientStatus.Text = "Enregistrement impossible : " + exception.Message;
        }
    }
}
