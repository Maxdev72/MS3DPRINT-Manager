using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using MS3DPRINT.Manager.Core.Projects;

namespace MS3DPRINT.Manager.Desktop;

public sealed class ProjectTaskWindow : DialogWindow
{
    public ProjectTaskWindow(ProjectTask task, Func<string, string?, Task<string?>> save)
    {
        Title = "Modifier une tâche"; Width = 500; Height = 520;
        var title = new TextBox { Text = task.Title, MaxLength = 500 };
        var notes = new TextBox { Text = task.Notes, AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MinHeight = 140, MaxLength = 10000 };
        AutomationProperties.SetName(title, "Texte de la tâche"); AutomationProperties.SetName(notes, "Notes de la tâche");
        var error = FormKit.ErrorText();
        var confirm = new Button { Content = "Enregistrer" };
        confirm.Click += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(title.Text)) { FormKit.ShowError(error, title, "Saisissez le texte de la tâche."); return; }
            confirm.IsEnabled = false;
            try
            {
                error.Text = await save(title.Text.Trim(), notes.Text);
                error.IsVisible = error.Text is not null;
                if (error.Text is null) Close(true);
            }
            finally { confirm.IsEnabled = true; }
        };
        Layout("Modifier une tâche", "Le texte et les notes sont conservés dans ce projet.",
            [FormKit.Field("Texte de la tâche", title), FormKit.Field("Notes de la tâche", notes, hint: "Facultatives. Cliquez sur une tâche pour retrouver ses notes complètes."), error], confirm);
        Opened += (_, _) => title.Focus();
    }
}
