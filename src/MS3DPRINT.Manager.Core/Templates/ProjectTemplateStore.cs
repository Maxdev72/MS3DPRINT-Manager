using System.Text.Json;
using System.Text.Json.Serialization;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Templates;

/// <summary>Keeps the project template chosen by the user in the workspace metadata.</summary>
public sealed class ProjectTemplateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    private readonly WorkspaceMetadataPaths _paths;

    public ProjectTemplateStore(WorkspaceMetadataPaths paths) => _paths = paths ?? throw new ArgumentNullException(nameof(paths));

    private string FilePath => Path.Combine(_paths.MetadataDirectory, "project-template.json");

    /// <summary>The saved template; the standard one when nothing is saved or the file cannot be used.</summary>
    public ProjectTemplate Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return ProjectTemplate.Default;
            var template = JsonSerializer.Deserialize<ProjectTemplate>(File.ReadAllText(FilePath), JsonOptions);
            return template is not null && template.Validate() is null ? template : ProjectTemplate.Default;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return ProjectTemplate.Default;
        }
    }

    public void Save(ProjectTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        if (template.Validate() is { } problem) throw new ArgumentException(problem, nameof(template));
        WorkspacePathSafety.EnsureNoLinks(_paths.MetadataDirectory);
        Directory.CreateDirectory(_paths.MetadataDirectory);
        var temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(template, JsonOptions));
        File.Move(temporary, FilePath, overwrite: true);
    }

    /// <summary>Goes back to the standard template.</summary>
    public void Reset()
    {
        WorkspacePathSafety.EnsureNoLinks(_paths.MetadataDirectory);
        if (File.Exists(FilePath)) File.Delete(FilePath);
    }
}
