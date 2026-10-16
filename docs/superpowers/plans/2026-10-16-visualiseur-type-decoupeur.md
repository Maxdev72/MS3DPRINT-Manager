# Visualiseur 3D type découpeur : plan d'implémentation

> **Pour les agents d'exécution :** utiliser `superpowers:subagent-driven-development` (recommandé) ou `superpowers:executing-plans`
> pour réaliser ce plan tâche par tâche. Les étapes utilisent des cases à cocher (`- [ ]`).

**Objectif :** poser la pièce sur un plateau d'impression aux dimensions d'une imprimante, la réorienter (face au sol par clic,
rotations, orientation automatique) et faire suivre l'orientation par l'estimation, la faisabilité et la découpe Bambu.

**Architecture :** la géométrie d'orientation vit dans la bibliothèque `MS3DPRINT.Estimator` (`PrintMesh.Oriented`, `OrientationMath`,
`AutoOrient`) ; le visualiseur garde `_original` et un état `ModelOrientation`, et en dérive `_mesh` (copie tournée et posée) que tout
le code existant utilise déjà. Le plateau est dessiné par le shader de la grille infinie.

**Stack :** .NET 10, Avalonia 12 (rendu OpenGL ES 3), xUnit. Les tests du moteur sont dans `tests/MS3DPRINT.Estimator.Tests`, ceux
du visualiseur dans `tests/MS3DPRINT.Manager.Core.Tests/Desktop`.

**Spécification :** `docs/superpowers/specs/2026-10-16-visualiseur-type-decoupeur-design.md`.

## Contraintes globales

- Tout est sous `C:\MS3DPRINT\Nextcloud\MS3DPRINT\98_DEVELOPPEMENT\MS3DPRINT_MANAGER\sources` (dépôt Git). Commandes lancées depuis ce dossier.
- `TreatWarningsAsErrors` est actif : aucun avertissement de compilation toléré.
- Le fichier d'origine d'un modèle n'est **jamais** modifié.
- Textes visibles en **français**, commentaires et noms de code en anglais, comme dans le reste du dépôt.
- Les tests d'interface ne sont pas automatisables : la logique est mise dans des classes pures testées, l'écran est vérifié par capture.
- Ne **committer que si l'utilisateur le demande** : les étapes « Commit » ci-dessous sont à sauter sans demande explicite
  (le dépôt contient déjà des modifications non commitées d'autres chantiers : ne jamais faire `git add -A`).
- Livraison de l'exécutable : seulement à la demande, selon `AGENTS.md` (publication, sauvegarde dans `outputs/backups`, remplacement, SHA256).
- Dans `ModelViewerView*.cs`, `Vector3` est `System.Numerics.Vector3` ; `Quaternion.Concatenate(premier, second)` applique `premier` puis `second`
  (ne jamais utiliser l'opérateur `*` sur des quaternions : l'ordre y est inversé).

## Carte des fichiers

| Fichier | Rôle | Tâche |
|---|---|---|
| `src/MS3DPRINT.Estimator/OrientationMath.cs` (nouveau) | rotation la plus courte entre deux directions | 1 |
| `src/MS3DPRINT.Estimator/PrintMesh.cs` | `Oriented(Quaternion)` : tourne, centre en X/Y, pose à Z = 0 | 1 |
| `src/MS3DPRINT.Manager.Desktop/Viewer/PlateSize.cs` (nouveau) | dimensions du plateau, lecture du texte libre | 2 |
| `src/MS3DPRINT.Manager.Desktop/Viewer/ModelOrientation.cs` (nouveau) | état d'orientation, angles d'Euler, `Apply` | 3 |
| `src/MS3DPRINT.Estimator/AutoOrient.cs` (nouveau) | candidats, notes, proposition | 4 |
| `src/MS3DPRINT.Estimator/EstimateSettings.cs`, `PrintEstimator.cs` | `BaseAreaMm2` dans le résultat | 4 |
| `src/MS3DPRINT.Manager.Desktop/Viewer/StlWriter.cs` (nouveau) | écriture STL binaire en millimètres | 5 |
| `src/MS3DPRINT.Manager.Desktop/Viewer/ModelViewerView.Orientation.cs` (nouveau) | boutons, angles, `OrientationChanged`, face au sol, orientation auto, enregistrement | 6, 8, 9, 10 |
| `src/MS3DPRINT.Manager.Desktop/Viewer/ModelViewerView.Plate.cs` (nouveau) | choix du plateau, état, envoi au rendu | 7 |
| `src/MS3DPRINT.Manager.Desktop/Viewer/ModelViewerView.cs` | points d'accroche (chargement, barre d'outils, touches, découpe) | 6 à 10 |
| `src/MS3DPRINT.Manager.Desktop/Viewer/ModelViewerView.Assessment.cs` | entrées d'estimation partagées, volume du plateau | 7, 9 |
| `src/MS3DPRINT.Manager.Desktop/Viewer/GpuMeshViewport.cs`, `GpuViewportHost.cs`, `MeshViewport.cs` | propriétés et shader du plateau | 7 |
| `src/MS3DPRINT.Manager.Desktop/Styles/Theme.axaml` | icône « poser sur une face » | 8 |
| `src/MS3DPRINT.Manager.Desktop/MainWindow.Viewer.cs` | liste des imprimantes avec leur volume | 7 |
| `tests/MS3DPRINT.Estimator.Tests/*`, `tests/MS3DPRINT.Manager.Core.Tests/Desktop/*` | tests | toutes |

---

### Tâche 1 : `OrientationMath` et `PrintMesh.Oriented`

**Fichiers :**
- Créer : `src/MS3DPRINT.Estimator/OrientationMath.cs`
- Modifier : `src/MS3DPRINT.Estimator/PrintMesh.cs`
- Test : `tests/MS3DPRINT.Estimator.Tests/OrientationTests.cs`

