using System.Windows;
using System.Windows.Controls;
using MS3DPRINT.Manager.App.Controls;
using MS3DPRINT.Manager.Core.Files;

namespace MS3DPRINT.Manager.Core.Tests.Views;

[Collection("Responsive layout UI")]
public sealed class FileManagementSelectionTests
{
    [Fact]
    public void SelectionEnablesVisibleActionsWithoutOpeningThenOpenUsesSelection()
    {
        Exception? failure = null;
        var worker = new Thread(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "ms3d-file-selection-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(root);
                var path = Path.Combine(root, "document.txt"); File.WriteAllText(path, "fixture");
                var entry = new ProjectFileEntry("document.txt", path, false, 7, DateTimeOffset.UtcNow);
                var list = new ListView { ItemsSource = new[] { entry } };
                var toolbar = new WrapPanel(); var opened = new List<ProjectFileEntry>();
                FileManagement.Attach(toolbar, list, root, () => root, () => Task.CompletedTask, opened.Add, null);
                Button FindButton(string label) => Assert.Single(toolbar.Children.OfType<Button>().Where(button => Equals(button.Content, label)));
                foreach (var label in new[] { "Ouvrir", "Renommer…", "Déplacer…", "Supprimer…" }) Assert.False(FindButton(label).IsEnabled);
                list.SelectedItem = entry;
                Assert.Empty(opened);
                foreach (var label in new[] { "Ouvrir", "Renommer…", "Déplacer…", "Supprimer…" }) Assert.True(FindButton(label).IsEnabled);
                FindButton("Ouvrir").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Same(entry, Assert.Single(opened));
                list.SelectedItem = null;
                Assert.False(FindButton("Ouvrir").IsEnabled);
                FileManagement.Attach(toolbar, list, root, () => root, () => Task.CompletedTask, opened.Add, null);
                Assert.Single(toolbar.Children.OfType<Button>().Where(button => Equals(button.Content, "Ouvrir")));
            }
            catch (Exception exception) { failure = exception; }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        });
        worker.SetApartmentState(ApartmentState.STA); worker.Start(); worker.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
