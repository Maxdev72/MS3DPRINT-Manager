using MS3DPRINT.Manager.Core.Projects;

namespace MS3DPRINT.Manager.Core.Tasks;

public sealed record TaskStep(Guid Id, string Title, bool IsCompleted = false);

/// <summary>A task on its own: linked to a project or a client only when that helps. <c>List</c> null is the inbox.</summary>
public sealed record WorkTask(
    Guid Id,
    string Title,
    string? Notes,
    ProjectPriority Priority,
    DateOnly? DueDate,
    string? List,
    Guid? ProjectId,
    Guid? ClientId,
    double Order,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt = null,
    DateTimeOffset? DeletedAt = null,
    IReadOnlyList<TaskStep>? Steps = null)
{
    public const string Inbox = "Boîte de réception";
    public bool IsCompleted => CompletedAt is not null;
    public bool IsDeleted => DeletedAt is not null;

    public static WorkTask New(string title, double order) =>
        new(Guid.NewGuid(), title.Trim(), null, ProjectPriority.None, null, null, null, null, order, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    public static void Validate(WorkTask task)
    {
        if (task.Id == Guid.Empty) throw new ArgumentException("L’identifiant de la tâche est requis.");
        if (string.IsNullOrWhiteSpace(task.Title) || task.Title.Length > 500) throw new ArgumentException("Le texte de la tâche est requis (500 caractères maximum).");
        if (task.Notes?.Length > 10000) throw new ArgumentException("Les notes sont limitées à 10 000 caractères.");
        if (task.List is { } list && (string.IsNullOrWhiteSpace(list) || list.Length > 60)) throw new ArgumentException("Le nom de la liste est invalide (60 caractères maximum).");
        if (!Enum.IsDefined(task.Priority)) throw new ArgumentException("La priorité de la tâche est invalide.");
        if (task.ProjectId == Guid.Empty || task.ClientId == Guid.Empty) throw new ArgumentException("Le projet ou le client lié est invalide.");
        if (!double.IsFinite(task.Order)) throw new ArgumentException("L’ordre de la tâche est invalide.");
        var steps = task.Steps ?? [];
        if (steps.Count > 100) throw new ArgumentException("Une tâche contient au plus 100 sous-tâches.");
        if (steps.Any(s => s is null || s.Id == Guid.Empty || string.IsNullOrWhiteSpace(s.Title) || s.Title.Length > 500) || steps.Select(s => s.Id).Distinct().Count() != steps.Count)
            throw new ArgumentException("Les sous-tâches sont invalides.");
    }
}
