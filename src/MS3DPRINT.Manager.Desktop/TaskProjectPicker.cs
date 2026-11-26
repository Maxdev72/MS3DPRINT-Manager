using System.Globalization;
using System.Text;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Tasks;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>
/// Which projects the task project picker offers. Nothing typed: the 5 recent ones (last task activity, then nearest due date).
/// Something typed: up to 8 matches on reference, name or client, accents ignored, references that start with it first.
/// </summary>
public static class TaskProjectPicker
{
    public const int Recent = 5, Matches = 8;

    public static IReadOnlyList<ProjectSummary> Rank(IEnumerable<ProjectSummary> projects, IEnumerable<WorkTask> tasks, string? query, bool withOpenTasks = false, Guid? keep = null)
    {
        var live = tasks.Where(t => !t.IsDeleted && t.ProjectId is not null).ToArray();
        var open = live.Where(t => !t.IsCompleted).Select(t => t.ProjectId!.Value).ToHashSet();
        var pool = projects.Where(p => p.Profile is { } profile
            && (profile.Id == keep || !p.IsArchived && profile.Status != ProjectStatus.Completed && (!withOpenTasks || open.Contains(profile.Id)))).ToArray();
        if (string.IsNullOrWhiteSpace(query))
        {
            var activity = live.GroupBy(t => t.ProjectId!.Value).ToDictionary(g => g.Key, g => g.Max(t => t.UpdatedAt));
            return pool.OrderByDescending(p => activity.TryGetValue(p.Profile!.Id, out var at) ? at : DateTimeOffset.MinValue)
                .ThenBy(p => p.Profile!.DueDate is null).ThenBy(p => p.Profile!.DueDate).Take(Recent).ToArray();
        }
        var needle = Fold(query.Trim());
        return pool.Where(p => Fold($"{p.Reference} {p.Title()} {p.ClientTitle()}").Contains(needle))
            .OrderByDescending(p => Fold(p.Reference).StartsWith(needle, StringComparison.Ordinal))
            .ThenByDescending(p => Fold(p.Title()).StartsWith(needle, StringComparison.Ordinal))
            .ThenBy(p => p.Title(), StringComparer.CurrentCultureIgnoreCase).Take(Matches).ToArray();
    }

    /// <summary>Lower case without accents: « Pièce » and « piece » match.</summary>
    public static string Fold(string value) => new string(value.Normalize(NormalizationForm.FormD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).ToLowerInvariant();
}
