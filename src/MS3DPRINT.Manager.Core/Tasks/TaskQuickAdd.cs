using System.Globalization;
using MS3DPRINT.Manager.Core.Projects;

namespace MS3DPRINT.Manager.Core.Tasks;

/// <summary>What the quick add understood. <c>Project</c> and <c>Client</c> are the typed names, matched by the page.</summary>
public sealed record QuickTask(string Title, DateOnly? DueDate, ProjectPriority Priority, string? List, string? Project = null, string? Client = null);

/// <summary>Reads « Commander PLA demain !haute #Achats @vélo +Dupont »: dates, priority, list, project and client leave the title.</summary>
public static class TaskQuickAdd
{
    private static readonly string[] Days = ["dimanche", "lundi", "mardi", "mercredi", "jeudi", "vendredi", "samedi"];
    private static readonly string[][] Months =
    [
        ["janvier", "janv", "jan"], ["février", "fevrier", "févr", "fevr", "fév", "fev"], ["mars", "mar"], ["avril", "avr"], ["mai"], ["juin"],
        ["juillet", "juil"], ["août", "aout"], ["septembre", "sept", "sep"], ["octobre", "oct"], ["novembre", "nov"], ["décembre", "decembre", "déc", "dec"]
    ];

    public static QuickTask Parse(string text, DateOnly today)
    {
        DateOnly? due = null; var priority = ProjectPriority.None; string? list = null, project = null, client = null;
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var kept = new List<string>();
        for (var i = 0; i < words.Length; i++)
        {
            var word = words[i];
            var lower = word.ToLowerInvariant();
            if (lower.StartsWith('!') && Priority(lower[1..]) is { } p) { priority = p; continue; }
            if (word.Length > 1 && word[0] is '#' or '@' or '+')
            {
                var name = word[1..].Replace('_', ' ');
                if (word[0] == '#') list = name; else if (word[0] == '@') project = name; else client = name;
                continue;
            }
            if (Date(words, i, today) is var (date, used) && used > 0) { due = date; i += used - 1; continue; }
            kept.Add(word);
        }
        var title = string.Join(' ', kept);
        // Only tokens: keep the text as typed rather than an empty task.
        return title.Length == 0 ? new(text.Trim(), null, ProjectPriority.None, null) : new(title, due, priority, list, project, client);
    }

    private static ProjectPriority? Priority(string word) => word switch
    {
        "haute" or "h" or "1" => ProjectPriority.High,
        "moyenne" or "m" or "2" => ProjectPriority.Medium,
        "basse" or "b" or "3" => ProjectPriority.Low,
        _ => null
    };

    /// <summary>A date starting at word <paramref name="i"/>, and how many words it used (0: none).</summary>
    private static (DateOnly Date, int Used) Date(string[] words, int i, DateOnly today)
    {
        string At(int k) => i + k < words.Length ? words[i + k].ToLowerInvariant().TrimEnd(',', '.') : "";
        var word = At(0);
        switch (word)
        {
            case "aujourd'hui" or "aujourd’hui" or "auj": return (today, 1);
            case "demain": return (today.AddDays(1), 1);
            case "après-demain" or "apres-demain": return (today.AddDays(2), 1);
        }
        if (word == "dans" && int.TryParse(At(1), out var count) && count is > 0 and < 1000)
        {
            switch (At(2))
            {
                case "jour" or "jours": return (today.AddDays(count), 3);
                case "semaine" or "semaines": return (today.AddDays(7 * count), 3);
                case "mois": return (today.AddMonths(count), 3);
            }
        }
        if (word == "semaine" && At(1) == "prochaine") return (NextMonday(today), 2);
        if (word == "fin" && At(1) == "du" && At(2) == "mois") return (new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month)), 3);
        if (word == "fin" && At(1) == "de" && At(2) == "semaine") return (today.AddDays(((int)DayOfWeek.Friday - (int)today.DayOfWeek + 7) % 7), 3);
        var day = Array.IndexOf(Days, word);
        if (day >= 0)
        {
            var ahead = (day - (int)today.DayOfWeek + 7) % 7;
            // « lundi prochain » never means today.
            return At(1) == "prochain" ? (today.AddDays(ahead == 0 ? 7 : ahead), 2) : (today.AddDays(ahead), 1);
        }
        if (int.TryParse(word, out var dayOfMonth) && dayOfMonth is >= 1 and <= 31 && Month(At(1)) is { } month
            && dayOfMonth <= DateTime.DaysInMonth(today.Year, month))
        {
            var date = new DateOnly(today.Year, month, dayOfMonth);
            return (date < today ? date.AddYears(1) : date, 2);
        }
        if (DateOnly.TryParseExact(word, ["d/M/yyyy", "d/M/yy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var full)) return (full, 1);
        if (DateOnly.TryParseExact(word + "/" + today.Year, "d/M/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var shortDate))
            return (shortDate < today ? shortDate.AddYears(1) : shortDate, 1);
        return (default, 0);
    }

    private static int? Month(string word)
    {
        for (var m = 0; m < Months.Length; m++) if (Months[m].Contains(word)) return m + 1;
        return null;
    }

    private static DateOnly NextMonday(DateOnly today)
    {
        var ahead = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        return today.AddDays(ahead == 0 ? 7 : ahead);
    }
}
