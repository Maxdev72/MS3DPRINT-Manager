using System.Globalization;
using MS3DPRINT.Manager.Core.Naming;

namespace MS3DPRINT.Manager.Core.Projects;

public static class ProjectReferenceGenerator
{
    public static ProjectReference Create(string clientCode, int year, IEnumerable<string> existingNames, string projectName)
    {
        ArgumentNullException.ThrowIfNull(clientCode);
        ArgumentNullException.ThrowIfNull(existingNames);
        ArgumentNullException.ThrowIfNull(projectName);
        if (year is < 1000 or > 9999)
        {
            throw new ArgumentOutOfRangeException(nameof(year), "Year must have exactly four digits.");
        }

        var normalizedCode = NameNormalizer.Normalize(clientCode);
        if (normalizedCode.Length == 0)
        {
            throw new ArgumentException("Client code must contain a letter or digit.", nameof(clientCode));
        }

        var normalizedProjectName = NameNormalizer.Normalize(projectName);
        if (normalizedProjectName.Length == 0)
        {
            throw new ArgumentException("Project name must contain a letter or digit.", nameof(projectName));
        }

        var prefix = normalizedCode + "-" + year.ToString("D4", CultureInfo.InvariantCulture) + "-";
        var highestSequence = 0;

        foreach (var existingName in existingNames)
        {
            if (existingName is null)
            {
                continue;
            }

            if (ProjectReferenceFormat.TryParseFolderName(existingName, out var reference) &&
                existingName.Length > reference.Length && existingName[reference.Length] == '_' &&
                reference.StartsWith(prefix, StringComparison.Ordinal))
            {
                highestSequence = Math.Max(highestSequence, int.Parse(reference[^3..], CultureInfo.InvariantCulture));
            }
        }

        if (highestSequence == 999)
        {
            throw new ArgumentOutOfRangeException(nameof(existingNames), "No project sequence remains for this client and year.");
        }

        return new ProjectReference(normalizedCode, year, highestSequence + 1, normalizedProjectName);
    }
}
