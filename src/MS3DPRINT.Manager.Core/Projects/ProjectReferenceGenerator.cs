using System.Globalization;
using System.Text.RegularExpressions;
using MS3DPRINT.Manager.Core.Naming;

namespace MS3DPRINT.Manager.Core.Projects;

public static class ProjectReferenceGenerator
{
    public static ProjectReference Create(string clientCode, int year, IEnumerable<string> existingNames, string projectName)
    {
        ArgumentNullException.ThrowIfNull(clientCode);
        ArgumentNullException.ThrowIfNull(existingNames);
        ArgumentNullException.ThrowIfNull(projectName);

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

        var prefix = $"^{Regex.Escape(normalizedCode)}-{year.ToString("D4", CultureInfo.InvariantCulture)}-([0-9]{{3}})_";
        var pattern = new Regex(prefix, RegexOptions.CultureInvariant);
        var highestSequence = 0;

        foreach (var existingName in existingNames)
        {
            if (existingName is null)
            {
                continue;
            }

            var match = pattern.Match(existingName);
            if (match.Success)
            {
                highestSequence = Math.Max(highestSequence, int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
            }
        }

        if (highestSequence == 999)
        {
            throw new ArgumentOutOfRangeException(nameof(existingNames), "No project sequence remains for this client and year.");
        }

        return new ProjectReference(normalizedCode, year, highestSequence + 1, normalizedProjectName);
    }
}
