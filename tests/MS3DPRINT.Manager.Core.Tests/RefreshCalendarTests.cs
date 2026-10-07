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
    public void DatePickerPopup_UsesReadableDarkCalendarColors()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? host = null;
            try
            {
                var resources = ThemeTestResources.Load();
                ThemeManager.Apply(MS3DPRINT.Manager.Core.Storage.ThemePreference.Dark, resources);
                var picker = new DatePicker { Resources = resources, DisplayDate = new DateTime(2026, 10, 1) };
                picker.Style = (Style)resources[typeof(DatePicker)];
                host = new Window { Content = picker, Width = 350, Height = 100, ShowInTaskbar = false, ShowActivated = false };
                host.Show();
                picker.IsDropDownOpen = true;
                picker.UpdateLayout();
                var popup = Assert.IsType<Popup>(picker.Template.FindName("PART_Popup", picker));
                Assert.NotNull(popup.Child);
                var calendar = new[] { popup.Child }.Concat(Descendants(popup.Child)).OfType<Calendar>().Single();
                calendar.UpdateLayout();
                var item = Descendants(calendar).OfType<CalendarItem>().Single();
                var surface = ((SolidColorBrush)resources["SurfaceBrush"]).Color;
                Assert.Equal(surface, ((SolidColorBrush)Descendants(item).OfType<Border>().First().Background).Color);
                var day = Descendants(calendar).OfType<CalendarDayButton>().First(button => !button.IsInactive && !button.IsSelected);
                Assert.True(Contrast(day.Foreground, (Brush)resources["SurfaceBrush"]) >= 4.5);
            }
            catch (Exception exception) { failure = new InvalidOperationException(exception.ToString(), exception); }
            finally { host?.Close(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        if (failure is not null) throw failure;
    }

    [Theory]
    [InlineData("file-rename", "clients")]
    [InlineData("directory-rename", "clients")]
    [InlineData("directory-delete", "clients")]
    [InlineData("file-rename", "projects")]
    [InlineData("directory-rename", "projects")]
    [InlineData("directory-delete", "projects")]
    public async Task Watcher_ObservesRemovalOfRelevantMetadata(string operation, string folder)
    {
        var root = Path.Combine(Path.GetTempPath(), "ms3d-refresh-removal-" + Guid.NewGuid().ToString("N"));
        var clients = Path.Combine(root, ".ms3dprint-manager", folder);
        Directory.CreateDirectory(clients);
        var file = Path.Combine(clients, "client.json");
        File.WriteAllText(file, "{}");
        if (operation == "directory-delete") File.Delete(file);
        try
        {
            var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var watcher = new StorageChangeWatcher(root, () => signal.TrySetResult(), _ => { }, TimeSpan.FromMilliseconds(100));
            switch (operation)
            {
                case "file-rename": File.Move(file, Path.Combine(clients, "client.tmp")); break;
                case "directory-rename": Directory.Move(clients, Path.Combine(root, ".ms3dprint-manager", "archive")); break;
                default: Directory.Delete(clients, true); break;
            }
            var completed = await Task.WhenAny(signal.Task, Task.Delay(1500));
            Assert.Same(signal.Task, completed);
        }
        finally { Directory.Delete(root, true); }
    }
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
                var resources = ThemeTestResources.Load();
                foreach (var dark in new[] { false, true })
                {
                    var apply = typeof(ThemeManager).GetMethod("Apply", new[] { typeof(MS3DPRINT.Manager.Core.Storage.ThemePreference), typeof(ResourceDictionary) });
                    Assert.NotNull(apply);
                    apply.Invoke(null, new object[] { dark ? MS3DPRINT.Manager.Core.Storage.ThemePreference.Dark : MS3DPRINT.Manager.Core.Storage.ThemePreference.Light, resources });
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
                    Assert.True(Contrast(selected.Foreground, selected.Background) >= 4.5);
                    Assert.Equal(resources["AccentBrush"], selected.Background);
                    var disabled = days.First(day => !day.IsSelected && !day.IsInactive);
                    disabled.IsEnabled = false;
                    Assert.Equal(resources["MutedBrush"], disabled.Foreground);
                    foreach (var day in new[] { inactive, selected, disabled })
                    {
                        var cell = Assert.IsType<Border>(day.Template.FindName("CalendarCell", day));
                        Assert.Equal(day.Foreground, TextElementForeground(cell.Child));
                    }
                    calendar.DisplayMode = CalendarMode.Year;
                    calendar.UpdateLayout();
                    var selectedMonth = Descendants(calendar).OfType<CalendarButton>().Single(month => month.HasSelectedDays);
                    Assert.True(Contrast(selectedMonth.Foreground, selectedMonth.Background) >= 4.5);
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
    private static double Contrast(Brush foreground, Brush background)
    {
        static double Luminance(Brush brush)
        {
            var color = ((SolidColorBrush)brush).Color;
            static double Linear(byte channel) { var value = channel / 255.0; return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4); }
            return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
        }
        var first = Luminance(foreground); var second = Luminance(background);
        return (Math.Max(first, second) + .05) / (Math.Min(first, second) + .05);
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
