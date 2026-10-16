using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using EstimatorBench;
using MS3DPRINT.Estimator;
using MS3DPRINT.Manager.Core.Slicing;
using MS3DPRINT.Manager.Desktop.Viewer;

// Compares MS3DPRINT.Estimator with Bambu Studio on reference parts, several printers and two materials.
//   dotnet run --project tools/EstimatorBench -c Release -- [slice|compare] [folder holding real STL files]
// "slice" asks Bambu Studio for every missing combination and keeps the answers in reference-multi.json;
// "compare" (default) slices what is missing, then prints how far the estimator is from those answers and writes features.csv.
// Bambu Studio is only a measuring instrument here: nothing of its code or profiles is used by the estimator.

var toolDirectory = FindToolDirectory();
var cachePath = Path.Combine(toolDirectory, "reference-multi.json");
string[] machineNames = ["Bambu Lab A1 mini 0.4 nozzle", "Bambu Lab A1 0.4 nozzle", "Bambu Lab P1S 0.4 nozzle", "Bambu Lab X1 Carbon 0.4 nozzle", "Bambu Lab H2S 0.4 nozzle"];

// the quality profile is chosen by its layer height, the first name that exists for the machine
string[] fine = ["0.12mm Fine", "0.12mm High Quality"];
string[] standard = ["0.20mm Standard"];
string[] draft = ["0.28mm Extra Draft", "0.24mm Draft", "0.24mm Standard"];
var settings = new Setting[]
{
    new("PLA", "Bambu PLA Basic", standard, 15, 2, false),
    new("PLA", "Bambu PLA Basic", fine, 20, 3, false),
    new("PLA", "Bambu PLA Basic", draft, 10, 2, false),
    new("PLA", "Bambu PLA Basic", standard, 50, 4, false),
    new("PLA", "Bambu PLA Basic", standard, 15, 2, true),
    new("PETG", "Bambu PETG Basic", standard, 15, 2, false)
};

