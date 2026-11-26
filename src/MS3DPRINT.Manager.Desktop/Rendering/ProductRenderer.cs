using System.ComponentModel;
using System.Diagnostics;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MS3DPRINT.Manager.Desktop.Viewer;

namespace MS3DPRINT.Manager.Desktop.Rendering;

/// <summary>What the render script needs besides the model: names match the keys read by product_render.py.</summary>
public sealed record RenderSettings(string Color, string Finish, double LayerHeight, string Scene, string Background,
    double Rotation, double Elevation, int Width, int Height, int Samples, string Environment = "", double EnvironmentRotation = 0, RenderCamera? Camera = null, string Engine = "cycles",
    bool Shadow = true, double ShadowStrength = 0.3, double ShadowSoftness = 0.5, double LightAngle = 0, double LightHeight = 39, bool GlowDark = false, string? BandColor = null, double BandHeight = 0, string Device = "auto", string? Tiff = null, IReadOnlyList<RenderZone>? Zones = null, double? GroundZ = null, double NoiseThreshold = 0);

/// <summary>A painted zone sent to Blender: its own STL file, colour and finish.</summary>
public sealed record RenderZone(string Model, string Color, string Finish);

/// <summary>The camera of the 3D view in model millimetres: Blender frames the part exactly as on screen.</summary>
public sealed record RenderCamera(float[] Location, float[] Target, double FieldOfView, double ShiftX, double ShiftY)
{
    /// <summary>The 3D view's vertical field of view (see <see cref="ViewMath"/>).</summary>
    public const double ViewFieldOfView = 40;

    /// <summary>
    /// The view's orbit around the vertical axis becomes a turn of the part (returned in degrees), so the studio lights and
    /// backdrop stay behind the camera; the height, zoom and pan become the Blender camera.
    /// </summary>
    public static (RenderCamera Camera, double RotationDegrees) From(MeshModel mesh, ViewCamera view, int width, int height)
    {
        var distance = mesh.Radius * 2.9 / Math.Max(ViewCamera.MinZoom, view.Zoom);
        var toModel = Matrix4x4.Transpose(Matrix4x4.CreateRotationX((float)view.Pitch));
        var location = mesh.Center + Vector3.Transform(new Vector3(0, (float)-distance, 0), toModel);
        // The view pans in pixels of a 760 px high view; Blender shifts in parts of the larger image side.
        var heightShare = (double)height / Math.Max(width, height);
        return (new RenderCamera([location.X, location.Y, location.Z], [mesh.Center.X, mesh.Center.Y, mesh.Center.Z], ViewFieldOfView,
            -view.PanX / 760 * heightShare, view.PanY / 760 * heightShare), view.Yaw * 180 / Math.PI);
    }
}

/// <summary>An environment image (HDRI) that lights the part and shows behind it.</summary>
public sealed record RenderEnvironment(string Label, string Path)
{
    public override string ToString() => Label;
}

/// <summary>Finds Blender on this computer: the newest version of the official installer, else the Steam one.</summary>
public static partial class BlenderLocator
{
    public static string? Detect()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        return Detect(Path.Combine(programFiles, "Blender Foundation"), Path.Combine(programFilesX86, "Steam", "steamapps", "common", "Blender", "blender.exe"));
    }

    public static string? Detect(string foundationFolder, string steamExecutable)
    {
        if (Directory.Exists(foundationFolder))
        {
            var newest = Directory.EnumerateDirectories(foundationFolder)
                .Select(folder => (Exe: Path.Combine(folder, "blender.exe"), Version: VersionOf(Path.GetFileName(folder))))
                .Where(candidate => File.Exists(candidate.Exe))
                .OrderByDescending(candidate => candidate.Version)
                .FirstOrDefault();
            if (newest.Exe is not null) return newest.Exe;
        }
        return File.Exists(steamExecutable) ? steamExecutable : null;
    }

    private static readonly Dictionary<string, string> EnvironmentNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["city"] = "Ville", ["courtyard"] = "Cour ensoleillée", ["forest"] = "Forêt", ["interior"] = "Intérieur", ["night"] = "Nuit",
        ["studio"] = "Studio photo", ["sunrise"] = "Lever du soleil", ["sunset"] = "Rue au coucher du soleil"
    };

    /// <summary>The environments shipped with this Blender (datafiles/studiolights/world next to blender.exe).</summary>
    public static IReadOnlyList<RenderEnvironment> Environments(string blenderExecutable)
    {
        var folder = Path.GetDirectoryName(blenderExecutable);
        if (folder is null || !Directory.Exists(folder)) return [];
        return Directory.EnumerateDirectories(folder)
            .Select(version => Path.Combine(version, "datafiles", "studiolights", "world"))
            .Where(Directory.Exists)
            .SelectMany(world => Directory.EnumerateFiles(world).Where(IsEnvironmentFile))
            .Select(file => new RenderEnvironment(EnvironmentNames.GetValueOrDefault(Path.GetFileNameWithoutExtension(file), Path.GetFileNameWithoutExtension(file)), file))
            .DistinctBy(environment => environment.Label)
            .OrderBy(environment => environment.Label, StringComparer.CurrentCulture)
            .ToList();
    }

    public static bool IsEnvironmentFile(string path)
        => Path.GetExtension(path).Equals(".exr", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".hdr", StringComparison.OrdinalIgnoreCase);

    private static Version VersionOf(string folderName)
        => VersionNumber().Match(folderName) is { Success: true } match && Version.TryParse(match.Value, out var version) ? version : new Version(0, 0);

    [GeneratedRegex(@"\d+(\.\d+)+")]
    private static partial Regex VersionNumber();
}

