using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using MS3DPRINT.Manager.App.Views;
using MS3DPRINT.Manager.App;

namespace MS3DPRINT.Manager.Core.Tests;

public sealed class RefreshCalendarTests
{
    [Fact]
    public async Task Watcher_DebouncesWritesAndStopsAfterDispose()
    {
        var root = Path.Combine(Path.GetTempPath(), "ms3d-refresh-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var notifications = 0;
            var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var watcher = new StorageChangeWatcher(root, () => { Interlocked.Increment(ref notifications); signal.TrySetResult(); }, _ => { }, TimeSpan.FromMilliseconds(150));
            using (watcher)
            {
                var history = Path.Combine(root, ".ms3dprint-manager", "history");
                Directory.CreateDirectory(history);
                File.WriteAllText(Path.Combine(history, "audit.json"), "history");
                await Task.Delay(300);
                Assert.Equal(0, Volatile.Read(ref notifications));
                for (var i = 0; i < 8; i++) File.WriteAllText(Path.Combine(root, "sync.json"), i.ToString());
                await signal.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await Task.Delay(250);
                Assert.Equal(1, Volatile.Read(ref notifications));
            }
            File.WriteAllText(Path.Combine(root, "after.json"), "closed");
            await Task.Delay(250);
            Assert.Equal(1, Volatile.Read(ref notifications));
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public void CatalogViews_ExposeInPlaceAsyncRefresh()
    {
        foreach (var type in new[] { typeof(ClientsView), typeof(ProjectsView), typeof(CollectionView), typeof(ClientDetailView), typeof(ProjectDetailView), typeof(CollectionDetailView) })
            Assert.NotNull(type.GetMethod("RefreshAsync"));
    }

    [Fact]
    public void CalendarTemplates_UseThemeBackgroundAndContentForeground()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var directory = new DirectoryInfo(AppContext.BaseDirectory);
                while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "src", "MS3DPRINT.Manager.App", "App.xaml"))) directory = directory.Parent;
                var document = System.Xml.Linq.XDocument.Load(Path.Combine(directory!.FullName, "src", "MS3DPRINT.Manager.App", "App.xaml"));
                var ns = document.Root!.Name.Namespace;
                var dictionary = new System.Xml.Linq.XElement(ns + "ResourceDictionary",
                    new System.Xml.Linq.XAttribute(System.Xml.Linq.XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml"),
                    document.Root.Element(ns + "Application.Resources")!.Elements());
                var resources = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(dictionary.ToString());
                foreach (var dark in new[] { false, true })
                {
                    resources["TextBrush"] = new SolidColorBrush(dark ? Colors.White : Colors.Black);
                    resources["SurfaceBrush"] = new SolidColorBrush(dark ? Colors.Black : Colors.White);
                    var calendar = new Calendar { Resources = resources, DisplayDate = new DateTime(2026, 10, 1) };
                    calendar.Style = (Style)resources[typeof(Calendar)];
                    var host = new Window { Content = calendar, Width = 350, Height = 350, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
                    host.Show();
                    calendar.Measure(new Size(300, 300)); calendar.Arrange(new Rect(0, 0, 300, 300)); calendar.UpdateLayout();
                    var item = Descendants(calendar).OfType<CalendarItem>().Single();
                    Assert.Equal(((SolidColorBrush)resources["SurfaceBrush"]).Color, ((SolidColorBrush)Descendants(item).OfType<Border>().First().Background).Color);
                    var days = Descendants(calendar).OfType<CalendarDayButton>().ToArray();
                    Assert.True(days.Length >= 28);
                    var inactive = days.First(day => day.IsInactive);
                    Assert.Equal(resources["MutedBrush"], inactive.Foreground);
                    calendar.SelectedDate = new DateTime(2026, 10, 15);
                    calendar.UpdateLayout();
                    var selected = days.Single(day => day.IsSelected);
                    Assert.Equal(Colors.White, ((SolidColorBrush)selected.Foreground).Color);
                    Assert.Equal(resources["AccentBrush"], selected.Background);
                    var disabled = days.First(day => !day.IsSelected && !day.IsInactive);
                    disabled.IsEnabled = false;
                    Assert.Equal(resources["MutedBrush"], disabled.Foreground);
                    foreach (var day in new[] { inactive, selected, disabled })
                    {
                        var cell = Assert.IsType<Border>(day.Template.FindName("CalendarCell", day));
                        Assert.Equal(day.Foreground, TextElementForeground(cell.Child));
                    }
                    host.Close();
                    foreach (var button in new Button[] { new CalendarDayButton { Content = "15" }, new CalendarButton { Content = "octobre" } })
                    {
                        button.Resources = resources;
                        button.Style = (Style)resources[button.GetType()];
                        button.Measure(new Size(100, 40)); button.Arrange(new Rect(0, 0, 100, 40)); button.ApplyTemplate();
                        var border = button.Template.FindName("CalendarCell", button) as Border;
                        Assert.NotNull(border);
                        Assert.Equal(button.Background, border.Background);
                        Assert.Equal(button.Foreground, TextElementForeground(border.Child));
                    }
                }
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15))); Assert.Null(failure);
    }

    private static Brush TextElementForeground(UIElement child) => System.Windows.Documents.TextElement.GetForeground(child);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