var mode = args.Length > 0 ? args[0] : "compare";
var realRoot = args.Length > 1 ? args[1] : @"C:\MS3DPRINT\Nextcloud\MS3DPRINT";
var work = Path.Combine(Path.GetTempPath(), "ms3d-bench-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(work);

var parts = new List<(string Name, string Path)>();
foreach (var (name, build) in ShapeFactory.All())
{
    var (positions, indices) = build();
    var path = Path.Combine(work, name + ".stl");
    ShapeFactory.WriteStl(path, positions, indices);
    parts.Add((name, path));
}
if (Directory.Exists(realRoot))
{
    var real = new[] { "01_CLIENTS", "02_MODELES_3D" }.Select(folder => Path.Combine(realRoot, folder)).Where(Directory.Exists)
        .SelectMany(folder => Directory.EnumerateFiles(folder, "*.stl", SearchOption.AllDirectories))
        .Where(file => new FileInfo(file).Length is > 20_000 and < 30_000_000)
        .OrderBy(file => file, StringComparer.OrdinalIgnoreCase).Take(8).ToList();
    for (var i = 0; i < real.Count; i++) parts.Add(($"reel-{i + 1}", real[i]));
}

var cache = File.Exists(cachePath) ? JsonSerializer.Deserialize<Dictionary<string, Reference>>(File.ReadAllText(cachePath)) ?? [] : [];
var install = SlicerLocator.Detect() ?? throw new InvalidOperationException("Aucun découpeur trouvé.");
var profiles = new SlicerProfiles(install);
var allMachines = profiles.Machines();
Console.WriteLine($"{install.Name} · {allMachines.Count} machines connues");

// every run: machine × setting, with the profile names the machine really has
var runs = new List<Run>();
foreach (var machineName in machineNames)
{
    var machine = allMachines.FirstOrDefault(m => m.Name == machineName);
    if (machine is null) { Console.WriteLine($"  machine absente : {machineName}"); continue; }
    var processes = profiles.Processes(machine);
    foreach (var setting in settings)
    {
        var process = setting.ProcessPrefixes.Select(prefix => processes.FirstOrDefault(p => p.StartsWith(prefix, StringComparison.Ordinal))).FirstOrDefault(p => p is not null);
        var filament = FindFilament(machine, setting.FilamentPrefix);
        if (process is null || filament is null) { Console.WriteLine($"  ignoré : {machineName} · {setting.Material} · {setting.ProcessPrefixes[0]}"); continue; }
        runs.Add(new Run(machine, setting, process, filament));
    }
}

var missing = (from run in runs from part in parts where !cache.ContainsKey(Key(run, part.Name)) select (run, part)).ToList();
Console.WriteLine($"{runs.Count} combinaisons × {parts.Count} pièces ; {missing.Count} découpes à faire.");
using var gate = new SemaphoreSlim(3);
var lockObject = new object();
var done = 0;
await Task.WhenAll(missing.Select(async item =>
{
    await gate.WaitAsync();
    try
    {
        var reference = await SliceAsync(item.part.Path, item.run);
        lock (lockObject) { cache[Key(item.run, item.part.Name)] = reference; File.WriteAllText(cachePath, JsonSerializer.Serialize(cache, new JsonSerializerOptions { WriteIndented = true })); }
        Console.WriteLine($"  [{Interlocked.Increment(ref done)}/{missing.Count}] {item.run.Machine.Name} · {item.run.Process} · {item.part.Name} → {reference.Seconds / 60:0.0} min");
    }
    catch (Exception exception) { Console.WriteLine($"  ÉCHEC {item.run.Machine.Name} · {item.run.Process} · {item.part.Name} : {exception.Message}"); }
    finally { gate.Release(); }
}));

if (mode == "slice") { Cleanup(); return; }

var features = new List<string> { "machine;material;process;part;layer;infill;walls;support;layers;wall;skin;infillv;supportv;contour;islands;turn;ovh;est_vol;ref_vol;est_s;ref_s;maxflow;" + string.Join(";", Reference.ConfigKeys) };
var rows = new List<Row>();
foreach (var part in parts)
{
    var model = MeshLoader.Load(part.Path);
    var mesh = new PrintMesh(model.Positions, model.Indices);
    foreach (var run in runs)
    {
        if (!cache.TryGetValue(Key(run, part.Name), out var reference)) continue;
        var estimateSettings = new EstimateSettings
        {
            LayerHeightMm = reference.LayerHeight, InfillPercent = run.Setting.Infill, WallCount = run.Setting.Walls,
            Supports = run.Setting.Support ? SupportMode.Auto : SupportMode.None,
            MaterialMaxFlowMm3S = reference.MaxFlow > 0 ? reference.MaxFlow : 20
        };
        // BENCH_CLASSES=1 gives every machine its speed class (fast or slow) instead of the average, to see what the setting is worth
        var speed = Environment.GetEnvironmentVariable("BENCH_CLASSES") == "1" ? (run.Machine.Name.Contains("A1") ? 0.6 : 1.35) : 1.0;
        var estimate = PrintEstimator.Estimate(mesh, estimateSettings, new MachineProfile { HeadSpeedFactor = speed });
        var raw = PrintEstimator.Estimate(mesh, estimateSettings, new MachineProfile { WallFlowFactor = 1, SkinFlowFactor = 1, InfillFlowFactor = 1 });
        string F(double value) => value.ToString("F2", CultureInfo.InvariantCulture);
        features.Add(string.Join(";", new[]
        {
            run.Machine.Name, run.Setting.Material, run.Process, part.Name, F(reference.LayerHeight), run.Setting.Infill.ToString(), run.Setting.Walls.ToString(), run.Setting.Support ? "1" : "0",
            raw.LayerCount.ToString(), F(raw.WallVolumeMm3), F(raw.SkinVolumeMm3), F(raw.InfillVolumeMm3), F(raw.SupportVolumeMm3), F(raw.ContourLengthMm), raw.IslandCount.ToString(),
            F(raw.TurningRadians), F(raw.OverhangAreaMm2), F(estimate.PrintedVolumeMm3), F(reference.FilamentMm3), F(estimate.TimeSeconds), F(reference.Seconds), F(reference.MaxFlow)
        }.Concat(Reference.ConfigKeys.Select(key => reference.Config.TryGetValue(key, out var value) ? value : ""))));
        rows.Add(new Row(run.Machine.Name, run.Setting.Material, run.Process, part.Name, estimate.PrintedVolumeMm3 / reference.FilamentMm3, estimate.TimeSeconds / reference.Seconds));
    }
}
File.WriteAllLines(Path.Combine(toolDirectory, "features.csv"), features);

Console.WriteLine($"\n{"machine",-34} {"n",4}  {"volume méd.",11} {"|écart| méd.",12}   {"temps méd.",10} {"|écart| méd.",12}");
foreach (var group in rows.GroupBy(row => row.Machine))
    Console.WriteLine($"{group.Key,-34} {group.Count(),4}  {Percent(Median(group.Select(r => r.VolumeRatio))),11} {AbsMedian(group.Select(r => r.VolumeRatio)),12}   {Percent(Median(group.Select(r => r.TimeRatio))),10} {AbsMedian(group.Select(r => r.TimeRatio)),12}");
foreach (var group in rows.GroupBy(row => row.Material))
    Console.WriteLine($"{"matière " + group.Key,-34} {group.Count(),4}  {Percent(Median(group.Select(r => r.VolumeRatio))),11} {AbsMedian(group.Select(r => r.VolumeRatio)),12}   {Percent(Median(group.Select(r => r.TimeRatio))),10} {AbsMedian(group.Select(r => r.TimeRatio)),12}");
Console.WriteLine($"{"TOUT",-34} {rows.Count,4}  {Percent(Median(rows.Select(r => r.VolumeRatio))),11} {AbsMedian(rows.Select(r => r.VolumeRatio)),12}   {Percent(Median(rows.Select(r => r.TimeRatio))),10} {AbsMedian(rows.Select(r => r.TimeRatio)),12}");
Cleanup();

string? FindFilament(SlicerMachine machine, string prefix)
{
    var directory = Path.Combine(install.ProfilesDirectory, "filament");
    foreach (var file in Directory.EnumerateFiles(directory, prefix + "*.json").OrderBy(file => file, StringComparer.Ordinal))
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            var root = document.RootElement;
            if (root.TryGetProperty("instantiation", out var instantiation) && instantiation.GetString() == "true"
                && root.TryGetProperty("compatible_printers", out var printers) && printers.ValueKind == JsonValueKind.Array
                && printers.EnumerateArray().Any(item => item.GetString() == machine.Name))
                return root.GetProperty("name").GetString();
        }
        catch (Exception exception) when (exception is IOException or JsonException) { }
    }
    return null;
}

