using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MS3DPRINT.Manager.Core.Files;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Templates;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>Files that a slicer saved inside the project and that can be filed in the right folder, with the user's confirmation.</summary>
public partial class MainWindow
{
    private static readonly TimeSpan ClassificationScanInterval = TimeSpan.FromSeconds(10);

    private Control BuildClassificationSection(ProjectSummary project)
    {
        var root = WorkspaceRoot;
        var list = new StackPanel { Spacing = 8 };
        var heading = new TextBlock { Text = "Fichiers à classer", FontSize = 18, FontWeight = FontWeight.SemiBold, Classes = { "brand-heading" } };
        var subtitle = new TextBlock { Text = "Projets enregistrés par un logiciel externe, à ranger dans ce projet.", Classes = { "muted" }, TextWrapping = TextWrapping.Wrap };
        var section = new StackPanel { Spacing = 10, IsVisible = false, Children = { heading, subtitle, list } };
        var scanning = false;
        DispatcherTimer? timer = null;

        async Task RefreshAsync()
        {
            if (scanning || !string.Equals(root, WorkspaceRoot, StringComparison.OrdinalIgnoreCase)) return;
            scanning = true;
            try
            {
                var proposals = await Task.Run(() =>
                {
                    var template = new ProjectTemplateStore(new WorkspaceMetadataPaths(root)).Load();
                    var ignored = new IgnoredClassificationStore(root).Load();
                    return ExternalFileClassifier.Scan(project.ProjectPath, project.FolderName, template)
                        .Where(p => !ignored.Contains(Path.GetRelativePath(root, p.FilePath).Replace('\\', '/'))).ToArray();
                });
                if (!string.Equals(root, WorkspaceRoot, StringComparison.OrdinalIgnoreCase)) return;
                Render(proposals);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException)
            { section.IsVisible = false; }
            finally { scanning = false; }
        }

        void Render(IReadOnlyList<ClassificationProposal> proposals)
        {
            list.Children.Clear();
            section.IsVisible = proposals.Count > 0;
            heading.Text = $"Fichiers à classer ({proposals.Count})";
            foreach (var proposal in proposals)
            {
                var name = new TextBlock { Text = Path.GetFileName(proposal.FilePath), FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
                var detail = new TextBlock
                {
                    Text = $"{proposal.Reason}\nProposé : {proposal.Destination.RelativeDirectory.Replace('\\', '/')}/{proposal.Destination.FileName}",
                    Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, FontSize = 14, LineHeight = 21
                };
                var accept = new Button { Content = Icons.Label("IconMove", "Classer"), Classes = { "primary-action" }, Tag = proposal };
                var ignore = new Button { Content = "Ignorer", Classes = { "ghost" }, Tag = proposal };
                accept.Click += async (_, _) =>
                {
                    try
                    {
                        await Task.Run(() => new ProjectFileTransferService().Move(proposal.FilePath, project.ProjectPath, proposal.Destination));
                        Notify($"« {Path.GetFileName(proposal.FilePath)} » classé dans {proposal.Destination.RelativeDirectory.Replace('\\', '/')}.");
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or ProjectFileTransferException or MS3DPRINT.Manager.Core.Storage.FolderConflictException)
                    { Notify("Classement impossible : " + exception.Message, error: true); }
                    await RefreshAsync();
                };
                ignore.Click += async (_, _) =>
                {
                    try { await Task.Run(() => new IgnoredClassificationStore(root).Add(Path.GetRelativePath(root, proposal.FilePath))); }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
                    { Notify(exception.Message, error: true); }
                    await RefreshAsync();
                };
                var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { accept, ignore } };
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 16, Children = { new StackPanel { Spacing = 4, Children = { name, detail } }, buttons } };
                Grid.SetColumn(buttons, 1);
                list.Children.Add(row);
            }
        }

        section.AttachedToVisualTree += (_, _) =>
        {
            timer ??= new DispatcherTimer { Interval = ClassificationScanInterval };
            timer.Tick -= OnTick; timer.Tick += OnTick; timer.Start();
            _ = RefreshAsync();
        };
        section.DetachedFromVisualTree += (_, _) => timer?.Stop();
        async void OnTick(object? sender, EventArgs e) => await RefreshAsync();
        return section;
    }
}
