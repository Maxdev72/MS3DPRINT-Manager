using System.Globalization;
using MS3DPRINT.Manager.Core.Projects;

namespace MS3DPRINT.Manager.Core.Tasks;

public sealed record QuickTask(string Title, DateOnly? DueDate, ProjectPriority Priority, string? List);

/// <summary>Reads « Commander PLA demain !haute #Achats »: date words, <c>!priorité</c> and <c>#liste</c> leave the title.</summary>
public static class TaskQuickAdd
{
    private static readonly string[] Days = ["dimanche", "lundi", "mardi", "mercredi", "jeudi", "vendredi", "samedi"];

    public static QuickTask Parse(string text, DateOnly today)
    {
        DateOnly? due = null; var priority = ProjectPriority.None; string? list = null;
        var kept = new List<string>();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var lower = word.ToLowerInvariant();
            if (lower.StartsWith('!') && Priority(lower[1..]) is { } p) { priority = p; continue; }
            if (word.Length > 1 && word[0] == '#') { list = word[1..].Replace('_', ' '); continue; }
            if (Date(lower, today) is { } d) { due = d; continue; }
            kept.Add(word);
        }
        var title = string.Join(' ', kept);
        // Only tokens: keep the text as typed rather than an empty task.
        return title.Length == 0 ? new(text.Trim(), null, ProjectPriority.None, null) : new(title, due, priority, list);
    }

    private static ProjectPriority? Priority(string word) => word switch
    {
        "haute" or "h" or "1" => ProjectPriority.High,
        "moyenne" or "m" or "2" => ProjectPriority.Medium,
        "basse" or "b" or "3" => ProjectPriority.Low,
        _ => null
    };

    private static DateOnly? Date(string word, DateOnly today)
    {
        switch (word)
        {
            case "aujourd'hui" or "aujourd’hui" or "auj": return today;
            case "demain": return today.AddDays(1);
            case "après-demain" or "apres-demain": return today.AddDays(2);
        }
        var day = Array.IndexOf(Days, word);
        if (day >= 0) return today.AddDays(((day - (int)today.DayOfWeek) + 7) % 7);
        if (DateOnly.TryParseExact(word, ["d/M/yyyy", "d/M/yy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var full)) return full;
        if (DateOnly.TryParseExact(word + "/" + today.Year, "d/M/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return date < today ? date.AddYears(1) : date;
        return null;
    }
}