**Interfaces :**
- Produit : `OrientationMath.Between(Vector3 from, Vector3 to) -> Quaternion` ; `PrintMesh.Oriented(Quaternion rotation) -> PrintMesh`
  (tourne autour du centre de la boîte englobante, centre le résultat en X et Y sur l'origine, pose le point le plus bas à Z = 0 ; triangles inchangés).

- [ ] **Étape 1 : écrire le test qui échoue**

Créer `tests/MS3DPRINT.Estimator.Tests/OrientationTests.cs` :

```csharp
using System.Numerics;
using MS3DPRINT.Estimator;

namespace MS3DPRINT.Estimator.Tests;

public sealed class OrientationTests
{
    private static PrintMesh Block(float x, float y, float z)
    {
        Vector3[] corners = [new(0, 0, 0), new(x, 0, 0), new(x, y, 0), new(0, y, 0), new(0, 0, z), new(x, 0, z), new(x, y, z), new(0, y, z)];
        int[] faces = [0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 2, 3, 7, 2, 7, 6, 0, 4, 7, 0, 7, 3, 1, 2, 6, 1, 6, 5];
        return new PrintMesh(corners, faces);
    }

    private static void AssertNear(Vector3 expected, Vector3 actual, float tolerance = 1e-4f) =>
        Assert.True(Vector3.Distance(expected, actual) < tolerance, $"attendu {expected}, obtenu {actual}");

    [Fact]
    public void BetweenTakesOneDirectionOntoTheOther()
    {
        var turn = OrientationMath.Between(Vector3.UnitX, -Vector3.UnitZ);

        AssertNear(-Vector3.UnitZ, Vector3.Transform(Vector3.UnitX, turn));
    }

    [Fact]
    public void BetweenTwoEqualDirectionsDoesNothing() =>
        Assert.Equal(Quaternion.Identity, OrientationMath.Between(new Vector3(0, 0, -3), -Vector3.UnitZ));

    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(1, 0, 0)]
    [InlineData(0, 1, 0)]
    public void BetweenOppositeDirectionsIsAHalfTurn(float x, float y, float z)
    {
        var from = new Vector3(x, y, z);

        var turn = OrientationMath.Between(from, -from);

        AssertNear(-from, Vector3.Transform(from, turn));
    }

    [Fact]
    public void OrientedStandsTheMeshOnTheBedAndCentresIt()
    {
        var mesh = Block(10, 20, 30);

        var stood = mesh.Oriented(Quaternion.Identity);

        Assert.Equal(0, stood.Min.Z, 4);
        Assert.Equal(0, (stood.Min.X + stood.Max.X) / 2, 4);
        Assert.Equal(0, (stood.Min.Y + stood.Max.Y) / 2, 4);
        Assert.Equal(new Vector3(10, 20, 30), stood.Size);
    }

    [Fact]
    public void OrientedTurnsAboutTheCentreThenSitsOnTheBed()
    {
        var mesh = Block(10, 20, 30);

        var lying = mesh.Oriented(Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2));

        Assert.Equal(10, lying.Size.X, 3);
        Assert.Equal(30, lying.Size.Y, 3);
        Assert.Equal(20, lying.Size.Z, 3);
        Assert.Equal(0, lying.Min.Z, 4);
    }

    [Fact]
    public void OrientedKeepsTheTrianglesAndLeavesTheSourceUntouched()
    {
        var mesh = Block(10, 20, 30);
        var before = mesh.Positions[6];

        var turned = mesh.Oriented(Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.5f));

        Assert.Same(mesh.Indices, turned.Indices);
        Assert.Equal(before, mesh.Positions[6]);
    }

    [Fact]
    public void OrientedOfAnEmptyMeshIsTheSameMesh()
    {
        var empty = new PrintMesh([], []);

        Assert.Same(empty, empty.Oriented(Quaternion.Identity));
    }
}
```

- [ ] **Étape 2 : vérifier l'échec**

Run : `dotnet test tests/MS3DPRINT.Estimator.Tests --filter OrientationTests`
Attendu : échec de compilation (`OrientationMath` et `Oriented` n'existent pas).

- [ ] **Étape 3 : implémenter**

Créer `src/MS3DPRINT.Estimator/OrientationMath.cs` :

```csharp
using System.Numerics;

namespace MS3DPRINT.Estimator;

public static class OrientationMath
{
    /// <summary>The shortest rotation taking the direction <paramref name="from"/> onto <paramref name="to"/> (neither needs to be a unit vector).</summary>
    public static Quaternion Between(Vector3 from, Vector3 to)
    {
        from = Vector3.Normalize(from);
        to = Vector3.Normalize(to);
        var dot = Vector3.Dot(from, to);
        if (dot > 0.999999f) return Quaternion.Identity;
        if (dot < -0.999999f)
        {
            // opposite directions: a half turn about any axis at right angles to them
            var axis = Vector3.Cross(from, Vector3.UnitX);
            if (axis.LengthSquared() < 1e-6f) axis = Vector3.Cross(from, Vector3.UnitY);
            return Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.PI);
        }
        var cross = Vector3.Cross(from, to);
        return Quaternion.Normalize(new Quaternion(cross, 1 + dot));
    }
}
```

Dans `src/MS3DPRINT.Estimator/PrintMesh.cs`, ajouter avant la méthode `Scaled` :

```csharp
    /// <summary>
    /// The mesh turned about the centre of its bounding box, then stood on the bed: centred on the origin in X and Y and its lowest
    /// point at Z = 0. The triangles are unchanged and the mesh itself is not modified.
    /// </summary>
    public PrintMesh Oriented(Quaternion rotation)
    {
        if (Positions.Length == 0) return this;
        var centre = (Min + Max) / 2;
        var turned = new Vector3[Positions.Length];
        var low = new Vector3(float.PositiveInfinity);
        var high = new Vector3(float.NegativeInfinity);
        for (var i = 0; i < turned.Length; i++)
        {
            var point = Vector3.Transform(Positions[i] - centre, rotation);
            turned[i] = point;
            low = Vector3.Min(low, point);
            high = Vector3.Max(high, point);
        }
        var shift = new Vector3(-(low.X + high.X) / 2, -(low.Y + high.Y) / 2, -low.Z);
        for (var i = 0; i < turned.Length; i++) turned[i] += shift;
        return new PrintMesh(turned, Indices);
    }
```

- [ ] **Étape 4 : vérifier la réussite**

Run : `dotnet test tests/MS3DPRINT.Estimator.Tests --filter OrientationTests`
Attendu : 8 tests réussis (le test `[Theory]` compte trois cas).

- [ ] **Étape 5 : commit (seulement si demandé)**

```bash
git add src/MS3DPRINT.Estimator/OrientationMath.cs src/MS3DPRINT.Estimator/PrintMesh.cs tests/MS3DPRINT.Estimator.Tests/OrientationTests.cs
git commit -m "feat(estimator): rotation la plus courte et pose du maillage sur le plateau"
```

---

### Tâche 2 : `PlateSize`

**Fichiers :**
- Créer : `src/MS3DPRINT.Manager.Desktop/Viewer/PlateSize.cs`
- Test : `tests/MS3DPRINT.Manager.Core.Tests/Desktop/PlateSizeTests.cs`

**Interfaces :**
- Produit : `readonly record struct PlateSize(double WidthMm, double DepthMm, double HeightMm)` avec `static PlateSize Default` (256 × 256 × 256),
  `static PlateSize? Parse(string?)`, `bool Fits(Vector3 sizeMm)`, `Vector3 Overflow(Vector3 sizeMm)` (dépassement par axe, 0 si ça tient),
  `Vector3 ToVector3()`, `string Label` (« 256 × 256 × 256 mm »).

- [ ] **Étape 1 : écrire le test qui échoue**

Créer `tests/MS3DPRINT.Manager.Core.Tests/Desktop/PlateSizeTests.cs` :

```csharp
using System.Numerics;
using MS3DPRINT.Manager.Desktop.Viewer;

namespace MS3DPRINT.Manager.Core.Tests.Desktop;

public sealed class PlateSizeTests
{
    [Theory]
    [InlineData("256 × 256 × 256 mm", 256, 256, 256)]
    [InlineData("250x210x220", 250, 210, 220)]
    [InlineData("180*180*180", 180, 180, 180)]
    [InlineData("256 X 256 X 256", 256, 256, 256)]
    [InlineData("25,6 x 25 x 24 cm", 256, 250, 240)]
    [InlineData("220.5 x 220 x 250 mm", 220.5, 220, 250)]
    [InlineData("256 × 256 × 256 mm (10 in)", 256, 256, 256)]
    public void Parse_reads_the_usual_ways_of_writing_a_build_volume(string text, double width, double depth, double height)
    {
        var plate = PlateSize.Parse(text);

        Assert.NotNull(plate);
        Assert.Equal(width, plate.Value.WidthMm, 6);
        Assert.Equal(depth, plate.Value.DepthMm, 6);
        Assert.Equal(height, plate.Value.HeightMm, 6);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("grand plateau")]
    [InlineData("256 × 256")]
    [InlineData("Ø 200 × 200 × 250")]
    [InlineData("diam 200 x 200 x 250")]
    [InlineData("0 × 100 × 100")]
    [InlineData("99999 × 100 × 100")]
    public void Parse_gives_nothing_for_text_that_is_not_a_box(string? text) => Assert.Null(PlateSize.Parse(text));

    [Fact]
    public void The_default_plate_is_256_millimetres_in_every_direction()
    {
        Assert.Equal(new PlateSize(256, 256, 256), PlateSize.Default);
        Assert.Equal("256 × 256 × 256 mm", PlateSize.Default.Label);
    }

    [Fact]
    public void A_part_fits_when_no_side_is_larger_than_the_plate()
    {
        var plate = new PlateSize(100, 80, 60);

        Assert.True(plate.Fits(new Vector3(100, 80, 60)));
        Assert.True(plate.Fits(new Vector3(10, 10, 10)));
        Assert.False(plate.Fits(new Vector3(101, 10, 10)));
        Assert.False(plate.Fits(new Vector3(10, 10, 61)));
    }

    [Fact]
    public void Overflow_gives_how_far_each_side_sticks_out()
    {
        var plate = new PlateSize(100, 80, 60);

        Assert.Equal(new Vector3(20, 0, 5), plate.Overflow(new Vector3(120, 50, 65)));
        Assert.Equal(Vector3.Zero, plate.Overflow(new Vector3(100, 80, 60)));
    }
}
```

- [ ] **Étape 2 : vérifier l'échec**

Run : `dotnet test tests/MS3DPRINT.Manager.Core.Tests --filter PlateSizeTests`
Attendu : échec de compilation (`PlateSize` n'existe pas).

- [ ] **Étape 3 : implémenter**

Créer `src/MS3DPRINT.Manager.Desktop/Viewer/PlateSize.cs` :

```csharp
using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace MS3DPRINT.Manager.Desktop.Viewer;

/// <summary>Build volume of a printer: width (X), depth (Y) and height (Z) in millimetres.</summary>
public readonly partial record struct PlateSize(double WidthMm, double DepthMm, double HeightMm)
{
    private const double Tolerance = 0.01;
    private const double MaxMillimetres = 5000;

    public static PlateSize Default => new(256, 256, 256);

    /// <summary>
    /// Reads "256 × 256 × 256 mm", "250x210x220", "25,6 x 25 x 24 cm"…: the first three numbers are width, depth and height, and a unit
    /// of centimetres multiplies them by ten. Text that is not a box (a round bed, free words) gives null.
    /// </summary>
    public static PlateSize? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (text.Contains('Ø') || text.Contains('⌀') || text.Contains("diam", StringComparison.OrdinalIgnoreCase) || text.Contains("rond", StringComparison.OrdinalIgnoreCase)) return null;
        var numbers = Number().Matches(text);
        if (numbers.Count < 3) return null;
        var factor = Centimetres().IsMatch(text) ? 10.0 : 1.0;
        var values = new double[3];
        for (var i = 0; i < 3; i++)
        {
            if (!double.TryParse(numbers[i].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return null;
            values[i] = value * factor;
            if (!(values[i] > 0 && values[i] <= MaxMillimetres)) return null;
        }
        return new PlateSize(values[0], values[1], values[2]);
    }

    public bool Fits(Vector3 sizeMm) => Overflow(sizeMm) == Vector3.Zero;

    /// <summary>How far a part of this size sticks out of the plate along each axis, 0 where it fits.</summary>
    public Vector3 Overflow(Vector3 sizeMm) => new(
        (float)Math.Max(0, sizeMm.X - WidthMm - Tolerance),
        (float)Math.Max(0, sizeMm.Y - DepthMm - Tolerance),
        (float)Math.Max(0, sizeMm.Z - HeightMm - Tolerance));

    public Vector3 ToVector3() => new((float)WidthMm, (float)DepthMm, (float)HeightMm);

    public string Label => string.Create(CultureInfo.InvariantCulture, $"{WidthMm:0.##} × {DepthMm:0.##} × {HeightMm:0.##} mm");

    [GeneratedRegex(@"\d+(?:[.,]\d+)?")]
    private static partial Regex Number();

    [GeneratedRegex(@"\bcm\b", RegexOptions.IgnoreCase)]
    private static partial Regex Centimetres();
}
```

- [ ] **Étape 4 : vérifier la réussite**

Run : `dotnet test tests/MS3DPRINT.Manager.Core.Tests --filter PlateSizeTests`
Attendu : tous réussis. Si `Parse("256 × 256 × 256 mm (10 in)")` échoue, c'est que les trois premiers nombres doivent être pris, pas tous : vérifier `numbers.Count < 3`.

- [ ] **Étape 5 : commit (seulement si demandé)**

```bash
git add src/MS3DPRINT.Manager.Desktop/Viewer/PlateSize.cs tests/MS3DPRINT.Manager.Core.Tests/Desktop/PlateSizeTests.cs
git commit -m "feat(viewer): dimensions du plateau d'impression"
```

---

### Tâche 3 : `ModelOrientation`

**Fichiers :**
- Créer : `src/MS3DPRINT.Manager.Desktop/Viewer/ModelOrientation.cs`
- Test : `tests/MS3DPRINT.Manager.Core.Tests/Desktop/ModelOrientationTests.cs`

**Interfaces :**
- Consomme : `OrientationMath.Between`, `PrintMesh.Oriented` (tâche 1) ; `MeshModel(Vector3[] positions, int[] indices)`.
- Produit : `ModelOrientation` avec `Quaternion Rotation`, `bool IsIdentity`, `Reset()`, `RotateAxis(int axis, double degrees)` (axe 0 = X, 1 = Y, 2 = Z du plateau),
  `Compose(Quaternion turn)` (applique `turn` **après** l'orientation actuelle), `FaceDown(Vector3 normalInCurrentCoordinates)`,
  `Vector3 EulerDegrees` (X puis Y puis Z), `SetEuler(double x, double y, double z)`, `MeshModel Apply(MeshModel original)`.
  La normale de `FaceDown` est exprimée dans le repère **actuel** (celui de la pièce affichée), pas dans celui du fichier d'origine.

- [ ] **Étape 1 : écrire le test qui échoue**

Créer `tests/MS3DPRINT.Manager.Core.Tests/Desktop/ModelOrientationTests.cs` :

```csharp
using System.Numerics;
using MS3DPRINT.Manager.Desktop.Viewer;

namespace MS3DPRINT.Manager.Core.Tests.Desktop;

public sealed class ModelOrientationTests
{
    private static MeshModel Cube(float size = 10)
    {
        Vector3[] corners = [new(0, 0, 0), new(size, 0, 0), new(size, size, 0), new(0, size, 0), new(0, 0, size), new(size, 0, size), new(size, size, size), new(0, size, size)];
        int[] faces = [0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 2, 3, 7, 2, 7, 6, 0, 4, 7, 0, 7, 3, 1, 2, 6, 1, 6, 5];
        return new MeshModel(corners, faces);
    }

    [Fact]
    public void A_new_orientation_is_the_identity_and_still_stands_the_part_on_the_plate()
    {
        var orientation = new ModelOrientation();
        var shifted = new MeshModel(Cube().Positions.Select(p => p + new Vector3(50, -30, 7)).ToArray(), Cube().Indices);

        var placed = orientation.Apply(shifted);

        Assert.True(orientation.IsIdentity);
        Assert.Equal(0, placed.Min.Z, 4);
        Assert.Equal(0, (placed.Min.X + placed.Max.X) / 2, 4);
        Assert.Equal(0, (placed.Min.Y + placed.Max.Y) / 2, 4);
    }

    [Fact]
    public void Four_quarter_turns_about_an_axis_come_back_to_the_start()
    {
        var orientation = new ModelOrientation();

        for (var i = 0; i < 4; i++) orientation.RotateAxis(1, 90);

        Assert.True(orientation.IsIdentity);
    }

    [Fact]
    public void A_quarter_turn_about_X_lays_a_standing_part_down()
    {
        var orientation = new ModelOrientation();
        var tall = new MeshModel(Cube().Positions.Select(p => new Vector3(p.X, p.Y, p.Z * 3)).ToArray(), Cube().Indices);

        orientation.RotateAxis(0, 90);
        var lying = orientation.Apply(tall);

        Assert.Equal(10, lying.Size.X, 3);
        Assert.Equal(30, lying.Size.Y, 3);
        Assert.Equal(10, lying.Size.Z, 3);
    }

    [Fact]
    public void FaceDown_puts_the_chosen_face_on_the_plate()
    {
        var orientation = new ModelOrientation();

        orientation.FaceDown(Vector3.UnitX);
        var placed = orientation.Apply(Cube());

        // the corners that had x = 10 (indices 1, 2, 5, 6) are the face that now lies on the plate
        foreach (var corner in new[] { 1, 2, 5, 6 }) Assert.Equal(0, placed.Positions[corner].Z, 4);
        Assert.All(placed.Positions, point => Assert.True(point.Z > -1e-4f));
    }

    [Fact]
    public void FaceDown_takes_the_normal_in_the_coordinates_of_the_part_as_shown()
    {
        var orientation = new ModelOrientation();
        orientation.RotateAxis(1, 90);          // the original +X face now looks up (or down)
        var shown = orientation.Apply(Cube());
        var upFace = shown.Positions[5].Z > shown.Positions[0].Z ? Vector3.UnitZ : -Vector3.UnitZ;

        orientation.FaceDown(upFace);            // lay that same face on the plate
        var flipped = orientation.Apply(Cube());

        Assert.Equal(0, flipped.Min.Z, 4);
        Assert.Equal(10, flipped.Size.Z, 3);
    }

    [Theory]
    [InlineData(10, 20, 30)]
    [InlineData(-45, 60, 170)]
    [InlineData(0, 0, 0)]
    [InlineData(90, -30, -90)]
    public void Euler_angles_come_back_as_they_were_set(double x, double y, double z)
    {
        var orientation = new ModelOrientation();

        orientation.SetEuler(x, y, z);
        var angles = orientation.EulerDegrees;

        Assert.Equal(x, angles.X, 2);
        Assert.Equal(y, angles.Y, 2);
        Assert.Equal(z, angles.Z, 2);
    }

    [Fact]
    public void Euler_angles_stay_consistent_where_the_second_axis_locks()
    {
        var orientation = new ModelOrientation();
        orientation.SetEuler(20, 90, 35);
        var before = orientation.Rotation;

        var angles = orientation.EulerDegrees;
        orientation.SetEuler(angles.X, angles.Y, angles.Z);

        Assert.True(MathF.Abs(Quaternion.Dot(before, orientation.Rotation)) > 0.9999f);
    }

    [Fact]
    public void Apply_keeps_every_triangle_and_never_touches_the_original()
    {
        var orientation = new ModelOrientation();
        orientation.SetEuler(33, 21, 77);
        var original = Cube();
        var corner = original.Positions[6];

        var placed = orientation.Apply(original);

        Assert.Same(original.Indices, placed.Indices);
        Assert.Equal(corner, original.Positions[6]);
        Assert.Equal(0, placed.Min.Z, 4);
    }

    [Fact]
    public void Reset_goes_back_to_the_loaded_orientation()
    {
        var orientation = new ModelOrientation();
        orientation.SetEuler(10, 20, 30);

        orientation.Reset();

        Assert.True(orientation.IsIdentity);
    }

    [Fact]
    public void An_unknown_axis_is_refused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ModelOrientation().RotateAxis(3, 90));
}
```

- [ ] **Étape 2 : vérifier l'échec**

Run : `dotnet test tests/MS3DPRINT.Manager.Core.Tests --filter ModelOrientationTests`
Attendu : échec de compilation (`ModelOrientation` n'existe pas).

- [ ] **Étape 3 : implémenter**

Créer `src/MS3DPRINT.Manager.Desktop/Viewer/ModelOrientation.cs` :

```csharp
using System.Numerics;
using MS3DPRINT.Estimator;

namespace MS3DPRINT.Manager.Desktop.Viewer;

/// <summary>
/// How the model was turned on the plate: one rotation applied to the model as loaded. <see cref="Apply"/> gives the part as shown,
/// turned and stood on the plate (see <see cref="PrintMesh.Oriented"/>).
/// </summary>
public sealed class ModelOrientation
{
    public Quaternion Rotation { get; private set; } = Quaternion.Identity;

    public bool IsIdentity => MathF.Abs(MathF.Abs(Rotation.W) - 1) < 1e-6f;

    public void Reset() => Rotation = Quaternion.Identity;

    /// <summary>Turns the part about an axis of the plate (0 = X, 1 = Y, 2 = Z) after its current orientation.</summary>
    public void RotateAxis(int axis, double degrees)
    {
        var direction = axis switch
        {
            0 => Vector3.UnitX,
            1 => Vector3.UnitY,
            2 => Vector3.UnitZ,
            _ => throw new ArgumentOutOfRangeException(nameof(axis), "L'axe attendu est 0 (X), 1 (Y) ou 2 (Z).")
        };
        Compose(Quaternion.CreateFromAxisAngle(direction, (float)(degrees * Math.PI / 180)));
    }

    /// <summary>Applies <paramref name="turn"/> after the current orientation, in the coordinates of the part as shown.</summary>
    public void Compose(Quaternion turn) => Rotation = Quaternion.Normalize(Quaternion.Concatenate(Rotation, turn));

    /// <summary>Lays the face whose outward normal is <paramref name="normal"/> (in the coordinates of the part as shown) on the plate.</summary>
    public void FaceDown(Vector3 normal) => Compose(OrientationMath.Between(normal, -Vector3.UnitZ));

    /// <summary>
    /// The rotation as three angles in degrees, turning about X first, then Y, then Z (axes of the plate). Where the second angle is ±90°
    /// the first and third cannot be told apart: the third is then 0.
    /// </summary>
    public Vector3 EulerDegrees
    {
        get
        {
            var m = Matrix4x4.CreateFromQuaternion(Rotation);
            var sinB = Math.Clamp(-m.M13, -1f, 1f);
            var b = MathF.Asin(sinB);
            float a, c;
            if (MathF.Abs(MathF.Cos(b)) < 1e-4f)
            {
                a = MathF.Atan2(-m.M32, m.M22);
                c = 0;
            }
            else
            {
                a = MathF.Atan2(m.M23, m.M33);
                c = MathF.Atan2(m.M12, m.M11);
            }
            const float ToDegrees = 180f / MathF.PI;
            return new Vector3(a * ToDegrees, b * ToDegrees, c * ToDegrees);
        }
    }

    public void SetEuler(double x, double y, double z)
    {
        const double ToRadians = Math.PI / 180;
        var qx = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)(x * ToRadians));
        var qy = Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)(y * ToRadians));
        var qz = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)(z * ToRadians));
        Rotation = Quaternion.Normalize(Quaternion.Concatenate(Quaternion.Concatenate(qx, qy), qz));
    }

    /// <summary>The part as shown: the loaded model turned, centred on the plate in X and Y and standing on it.</summary>
    public MeshModel Apply(MeshModel original)
    {
        var placed = new PrintMesh(original.Positions, original.Indices).Oriented(Rotation);
        return new MeshModel(placed.Positions, placed.Indices);
    }
}
```

- [ ] **Étape 4 : vérifier la réussite**

Run : `dotnet test tests/MS3DPRINT.Manager.Core.Tests --filter ModelOrientationTests`
Attendu : tous réussis. Si `Euler_angles_come_back_as_they_were_set` échoue pour un cas, vérifier que les extractions utilisent bien
`M13`, `M23`, `M33`, `M12`, `M11` (matrice « ligne » de `System.Numerics`) et que `SetEuler` empile X, puis Y, puis Z avec `Concatenate`.

- [ ] **Étape 5 : commit (seulement si demandé)**

```bash
git add src/MS3DPRINT.Manager.Desktop/Viewer/ModelOrientation.cs tests/MS3DPRINT.Manager.Core.Tests/Desktop/ModelOrientationTests.cs
git commit -m "feat(viewer): état d'orientation de la pièce"
```

---

### Tâche 4 : `AutoOrient` et `BaseAreaMm2`

**Fichiers :**
- Créer : `src/MS3DPRINT.Estimator/AutoOrient.cs`
- Modifier : `src/MS3DPRINT.Estimator/EstimateSettings.cs` (record `EstimateResult`), `src/MS3DPRINT.Estimator/PrintEstimator.cs`
- Test : `tests/MS3DPRINT.Estimator.Tests/AutoOrientTests.cs`, `tests/MS3DPRINT.Estimator.Tests/PrintEstimatorTests.cs`

**Interfaces :**
- Consomme : `OrientationMath.Between`, `PrintMesh.Oriented` (tâche 1), `PrintEstimator.Estimate`.
- Produit : `EstimateResult.BaseAreaMm2` (aire de la première couche, mm²) ;
  `enum AutoOrientGoal { FewestSupports, Fastest, MostStable }` ;
  `record OrientationCandidate(Quaternion Rotation, double SupportVolumeMm3, double TimeSeconds, double BaseAreaMm2, double HeightMm, bool FitsPlate, double Score)` ;
  `record OrientationProposal(OrientationCandidate Best, OrientationCandidate Current, IReadOnlyList<OrientationCandidate> Candidates, bool Limited)` avec `IsImprovement` ;
  `AutoOrient.Propose(PrintMesh mesh, EstimateSettings settings, MachineProfile? machine, AutoOrientGoal goal, Vector3? plateMm, IProgress<double>? progress, CancellationToken cancellationToken) -> OrientationProposal`.
  La `Rotation` d'un candidat est **relative à la pièce donnée** : `mesh.Oriented(Rotation)` est la nouvelle position.

- [ ] **Étape 1 : écrire les tests qui échouent**

Dans `tests/MS3DPRINT.Estimator.Tests/PrintEstimatorTests.cs`, ajouter dans la classe :

```csharp
    [Fact]
    public void TheBaseAreaIsTheSectionOnTheBed()
    {
        var result = PrintEstimator.Estimate(Cube(), new EstimateSettings());

        Assert.Equal(400, result.BaseAreaMm2, 0);
    }
```

Créer `tests/MS3DPRINT.Estimator.Tests/AutoOrientTests.cs` :

```csharp
using System.Numerics;
using MS3DPRINT.Estimator;

namespace MS3DPRINT.Estimator.Tests;

public sealed class AutoOrientTests
{
    private static PrintMesh Block(float x, float y, float z)
    {
        Vector3[] corners = [new(0, 0, 0), new(x, 0, 0), new(x, y, 0), new(0, y, 0), new(0, 0, z), new(x, 0, z), new(x, y, z), new(0, y, z)];
        int[] faces = [0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 2, 3, 7, 2, 7, 6, 0, 4, 7, 0, 7, 3, 1, 2, 6, 1, 6, 5];
        return new PrintMesh(corners, faces).Oriented(Quaternion.Identity);
    }

    /// <summary>A closed solid of revolution around Z from a (radius, height) profile, wound outward whatever the profile direction.</summary>
    private static PrintMesh Revolve((double R, double Z)[] profile, int segments = 32)
    {
        var positions = new List<Vector3>();
        var indices = new List<int>();
        for (var ring = 0; ring < segments; ring++)
            foreach (var (r, z) in profile)
            {
                var angle = 2 * Math.PI * ring / segments;
                positions.Add(new Vector3((float)(r * Math.Cos(angle)), (float)(r * Math.Sin(angle)), (float)z));
            }
        var n = profile.Length;
        void Add(int a, int b, int c)
        {
            if (Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]).LengthSquared() < 1e-9f) return;
            indices.AddRange([a, b, c]);
        }
        for (var ring = 0; ring < segments; ring++)
        {
            var next = (ring + 1) % segments;
            for (var k = 0; k < n; k++)
            {
                var l = (k + 1) % n;
                int a = ring * n + k, b = next * n + k, c = next * n + l, d = ring * n + l;
                Add(a, b, c);
                Add(a, c, d);
            }
        }
        var volume = 0.0;
        for (var t = 0; t < indices.Count; t += 3)
            volume += Vector3.Dot(positions[indices[t]], Vector3.Cross(positions[indices[t + 1]], positions[indices[t + 2]])) / 6.0;
        if (volume < 0)
            for (var t = 0; t < indices.Count; t += 3) (indices[t + 1], indices[t + 2]) = (indices[t + 2], indices[t + 1]);
        return new PrintMesh([.. positions], [.. indices]).Oriented(Quaternion.Identity);
    }

    private static EstimateSettings Settings() => new() { InfillPercent = 15 };

    [Fact]
    public void A_cube_is_left_where_it_is()
    {
        var proposal = AutoOrient.Propose(Block(20, 20, 20), Settings(), null, AutoOrientGoal.FewestSupports, null, null, default);

        Assert.False(proposal.IsImprovement);
        Assert.Same(proposal.Current, proposal.Best);
    }

    [Fact]
    public void A_plate_standing_on_its_edge_is_laid_flat()
    {
        var standing = Block(2, 40, 60);

        var proposal = AutoOrient.Propose(standing, Settings(), null, AutoOrientGoal.FewestSupports, null, null, default);
        var placed = standing.Oriented(proposal.Best.Rotation);

        Assert.True(proposal.IsImprovement);
        Assert.Equal(2, placed.Size.Z, 2);
    }

    [Fact]
    public void A_mushroom_standing_on_its_stem_is_turned_cap_down()
    {
        // stem 3 mm in radius and 20 mm high, then a cap 15 mm in radius: the cap hangs in the air
        var mushroom = Revolve([(0, 0), (3, 0), (3, 20), (15, 20), (15, 24), (0, 24)]);

        var proposal = AutoOrient.Propose(mushroom, Settings(), null, AutoOrientGoal.FewestSupports, null, null, default);

        Assert.True(proposal.IsImprovement);
        Assert.True(proposal.Current.SupportVolumeMm3 > 100);
        Assert.True(proposal.Best.SupportVolumeMm3 < 0.2 * proposal.Current.SupportVolumeMm3);
    }

    [Fact]
    public void A_pillar_that_is_too_tall_for_the_plate_is_laid_down_to_fit()
    {
        var pillar = Block(20, 20, 60);

        var proposal = AutoOrient.Propose(pillar, Settings(), null, AutoOrientGoal.FewestSupports, new Vector3(100, 100, 30), null, default);
        var placed = pillar.Oriented(proposal.Best.Rotation);

        Assert.True(proposal.Best.FitsPlate);
        Assert.False(proposal.Current.FitsPlate);
        Assert.True(placed.Size.Z <= 30.01f);
    }

    [Fact]
    public void Candidates_include_the_current_position_first()
    {
        var proposal = AutoOrient.Propose(Block(20, 30, 40), Settings(), null, AutoOrientGoal.FewestSupports, null, null, default);

        Assert.Same(proposal.Current, proposal.Candidates[0]);
        Assert.True(proposal.Candidates.Count >= 6);
    }

    [Fact]
    public void Progress_is_reported_up_to_the_end()
    {
        var reports = new List<double>();

        AutoOrient.Propose(Block(20, 30, 40), Settings(), null, AutoOrientGoal.Fastest, null, new SynchronousProgress(reports.Add), default);

        Assert.NotEmpty(reports);
        Assert.Equal(1.0, reports[^1], 6);
    }

    [Fact]
    public void A_cancelled_search_stops()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            AutoOrient.Propose(Block(20, 30, 40), Settings(), null, AutoOrientGoal.FewestSupports, null, null, cancelled.Token));
    }

    private sealed class SynchronousProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
```

- [ ] **Étape 2 : vérifier l'échec**

Run : `dotnet test tests/MS3DPRINT.Estimator.Tests --filter "AutoOrientTests|TheBaseAreaIsTheSectionOnTheBed"`
Attendu : échec de compilation (`AutoOrient`, `BaseAreaMm2` n'existent pas).

- [ ] **Étape 3 : ajouter `BaseAreaMm2` au résultat de l'estimation**

Dans `src/MS3DPRINT.Estimator/EstimateSettings.cs`, le record `EstimateResult` se termine par
`..., int IslandCount = 0, double TurningRadians = 0, double OverhangAreaMm2 = 0)` : remplacer la fin par
`..., int IslandCount = 0, double TurningRadians = 0, double OverhangAreaMm2 = 0, double BaseAreaMm2 = 0)`.

Dans `src/MS3DPRINT.Estimator/PrintEstimator.cs`, remplacer
`sections.Sum(section => section.Perimeter), islands, turning, overhang);` par
`sections.Sum(section => section.Perimeter), islands, turning, overhang, area[0]);`.

- [ ] **Étape 4 : implémenter `AutoOrient`**

Créer `src/MS3DPRINT.Estimator/AutoOrient.cs` :

```csharp
using System.Numerics;

namespace MS3DPRINT.Estimator;

public enum AutoOrientGoal { FewestSupports, Fastest, MostStable }

/// <summary>One way to stand the part and what it costs. The rotation is relative to the part that was given.</summary>
public sealed record OrientationCandidate(
    Quaternion Rotation, double SupportVolumeMm3, double TimeSeconds, double BaseAreaMm2, double HeightMm, bool FitsPlate, double Score);

public sealed record OrientationProposal(OrientationCandidate Best, OrientationCandidate Current, IReadOnlyList<OrientationCandidate> Candidates, bool Limited)
{
    public bool IsImprovement => !ReferenceEquals(Best, Current);
}

/// <summary>
/// Looks for the way to stand a part that suits the goal best. Candidates are the six directions of the axes and the largest families of
/// flat faces (normals within 5° of each other); each is laid on the bed and measured with the estimator.
/// </summary>
public static class AutoOrient
{
    public const int MaxFaceFamilies = 12;
    public const double FamilyAngleDegrees = 5;
    /// <summary>Above this many triangles only the six axes are tried: every candidate costs a full estimate.</summary>
    public const int LargeMeshTriangles = 1_000_000;
    private const double SmallBaseMm2 = 20;

    public static OrientationProposal Propose(
        PrintMesh mesh, EstimateSettings settings, MachineProfile? machine, AutoOrientGoal goal, Vector3? plateMm,
        IProgress<double>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(settings);
        var limited = mesh.TriangleCount > LargeMeshTriangles;

        // outward normals that will point down; the first one is the position the part is in now
        var normals = new List<Vector3> { -Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY };
        if (!limited)
            foreach (var family in FaceFamilies(mesh, cancellationToken))
                if (normals.All(known => Vector3.Dot(known, family) < 0.99985f)) normals.Add(family);

        var measuring = settings with { Supports = SupportMode.Auto };
        var raw = new List<(Quaternion Rotation, double Support, double Time, double Base, double Height, bool Fits)>();
        for (var i = 0; i < normals.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rotation = OrientationMath.Between(normals[i], -Vector3.UnitZ);
            var placed = mesh.Oriented(rotation);
            try
            {
                var estimate = PrintEstimator.Estimate(placed, measuring, machine, cancellationToken);
                raw.Add((rotation, estimate.SupportVolumeMm3, estimate.TimeSeconds, estimate.BaseAreaMm2, placed.Size.Z, Fits(placed.Size, plateMm)));
            }
            catch (ArgumentException) when (i > 0)
            {
                // a position the estimator refuses (far too many layers) is simply not a candidate
            }
            progress?.Report((i + 1.0) / normals.Count);
        }

        var maxSupport = Math.Max(1, raw.Max(r => r.Support));
        var maxTime = Math.Max(1, raw.Max(r => r.Time));
        var maxBase = Math.Max(1, raw.Max(r => r.Base));
        var (weightSupport, weightTime, weightBase) = goal switch
        {
            AutoOrientGoal.Fastest => (0.25, 1.0, 0.1),
            AutoOrientGoal.MostStable => (0.25, 0.25, 1.0),
            _ => (1.0, 0.25, 0.1)
        };
        var candidates = raw.Select(r => new OrientationCandidate(
            r.Rotation, r.Support, r.Time, r.Base, r.Height, r.Fits,
            weightSupport * r.Support / maxSupport + weightTime * r.Time / maxTime + weightBase * (1 - r.Base / maxBase)
            + (r.Base < SmallBaseMm2 ? 0.5 : 0) + (r.Fits ? 0 : 100))).ToList();

        var current = candidates[0];
        var best = candidates.MinBy(candidate => candidate.Score)!;
        // a tie keeps the part where it is
        if (best.Score >= current.Score - 1e-9) best = current;
        return new OrientationProposal(best, current, candidates, limited);
    }

    /// <summary>The part fits the plate, turned a quarter turn about Z if that helps.</summary>
    private static bool Fits(Vector3 size, Vector3? plate) =>
        plate is not { } p
        || (size.Z <= p.Z + 0.01f && ((size.X <= p.X + 0.01f && size.Y <= p.Y + 0.01f) || (size.X <= p.Y + 0.01f && size.Y <= p.X + 0.01f)));

    /// <summary>Outward normals of the largest flat regions of the mesh, largest first.</summary>
    private static List<Vector3> FaceFamilies(PrintMesh mesh, CancellationToken cancellationToken)
    {
        var count = mesh.TriangleCount;
        var normals = new Vector3[count];
        var areas = new float[count];
        for (var t = 0; t < count; t++)
        {
            if ((t & 0x3FFF) == 0) cancellationToken.ThrowIfCancellationRequested();
            var a = mesh.Positions[mesh.Indices[t * 3]];
            var b = mesh.Positions[mesh.Indices[t * 3 + 1]];
            var c = mesh.Positions[mesh.Indices[t * 3 + 2]];
            var cross = Vector3.Cross(b - a, c - a);
            var length = cross.Length();
            areas[t] = length / 2;
            normals[t] = length > 1e-12f ? cross / length : Vector3.Zero;
        }

        var limit = (float)Math.Cos(FamilyAngleDegrees * Math.PI / 180);
        var taken = new bool[count];
        var families = new List<(Vector3 Normal, double Area)>();
        for (var round = 0; round < MaxFaceFamilies * 2; round++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var seed = -1;
            var largest = 0f;
            for (var t = 0; t < count; t++)
                if (!taken[t] && areas[t] > largest) { largest = areas[t]; seed = t; }
            if (seed < 0) break;
            var axis = normals[seed];
            double total = 0;
            for (var t = 0; t < count; t++)
                if (!taken[t] && Vector3.Dot(normals[t], axis) >= limit) { taken[t] = true; total += areas[t]; }
            families.Add((axis, total));
        }
        return families.OrderByDescending(family => family.Area).Take(MaxFaceFamilies).Select(family => family.Normal).ToList();
    }
}
```

- [ ] **Étape 5 : vérifier la réussite**

Run : `dotnet test tests/MS3DPRINT.Estimator.Tests`
Attendu : tout réussit (les 40 tests existants et les nouveaux). Si `A_mushroom_standing_on_its_stem_is_turned_cap_down` échoue sur
`Current.SupportVolumeMm3 > 100`, imprimer `proposal.Current` : le champignon doit avoir un chapeau en surplomb (rayon 15, 12 mm au-dessus du plateau).

- [ ] **Étape 6 : commit (seulement si demandé)**

```bash
git add src/MS3DPRINT.Estimator/AutoOrient.cs src/MS3DPRINT.Estimator/EstimateSettings.cs src/MS3DPRINT.Estimator/PrintEstimator.cs tests/MS3DPRINT.Estimator.Tests/AutoOrientTests.cs tests/MS3DPRINT.Estimator.Tests/PrintEstimatorTests.cs
git commit -m "feat(estimator): orientation automatique de la pièce"
```

---

### Tâche 5 : `StlWriter`

**Fichiers :**
- Créer : `src/MS3DPRINT.Manager.Desktop/Viewer/StlWriter.cs`
- Test : `tests/MS3DPRINT.Manager.Core.Tests/Desktop/StlWriterTests.cs`

**Interfaces :**
- Consomme : `MeshModel` (`Positions`, `Indices`, `Normals`, `TriangleCount`), `MeshLoader.Load(string path, CancellationToken)`.
- Produit : `StlWriter.Write(string path, MeshModel mesh, double millimetresPerUnit = 1)` : STL binaire, sommets multipliés par `millimetresPerUnit`.

- [ ] **Étape 1 : écrire le test qui échoue**

Créer `tests/MS3DPRINT.Manager.Core.Tests/Desktop/StlWriterTests.cs` :

```csharp
using System.Numerics;
using MS3DPRINT.Manager.Desktop.Viewer;

namespace MS3DPRINT.Manager.Core.Tests.Desktop;

public sealed class StlWriterTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ms3d-stl-" + Guid.NewGuid().ToString("N"));

    public StlWriterTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static MeshModel Cube(float size)
    {
        Vector3[] corners = [new(0, 0, 0), new(size, 0, 0), new(size, size, 0), new(0, size, 0), new(0, 0, size), new(size, 0, size), new(size, size, size), new(0, size, size)];
        int[] faces = [0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 2, 3, 7, 2, 7, 6, 0, 4, 7, 0, 7, 3, 1, 2, 6, 1, 6, 5];
        return new MeshModel(corners, faces);
    }

    [Fact]
    public void A_written_model_reads_back_with_the_same_triangles()
    {
        var path = Path.Combine(_directory, "cube.stl");

        StlWriter.Write(path, Cube(10));
        var back = MeshLoader.Load(path);

        Assert.Equal(12, back.TriangleCount);
        Assert.Equal(10, back.Size.X, 4);
        Assert.Equal(10, back.Size.Z, 4);
    }

    [Fact]
    public void The_file_is_a_binary_stl_of_the_expected_size()
    {
        var path = Path.Combine(_directory, "cube.stl");

        StlWriter.Write(path, Cube(10));

        Assert.Equal(84 + 50 * 12, new FileInfo(path).Length);
    }

    [Fact]
    public void Units_are_converted_to_millimetres_on_the_way_out()
    {
        var path = Path.Combine(_directory, "inch.stl");

        StlWriter.Write(path, Cube(1), 25.4);
        var back = MeshLoader.Load(path);

        Assert.Equal(25.4, back.Size.X, 3);
        Assert.Equal(25.4, back.Size.Y, 3);
    }

    [Fact]
    public void An_existing_file_is_replaced()
    {
        var path = Path.Combine(_directory, "cube.stl");
        File.WriteAllText(path, new string('x', 5000));

        StlWriter.Write(path, Cube(10));

        Assert.Equal(84 + 50 * 12, new FileInfo(path).Length);
    }

    [Fact]
    public void A_scale_that_is_not_positive_is_refused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => StlWriter.Write(Path.Combine(_directory, "x.stl"), Cube(1), 0));
}
```

- [ ] **Étape 2 : vérifier l'échec**

Run : `dotnet test tests/MS3DPRINT.Manager.Core.Tests --filter StlWriterTests`
Attendu : échec de compilation (`StlWriter` n'existe pas).

- [ ] **Étape 3 : implémenter**

Créer `src/MS3DPRINT.Manager.Desktop/Viewer/StlWriter.cs` :

```csharp
using System.Buffers.Binary;
using System.Text;

namespace MS3DPRINT.Manager.Desktop.Viewer;

/// <summary>Writes a mesh as a binary STL, in millimetres (STL has no unit, so the unit of the model is converted on the way out).</summary>
public static class StlWriter
{
    private const int HeaderBytes = 80;
    private const int TriangleBytes = 50;

    public static void Write(string path, MeshModel mesh, double millimetresPerUnit = 1)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        if (!(millimetresPerUnit > 0) || !double.IsFinite(millimetresPerUnit)) throw new ArgumentOutOfRangeException(nameof(millimetresPerUnit));
        var scale = (float)millimetresPerUnit;
        var count = mesh.TriangleCount;
        var bytes = new byte[HeaderBytes + 4 + count * TriangleBytes];
        Encoding.ASCII.GetBytes("MS3DPRINT Manager").CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(HeaderBytes), (uint)count);
        var offset = HeaderBytes + 4;
        for (var t = 0; t < count; t++)
        {
            var normal = mesh.Normals[t];
            WriteVector(bytes, offset, normal.X, normal.Y, normal.Z);
            for (var corner = 0; corner < 3; corner++)
            {
                var point = mesh.Positions[mesh.Indices[t * 3 + corner]] * scale;
                WriteVector(bytes, offset + 12 + corner * 12, point.X, point.Y, point.Z);
            }
            offset += TriangleBytes;
        }
        File.WriteAllBytes(path, bytes);
    }

    private static void WriteVector(byte[] bytes, int offset, float x, float y, float z)
    {
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset), x);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset + 4), y);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset + 8), z);
    }
}
```

- [ ] **Étape 4 : vérifier la réussite**

Run : `dotnet test tests/MS3DPRINT.Manager.Core.Tests --filter StlWriterTests`
Attendu : 5 tests réussis.

- [ ] **Étape 5 : commit (seulement si demandé)**

```bash
git add src/MS3DPRINT.Manager.Desktop/Viewer/StlWriter.cs tests/MS3DPRINT.Manager.Core.Tests/Desktop/StlWriterTests.cs
git commit -m "feat(viewer): écriture STL binaire en millimètres"
```

---

### Tâche 6 : orientation dans le visualiseur (boutons, angles, réinitialisation)

**Fichiers :**
- Créer : `src/MS3DPRINT.Manager.Desktop/Viewer/ModelViewerView.Orientation.cs`
- Modifier : `src/MS3DPRINT.Manager.Desktop/Viewer/ModelViewerView.cs`

**Interfaces :**
- Consomme : `ModelOrientation` (tâche 3) ; dans `ModelViewerView` : `_mesh`, `_overlay`, `_viewport`, `_measures`, `_slice`, `ClearMeasures()`, `ClearComparison()`, `UpdateCut()`, `RefreshDimensions()`, `ScheduleSlice()`, `ClearAssessment()`, `ScheduleAssessment()`, `RefreshVolume()`, `Muted(string)`, `FormKit.SectionLabel(string)`.
- Produit : champs `_original` (`MeshModel?`) et `_orientation` (`ModelOrientation`) ; `OrientationChanged()` (recalcule `_mesh` depuis `_original`) ;
  `ShowOrientedMesh(MeshModel)` ; `InitializeOrientation()` ; `SyncAngles()` ; `_orientGroup` (`StackPanel` du groupe d'outils).

Cette tâche est de l'interface : pas de test automatique ; la vérification est une capture d'écran (étape 5).

- [ ] **Étape 1 : créer le fichier partiel**

Créer `src/MS3DPRINT.Manager.Desktop/Viewer/ModelViewerView.Orientation.cs` :

```csharp
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
```

- [ ] **Étape 2 : brancher dans `ModelViewerView.cs`**

Quatre modifications dans `src/MS3DPRINT.Manager.Desktop/Viewer/ModelViewerView.cs` :

a) Dans `LoadModelAsync`, remplacer

```csharp
            var mesh = await Task.Run(() => MeshLoader.Load(path, load.Token), load.Token);
            if (version != _loadVersion) return;
            _mesh = mesh;
            _overlay.Mesh = mesh;
```

par

```csharp
            var loaded = await Task.Run(() => MeshLoader.Load(path, load.Token), load.Token);
            if (version != _loadVersion) return;
            // the model is stood on the build plate in the orientation it was saved in: centred, lowest point at Z = 0
            _original = loaded;
            _orientation.Reset();
            SyncAngles();
            var mesh = _orientation.Apply(loaded);
            _mesh = mesh;
            _overlay.Mesh = mesh;
```

b) Toujours dans `LoadModelAsync`, juste après la ligne `_cutToggle.IsEnabled = true;`, ajouter :

```csharp
            _orientGroup.IsEnabled = true;
```

c) Dans le constructeur, remplacer la ligne `InitializeAssessment();` par :

```csharp
        InitializeAssessment();
        InitializeOrientation();
```

d) Dans le constructeur, remplacer la ligne
`var toolsTab = new StackPanel { Spacing = 14, Children = { _toolsHint, _measureGroup, _cutGroup, compareGroup } };` par :

```csharp
        var toolsTab = new StackPanel { Spacing = 14, Children = { _orientGroup, _toolsHint, _measureGroup, _cutGroup, compareGroup } };
```

e) Variable de démarrage pour les captures d'écran : dans `LoadModelAsync`, juste avant le commentaire
`// development hook, like the other MS3DPRINT_START_* variables: open with the ground grid shown`, ajouter :

```csharp
            // development hook, like the other MS3DPRINT_START_* variables: open turned a quarter turn about X by this many degrees
            if (double.TryParse(Environment.GetEnvironmentVariable("MS3DPRINT_START_ROTATE_X"), NumberStyles.Float, CultureInfo.InvariantCulture, out var startRotateX))
            {
                _orientation.RotateAxis(0, startRotateX);
                OrientationChanged();
            }
```

- [ ] **Étape 2 bis : compiler**

Run : `dotnet build src/MS3DPRINT.Manager.Desktop -c Release 2>&1 | Select-String "error|Avertissement|Erreur"`
Attendu : 0 erreur, 0 avertissement.

- [ ] **Étape 3 : lancer les tests**

Run : `dotnet test tests/MS3DPRINT.Manager.Core.Tests` puis `dotnet test tests/MS3DPRINT.Estimator.Tests`
Attendu : tout réussit.

- [ ] **Étape 4 : vérification à l'écran**

Compiler l'application (`dotnet build src/MS3DPRINT.Manager.Windows.App -c Release`) puis la lancer avec les variables de démarrage
(`MS3DPRINT_START_DIALOG=viewer`, `MS3DPRINT_START_MODEL=<un STL>`, `MS3DPRINT_START_TAB=2`, `MS3DPRINT_START_GRID=1`, et une fois `MS3DPRINT_START_ROTATE_X=90`).
Vérifier : le groupe « Orientation » est en haut de l'onglet Outils ; avec `ROTATE_X=90` la pièce est couchée sur la grille et le champ X affiche 90 ;
la pièce touche la grille (rien en dessous, rien en l'air). Ne lancer qu'une instance de développement et la fermer ensuite.
Attention : une capture de la fenêtre peut déborder sur un autre écran ; la rogner et supprimer le fichier après lecture.

- [ ] **Étape 5 : commit (seulement si demandé)**

```bash
git add src/MS3DPRINT.Manager.Desktop/Viewer/ModelViewerView.Orientation.cs src/MS3DPRINT.Manager.Desktop/Viewer/ModelViewerView.cs
git commit -m "feat(viewer): réorientation de la pièce (rotations, angles, réinitialisation)"
```

---

### Tâche 7 : plateau d'impression

**Fichiers :**
- Créer : `src/MS3DPRINT.Manager.Desktop/Viewer/ModelViewerView.Plate.cs`
- Modifier : `GpuMeshViewport.cs`, `GpuViewportHost.cs`, `MeshViewport.cs` (dans `Viewer/`), `ModelViewerView.cs`, `ModelViewerView.Assessment.cs`, `MainWindow.Viewer.cs`

**Interfaces :**
- Consomme : `PlateSize` (tâche 2), `OrientationChanged`/`ShowOrientedMesh` (tâche 6), `ApplyGrid(IMeshViewport?)`, `MeasureScale`.
- Produit : `IMeshViewport.PlateHalfWidth`, `PlateHalfDepth` (flottants, unités du modèle, 0 = pas de plateau), `PlateOverflow` (bool) ;
  `record ViewerPrinter(string Name, string BuildVolume)` ; paramètre de constructeur `Func<IReadOnlyList<ViewerPrinter>>? loadPrinters = null` ;
  champ `_plate` (`PlateSize`) ; `ApplyPlate()`, `PushPlate(IMeshViewport)`.

- [ ] **Étape 1 : propriétés du plateau dans l'interface et les deux rendus**

Dans `Viewer/GpuMeshViewport.cs`, dans l'interface `IMeshViewport`, après la ligne `float GridSpacing { get; set; }` ajouter :

```csharp
    /// <summary>Build plate centred on the origin of the floor: half its width (X) and depth (Y) in model units; 0 draws none (GPU view only).</summary>
    float PlateHalfWidth { get; set; }
    float PlateHalfDepth { get; set; }
    /// <summary>The part sticks out of the plate: the outline of the plate is drawn in red.</summary>
    bool PlateOverflow { get; set; }
```

Dans la classe `GpuMeshViewport`, après la propriété `GridSpacing` (celle qui a un getter et un setter sur `_gridSpacing`), ajouter :

```csharp
    public float PlateHalfWidth { get; set; }
    public float PlateHalfDepth { get; set; }
    public bool PlateOverflow { get; set; }
```

Dans `Viewer/GpuViewportHost.cs`, après la ligne `public float GridSpacing { ... }` ajouter :

```csharp
    public float PlateHalfWidth { get => _gpu.PlateHalfWidth; set => _gpu.PlateHalfWidth = value; }
    public float PlateHalfDepth { get => _gpu.PlateHalfDepth; set => _gpu.PlateHalfDepth = value; }
    public bool PlateOverflow { get => _gpu.PlateOverflow; set => _gpu.PlateOverflow = value; }
```

Dans `Viewer/MeshViewport.cs`, après `public float GridSpacing { get; set; }` ajouter :

```csharp
    /// <summary>Not drawn by the CPU view; kept so both views share one interface.</summary>
    public float PlateHalfWidth { get; set; }
    public float PlateHalfDepth { get; set; }
    public bool PlateOverflow { get; set; }
```

- [ ] **Étape 2 : dessiner le plateau dans le shader de la grille**

Dans `Viewer/GpuMeshViewport.cs`, remplacer la constante `GridFragment` par :

```csharp
    private const string GridFragment = @"
in vec2 vNdc;
uniform mat4 uInverseViewProjection;
uniform float uPlaneZ;
uniform float uSpacing;
uniform vec3 uBackground;
uniform vec3 uMinor;
uniform vec3 uMajor;
uniform vec3 uPlate;       // half width, half depth, 1 when the part sticks out; a half width of 0 means no plate
uniform vec3 uPlateFill;
uniform vec3 uPlateEdge;
uniform vec3 uPlateOver;
out vec4 outColor;

// Lines of one cell size, one pixel wide whatever the distance. When they come closer than a few pixels they would blur into
// a haze, so they fade out and the next, coarser, level takes over.
float level(vec2 p, float cell) {
    vec2 coord = p / cell;
    vec2 width = max(fwidth(coord), vec2(1e-7));
    vec2 g = abs(fract(coord - 0.5) - 0.5) / width;
    float line = 1.0 - min(min(g.x, g.y), 1.0);
    return line * (1.0 - smoothstep(0.12, 0.5, max(width.x, width.y)));
}

void main() {
    vec4 nearPoint = uInverseViewProjection * vec4(vNdc, -1.0, 1.0);
    vec4 farPoint = uInverseViewProjection * vec4(vNdc, 1.0, 1.0);
    vec3 origin = nearPoint.xyz / nearPoint.w;
    vec3 direction = farPoint.xyz / farPoint.w - origin;
    if (abs(direction.z) < 1e-12) discard;
    float t = (uPlaneZ - origin.z) / direction.z;
    if (t <= 0.0) discard;
    vec2 p = (origin + direction * t).xy;

    // the plate: a lighter rectangle with an outline, the grid goes on around it a little dimmer
    float onPlate = 0.0;
    float edge = 0.0;
    if (uPlate.x > 0.0) {
        vec2 q = abs(p) - uPlate.xy;
        float d = max(q.x, q.y);
        float w = max(fwidth(d), 1e-7);
        onPlate = 1.0 - smoothstep(-w, w, d);
        edge = 1.0 - smoothstep(0.0, 1.5 * w, abs(d));
    }
    float strength = uPlate.x > 0.0 ? mix(0.55, 1.0, onPlate) : 1.0;

    float minor = level(p, uSpacing);
    float major = 0.0;
    float cell = uSpacing * 5.0;
    for (int k = 0; k < 7; k++) {
        major = max(major, level(p, cell));
        cell *= 5.0;
    }
    vec3 color = mix(uBackground, uPlateFill, onPlate);
    color = mix(color, uMinor, minor * strength);
    color = mix(color, uMajor, major * strength);
    color = mix(color, uPlate.z > 0.5 ? uPlateOver : uPlateEdge, edge);
    // towards the horizon the floor dissolves into the background
    float elevation = abs(direction.z) / length(direction);
    outColor = vec4(mix(uBackground, color, smoothstep(0.0, 0.06, elevation)), 1.0);
}";
```

Dans la méthode `Draw`, dans le bloc `if (ShowGrid)`, juste après la ligne qui envoie `uMajor`
(`_gl.Uniform3(_gl.GetUniformLocation(_gridProgram, "uMajor"), ...)`), ajouter :

```csharp
            _gl.Uniform3(_gl.GetUniformLocation(_gridProgram, "uPlate"), PlateHalfWidth, PlateHalfDepth, PlateOverflow ? 1f : 0f);
            _gl.Uniform3(_gl.GetUniformLocation(_gridProgram, "uPlateFill"), 0.10f, 0.15f, 0.21f);
            _gl.Uniform3(_gl.GetUniformLocation(_gridProgram, "uPlateEdge"), 0.45f, 0.62f, 0.85f);
            _gl.Uniform3(_gl.GetUniformLocation(_gridProgram, "uPlateOver"), 0.96f, 0.50f, 0.50f);
```

- [ ] **Étape 3 : liste des imprimantes avec leur volume d'impression**

Créer `src/MS3DPRINT.Manager.Desktop/Viewer/ViewerPrinter.cs` :

```csharp
namespace MS3DPRINT.Manager.Desktop.Viewer;

/// <summary>An active printer of the workshop as the viewer needs it: its name and its build volume as typed on the Printers page.</summary>
public sealed record ViewerPrinter(string Name, string BuildVolume);
```

Dans `MainWindow.Viewer.cs`, le constructeur est appelé ainsi :
`new ModelViewerView(CloseModelViewer, () => { ... filaments ... }, () => { ... noms ... });` Ajouter un quatrième argument après la lambda des noms :

```csharp
            }, () =>
            {
                var root = WorkspaceRoot;
                return new WorkspaceInventoryReader().LoadPrinters(root).Where(printer => printer.IsActive)
                    .Select(printer => new ViewerPrinter(printer.DisplayName, printer.BuildVolume)).ToList();
            });
```

(c'est-à-dire que la fermeture `});` de l'appel devient `}, () => { ... });` : le troisième argument se termine par `},` et le quatrième est la lambda ci-dessus.)

Dans `ModelViewerView.cs`, signature du constructeur : ajouter le paramètre
`Func<IReadOnlyList<ViewerPrinter>>? loadPrinters = null` après `loadPrinterNames`, et au début du constructeur, après `_loadPrinterNames = loadPrinterNames;` ajouter `_loadPrinters = loadPrinters;`.

- [ ] **Étape 4 : le fichier partiel du plateau**

Créer `src/MS3DPRINT.Manager.Desktop/Viewer/ModelViewerView.Plate.cs` :

```csharp
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
    private void ApplyPlate()
    {
        var choice = _plateChoice.SelectedItem as PlateChoice;
        if (choice?.Custom == true)
        {
            _plateText.IsVisible = true;
            _plate = PlateSize.Parse(_plateText.Text) ?? PlateSize.Default;
        }
        else
        {
            _plateText.IsVisible = false;
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
        viewport.PlateHalfWidth = (float)(_plate.WidthMm / 2 / scale);
        viewport.PlateHalfDepth = (float)(_plate.DepthMm / 2 / scale);
        if (_mesh is not { } mesh)
        {
            viewport.PlateOverflow = false;
            _plateStatus.Text = $"Plateau {_plate.Label}.";
            return;
        }
        var size = mesh.Size * (float)scale;
        var overflow = _plate.Overflow(size);
        viewport.PlateOverflow = overflow != Vector3.Zero;
        if (overflow == Vector3.Zero)
        {
            _plateStatus.Text = $"Plateau {_plate.Label} : la pièce tient.";
            return;
        }
        var parts = new List<string>();
        if (overflow.X > 0) parts.Add($"{overflow.X.ToString("0.#", French)} mm en largeur");
        if (overflow.Y > 0) parts.Add($"{overflow.Y.ToString("0.#", French)} mm en profondeur");
        if (overflow.Z > 0) parts.Add($"{overflow.Z.ToString("0.#", French)} mm en hauteur");
        _plateStatus.Text = $"Plateau {_plate.Label} : la pièce dépasse de {string.Join(", ", parts)}. Essayez une autre orientation.";
    }
}
```

- [ ] **Étape 5 : brancher le plateau dans `ModelViewerView.cs`**

a) Dans le constructeur, après `InitializeOrientation();` ajouter `InitializePlate();`.

b) Dans l'onglet Infos (`var infoTab = ...`), remplacer
`_supports, Muted("Vitesse de la machine"), _speedClass, _volumeNote,` par :

```csharp
                _supports, Muted("Vitesse de la machine"), _speedClass,
                Muted("Plateau d’impression"), _plateChoice, _plateText, _plateStatus, _volumeNote,
```

c) Dans `ApplyGrid`, juste avant la dernière ligne `viewport.RequestRender(false);` de la méthode, ajouter `PushPlate(viewport);`.

d) Dans le gestionnaire `_scale.TextChanged += (_, _) => { RefreshMeasures(); RefreshDimensions(); RebuildComparison(); };`
(ligne avec `RefreshDimensions`), ajouter `ApplyPlate();` à la fin des instructions entre accolades.

