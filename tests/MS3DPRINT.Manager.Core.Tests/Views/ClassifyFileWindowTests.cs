using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MS3DPRINT.Manager.App.ViewModels;
using MS3DPRINT.Manager.App.Views;

namespace MS3DPRINT.Manager.Core.Tests.Views;

[Collection("Responsive layout UI")]
public sealed class ClassifyFileWindowTests
{
    [Fact]
    public void ProjectSelector_StartsDisabledWhileProjectsAreLoading()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var root = Path.Combine(Path.GetTempPath(), "ms3d-classify-dialog-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(root);
                var window = new ClassifyFileWindow(root);
                try
                {
                    var projectBox = Assert.IsType<ComboBox>(window.FindName("ProjectBox"));
                    Assert.False(projectBox.IsEnabled);
                }
                finally
                {
                    window.Close();
                    Directory.Delete(root, recursive: true);
                }
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        if (failure is not null) throw failure;
    }

    [Fact]
    public void MissingSource_IsExplainedInTheDialog()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "ms3d-classify-dialog-" + Guid.NewGuid().ToString("N"));
            try
            {
                var project = Path.Combine(root, "01_CLIENTS", "ATELIER", "AT-2026-001_TEST");
                Directory.CreateDirectory(Path.Combine(project, "01_DEVIS_FACTURES"));
                var window = new ClassifyFileWindow(root);
                try
                {
                    window.ShowInTaskbar = false;
                    window.ShowActivated = false;
                    window.Show();
                    var model = Assert.IsType<ClassifyFileViewModel>(window.DataContext);
                    model.SourcePath = Path.Combine(root, "fichier-supprime.pdf");
                    window.UpdateLayout();

                    var sourceIssue = Assert.IsType<TextBlock>(window.FindName("SourceIssueText"));
                    Assert.Equal("Le fichier source est introuvable. Sélectionnez-le à nouveau.", sourceIssue.Text);
                }
                finally { window.Close(); }
            }
            catch (Exception exception) { failure = exception; }
            finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        if (failure is not null) throw failure;
    }

    [Fact]
    public void MoveAction_IsDisabledUntilSourceProjectAndDestinationAreReady()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "ms3d-classify-dialog-" + Guid.NewGuid().ToString("N"));
            try
            {
                var project = Path.Combine(root, "01_CLIENTS", "ATELIER", "AT-2026-001_TEST");
                Directory.CreateDirectory(Path.Combine(project, "01_DEVIS_FACTURES"));
                var source = Path.Combine(root, "DEV2026-05.pdf");
                File.WriteAllText(source, "test");
                var window = new ClassifyFileWindow(root);
                try
                {
                    window.ShowInTaskbar = false;
                    window.ShowActivated = false;
                    window.Show();
                    var move = Assert.IsType<Button>(window.FindName("MoveButton"));
                    var model = Assert.IsType<ClassifyFileViewModel>(window.DataContext);
                    var projectBox = Assert.IsType<ComboBox>(window.FindName("ProjectBox"));
                    Assert.False(move.IsEnabled);

                    model.SourcePath = source;
                    WaitUntil(() => model.Projects.Count == 1 && projectBox.IsEnabled);
                    model.SelectedProject = Assert.Single(model.Projects);
                    window.UpdateLayout();
                    Assert.True(move.IsEnabled);
                }
                finally { window.Close(); }
            }
            catch (Exception exception) { failure = exception; }
            finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        if (failure is not null) throw failure;
    }

    private static void WaitUntil(Func<bool> condition)
    {
        var completed = false;
        var frame = new DispatcherFrame();
        var poll = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(10) };
        var timeout = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(5) };
        poll.Tick += (_, _) =>
        {
            if (!condition()) return;
            completed = true;
            frame.Continue = false;
        };
        timeout.Tick += (_, _) => frame.Continue = false;
        poll.Start();
        timeout.Start();
        Dispatcher.PushFrame(frame);
        poll.Stop();
        timeout.Stop();
        Assert.True(completed, "Le chargement des projets n’a pas abouti dans le délai imparti.");
    }
}
