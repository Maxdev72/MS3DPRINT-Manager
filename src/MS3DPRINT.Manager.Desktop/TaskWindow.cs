using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using MS3DPRINT.Manager.Core.Tasks;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>A link offered in the task form: a project or a client, or none.</summary>
public sealed record TaskLinkChoice(Guid? Id, string Label, Guid? ClientId = null)
{
    public override string ToString() => Label;
}

/// <summary>Every field of a task. Closes with true once saved; « Retirer » asks first, then removes.</summary>
public sealed class TaskWindow : DialogWindow
{
    private readonly List<TaskStep> _steps;
    private readonly StackPanel _stepRows = new() { Spacing = 4 };

    public TaskWindow(WorkTask task, IReadOnlyList<string> lists, IReadOnlyList<TaskLinkChoice> projects, IReadOnlyList<TaskLinkChoice> clients,
        Func<WorkTask, Task<string?>> save, Func<Task<string?>>? remove)
    {
        var isNew = remove is null;
        Title = isNew ? "Nouvelle tâche" : "Modifier la tâche"; Width = 560; Height = 760;
        _steps = [.. task.Steps ?? []];
        var title = new TextBox { Text = task.Title, MaxLength = 500 };
        var notes = new TextBox { Text = task.Notes, AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MinHeight = 90, MaxLength = 10000 };
        var due = new DateBox(task.DueDate, "Aucune");
        var priority = new ProjectPriorityPicker(task.Priority);
        var list = new AutoCompleteBox { Text = task.List, ItemsSource = lists, FilterMode = AutoCompleteFilterMode.ContainsOrdinal, MinimumPrefixLength = 0, PlaceholderText = WorkTask.Inbox, MaxLength = 60 };
        var project = new ComboBox { ItemsSource = projects, HorizontalAlignment = HorizontalAlignment.Stretch, SelectedItem = projects.FirstOrDefault(p => p.Id == task.ProjectId) ?? projects[0] };
        var client = new ComboBox { ItemsSource = clients, HorizontalAlignment = HorizontalAlignment.Stretch, SelectedItem = clients.FirstOrDefault(c => c.Id == task.ClientId) ?? clients[0] };
        void LinkClient()
        {
            // A project carries its client: the client picker only matters for a task without project.
            var chosen = (TaskLinkChoice)project.SelectedItem!;
            client.IsEnabled = chosen.Id is null;
            if (chosen.ClientId is { } owner) client.SelectedItem = clients.FirstOrDefault(c => c.Id == owner) ?? clients[0];
        }
        project.SelectionChanged += (_, _) => LinkClient(); LinkClient();
        AutomationProperties.SetName(title, "Texte de la tâche"); AutomationProperties.SetName(notes, "Notes de la tâche");
        AutomationProperties.SetName(list, "Liste de la tâche"); AutomationProperties.SetName(project, "Projet lié"); AutomationProperties.SetName(client, "Client lié");

        var newStep = new TextBox { PlaceholderText = "Ajouter une sous-tâche…", MaxLength = 500 };
        AutomationProperties.SetName(newStep, "Nouvelle sous-tâche");
        newStep.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || string.IsNullOrWhiteSpace(newStep.Text)) return;
            e.Handled = true;
            if (_steps.Count >= 100) return;
            _steps.Add(new TaskStep(Guid.NewGuid(), newStep.Text.Trim())); newStep.Text = ""; RenderSteps();
        };
        RenderSteps();

        var error = FormKit.ErrorText();
        var confirm = new Button { Content = isNew ? "Créer la tâche" : "Enregistrer" };
        confirm.Click += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(title.Text)) { FormKit.ShowError(error, title, "Saisissez le texte de la tâche."); return; }
            var chosenProject = (TaskLinkChoice)project.SelectedItem!;
            var updated = task with
            {
                Title = title.Text.Trim(), Notes = string.IsNullOrWhiteSpace(notes.Text) ? null : notes.Text.Trim(),
                DueDate = due.Date, Priority = priority.Value, List = string.IsNullOrWhiteSpace(list.Text) || list.Text.Trim() == WorkTask.Inbox ? null : list.Text.Trim(),
                ProjectId = chosenProject.Id, ClientId = chosenProject.Id is not null ? chosenProject.ClientId : ((TaskLinkChoice)client.SelectedItem!).Id,
                Steps = _steps.Count == 0 ? null : _steps.Select(s => s with { Title = s.Title.Trim() }).Where(s => s.Title.Length > 0).ToArray()
            };
            confirm.IsEnabled = false;
            try
            {
                error.Text = await save(updated);
                error.IsVisible = error.Text is not null;
                if (error.Text is null) Close(true);
            }
            finally { confirm.IsEnabled = true; }
        };
        Button? removeButton = null;
        if (remove is not null)
        {
            removeButton = new Button { Content = Icons.Label("IconTrash", "Retirer"), Classes = { "menu-destructive" } };
            removeButton.Click += async (_, _) =>
            {
                if (!await new ConfirmRemovalWindow(task.Title, "Cette tâche, ses notes et ses sous-tâches seront retirées. Le projet et le client liés ne sont pas concernés.").ShowDialog<bool>(this)) return;
                error.Text = await remove();
                error.IsVisible = error.Text is not null;
                if (error.Text is null) Close(true);
            };
        }
        var links = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 12 };
        var clientField = FormKit.Field("Client", client); Grid.SetColumn(clientField, 1);
        links.Children.Add(FormKit.Field("Projet", project)); links.Children.Add(clientField);
        var planning = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 12 };
        var listField = FormKit.Field("Liste", list, hint: "Vide : Boîte de réception. Un nouveau nom crée la liste."); Grid.SetColumn(listField, 1);
        planning.Children.Add(FormKit.Field("Échéance", due)); planning.Children.Add(listField);
        Layout(isNew ? "Nouvelle tâche" : "Modifier la tâche", null,
            [FormKit.Field("Tâche", title), planning, FormKit.Field("Priorité", priority), links,
             FormKit.Field("Sous-tâches", new StackPanel { Spacing = 6, Children = { _stepRows, newStep } }),
             FormKit.Field("Notes", notes), error], confirm, extraAction: removeButton);
        Opened += (_, _) => title.Focus();
    }

    private void RenderSteps()
    {
        _stepRows.Children.Clear();
        foreach (var step in _steps.ToArray())
        {
            var check = new CheckBox { IsChecked = step.IsCompleted, VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(check, (step.IsCompleted ? "Rouvrir la sous-tâche : " : "Terminer la sous-tâche : ") + step.Title);
            var text = new TextBox { Text = step.Title, MaxLength = 500 };
            AutomationProperties.SetName(text, "Sous-tâche");
            var drop = new Button { Content = Icons.Glyph("IconClose", 14), Classes = { "tool" }, VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(drop, "Retirer la sous-tâche : " + step.Title);
            void Replace(TaskStep value) { var i = _steps.FindIndex(s => s.Id == step.Id); if (i >= 0) _steps[i] = value; }
            check.IsCheckedChanged += (_, _) => Replace(_steps.First(s => s.Id == step.Id) with { IsCompleted = check.IsChecked == true });
            text.TextChanged += (_, _) => Replace(_steps.First(s => s.Id == step.Id) with { Title = text.Text ?? "" });
            drop.Click += async (_, _) =>
            {
                if (!await new ConfirmRemovalWindow(step.Title, "Cette sous-tâche sera retirée à l’enregistrement de la tâche.").ShowDialog<bool>(this)) return;
                _steps.RemoveAll(s => s.Id == step.Id); RenderSteps();
            };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 8 };
            Grid.SetColumn(text, 1); Grid.SetColumn(drop, 2);
            row.Children.Add(check); row.Children.Add(text); row.Children.Add(drop);
            _stepRows.Children.Add(row);
        }
    }
}
