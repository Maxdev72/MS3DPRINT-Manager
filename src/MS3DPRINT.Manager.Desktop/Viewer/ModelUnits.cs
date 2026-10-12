using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace MS3DPRINT.Manager.Desktop.Viewer;

/// <summary>A length unit a model can be drawn in; STL and OBJ files do not say which.</summary>
public sealed record ModelUnit(string Key, string Label, string Short, double MmPerUnit);

public static class ModelUnits
{
    public const string CustomKey = "custom";

    public static readonly IReadOnlyList<ModelUnit> All =
    [
        new("mm", "Millimètres (mm)", "mm", 1),
        new("cm", "Centimètres (cm)", "cm", 10),
        new("m", "Mètres (m)", "m", 1000),
        new("in", "Pouces (in)", "in", 25.4)
    ];

    public static ModelUnit Default => All[0];

    public static ModelUnit? ByKey(string? key) => All.FirstOrDefault(unit => unit.Key == key);

    public static string Format(double value) => value.ToString("0.##", CultureInfo.GetCultureInfo("fr-FR"));

    /// <summary>Size of the model in the chosen unit, followed by the same size in millimetres when the unit is another one.</summary>
    public static string FormatSize(Vector3 size, double mmPerUnit, string shortLabel)
    {
        var text = $"{Format(size.X)} × {Format(size.Y)} × {Format(size.Z)} {shortLabel}";
        return Math.Abs(mmPerUnit - 1) < 1e-9 ? text : $"{text} ({Format(size.X * mmPerUnit)} × {Format(size.Y * mmPerUnit)} × {Format(size.Z * mmPerUnit)} mm)";
    }

    /// <summary>A hint when the largest side would be tiny or huge for a 3D print in this unit; null when it looks plausible.</summary>
    public static string? Warning(Vector3 size, double mmPerUnit)
    {
        var largest = Math.Max(size.X, Math.Max(size.Y, size.Z)) * mmPerUnit;
        if (largest <= 0 || !double.IsFinite(largest)) return null;
        if (largest < 2) return $"Le plus grand côté ne ferait que {Format(largest)} mm : vérifiez l’unité du modèle.";
        if (largest > 1500) return $"Le plus grand côté ferait {Format(largest / 1000)} m : vérifiez l’unité du modèle.";
        return null;
    }
}

/// <summary>Remembers, per model file, the unit the user chose (a preset key, or "custom:" followed by millimetres per unit).</summary>
public sealed class ModelUnitStore(string path)
{
    private const int MaxEntries = 500;

    public static ModelUnitStore Default { get; } = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MS3DPRINT Manager", "model-units.json"));

    public string? Get(string modelPath) => Load().TryGetValue(Key(modelPath), out var value) ? value : null;

    public void Set(string modelPath, string value)
    {
        try
        {
            var all = Load();
            all.Remove(Key(modelPath));
            all[Key(modelPath)] = value;
            while (all.Count > MaxEntries) all.Remove(all.Keys.First());
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(all));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { /* a lost preference is not worth an error */ }
    }

    private Dictionary<string, string> Load()
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? [] : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { return []; }
    }

    private static string Key(string modelPath) => Path.GetFullPath(modelPath).ToLowerInvariant();
}
