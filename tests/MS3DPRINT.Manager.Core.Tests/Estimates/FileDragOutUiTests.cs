using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MS3DPRINT.Manager.Desktop;

namespace MS3DPRINT.Manager.Core.Tests.Estimates;

public sealed partial class EstimateUiTests
{
    private sealed class RecordingFileDrag : IPlatformDragSource
    {
        public List<string[]> Calls { get; } = [];
        public Task<DragDropEffects> DoDragDropAsync(PointerPressedEventArgs triggerEvent, IDataTransfer dataTransfer, DragDropEffects allowedEffects)
        {
            Assert.Equal(DragDropEffects.Copy, allowedEffects);
            Calls.Add(dataTransfer.TryGetFiles()!.Select(file => file.TryGetLocalPath()!).ToArray());
            triggerEvent.Pointer.Capture(null);
            dataTransfer.Dispose();
            return Task.FromResult(DragDropEffects.Copy);
        }
    }

    [Fact]
    public Task File_button_starts_native_copy_drag_and_keeps_click_and_multiselection() => Run(async () =>
    {
        using var scope = AvaloniaLocator.EnterScope();
        var drag = new RecordingFileDrag();
        AvaloniaLocator.CurrentMutable.Bind<IPlatformDragSource>().ToConstant(drag);
        var owner = new Window { Width = 1000, Height = 700 };
        try
        {
            var folder = Directory.CreateDirectory(Path.Combine(_root, "02_MODELES_3D", "Export")).FullName;
            var a = Path.Combine(folder, "a.txt"); var b = Path.Combine(folder, "b.txt");
            File.WriteAllText(a, "a"); File.WriteAllText(b, "b");
            var clicks = 0;
            var entries = new MS3DPRINT.Manager.Core.Files.ProjectFileBrowser().Enumerate(folder, folder).ToArray();
            var browser = new FileBrowserView(entries, entry =>
            {
                var open = new Button { Tag = entry.FullPath, Content = entry.Name, Height = 48 };
                open.Click += (_, _) => clicks++;
                return new Grid { Children = { open } };
            }, () => new TextBlock { Text = "Fichiers" }, () => Task.CompletedTask);
            owner.Content = browser; owner.Show();
            Dispatcher.UIThread.RunJobs(); owner.UpdateLayout();
            var button = browser.GetVisualDescendants().OfType<Button>().Single(button => button.Tag as string == a && button is not CheckBox);
            var point = button.TranslatePoint(new Point(30, button.Bounds.Height / 2), owner)!.Value;
            async Task Drag()
            {
                Dispatcher.UIThread.RunJobs(); owner.UpdateLayout();
                point = button.TranslatePoint(new Point(30, button.Bounds.Height / 2), owner)!.Value;
                var before = drag.Calls.Count;
                owner.MouseDown(point, MouseButton.Left);
                owner.MouseMove(point + new Vector(12, 0), RawInputModifiers.LeftMouseButton);
                for (var i = 0; i < 30 && drag.Calls.Count == before; i++) await Task.Delay(10);
                owner.MouseUp(point + new Vector(12, 0), MouseButton.Left);
            }
            await Drag();
            Assert.Equal(new[] { a }, Assert.Single(drag.Calls));
            Assert.Equal(0, clicks);
            browser.SelectFile(a, KeyModifiers.Control); browser.SelectFile(b, KeyModifiers.Control);
            await Drag();
            Assert.Equal(new[] { a, b }.Order(), drag.Calls.Last().Order());
            Assert.Equal(2, drag.Calls.Count);
            owner.MouseDown(point, MouseButton.Left); owner.MouseUp(point, MouseButton.Left);
            Assert.Equal(1, clicks);
            Assert.Equal("a", File.ReadAllText(a)); Assert.Equal("b", File.ReadAllText(b));
        }
        finally { owner.Close(); }
    });
}
