using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MS3DPRINT.Manager.Core.Slicing;

/// <summary>A slicer found on this computer, driven through its command line (Bambu Studio and OrcaSlicer share it).</summary>
public sealed record SlicerInstall(string Name, string ExecutablePath, string ProfilesDirectory);

/// <summary>A machine profile of the slicer (printer and nozzle) with its default quality and filament profiles.</summary>
public sealed record SlicerMachine(string Name, string PrinterModel, string FilePath, string? DefaultProcess, string? DefaultFilament, int ExtruderCount = 1)
{
    /// <summary>The command line cannot map a filament to the nozzles of a multi-nozzle printer, so such machines cannot be sliced this way.</summary>
    public bool CanBeSlicedAutomatically => ExtruderCount <= 1;
}

public sealed record SliceRequest(string ModelPath, SlicerMachine Machine, string ProcessName, string? FilamentName, int? InfillPercent = null, int? WallLoops = null, double MillimetresPerUnit = 1);

/// <summary>Print time and filament use computed by the slicer for one plate.</summary>
public sealed record SliceResult(double TimeSeconds, double FilamentLengthMm, double FilamentDiameterMm, double LayerHeight, double InfillPercent, int WallLoops, int Layers)
{
    public double TimeHours => TimeSeconds / 3600.0;
    public double FilamentVolumeCm3 => Math.PI * Math.Pow(FilamentDiameterMm / 2, 2) * FilamentLengthMm / 1000.0;
    /// <summary>Printed mass for a filament density in g/cm³ (the slicer's generic profiles carry none).</summary>
    public double WeightGrams(double density) => FilamentVolumeCm3 * density;
}

/// <summary>Finds an installed slicer and reads its system profiles.</summary>
public static class SlicerLocator
{
    public static SlicerInstall? Detect()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Detect(
            Path.Combine(programFiles, "Bambu Studio", "bambu-studio.exe"), Path.Combine(appData, "BambuStudio", "system", "BBL"),
            Path.Combine(programFiles, "OrcaSlicer", "orca-slicer.exe"), Path.Combine(appData, "OrcaSlicer", "system", "BBL"));
    }

    public static SlicerInstall? Detect(string bambuExe, string bambuProfiles, string orcaExe, string orcaProfiles)
    {
        if (File.Exists(bambuExe) && Directory.Exists(Path.Combine(bambuProfiles, "machine"))) return new SlicerInstall("Bambu Studio", bambuExe, bambuProfiles);
        if (File.Exists(orcaExe) && Directory.Exists(Path.Combine(orcaProfiles, "machine"))) return new SlicerInstall("OrcaSlicer", orcaExe, orcaProfiles);
        return null;
    }
}

public sealed class SlicerProfiles(SlicerInstall install)
{
    private static readonly Regex NozzleSize = new(@"(\d\.\d+) nozzle$", RegexOptions.Compiled);

