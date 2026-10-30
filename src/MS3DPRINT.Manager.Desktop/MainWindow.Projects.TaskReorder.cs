using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MS3DPRINT.Manager.Core.Projects;

namespace MS3DPRINT.Manager.Desktop;

public partial class MainWindow
{
    private sealed record TaskRowIdentity(Guid ProjectId, Guid TaskId);
    private sealed class TaskDrag(IPointer pointer, Point start)
    {
        public IPointer Pointer { get; } = pointer;
        public Point Start { get; } = start;
        public bool Started { get; set; }
        public Guid? Target { get; set; }
        public bool Before { get; set; }
    }

    private Button CreateProjectTaskGrip(ProjectSummary project, ProjectTask task, Border sourceRow)
    {
        var root = WorkspaceRoot;
        var handle = new Button { Content = Icons.Glyph("IconGrip", 18), Classes = { "icon-action", "task-drag-grip" }, Cursor = new Cursor(StandardCursorType.SizeAll), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(handle, "Déplacer la tâche : " + task.Title);
        ToolTip.SetTip(handle, "Glisser pour reclasser · Alt + Haut / Bas au clavier");
        TaskDrag? drag = null;
        Border[] Rows() => ProjectList.GetVisualDescendants().OfType<Border>()
            .Where(b => b.Tag is TaskRowIdentity id && id.ProjectId == project.Profile!.Id).ToArray();
        void ClearMarkers()
        {
            sourceRow.Opacity = 1;
            foreach (var row in Rows()) { row.Classes.Remove("drop-before"); row.Classes.Remove("drop-after"); }
        }
        void Cancel()
        {
            var old = drag; drag = null; ClearMarkers();
            if (old?.Pointer.Captured == handle) old.Pointer.Capture(null);
        }
        void RestoreFocus() => Dispatcher.UIThread.Post(() =>
        {
            if (!ProjectTaskContextCurrent(root)) return;
            Rows().FirstOrDefault(r => r.Tag is TaskRowIdentity id && id.TaskId == task.Id)?
                .GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Classes.Contains("task-drag-grip"))?.Focus();
        });
        handle.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (!e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed || !ProjectTaskContextCurrent(root) || _projectTaskBusy) return;
            drag = new TaskDrag(e.Pointer, e.GetPosition(this));
            handle.Focus(); e.Pointer.Capture(handle); e.Handled = true;
        }, RoutingStrategies.Tunnel);
        handle.PointerMoved += (_, e) =>
        {
            if (drag is null) return;
            if (!ProjectTaskContextCurrent(root) || _projectTaskBusy || drag.Pointer.Captured != handle) { Cancel(); return; }
            var delta = e.GetPosition(this) - drag.Start;
            if (!drag.Started && Math.Abs(delta.X) < 5 && Math.Abs(delta.Y) < 5) return;
            drag.Started = true; drag.Target = null; ClearMarkers(); sourceRow.Opacity = .55;
            foreach (var row in Rows())
            {
                var point = e.GetPosition(row);
                if (point.X < 0 || point.X > row.Bounds.Width || point.Y < 0 || point.Y > row.Bounds.Height || ReferenceEquals(row, sourceRow)) continue;
                drag.Target = ((TaskRowIdentity)row.Tag!).TaskId;
                drag.Before = point.Y < row.Bounds.Height / 2;
                row.Classes.Add(drag.Before ? "drop-before" : "drop-after");
                break;
            }
            e.Handled = true;
        };
        handle.AddHandler(PointerReleasedEvent, async (_, e) =>
        {
            var dropped = drag; Cancel();
            if (dropped is not { Started: true, Target: { } target }) return;
            e.Handled = true;
            var error = await SaveProjectTaskAsync(root, project, (store, profile) => ProjectTasks.MoveRelative(store, profile, task.Id, target, dropped.Before));
            if (error is not null) Notify(error, error: true);
            else RestoreFocus();
        }, RoutingStrategies.Tunnel);
        handle.PointerCaptureLost += (_, _) => { drag = null; ClearMarkers(); };
        handle.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Escape && drag is not null) { Cancel(); e.Handled = true; return; }
            if (e.KeyModifiers != KeyModifiers.Alt || e.Key is not (Key.Up or Key.Down) || !ProjectTaskContextCurrent(root)) return;
            e.Handled = true;
            if (await MoveProjectTaskAsync(project, task, e.Key == Key.Up ? -1 : 1)) RestoreFocus();
        };
        return handle;
    }
}