/// <summary>Runs Blender in the background on the bundled script; the scene is built by the script, so nothing needs configuring.</summary>
public static class ProductRenderer
{
    /// <summary>Blender reads STL only: every supported model (STL, OBJ, 3MF) goes through the viewer loader and is written as STL in millimetres.</summary>
    // shortcut: the model is taken as millimetres (the viewer's per-file unit choice is not applied); pass ModelUnits here if inch models show up.
    public static MeshModel PrepareModel(MeshModel loaded, Quaternion turn, string stlPath)
    {
        var mesh = Turned(loaded, turn);
        StlWriter.Write(stlPath, mesh);
        return mesh;
    }

    /// <summary>The part turned as the user stood it (a file drawn with Y up arrives lying down).</summary>
    public static MeshModel Turned(MeshModel mesh, Quaternion turn)
        => turn.IsIdentity ? mesh : new MeshModel(mesh.Positions.Select(p => Vector3.Transform(p, turn)).ToArray(), mesh.Indices);

    public static string SettingsJson(string stlPath, string outputPng, RenderSettings settings)
    {
        var node = JsonSerializer.SerializeToNode(settings, JsonSerializerOptions.Web)!.AsObject();
        node["model"] = stlPath;
        node["output"] = outputPng;
        return node.ToJsonString();
    }

    public static async Task RenderAsync(string blender, string stlPath, RenderSettings settings, string outputPng, CancellationToken cancellationToken)
    {
        var folder = Path.GetDirectoryName(outputPng)!;
        var settingsPath = Path.Combine(folder, Path.GetFileNameWithoutExtension(outputPng) + ".json");
        await File.WriteAllTextAsync(settingsPath, SettingsJson(stlPath, outputPng, settings), cancellationToken);
        var start = new ProcessStartInfo(blender)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            ArgumentList = { "-b", "--factory-startup", "--python-exit-code", "1", "-P", ScriptPath(), "--", settingsPath }
        };
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Blender n’a pas pu être lancé.");
        var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var errors = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try { await process.WaitForExitAsync(cancellationToken); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); process.WaitForExit(5000); }
            catch (Exception exception) when (exception is InvalidOperationException or Win32Exception) { }
            throw;
        }
        finally { try { File.Delete(settingsPath); } catch (IOException) { } }
        if (process.ExitCode == 0 && File.Exists(outputPng)) return;
        var log = (await output) + "\n" + (await errors);
        var reason = log.Split('\n').Select(line => line.Trim()).LastOrDefault(line => line.StartsWith("Error", StringComparison.OrdinalIgnoreCase) || line.Contains("Error:", StringComparison.Ordinal))
            ?? log.Split('\n').Select(line => line.Trim()).LastOrDefault(line => line.Length > 0) ?? "aucune sortie";
        throw new InvalidOperationException("Blender a échoué : " + reason);
    }

    private static readonly Lazy<string> Script = new(() =>
    {
        var folder = WorkFolder();
        var path = Path.Combine(folder, "product_render.py");
        using var source = typeof(ProductRenderer).Assembly.GetManifestResourceStream("MS3DPRINT.Manager.Desktop.Rendering.product_render.py")
            ?? throw new InvalidOperationException("Script de rendu absent de l’application.");
        using var target = File.Create(path);
        source.CopyTo(target);
        return path;
    });

    internal static string ScriptPath() => Script.Value;

    /// <summary>One temporary folder per running instance, so two windows never write the same files.</summary>
    public static string WorkFolder() => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "ms3dprint-render", Environment.ProcessId.ToString())).FullName;
}

/// <summary>
/// One Blender kept open for the render page: it starts once (about 2 s), keeps the imported part, and renders one job at a
/// time; after a few idle minutes it closes to give its memory back. Cancelling a render stops Blender, the next one restarts it.
/// </summary>
public sealed class BlenderSession(string blender) : IDisposable
{
    private static readonly TimeSpan IdleLimit = TimeSpan.FromMinutes(3);
    /// <summary>Blender prints its progress all along a render: this long without a line, it is stuck and is stopped.</summary>
    private static readonly TimeSpan SilenceLimit = TimeSpan.FromMinutes(5);
    private long _lastOutput;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _sync = new();
    private readonly Queue<string> _tail = new();
    private Process? _process;
    private TaskCompletionSource<string?>? _job;
    private Timer? _idle;

    /// <summary>The graphics card Blender renders with, e.g. "NVIDIA GeForce RTX 5070 (OPTIX)"; empty when none is usable, null before Blender started.</summary>
    public string? DeviceName { get; private set; }

    private IProgress<double>? _progress;

