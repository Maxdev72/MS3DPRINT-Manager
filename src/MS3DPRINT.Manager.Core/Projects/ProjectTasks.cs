namespace MS3DPRINT.Manager.Core.Projects;

public sealed record ProjectTask(Guid Id, string Title, string? Notes = null, bool IsCompleted = false);

/// <summary>Ordered workshop actions. Completing a task never changes status or accounting.</summary>
public static class ProjectTasks
{
    public static IReadOnlyList<ProjectTask> Items(ProjectProfile? profile) => profile?.Tasks ?? [];
    public static ProjectTask? Next(ProjectProfile? profile) => Items(profile).FirstOrDefault(t => !t.IsCompleted);

    public static ProjectProfile Add(ProjectProfileStore store, ProjectProfile profile, string title, string? notes)
        => Save(store, profile, [.. Items(profile), new(Guid.NewGuid(), title.Trim(), Clean(notes))]);

    public static ProjectProfile Edit(ProjectProfileStore store, ProjectProfile profile, Guid id, string title, string? notes)
        => Change(store, profile, id, t => t with { Title = title.Trim(), Notes = Clean(notes) });

    public static ProjectProfile SetCompleted(ProjectProfileStore store, ProjectProfile profile, Guid id, bool completed)
        => Change(store, profile, id, t => t with { IsCompleted = completed });

    public static ProjectProfile Remove(ProjectProfileStore store, ProjectProfile profile, Guid id)
    {
        Require(profile, id);
        return Save(store, profile, Items(profile).Where(t => t.Id != id).ToArray());
    }

    public static ProjectProfile Move(ProjectProfileStore store, ProjectProfile profile, Guid id, int direction)
    {
        if (direction is not (-1 or 1)) throw new ArgumentException("Le déplacement doit être d’une position.");
        Require(profile, id);
        var items = Items(profile).ToArray();
        var index = Array.FindIndex(items, t => t.Id == id);
        var target = index + direction;
        if (target < 0 || target >= items.Length) return profile;
        (items[index], items[target]) = (items[target], items[index]);
        return Save(store, profile, items);
    }

    public static void Validate(IReadOnlyList<ProjectTask>? tasks)
    {
        if (tasks is null) return;
        var ids = new HashSet<Guid>();
        foreach (var t in tasks)
        {
            if (t is null || t.Id == Guid.Empty || !ids.Add(t.Id)) throw new ArgumentException("Les identifiants des tâches sont invalides.");
            if (string.IsNullOrWhiteSpace(t.Title) || t.Title.Length > 500) throw new ArgumentException("Le texte de la tâche est requis (500 caractères maximum).");
            if (t.Notes?.Length > 10000) throw new ArgumentException("Les notes sont limitées à 10 000 caractères.");
        }
    }

    public static ProjectProfile MoveRelative(ProjectProfileStore store, ProjectProfile profile, Guid source, Guid target, bool before)
    {
        Require(profile, source); Require(profile, target);
        if (source == target) return profile;
        var items = Items(profile).ToList();
        var task = items.Single(t => t.Id == source);
        items.Remove(task);
        var index = items.FindIndex(t => t.Id == target) + (before ? 0 : 1);
        items.Insert(index, task);
        if (items.SequenceEqual(Items(profile))) return profile;
        return Save(store, profile, items.ToArray());
    }

    private static ProjectProfile Change(ProjectProfileStore store, ProjectProfile profile, Guid id, Func<ProjectTask, ProjectTask> change)
    {
        Require(profile, id);
        return Save(store, profile, Items(profile).Select(t => t.Id == id ? change(t) : t).ToArray());
    }
    private static void Require(ProjectProfile profile, Guid id)
    {
        if (!Items(profile).Any(t => t.Id == id)) throw new InvalidOperationException("La tâche est introuvable. Actualisez le projet.");
    }
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static ProjectProfile Save(ProjectProfileStore store, ProjectProfile profile, IReadOnlyList<ProjectTask> tasks)
        => store.Update(profile with { Tasks = tasks }, profile.UpdatedAt);
}
