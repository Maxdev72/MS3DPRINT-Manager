using System.Globalization;

namespace MS3DPRINT.Manager.Core.Projects;

public sealed record ProjectReference(string ClientCode, int Year, int Sequence, string NormalizedProjectName)
{
    public string FolderName => $"{ClientCode}-{Year.ToString("D4", CultureInfo.InvariantCulture)}-{Sequence.ToString("D3", CultureInfo.InvariantCulture)}_{NormalizedProjectName}";
}