e) Dans `ReloadFilamentsAsync`, au tout début (avant `await EnsureSlicerAsync();`), ajouter `ReloadPlates();`.

f) Variable de démarrage : dans `LoadModelAsync`, après le bloc `MS3DPRINT_START_GRID`, ajouter :

```csharp
            // development hook, like the other MS3DPRINT_START_* variables: open with a plate of these dimensions, e.g. "50x50x50"
            if (Environment.GetEnvironmentVariable("MS3DPRINT_START_PLATE") is { Length: > 0 } startPlate && _plateChoice.ItemsSource is IEnumerable<PlateChoice> plateChoices)
            {
                _plateChoice.SelectedItem = plateChoices.Last();
                _plateText.Text = startPlate;
            }
```

Dans `ShowOrientedMesh` (fichier `ModelViewerView.Orientation.cs`), avant `_viewport.RequestRender(false);` ajouter `PushPlate(_viewport);`.
Dans `LoadModelAsync`, après la ligne `ApplyRememberedUnit(path, mesh);` ajouter `PushPlate(_viewport);`.

- [ ] **Étape 6 : volume d'impression dans la faisabilité**

Dans `ModelViewerView.Assessment.cs`, méthode `RunAssessmentAsync`, avant `var token = cancellation.Token;` ajouter :