async Task<Reference> SliceAsync(string modelPath, Run run)
{
    var output = Path.Combine(work, "out-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(output);
    var start = new ProcessStartInfo(install.ExecutablePath) { UseShellExecute = false, CreateNoWindow = true };
    foreach (var argument in new[]
    {
        "--slice", "0", "--outputdir", output,
        "--load-settings", run.Machine.FilePath + ";" + profiles.ProcessFile(run.Process), "--load-filaments", profiles.FilamentFile(run.Filament),
        "--sparse-infill-density", run.Setting.Infill.ToString(CultureInfo.InvariantCulture) + "%",
        "--wall-loops", run.Setting.Walls.ToString(CultureInfo.InvariantCulture),
        "--enable-support=" + (run.Setting.Support ? "1" : "0"), modelPath
    }) start.ArgumentList.Add(argument);
    using var slicer = Process.Start(start) ?? throw new InvalidOperationException("Le découpeur n'a pas démarré.");
    using var limit = new CancellationTokenSource(TimeSpan.FromMinutes(10));
    await slicer.WaitForExitAsync(limit.Token);
    var result = SliceRunner.Read(output);
    var radius = result.FilamentDiameterMm / 2;
    var config = ReadConfig(Directory.EnumerateFiles(output, "*.gcode").FirstOrDefault());
    double flow = config.TryGetValue("filament_max_volumetric_speed", out var flowText) && double.TryParse(flowText, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 0;
    return new Reference(result.TimeSeconds, result.FilamentLengthMm * Math.PI * radius * radius, result.Layers, result.LayerHeight, flow, config);
}

// the resolved settings the slicer wrote at the top of its G-code, for the keys we want to compare machines with
static Dictionary<string, string> ReadConfig(string? gcodePath)
{
    var config = new Dictionary<string, string>();
    if (gcodePath is null) return config;
    using var reader = new StreamReader(gcodePath);
    var inside = false;
    while (reader.ReadLine() is { } line)
    {
        if (line.StartsWith("; CONFIG_BLOCK_START", StringComparison.Ordinal)) { inside = true; continue; }
        if (line.StartsWith("; CONFIG_BLOCK_END", StringComparison.Ordinal)) break;
        if (!inside || !line.StartsWith("; ", StringComparison.Ordinal)) continue;
        var separator = line.IndexOf(" = ", StringComparison.Ordinal);
        if (separator < 0) continue;
        var key = line[2..separator];
        if (Reference.ConfigKeys.Contains(key)) config[key] = line[(separator + 3)..].Split(',')[0].Trim();
    }
    return config;
}

void Cleanup() { try { Directory.Delete(work, recursive: true); } catch (IOException) { } }
static string Key(Run run, string part) => string.Join("|", run.Machine.Name, run.Filament, run.Process, run.Setting.Infill, run.Setting.Walls, run.Setting.Support ? "s" : "n", part);
static string Percent(double ratio) => (ratio - 1).ToString("+0.0%;-0.0%", CultureInfo.InvariantCulture);
static double Median(IEnumerable<double> values) { var sorted = values.Order().ToList(); return sorted.Count == 0 ? double.NaN : sorted[sorted.Count / 2]; }
static string AbsMedian(IEnumerable<double> ratios) => Median(ratios.Select(r => Math.Abs(r - 1))).ToString("0.0%", CultureInfo.InvariantCulture);
static string FindToolDirectory()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EstimatorBench.csproj"))) directory = directory.Parent;
    return directory?.FullName ?? Directory.GetCurrentDirectory();
}

internal sealed record Setting(string Material, string FilamentPrefix, string[] ProcessPrefixes, int Infill, int Walls, bool Support);
internal sealed record Run(SlicerMachine Machine, Setting Setting, string Process, string Filament);
internal sealed record Reference(double Seconds, double FilamentMm3, int Layers, double LayerHeight, double MaxFlow, Dictionary<string, string> Config)
{
    /// <summary>Slicer settings kept next to every measure, to see what separates one machine from another.</summary>
    public static readonly string[] ConfigKeys =
    [
        "outer_wall_speed", "inner_wall_speed", "sparse_infill_speed", "internal_solid_infill_speed", "top_surface_speed", "bridge_speed", "travel_speed",
        "default_acceleration", "outer_wall_acceleration", "inner_wall_acceleration", "sparse_infill_acceleration", "top_surface_acceleration", "travel_acceleration",
        "initial_layer_acceleration", "machine_max_acceleration_extruding", "filament_max_volumetric_speed", "slow_down_layer_time", "slow_down_min_speed", "layer_height"
    ];
}
internal sealed record Row(string Machine, string Material, string Process, string Part, double VolumeRatio, double TimeRatio);
