using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MS3DPRINT.Manager.App;
using MS3DPRINT.Manager.App.ViewModels;
using MS3DPRINT.Manager.App.Views;
using MS3DPRINT.Manager.Core.Collections;
using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Tests.Views;

[CollectionDefinition("Responsive layout UI", DisableParallelization = true)]
public sealed class ResponsiveLayoutUiCollection;

[Collection("Responsive layout UI")]
public sealed class ResponsiveLayoutTests
{
    [Fact]
    public void MainWindow_PageHostUsesAvailableWidthForNarrowPages()
    {
        RunSta(() =>
        {
            var window = new MainWindow();
            var host = Assert.IsType<ContentControl>(window.FindName("PageHost"));
            host.Content = new Grid();
            window.Width = 1180;
            window.Height = 800;
            window.ShowInTaskbar = false;
            window.ShowActivated = false;
            window.WindowStyle = WindowStyle.None;
            try
            {
                window.Show();
                window.UpdateLayout();

                Assert.True(host.ActualWidth >= 950, $"La page reste étroite : {host.ActualWidth:N0} px.");
                Assert.True(host.ActualWidth <= 1200);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void CollectionHeader_WrapsSubtitleBeforeCreateAction()
    {
        RunSta(() =>
        {
            const string subtitle = "Suivre les produits et leur documentation de fabrication.";
            var root = Path.Combine(Path.GetTempPath(), "ms3d-layout-" + Guid.NewGuid().ToString("N"));
            var model = new CollectionViewModel(new CollectionCatalog(), root, "03_PRODUITS_MS3DPRINT", "Produits", subtitle);
            var view = new CollectionView(model);
            view.Measure(new Size(460, 600));
            view.Arrange(new Rect(0, 0, 460, 600));
            view.UpdateLayout();

            var description = Descendants(view).OfType<TextBlock>().Single(text => text.Text == subtitle);
            var create = Descendants(view).OfType<Button>().Single(button => (string?)button.Content == "Nouveau dossier");
            var descriptionRight = description.TranslatePoint(new Point(description.ActualWidth, 0), view).X;
            var actionLeft = create.TranslatePoint(new Point(), view).X;
            Assert.True(description.ActualHeight > 20, "Le sous-titre long reste sur une seule ligne.");
            Assert.True(descriptionRight + 10 <= actionLeft, "Le sous-titre touche l’action de création.");
        });
    }

    [Fact]
    public void ClientRow_SeparatesLongNameFromMetadataAtCompactWidth()
    {
        RunSta(() =>
        {
            const string name = "ATELIER_DE_MODELISATION_ET_IMPRESSION_TRES_LONG";
            var root = Path.Combine(Path.GetTempPath(), "ms3d-layout-" + Guid.NewGuid().ToString("N"));
            var model = new ClientsViewModel(new ClientCatalog(new ClientProfileStore(new WorkspaceMetadataPaths(root))), root);
            model.ApplyCatalog([new ClientSummary(Path.Combine(root, "01_CLIENTS", name), name, name, "AT", ClientKind.Professional, null, 1)]);
            var view = new ClientsView(model);
            view.Measure(new Size(540, 600));
            view.Arrange(new Rect(0, 0, 540, 600));
            view.UpdateLayout();

            var title = Descendants(view).OfType<TextBlock>().Single(text => text.Text == name && text.FontWeight == FontWeights.SemiBold);
            var code = Descendants(view).OfType<TextBlock>().Single(text => text.Text == "CODE");
            var titleBottom = title.TranslatePoint(new Point(0, title.ActualHeight), view).Y;
            var codeTop = code.TranslatePoint(new Point(), view).Y;
            Assert.True(codeTop >= titleBottom, "Les métadonnées recouvrent le nom du client.");
        });
    }

    [Fact]
    public void ProjectRow_SeparatesReferenceFromClientAtCompactWidth()
    {
        RunSta(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "ms3d-layout-" + Guid.NewGuid().ToString("N"));
            var model = new ProjectsViewModel(new ProjectCatalog(new ProjectProfileStore(new WorkspaceMetadataPaths(root))), root);
            const string reference = "ATELIER-2026-001";
            model.ApplyCatalog([new ProjectSummary("ATELIER_DE_MODELISATION_ET_IMPRESSION", root, root, reference,
                reference + "_OUTILLAGE", null)]);
            var view = new ProjectsView(model);
            view.Measure(new Size(540, 600));
            view.Arrange(new Rect(0, 0, 540, 600));
            view.UpdateLayout();

            var title = Descendants(view).OfType<TextBlock>().Single(text => text.Text == reference);
            var client = Descendants(view).OfType<TextBlock>().Single(text => text.Text == "CLIENT");
            var titleBottom = title.TranslatePoint(new Point(0, title.ActualHeight), view).Y;
            var clientTop = client.TranslatePoint(new Point(), view).Y;
            Assert.True(clientTop >= titleBottom, "Le client recouvre la référence projet.");
            var clientName = Descendants(view).OfType<TextBlock>().Single(text => text.Text == "ATELIER_DE_MODELISATION_ET_IMPRESSION");
            var clientRight = clientName.TranslatePoint(new Point(clientName.ActualWidth, 0), view).X;
            Assert.True(clientRight <= view.ActualWidth - 28, "Le nom du client déborde de la liste.");
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ErrorColor_IsReadableOnMainAndDialogSurfaces(bool dark)
    {
        RunSta(() =>
        {
            var resources = LoadAppResources();
            ThemeManager.Apply(dark ? MS3DPRINT.Manager.Core.Storage.ThemePreference.Dark : MS3DPRINT.Manager.Core.Storage.ThemePreference.Light, resources);
            Assert.True(resources.Contains("ErrorBrush"));
            var error = ((SolidColorBrush)resources["ErrorBrush"]).Color;
            foreach (var backgroundKey in new[] { "BackgroundBrush", "SurfaceBrush" })
            {
                var background = ((SolidColorBrush)resources[backgroundKey]).Color;
                Assert.True(Contrast(error, background) >= 4.5, $"Contraste insuffisant sur {backgroundKey}.");
            }
        });
    }

    private static double Contrast(Color first, Color second)
    {
        static double Luminance(Color color)
        {
            static double Linear(byte channel)
            {
                var value = channel / 255.0;
                return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
            }
            return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
        }
        var a = Luminance(first);
        var b = Luminance(second);
        return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }

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
                var expected = ((SolidColorBrush)resources["DisabledButtonBrush"]).Color;
                Assert.Contains(Descendants(button).OfType<Border>(), border => border.Background is SolidColorBrush brush && brush.Color == expected);
                Assert.Equal(((SolidColorBrush)resources["DisabledButtonTextBrush"]).Color,
                    ((SolidColorBrush)button.Foreground).Color);
            }
            finally { host.Close(); }
        });
    }

    private static ResourceDictionary LoadAppResources() => ThemeTestResources.Load();

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