```csharp
        var plate = _plate.ToVector3();
```

et remplacer `new FeasibilitySettings { LayerHeightMm = layer }` par `new FeasibilitySettings { LayerHeightMm = layer, BuildVolumeMm = plate }`.

- [ ] **Étape 7 : compiler et tester**

Run : `dotnet build MS3DPRINT.Manager.sln -c Release 2>&1 | Select-String "error|Avertissement|Erreur"` puis `dotnet test tests/MS3DPRINT.Manager.Core.Tests`
Attendu : 0 erreur, 0 avertissement, tests réussis.

- [ ] **Étape 8 : vérification à l'écran**

Lancer l'application de développement avec `MS3DPRINT_START_GRID=1` : un rectangle plus clair avec contour entoure la pièce, la grille continue autour,
plus discrète. Relancer avec `MS3DPRINT_START_PLATE=50x50x50` : le contour du plateau devient **rouge** et l'onglet Infos dit que la pièce dépasse ;
dans l'onglet Analyse, le constat « dépasse le volume d'impression » apparaît. Rogner et supprimer les captures après lecture.

- [ ] **Étape 9 : commit (seulement si demandé)**

```bash
git add src/MS3DPRINT.Manager.Desktop/Viewer src/MS3DPRINT.Manager.Desktop/MainWindow.Viewer.cs
git commit -m "feat(viewer): plateau d'impression dans la grille infinie"
```

