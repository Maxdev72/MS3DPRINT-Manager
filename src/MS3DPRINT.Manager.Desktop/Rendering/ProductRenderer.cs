using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MS3DPRINT.Manager.Desktop.Viewer;

namespace MS3DPRINT.Manager.Desktop.Rendering;

/// <summary>What the render script needs besides the model: names match the keys read by product_render.py.</summary>
public sealed record RenderSettings(string Color, string Finish, double LayerHeight, string Scene, string Background,
    double Rotation, double Elevation, int Width, int Height, int Samples, string Environment = "", double EnvironmentRotation = 0);

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
    public static void PrepareModel(string modelPath, string stlPath) => StlWriter.Write(stlPath, MeshLoader.Load(modelPath));

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

    private static string ScriptPath() => Script.Value;

    /// <summary>One temporary folder per running instance, so two windows never write the same files.</summary>
    public static string WorkFolder() => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "ms3dprint-render", Environment.ProcessId.ToString())).FullName;
}