    /// <summary>Machine profiles (printer + nozzle), 0.4 mm nozzles first.</summary>
    public IReadOnlyList<SlicerMachine> Machines()
    {
        var machines = new List<SlicerMachine>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(install.ProfilesDirectory, "machine"), "*.json"))
        {
            var root = Read(file);
            if (root is not { } json || Text(json, "type") != "machine" || Text(json, "instantiation") != "true") continue;
            var name = Text(json, "name");
            var model = Text(json, "printer_model");
            if (name is null || model is null || !NozzleSize.IsMatch(name)) continue;
            machines.Add(new SlicerMachine(name, model, file, Text(json, "default_print_profile"), FirstOf(json, "default_filament_profile"), Count(json, "nozzle_diameter")));
        }
        return machines
            .OrderBy(machine => NozzleSize.Match(machine.Name).Groups[1].Value == "0.4" ? 0 : 1)
            .ThenBy(machine => machine.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Quality (process) profiles made for this machine, by increasing layer height.</summary>
    public IReadOnlyList<string> Processes(SlicerMachine machine)
        => Compatible("process", machine).Select(entry => entry.Name).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>The generic filament profile of a material (PLA, PETG…) for this machine, else the machine's default filament.</summary>
    public string? FilamentFor(SlicerMachine machine, string? material)
    {
        var compatible = Compatible("filament", machine).Select(entry => entry.Name).ToList();
        if (!string.IsNullOrWhiteSpace(material))
        {
            var generic = compatible.FirstOrDefault(name => name.StartsWith("Generic " + material.Trim() + " @", StringComparison.OrdinalIgnoreCase));
            if (generic is not null) return generic;
        }
        return machine.DefaultFilament;
    }

    public string ProcessFile(string name) => Path.Combine(install.ProfilesDirectory, "process", name + ".json");
    public string FilamentFile(string name) => Path.Combine(install.ProfilesDirectory, "filament", name + ".json");

    private IEnumerable<(string Name, string File)> Compatible(string kind, SlicerMachine machine)
    {
        var directory = Path.Combine(install.ProfilesDirectory, kind);
        if (!Directory.Exists(directory)) yield break;
        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            var root = Read(file);
            if (root is not { } json || Text(json, "instantiation") != "true" || Text(json, "name") is not { } name) continue;
            if (json.TryGetProperty("compatible_printers", out var printers) && printers.ValueKind == JsonValueKind.Array
                && printers.EnumerateArray().Any(item => item.GetString() == machine.Name))
                yield return (name, file);
        }
    }

    private static JsonElement? Read(string file)
    {
        try { using var document = JsonDocument.Parse(File.ReadAllText(file)); return document.RootElement.Clone(); }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    private static string? Text(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int Count(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Array ? Math.Max(1, value.GetArrayLength()) : 1;

    /// <summary>
    /// The machine to slice with for a printer of the workshop: its own profile, or for a double-nozzle printer the single-nozzle
    /// sibling that moves the same way (a H2D is sliced as a H2S); null when there is none.
    /// </summary>
    public static SlicerMachine? MachineFor(IEnumerable<SlicerMachine> machines, string printerModel)
    {
        var usable = machines.Where(machine => machine.CanBeSlicedAutomatically).ToList();
        var exact = usable.FirstOrDefault(machine => string.Equals(machine.PrinterModel, printerModel, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;
        var sibling = printerModel.StartsWith("Bambu Lab H2D", StringComparison.OrdinalIgnoreCase) ? "Bambu Lab H2S" : null;
        return sibling is null ? null : usable.FirstOrDefault(machine => string.Equals(machine.PrinterModel, sibling, StringComparison.OrdinalIgnoreCase) && machine.Name.EndsWith("0.4 nozzle", StringComparison.Ordinal));
    }

    private static string? FirstOf(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Array && value.GetArrayLength() > 0 ? value[0].GetString() : null;
}

/// <summary>Runs the slicer without its window and reads print time and filament use from what it writes.</summary>
public sealed class SliceRunner(SlicerInstall install)
{
    /// <summary>A slicer that has not finished after this long is stopped.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(10);

    public static IReadOnlyList<string> BuildArguments(SlicerInstall install, SlicerProfiles profiles, SliceRequest request, string outputDirectory)
    {
        var arguments = new List<string>
        {
            "--slice", "0", "--outputdir", outputDirectory,
            "--load-settings", request.Machine.FilePath + ";" + profiles.ProcessFile(request.ProcessName)
        };
        if (request.FilamentName is not null) { arguments.Add("--load-filaments"); arguments.Add(profiles.FilamentFile(request.FilamentName)); }
        if (request.InfillPercent is { } infill) { arguments.Add("--sparse-infill-density"); arguments.Add(infill.ToString(CultureInfo.InvariantCulture) + "%"); }
        if (request.WallLoops is { } walls) { arguments.Add("--wall-loops"); arguments.Add(walls.ToString(CultureInfo.InvariantCulture)); }
        // slicers read STL and OBJ as millimetres: a model drawn in another unit is scaled to millimetres
        if (Math.Abs(request.MillimetresPerUnit - 1) > 1e-9) { arguments.Add("--scale"); arguments.Add(request.MillimetresPerUnit.ToString("G9", CultureInfo.InvariantCulture)); }
        arguments.Add(request.ModelPath);
        return arguments;
    }

    public async Task<SliceResult> RunAsync(SlicerProfiles profiles, SliceRequest request, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(request.ModelPath)) throw new FileNotFoundException("Le modèle est introuvable.", request.ModelPath);
        var output = Path.Combine(Path.GetTempPath(), "ms3d-slice-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        try
        {
            var start = new ProcessStartInfo(install.ExecutablePath) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var argument in BuildArguments(install, profiles, request, output)) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Le découpeur n’a pas démarré.");
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(Timeout);
            try { await process.WaitForExitAsync(limit.Token); }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                if (cancellationToken.IsCancellationRequested) throw;
                throw new InvalidOperationException("Le découpeur n’a pas répondu à temps ; il a été arrêté.");
            }
            return Read(output);
        }
        finally
        {
            try { Directory.Delete(output, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>Reads <c>result.json</c> (status, time, layer height) and the G-code header (filament length and diameter).</summary>
    public static SliceResult Read(string outputDirectory)
    {
        var resultPath = Path.Combine(outputDirectory, "result.json");
        if (!File.Exists(resultPath)) throw new InvalidOperationException("Le découpeur n’a produit aucun résultat.");
        using var document = JsonDocument.Parse(File.ReadAllText(resultPath));
        var root = document.RootElement;
        var code = root.TryGetProperty("return_code", out var codeValue) ? codeValue.GetInt32() : -1;
        if (code != 0)
            throw new InvalidOperationException("Découpe impossible : " + (root.TryGetProperty("error_string", out var message) ? message.GetString() : "erreur " + code));

        var plate = root.GetProperty("sliced_plates")[0];
        var seconds = plate.TryGetProperty("total_predication", out var total) ? total.GetDouble() : 0;
        var gcode = Directory.EnumerateFiles(outputDirectory, "*.gcode").FirstOrDefault();
        var header = gcode is null ? string.Empty : ReadHeader(gcode);
        return ParseResult(
            seconds,
            root.TryGetProperty("layer_height", out var layer) ? layer.GetDouble() : 0,
            root.TryGetProperty("sparse_infill_density", out var infill) ? infill.GetDouble() : 0,
            root.TryGetProperty("wall_loops", out var walls) ? walls.GetInt32() : 0,
            header);
    }

    public static SliceResult ParseResult(double seconds, double layerHeight, double infill, int wallLoops, string gcodeHeader)
    {
        double Number(string key, double fallback)
        {
            var match = Regex.Match(gcodeHeader, @"^; " + Regex.Escape(key) + @"\s*:\s*([0-9.]+)", RegexOptions.Multiline);
            return match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;
        }
        if (seconds <= 0)
        {
            var time = Regex.Match(gcodeHeader, @"total estimated time:\s*(?:(\d+)d\s*)?(?:(\d+)h\s*)?(?:(\d+)m\s*)?(?:(\d+)s)?");
            if (time.Success)
                seconds = TimeSpan.FromDays(Parse(time.Groups[1])).TotalSeconds + Parse(time.Groups[2]) * 3600 + Parse(time.Groups[3]) * 60 + Parse(time.Groups[4]);
        }
        return new SliceResult(seconds, Number("total filament length [mm]", 0), Number("filament_diameter", 1.75), layerHeight, infill, wallLoops, (int)Number("total layer number", 0));
    }

    private static double Parse(Group group) => group.Success ? double.Parse(group.Value, CultureInfo.InvariantCulture) : 0;

    private static string ReadHeader(string gcodePath)
    {
        using var reader = new StreamReader(gcodePath);
        var lines = new List<string>();
        for (var i = 0; i < 40 && reader.ReadLine() is { } line; i++) lines.Add(line);
        return string.Join('\n', lines);
    }
}
