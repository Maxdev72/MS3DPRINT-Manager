using System.IO.Compression;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MS3DPRINT.Manager.Core.Files;

namespace MS3DPRINT.Manager.Core.Tests.Estimates;

public sealed partial class EstimateUiTests
{
    private static void WriteBambu3mf(string path)
    {
        using var stream = new FileStream(path, FileMode.Create);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        var model = archive.CreateEntry("3D/3dmodel.model");
        using (var writer = new StreamWriter(model.Open())) writer.Write("<model><metadata name=\"Application\">BambuStudio-02.06.00.51</metadata></model>");
        var settings = archive.CreateEntry("Metadata/model_settings.config");
        using (var writer = new StreamWriter(settings.Open())) writer.Write("<config><object id=\"1\"><metadata key=\"name\" value=\"Support.stl\"/></object></config>");
    }

    [Fact]
    public Task Project_overview_offers_to_file_a_slicer_3mf_and_accepting_moves_it_to_the_3mf_folder() => Run(async () =>
    {
        new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceClientCreator().CreateProfessional(_root, "Atelier Horizon", "HORIZON");
        var client = new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceClientReader().Load(_root).First();
        new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceProjectCreator().CreateTracked(_root, client, 2026, "Support");
        var project = new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceProjectReader().Load(_root).First();
        var stlFolder = Path.Combine(project.ProjectPath, "03_CAO_3D", "03_STL");
        Directory.CreateDirectory(stlFolder);
        File.WriteAllText(Path.Combine(stlFolder, "Support.stl"), "solid");
        var written = Path.Combine(stlFolder, "Support.3mf");
        WriteBambu3mf(written);
        File.SetLastWriteTimeUtc(written, DateTime.UtcNow.AddMinutes(-10));
        var window = await Open(); window.Show();
        try
        {
            var sheet = Assert.IsType<Border>(Invoke(window, "BuildProjectOverview", project));
            window.Content = sheet;
            Dispatcher.UIThread.RunJobs();
            for (var i = 0; i < 800 && !sheet.GetVisualDescendants().OfType<Button>().Any(b => b.Content is Control c && c.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Classer")); i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
            var accept = sheet.GetVisualDescendants().OfType<Button>().Single(b => b.Content is Control c && c.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Classer"));
            Assert.Contains(sheet.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("Support.stl") == true);
            accept.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var target = Path.Combine(project.ProjectPath, "04_IMPRESSION_3D", "02_3MF");
            for (var i = 0; i < 800 && !Directory.GetFiles(target, "*.3mf").Any(); i++) await Task.Delay(10);
            Assert.Single(Directory.GetFiles(target, "*.3mf"));
            Assert.False(File.Exists(written));
        }
        finally { window.Close(); }
    });
}