---

### Tâche 8 : outil « Poser sur une face »

**Fichiers :**
- Modifier : `src/MS3DPRINT.Manager.Desktop/Styles/Theme.axaml`, `Viewer/ModelViewerView.cs`, `Viewer/ModelViewerView.Orientation.cs`

**Interfaces :**
- Consomme : `ModelOrientation.FaceDown(Vector3)` (tâche 3, normale dans le repère de la pièce affichée), `ViewMath.Ray`, `MeshPicker.TryPick`, `OrientationChanged()` (tâche 6).
- Produit : `_faceToggle` (`ToggleButton`), `FaceClickAsync(Point)`, `UpdateFaceMode()`.

- [ ] **Étape 1 : icône**

Dans `src/MS3DPRINT.Manager.Desktop/Styles/Theme.axaml`, juste après la ligne `<StreamGeometry x:Key="IconCut">...</StreamGeometry>` ajouter :

```xml
      <StreamGeometry x:Key="IconFace">M12 3l8 4.5v9L12 21l-8-4.5v-9Z M4 16.5l8 4.5 8-4.5-8-4.5Z</StreamGeometry>
```

- [ ] **Étape 2 : le bouton et son câblage dans `ModelViewerView.cs`**

a) Champs, près de `_cutToggle` : ajouter
`private readonly ToggleButton _faceToggle = new() { IsEnabled = false };`