    public async Task RenderAsync(string stlPath, RenderSettings settings, string outputPng, CancellationToken cancellationToken, IProgress<double>? progress = null)
    {
        var settingsPath = Path.ChangeExtension(outputPng, ".json");
        try
        {
            await File.WriteAllTextAsync(settingsPath, ProductRenderer.SettingsJson(stlPath, outputPng, settings), cancellationToken);
            _progress = progress;
            await RunAsync(settingsPath, cancellationToken);
            if (!File.Exists(outputPng)) throw new InvalidOperationException("Blender n’a pas produit d’image.");
        }
        finally
        {
            _progress = null;
            try { File.Delete(settingsPath); } catch (IOException) { }
        }
    }

    /// <summary>Starts Blender and renders a tiny scene with this engine ("eevee" or "cycles"), so the first real image is quick.</summary>
    public async Task WarmUpAsync(string engine)
    {
        try { await RunAsync("WARMUP " + engine, CancellationToken.None); }
        catch (InvalidOperationException) { } // the real render will report the problem
    }

    /// <summary>One command to Blender (a settings file to render, or a warm-up), one at a time; cancelling stops Blender.</summary>
    private async Task RunAsync(string command, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _idle?.Dispose();
            var job = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            Process process;
            lock (_sync) { _job = job; process = Start(); }
            await process.StandardInput.WriteLineAsync(command);
            await process.StandardInput.FlushAsync(cancellationToken);
            string? error;
            Interlocked.Exchange(ref _lastOutput, Environment.TickCount64);
            using (cancellationToken.Register(() => { Stop(); job.TrySetCanceled(cancellationToken); }))
            {
                // A hung Blender would block the page until a manual cancel: silence past the limit stops it.
                while (!job.Task.IsCompleted)
                {
                    await Task.WhenAny(job.Task, Task.Delay(TimeSpan.FromSeconds(30)));
                    if (job.Task.IsCompleted || Environment.TickCount64 - Interlocked.Read(ref _lastOutput) < SilenceLimit.TotalMilliseconds) continue;
                    Stop();
                    job.TrySetResult($"Blender ne répond plus depuis {SilenceLimit.TotalMinutes:0} minutes : le rendu est arrêté. Relancez-le.");
                }
                error = await job.Task;
            }
            if (error is not null) throw new InvalidOperationException("Blender a échoué : " + error);
        }
        catch (IOException exception) when (!cancellationToken.IsCancellationRequested)
        {
            Stop();
            throw new InvalidOperationException("Blender s’est arrêté pendant le rendu : " + exception.Message, exception);
        }
        finally
        {
            _idle = new Timer(_ => { if (_gate.Wait(0)) { try { Stop(); } finally { _gate.Release(); } } }, null, IdleLimit, Timeout.InfiniteTimeSpan);
            _gate.Release();
        }
    }

    private Process Start()
    {
        if (_process is { HasExited: false } running) return running;
        var start = new ProcessStartInfo(blender)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8,
            ArgumentList = { "-b", "--factory-startup", "-P", ProductRenderer.ScriptPath(), "--", "--server" }
        };
        start.Environment["PYTHONUTF8"] = "1";
        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => Received(e.Data);
        process.ErrorDataReceived += (_, e) => Remember(e.Data);
        // Only a Blender that stopped by itself fails the job; one stopped on purpose is no longer the current one.
        process.Exited += (_, _) => { if (ReferenceEquals(_process, process)) Finish("Blender s’est arrêté. " + string.Join(" ", _tail)); };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return _process = process;
    }

    private void Received(string? line)
    {
        if (line is null) return;
        Interlocked.Exchange(ref _lastOutput, Environment.TickCount64);
        if (line.StartsWith("MS3D-PROGRESS ", StringComparison.Ordinal))
        {
            var parts = line["MS3D-PROGRESS ".Length..].Split('/');
            if (parts.Length == 2 && double.TryParse(parts[0], out var done) && double.TryParse(parts[1], out var total) && total > 0) _progress?.Report(done / total);
        }
        else if (line.StartsWith("MS3D-DEVICE ", StringComparison.Ordinal)) DeviceName = line["MS3D-DEVICE ".Length..].Trim();
        else if (line.StartsWith("MS3D-DONE", StringComparison.Ordinal)) Finish(null);
        else if (line.StartsWith("MS3D-ERROR ", StringComparison.Ordinal)) Finish(line["MS3D-ERROR ".Length..]);
        else Remember(line);
    }

    private void Remember(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        Interlocked.Exchange(ref _lastOutput, Environment.TickCount64);
        lock (_tail) { _tail.Enqueue(line.Trim()); while (_tail.Count > 3) _tail.Dequeue(); }
    }

    private void Finish(string? error)
    {
        TaskCompletionSource<string?>? job;
        lock (_sync) { job = _job; _job = null; }
        job?.TrySetResult(error);
    }

    private void Stop()
    {
        Process? process;
        lock (_sync) { process = _process; _process = null; }
        if (process is null) return;
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception) { }
        process.Dispose();
    }

    public void Dispose()
    {
        _idle?.Dispose();
        Stop();
    }
}
