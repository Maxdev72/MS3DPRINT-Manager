using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MS3DPRINT.Manager.App;
using MS3DPRINT.Manager.App.ViewModels;
using MS3DPRINT.Manager.App.Views;
using MS3DPRINT.Manager.Core.Collections;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Tests.Views;

[CollectionDefinition("Responsive layout UI", DisableParallelization = true)]
public sealed class ResponsiveLayoutUiCollection;

[Collection("Responsive layout UI")]
public sealed class ResponsiveLayoutTests
{
    [Fact]
    public void Projects_SearchRemainsUsableAtCompactWidth()
    {
        RunSta(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "ms3d-layout-" + Guid.NewGuid().ToString("N"));
            var model = new ProjectsViewModel(new ProjectCatalog(new ProjectProfileStore(new WorkspaceMetadataPaths(root))), root);
            var view = new ProjectsView(model);
            view.Measure(new Size(620, 600));
            view.Arrange(new Rect(0, 0, 620, 600));
            view.UpdateLayout();

            var search = Assert.IsType<TextBox>(view.FindName("SearchBox"));
            Assert.True(search.ActualWidth >= 180, $"Recherche réduite à {search.ActualWidth:N0} px.");
        });
    }

    [Fact]
    public void CollectionDetail_KeepsPathAboveActionsAtCompactWidth()
    {
        RunSta(() =>
        {
            var root = @"C:\MS3DPRINT\Nextcloud\MS3DPRINT\03_PRODUITS_MS3DPRINT\POT_ARRAIGNE";
            var model = new CollectionDetailViewModel(new CollectionItemSummary("POT_ARRAIGNE", root, DateTimeOffset.UtcNow));
            model.ApplyFileListing(new CollectionFileListing(root, []));
            var view = new CollectionDetailView(model);
            view.Measure(new Size(620, 600));
            view.Arrange(new Rect(0, 0, 620, 600));
            view.UpdateLayout();

            var path = Assert.IsType<TextBlock>(view.FindName("CurrentPathText"));
            var up = Descendants(view).OfType<Button>().Single(button => (string?)button.Content == "Remonter");
            var pathBottom = path.TranslatePoint(new Point(0, path.ActualHeight), view).Y;
            var actionTop = up.TranslatePoint(new Point(), view).Y;
            Assert.True(actionTop >= pathBottom, "Les actions recouvrent le chemin.");
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisabledButton_RendersThemeBackground(bool dark)
    {
        RunSta(() =>
        {
            var resources = LoadAppResources();
            ThemeManager.Apply(dark ? MS3DPRINT.Manager.Core.Storage.ThemePreference.Dark : MS3DPRINT.Manager.Core.Storage.ThemePreference.Light, resources);
            var button = new Button { Content = "Visualiser en 3D", Width = 170, Height = 36, IsEnabled = false, Resources = resources };
            button.Style = (Style)resources[typeof(Button)];
            var host = new Window { Content = button, Width = 220, Height = 90, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
            try
            {
                host.Show();
                host.UpdateLayout();
                var surface = Assert.IsType<Border>(button.Template.FindName("ButtonSurface", button));
                var expected = ((SolidColorBrush)resources["DisabledButtonBrush"]).Color;
                Assert.Equal(expected, ((SolidColorBrush)surface.Background).Color);
                Assert.Equal(((SolidColorBrush)resources["DisabledButtonTextBrush"]).Color,
                    ((SolidColorBrush)button.Foreground).Color);
            }
            finally { host.Close(); }
        });
    }

    private static ResourceDictionary LoadAppResources()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "src", "MS3DPRINT.Manager.App", "App.xaml"))) directory = directory.Parent;
        var source = System.Xml.Linq.XDocument.Load(Path.Combine(directory!.FullName, "src", "MS3DPRINT.Manager.App", "App.xaml"));
        var ns = source.Root!.Name.Namespace;
        var dictionary = new System.Xml.Linq.XElement(ns + "ResourceDictionary",
            new System.Xml.Linq.XAttribute(System.Xml.Linq.XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml"),
            source.Root.Element(ns + "Application.Resources")!.Elements());
        return (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(dictionary.ToString());
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Le test UI n’a pas terminé.");
        if (failure is not null) throw failure;
    }
}