b) Dans le constructeur, après `Tool(_cutToggle, "IconCut", "Plan de coupe (C)");` ajouter
`Tool(_faceToggle, "IconFace", "Poser sur une face (F)");`

c) Dans la barre d'outils, remplacer
`Children = { _reset, _wireToggle, divider, _measureToggle, _cutToggle, compareTool, _captureTool }` par
`Children = { _reset, _wireToggle, divider, _measureToggle, _cutToggle, _faceToggle, compareTool, _captureTool }`.

d) Remplacer la ligne
`_measureToggle.IsCheckedChanged += (_, _) => { if (_measureToggle.IsChecked == true) ShowTab(2); UpdateMeasureMode(); };` par :

```csharp
        _measureToggle.IsCheckedChanged += (_, _) => { if (_measureToggle.IsChecked == true) { _faceToggle.IsChecked = false; ShowTab(2); } UpdateMeasureMode(); };
        _faceToggle.IsCheckedChanged += (_, _) => { if (_faceToggle.IsChecked == true) { _measureToggle.IsChecked = false; ShowTab(2); } UpdateFaceMode(); };
```

e) Remplacer `next.Clicked += point => _ = MeasureClickAsync(point);` par :

```csharp
        next.Clicked += point => { if (_faceToggle.IsChecked == true) _ = FaceClickAsync(point); else _ = MeasureClickAsync(point); };
```

f) Dans `LoadModelAsync`, après `_orientGroup.IsEnabled = true;` ajouter `_faceToggle.IsEnabled = true;`.

g) Dans `Viewport_KeyDown`, ajouter (avant le `else if (e.Key == Key.W)`) :

```csharp
        else if (e.Key == Key.F && _faceToggle.IsEnabled) { _faceToggle.IsChecked = _faceToggle.IsChecked != true; e.Handled = true; }
        else if (e.Key == Key.Escape && _faceToggle.IsChecked == true) { _faceToggle.IsChecked = false; e.Handled = true; }
```

- [ ] **Étape 3 : la logique dans `ModelViewerView.Orientation.cs`**

Ajouter `using Avalonia;` et `using Avalonia.Input;` s'il manque, puis dans la classe :

```csharp
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
        var inverted = _measures?.Inverted == true;
        var picked = await Task.Run(() =>
        {
            var (origin, direction) = ViewMath.Ray(mesh, camera, bounds.Width, bounds.Height, click.X, click.Y);
            if (!MeshPicker.TryPick(mesh, origin, direction, out _, out var triangle)) return (System.Numerics.Vector3?)null;
            var normal = mesh.Normals[triangle];
            if (inverted) normal = -normal;
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
```

- [ ] **Étape 4 : compiler et tester**

Run : `dotnet build MS3DPRINT.Manager.sln -c Release 2>&1 | Select-String "error|Avertissement|Erreur"` puis `dotnet test tests/MS3DPRINT.Manager.Core.Tests`
Attendu : 0 erreur, 0 avertissement, tests réussis.

- [ ] **Étape 5 : vérification à l'écran**

Lancer l'application de développement (modèle posé sur le côté) : l'icône apparaît dans la barre d'outils, la touche F l'active (curseur en croix, message dans Infos),
un clic sur une face la pose sur le plateau. Si l'automatisation du clic n'est pas possible, vérifier au moins que l'outil s'active et que
Échap le quitte, et le dire.

- [ ] **Étape 6 : commit (seulement si demandé)**

```bash
git add src/MS3DPRINT.Manager.Desktop/Styles/Theme.axaml src/MS3DPRINT.Manager.Desktop/Viewer/ModelViewerView.cs src/MS3DPRINT.Manager.Desktop/Viewer/ModelViewerView.Orientation.cs
git commit -m "feat(viewer): poser une face sur le plateau par clic"
```

---

### Tâche 9 : orientation automatique dans l'interface

**Fichiers :**
- Modifier : `Viewer/ModelViewerView.Assessment.cs`, `Viewer/ModelViewerView.Orientation.cs`, `Viewer/ModelViewerView.cs`

**Interfaces :**
- Consomme : `AutoOrient.Propose(...)`, `AutoOrientGoal`, `OrientationProposal` (tâche 4), `ModelOrientation.Compose` (tâche 3), `PlateSize.ToVector3` (tâche 2), `ModelAssessment.ToPrintMesh`.
- Produit : `CurrentEstimateInputs() -> (EstimateSettings? Settings, MachineProfile Machine)` (partagé avec l'estimation rapide) ; `_autoOrient` (`Button`), `_autoGoal` (`ComboBox`), `AutoOrientAsync()`.

- [ ] **Étape 1 : extraire les entrées d'estimation**

Dans `ModelViewerView.Assessment.cs`, méthode `RunAssessmentAsync`, remplacer le bloc qui va de la ligne `var layer = ModelAssessment.LayerHeightFrom(...)` jusqu'à la ligne
`var machine = new MachineProfile { HeadSpeedFactor = ... };` incluse par :

```csharp
        var layer = ModelAssessment.LayerHeightFrom(_slicerQuality.SelectedItem as string);
        var material = _material.SelectedItem as MaterialChoice;
        var (settings, machine) = CurrentEstimateInputs();
```

Puis ajouter dans la même classe la méthode :

```csharp
    /// <summary>The estimate settings and machine for what the panel shows now; no settings when a field is not valid.</summary>
    private (EstimateSettings? Settings, MachineProfile Machine) CurrentEstimateInputs()
    {
        var layer = ModelAssessment.LayerHeightFrom(_slicerQuality.SelectedItem as string);
        var material = _material.SelectedItem as MaterialChoice;
        EstimateSettings? settings = null;
        if (material is not null && TryAmount(_infill.Text, out var infill) && infill <= 100 && TryAmount(_walls.Text, out var wallMm) && wallMm > 0)
            settings = new EstimateSettings
            {
                LayerHeightMm = layer, InfillPercent = infill, WallCount = Math.Min(20, ModelAssessment.WallLoopsFrom(wallMm)),
                DensityGcm3 = material.Density, Supports = _supports.IsChecked == true ? SupportMode.Auto : SupportMode.None,
                MaterialMaxFlowMm3S = material.MaxFlowMm3S ?? MeshVolume.MaxFlowFor(material.MaterialName)
            };
        var machine = new MachineProfile { HeadSpeedFactor = ModelAssessment.SpeedClasses[Math.Max(0, _speedClass.SelectedIndex)].Factor };
        return (settings, machine);
    }
```

Compiler : `dotnet build src/MS3DPRINT.Manager.Desktop -c Release`. Attendu : 0 erreur. (La variable `layer` reste utilisée par `FeasibilitySettings`, `material` par `_quickDensity`.)

- [ ] **Étape 2 : l'interface de l'orientation automatique**

Dans `ModelViewerView.Orientation.cs`, ajouter les champs :

```csharp
    private readonly Button _autoOrient = new() { Content = "Orientation automatique", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _autoGoal = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Moins de supports", "Plus rapide", "Plus stable" }, SelectedIndex = 0 };
    private CancellationTokenSource? _autoOrientCancel;
```

Dans `InitializeOrientation`, avant `_orientGroup.Children.Add(_resetOrientation);`, ajouter :

```csharp
        _autoOrient.Click += async (_, _) => await AutoOrientAsync();
        _orientGroup.Children.Add(Muted("Orientation automatique : objectif"));
        _orientGroup.Children.Add(_autoGoal);
        _orientGroup.Children.Add(_autoOrient);
```

Ajouter la méthode :

```csharp
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
        var goal = (Estimator.AutoOrientGoal)Math.Max(0, _autoGoal.SelectedIndex);
        var plate = _plate.ToVector3();
        var version = _loadVersion;
        using var cancellation = new CancellationTokenSource();
        _autoOrientCancel = cancellation;
        _autoOrient.Content = "Annuler";
        _orientStatus.Text = "Évaluation des orientations…";
        var progress = new Progress<double>(value => _orientStatus.Text = $"Évaluation des orientations… {value:P0}");
        try
        {
            var proposal = await Task.Run(() => Estimator.AutoOrient.Propose(ModelAssessment.ToPrintMesh(mesh, scale), settings, machine, goal, plate, progress, cancellation.Token), cancellation.Token);
            if (version != _loadVersion || !ReferenceEquals(_mesh, mesh)) return;
            if (!proposal.IsImprovement)
            {
                _orientStatus.Text = proposal.Current.FitsPlate
                    ? "L’orientation actuelle est déjà la meilleure parmi celles essayées."
                    : "Aucune orientation essayée ne tient sur le plateau : la pièce est trop grande.";
                return;
            }
            _orientation.Compose(proposal.Best.Rotation);
            OrientationChanged();
            var supports = (proposal.Best.SupportVolumeMm3 - proposal.Current.SupportVolumeMm3) / 1000;
            var minutes = (proposal.Best.TimeSeconds - proposal.Current.TimeSeconds) / 60;
            _orientStatus.Text = $"Orientation appliquée : supports {supports:+0.##;−0.##;0} cm³, durée {minutes:+0;−0;0} min"
                + (proposal.Best.FitsPlate ? "." : ", mais la pièce dépasse encore du plateau.")
                + (proposal.Limited ? " Modèle très volumineux : seuls les six axes ont été essayés." : string.Empty);
        }
        catch (OperationCanceledException) { _orientStatus.Text = "Évaluation annulée."; }
        catch (ArgumentException exception) { _orientStatus.Text = "Évaluation impossible : " + exception.Message; }
        finally
        {
            _autoOrientCancel = null;
            _autoOrient.Content = "Orientation automatique";
        }
    }
```

Remarque : `Estimator.` désigne l'espace de noms `MS3DPRINT.Estimator` ; si le compilateur le confond avec autre chose, ajouter en tête du fichier
`using MS3DPRINT.Estimator;` et écrire `AutoOrientGoal` / `AutoOrient` sans préfixe.

Dans `ModelViewerView.cs` :
- Dans `LoadModelAsync`, après `_faceToggle.IsEnabled = true;` : rien d'autre (`_orientGroup` active déjà le bouton).
- Dans `Viewport_KeyDown`, ajouter avant `else if (e.Key == Key.W)` :
  `else if (e.Key == Key.O && _original is not null) { _ = AutoOrientAsync(); e.Handled = true; }`
- Dans `Release()`, ajouter `_autoOrientCancel?.Cancel();`.

- [ ] **Étape 3 : compiler et tester**

Run : `dotnet build MS3DPRINT.Manager.sln -c Release 2>&1 | Select-String "error|Avertissement|Erreur"` puis `dotnet test tests/MS3DPRINT.Manager.Core.Tests`
Attendu : 0 erreur, 0 avertissement, tests réussis.

- [ ] **Étape 4 : vérification à l'écran**

Ouvrir un modèle avec surplombs (le crâne `02_MODELES_3D`), appuyer sur « Orientation automatique » : le message de progression apparaît puis
« Orientation appliquée : supports … cm³, durée … min » (ou « déjà la meilleure ») ; la section Faisabilité et le poids estimé se mettent à jour.
Mesurer le temps de calcul sur le crâne (207 000 triangles) et le noter dans le message de fin de tâche.

- [ ] **Étape 5 : commit (seulement si demandé)**

```bash
git add src/MS3DPRINT.Manager.Desktop/Viewer
git commit -m "feat(viewer): orientation automatique de la pièce"
```

---

### Tâche 10 : découpe Bambu sur la pièce tournée et enregistrement du modèle orienté

**Fichiers :**
- Modifier : `Viewer/ModelViewerView.cs` (méthode `RunSliceAsync`), `Viewer/ModelViewerView.Orientation.cs`
- Test : `tests/MS3DPRINT.Manager.Core.Tests/Slicing/SlicerTests.cs`

**Interfaces :**
- Consomme : `StlWriter.Write` (tâche 5), `ModelOrientation.IsIdentity` (tâche 3), `SliceRequest(string ModelPath, ..., double MillimetresPerUnit = 1, bool? Supports = null)`.
- Produit : `WriteOrientedCopy(MeshModel) -> string` (STL temporaire en mm), `SaveOrientedAsync()`, `_saveOriented` (`Button`).

- [ ] **Étape 1 : test que l'échelle n'est pas passée pour une copie en millimètres**

Dans `tests/MS3DPRINT.Manager.Core.Tests/Slicing/SlicerTests.cs`, ajouter un test (même classe, même modèle que `BuildArguments_ScalesAModelDrawnInInches`) :

```csharp
    [Fact]
    public void BuildArguments_DoesNotScaleAnOrientedCopyAlreadyInMillimetres()
    {
        var install = new SlicerInstall("Bambu Studio", "bambu-studio.exe", _root);
        var profiles = new SlicerProfiles(install);
        var machine = profiles.Machines()[0];

        var arguments = SliceRunner.BuildArguments(install, profiles, new SliceRequest(@"C:\temp\oriented.stl", machine, "0.20mm Standard @BBL A1", null, MillimetresPerUnit: 1), "out");

        Assert.DoesNotContain("--scale", arguments);
        Assert.Equal(@"C:\temp\oriented.stl", arguments[^1]);
    }
```

Run : `dotnet test tests/MS3DPRINT.Manager.Core.Tests --filter OrientedCopy`
Attendu : réussi (le comportement existe déjà ; ce test le verrouille).

- [ ] **Étape 2 : découper la copie tournée**

Dans `ModelViewerView.cs`, méthode `RunSliceAsync`, remplacer :

```csharp
        var request = new SliceRequest(_modelPath, machine, quality, _slicerProfiles.FilamentFor(machine, material?.MaterialName), infill, loops, MeasureScale, _supports.IsChecked == true);
        var path = _modelPath;
```

par :

```csharp
        // a turned model is sliced from a temporary copy in millimetres; an untouched one from its own file
        var orientedCopy = _orientation.IsIdentity || _mesh is not { } shown ? null : await WriteOrientedCopyAsync(shown);
        var request = new SliceRequest(orientedCopy ?? _modelPath, machine, quality, _slicerProfiles.FilamentFor(machine, material?.MaterialName), infill, loops,
            orientedCopy is null ? MeasureScale : 1, _supports.IsChecked == true);
        var path = _modelPath;
```

et dans le bloc `finally` de la même méthode (celui qui contient `if (ReferenceEquals(_sliceCancel, cancellation)) _sliceButton.IsEnabled = true;`) ajouter en premier :

```csharp
            if (orientedCopy is not null)
                try { File.Delete(orientedCopy); } catch (IOException) { } catch (UnauthorizedAccessException) { }
```

- [ ] **Étape 3 : copie temporaire et enregistrement**

Dans `ModelViewerView.Orientation.cs`, ajouter le champ
`private readonly Button _saveOriented = new() { Content = "Enregistrer le modèle orienté…", HorizontalAlignment = HorizontalAlignment.Stretch };`,
dans `InitializeOrientation`, après `_orientGroup.Children.Add(_autoOrient);` :

```csharp
        _saveOriented.Click += async (_, _) => await SaveOrientedAsync();
        _orientGroup.Children.Add(_saveOriented);
```

et les méthodes (ajouter `using Avalonia.Platform.Storage;` en tête du fichier) :

```csharp
    /// <summary>Writes the part as shown to a temporary STL in millimetres, for the slicer.</summary>
    private async Task<string> WriteOrientedCopyAsync(MeshModel mesh)
    {
        var path = Path.Combine(Path.GetTempPath(), "ms3d-oriented-" + Guid.NewGuid().ToString("N") + ".stl");
        var scale = MeasureScale;
        await Task.Run(() => StlWriter.Write(path, mesh, scale));
        return path;
    }

    /// <summary>Saves the part as shown to an STL in millimetres, next to the model by default. The original file is not touched.</summary>
    private async Task SaveOrientedAsync()
    {
        if (_mesh is not { } mesh || _modelPath is null) return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
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
        try
        {
            await Task.Run(() => StlWriter.Write(local, mesh, scale));
            _orientStatus.Text = "Modèle orienté enregistré : " + Path.GetFileName(local) + ".";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _orientStatus.Text = "Enregistrement impossible : " + exception.Message;
        }
    }
```

Dans `LoadModelAsync` rien à ajouter : `_orientGroup.IsEnabled` couvre le bouton.

- [ ] **Étape 4 : compiler et tester**

Run : `dotnet build MS3DPRINT.Manager.sln -c Release 2>&1 | Select-String "error|Avertissement|Erreur"` puis `dotnet test tests/MS3DPRINT.Manager.Core.Tests`
Attendu : 0 erreur, 0 avertissement, tests réussis.

- [ ] **Étape 5 : vérification à l'écran**

Ouvrir un modèle, le tourner (`MS3DPRINT_START_ROTATE_X=90`), laisser Bambu Studio découper : la durée et le poids de Bambu doivent correspondre
à l'orientation tournée (comparer avec l'orientation d'origine : un modèle haut couché change nettement de durée). Vérifier qu'aucun fichier
`ms3d-oriented-*.stl` ne reste dans `%TEMP%` après la découpe. Vérifier « Enregistrer le modèle orienté » : le STL écrit se rouvre dans le visualiseur
posé sur la grille aux bonnes dimensions.

- [ ] **Étape 6 : commit (seulement si demandé)**

```bash
git add src/MS3DPRINT.Manager.Desktop/Viewer tests/MS3DPRINT.Manager.Core.Tests/Slicing/SlicerTests.cs
git commit -m "feat(viewer): découpe sur la pièce tournée et enregistrement du modèle orienté"
```

---

### Tâche 11 : spécification, documentation et vérification d'ensemble

**Fichiers :**
- Modifier : `docs/superpowers/specs/2026-10-16-visualiseur-type-decoupeur-design.md`, `CHANGELOG.md`, `docs/api-devis-en-ligne-plan.md`

- [ ] **Étape 1 : aligner la spécification sur ce qui a été construit**

Dans la spécification, paragraphe 4.1, remplacer « tourne la pièce pour que la normale (en coordonnées du modèle d'origine) pointe vers −Z »
par « tourne la pièce pour que la normale (en coordonnées de la pièce **telle qu'affichée**) pointe vers −Z ». Au paragraphe 4.4, supprimer la phrase
« La face survolée est surlignée. » et ajouter « Pas de surlignage au survol : le résultat est visible tout de suite. ». Au paragraphe 4.5, remplacer
« fonction de chargement étendue » par « un nouveau paramètre de constructeur `loadPrinters` qui renvoie des `ViewerPrinter(Name, BuildVolume)` ».

- [ ] **Étape 2 : journal des modifications**

Dans `CHANGELOG.md`, sous `## 16 octobre 2026`, ajouter (en tête de section, avant « Visualiseur 3D : estimation instantanée… ») :

```markdown
### Visualiseur 3D type découpeur : plateau et réorientation
- La pièce est **posée sur un plateau d'impression** : un rectangle plus clair avec contour dans la grille infinie, aux dimensions d'une imprimante de l'atelier (champ « volume d'impression » de la page Imprimantes), d'un plateau générique de 256 × 256 × 256 mm ou saisies à la main. Le contour passe en rouge et l'onglet Infos dit de combien la pièce dépasse ; la faisabilité signale « dépasse le volume d'impression ».
- **Réorientation** (onglet Outils) : rotations de ±90° autour de X, Y et Z, champs d'angles en degrés, retour à l'orientation d'origine, et **« Poser sur une face »** (touche F) : on clique la face qui doit devenir le dessous.
- **Orientation automatique** (touche O) : le logiciel essaie les six axes et les plus grandes faces planes, les évalue avec le moteur d'estimation et applique celle qui demande le moins de supports (ou la plus rapide, ou la plus stable) ; il indique le gain en supports et en durée.
- Le poids, la durée, les supports, la faisabilité et la **découpe Bambu Studio** suivent l'orientation choisie. Le fichier d'origine n'est jamais modifié ; « Enregistrer le modèle orienté » écrit une copie STL en millimètres.
- Tourner la pièce efface les mesures, le plan de coupe et la comparaison, qui appartenaient à l'ancienne position.
```

- [ ] **Étape 3 : plan de l'API**

Dans `docs/api-devis-en-ligne-plan.md`, paragraphe 11, remplacer la ligne « **Orientation automatique** : à étudier après le MVP… » par :
« **Orientation automatique** : faite dans le moteur (`AutoOrient`, objectifs moins de supports, plus rapide, plus stable) ; à exposer dans l'API après le MVP, car elle change le poids de supports donc le prix. »

- [ ] **Étape 4 : vérification d'ensemble**

Run :
```
dotnet build MS3DPRINT.Manager.sln -c Release 2>&1 | Select-String "error|Avertissement|Erreur"
dotnet test tests/MS3DPRINT.Manager.Core.Tests
dotnet test tests/MS3DPRINT.Estimator.Tests
```
Attendu : 0 erreur, 0 avertissement ; tous les tests réussissent (le nombre de tests augmente de ≈ 60 par rapport à 556 + 40).
Faire un dernier essai complet à l'écran : ouvrir un modèle, vérifier le plateau, tourner, poser sur une face, orientation automatique, durée Bambu, enregistrement.
Rapporter honnêtement ce qui n'a pas pu être vérifié (clic sur une face, boîte d'enregistrement).

- [ ] **Étape 5 : livraison (seulement si demandée)**

Suivre `AGENTS.md` : `dotnet publish src/MS3DPRINT.Manager.Windows.App/MS3DPRINT.Manager.Windows.App.csproj -p:PublishProfile=PortableWinX64 -o outputs/MS3DPRINT-Manager`,
sauvegarde de l'ancien `MS3DPRINT Manager.exe` dans `outputs/backups` (nom horodaté `.exe.bak`), copie du binaire publié sous le nom
`MS3DPRINT Manager.exe` à la racine du projet, comparaison des SHA256, vérification que `data` est intact. Ne pas livrer si l'application tourne.

---

## Auto-relecture

**Couverture de la spécification.** Principe de copie transformée (§3) : tâches 3 et 6. `ModelOrientation` (§4.1) : tâche 3. `PlateSize` (§4.2) : tâche 2.
`AutoOrient` (§4.3) : tâche 4 (candidats : 6 axes + 12 familles à 5°, notes avec le moteur, objectifs, annulation, progression, limite à 6 candidats au-delà d'un million de triangles).
Outil « poser sur une face » (§4.4) : tâche 8. Plateau à l'écran et choix des imprimantes (§4.5) : tâche 7. `StlWriter`, découpe sur la pièce tournée, enregistrement (§4.6) : tâches 5 et 10.
Flux de données et effacement des mesures/coupe/comparaison (§5) : tâche 6 (`ShowOrientedMesh`). Interface (§6) : tâches 6 à 10 (touche F, touche O, désactivation pendant la comparaison via `ClearComparison`).
Cas limites (§7) : plateau illisible → générique (tâche 2, 7), pièce trop grande → rouge + message (tâches 7, 9), gros modèle → six axes (tâche 4), annulation (tâches 4, 9).
Tests (§8) : tâches 1 à 5 et 10.
**Écarts assumés par rapport à la spécification** (corrigés dans la tâche 11) : normale de `FaceDown` en repère de la pièce affichée ; pas de surlignage au survol ;
le maillage non étanche n'est pas traité à part (l'orientation automatique utilise les normales telles quelles : à noter dans le message de fin de tâche 9 si le crâne ou un modèle ouvert donne un résultat douteux).

**Cohérence des types.** `ModelOrientation.Compose/FaceDown/RotateAxis/SetEuler/EulerDegrees/Apply/IsIdentity/Reset` (tâche 3) sont les noms utilisés aux tâches 6, 8, 9, 10.
`PlateSize.Default/Parse/Fits/Overflow/ToVector3/Label` (tâche 2) : tâches 7 et 9. `AutoOrient.Propose(mesh, settings, machine, goal, plateMm, progress, token)` (tâche 4) : tâche 9.
`OrientationProposal.IsImprovement/Best/Current/Limited` et `OrientationCandidate.SupportVolumeMm3/TimeSeconds/FitsPlate` : tâche 9. `StlWriter.Write(path, mesh, millimetresPerUnit)` (tâche 5) : tâche 10.
`ViewerPrinter(Name, BuildVolume)` et `loadPrinters` (tâche 7) : constructeur de `ModelViewerView` et `MainWindow.Viewer.cs`.
`PlateHalfWidth/PlateHalfDepth/PlateOverflow` : interface + les deux rendus (tâche 7).
