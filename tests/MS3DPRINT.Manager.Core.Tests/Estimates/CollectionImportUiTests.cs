using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MS3DPRINT.Manager.Desktop;
using MS3DPRINT.Manager.Desktop.Dashboard;

namespace MS3DPRINT.Manager.Core.Tests.Estimates;

public sealed partial class EstimateUiTests
{
    [Fact]
    public Task Legacy_collection_without_a_normalizable_reference_still_imports_original_name() => Run(async () =>
    {
        var directory = Directory.CreateDirectory(Path.Combine(_root, "02_MODELES_3D", "日本語")).FullName;
        var source = Path.Combine(_root, "piece.stl"); File.WriteAllText(source, "original");
        var owner = await Open(); owner.Show();
        try
        {
            await OpenFileTestFolder(owner, directory);
            var pending = (Task)Invoke(owner, "ImportWorkspaceFilesAsync", _root, directory, new[] { source })!;
            await WaitForDialog<ConfirmImportSourceWindow>(owner); DialogAction(owner, "Conserver la source");
            await pending;
            Assert.Equal("original", File.ReadAllText(Path.Combine(directory, "piece.stl")));
            Assert.True(File.Exists(source));
        }
        finally { owner.Close(); }
    });

    [Theory]
    [InlineData("02_MODELES_3D")]
    [InlineData("03_PRODUITS_MS3DPRINT")]
    [InlineData("06_FOURNISSEURS")]
    public Task Collection_import_uses_collection_reference_in_subfolders_and_preserves_naming_choices(string category) => Run(async () =>
    {
        var collection = new WorkspaceCollectionCreator().Create(_root, category, "Dossier technique");
        new WorkspaceCollectionEditor().Save(_root, collection, category, "Pots décoratifs", null, null);
        var directory = Directory.CreateDirectory(Path.Combine(collection.Path, "Fichiers", "STL")).FullName;
        var sourceDirectory = Directory.CreateDirectory(Path.Combine(_root, "sources-import")).FullName;
        var sources = new[] { "piece.stl", "original.pdf", "deja__POTS_DECORATIFS.stl", "annule.txt" }
            .Select(name => Path.Combine(sourceDirectory, name)).ToArray();
        foreach (var source in sources) File.WriteAllText(source, Path.GetFileName(source));
        var owner = await Open(); owner.Width = 1400; owner.Height = 900; owner.Show();
        try
        {
            await OpenFileTestFolder(owner, directory);
            var pending = (Task)Invoke(owner, "ImportWorkspaceFilesAsync", _root, directory, sources)!;
            await WaitForDialog<ImportNameWindow>(owner);
            var body = owner.FindControl<Border>("SidePanelBody")!;
            Assert.Contains(body.GetVisualDescendants().OfType<TextBox>(), input => input.Text == "piece__POTS_DECORATIFS.stl");
            Assert.Contains(body.GetVisualDescendants().OfType<TextBlock>(), text => text.Text != null && text.Text.Contains("référence de la collection (POTS_DECORATIFS)"));
            Assert.Empty(Directory.GetFiles(directory));
            if (category == "02_MODELES_3D")
            {
                await Task.Delay(350); Dispatcher.UIThread.RunJobs(); owner.UpdateLayout();
                CaptureDraftUi(owner, "collection-import-naming");
                foreach (var button in body.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible))
                {
                    var x = button.TranslatePoint(default, body)!.Value.X;
                    Assert.InRange(x, 0, body.Bounds.Width - button.Bounds.Width);
                }
            }
            DialogAction(owner, "Importer ce fichier");
            await WaitForDialog<ConfirmImportSourceWindow>(owner); DialogAction(owner, "Conserver la source");
            await WaitForDialog<ImportNameWindow>(owner); DialogAction(owner, "Garder le nom d’origine");
            await WaitForDialog<ConfirmImportSourceWindow>(owner); DialogAction(owner, "Conserver la source");
            await WaitForDialog<ImportNameWindow>(owner);
            var input = body.GetVisualDescendants().OfType<TextBox>().Single(box => box.Text == "deja__POTS_DECORATIFS.stl");
            input.Text = "personnalise.stl";
            DialogAction(owner, "Importer ce fichier");
            await WaitForDialog<ConfirmImportSourceWindow>(owner); DialogAction(owner, "Conserver la source");
            var last = await WaitForDialog<ImportNameWindow>(owner); last.Close(false);
            await pending;
            Assert.Equal("piece.stl", File.ReadAllText(Path.Combine(directory, "piece__POTS_DECORATIFS.stl")));
            Assert.Equal("original.pdf", File.ReadAllText(Path.Combine(directory, "original.pdf")));
            Assert.Equal("deja__POTS_DECORATIFS.stl", File.ReadAllText(Path.Combine(directory, "personnalise.stl")));
            Assert.Equal(3, Directory.GetFiles(directory).Length);
            Assert.All(sources, source => Assert.True(File.Exists(source)));
            Assert.Contains("1 non traité", owner.FindControl<TextBlock>("ImportBatchSummary")!.Text);
            Assert.False(Field<bool>(owner, "_importBatchActive"));
        }
        finally { owner.Close(); }
    });

    [Fact]
    public Task Legacy_collection_import_preserves_existing_referenced_file_on_collision() => Run(async () =>
    {
        var directory = Directory.CreateDirectory(Path.Combine(_root, "02_MODELES_3D", "Pot Crâne", "STL")).FullName;
        var source = Path.Combine(_root, "piece.stl"); File.WriteAllText(source, "nouveau");
        var destination = Path.Combine(directory, "piece__POT_CRANE.stl"); File.WriteAllText(destination, "existant");
        var owner = await Open(); owner.Show();
        try
        {
            await OpenFileTestFolder(owner, directory);
            var pending = (Task)Invoke(owner, "ImportWorkspaceFilesAsync", _root, directory, new[] { source })!;
            await WaitForDialog<ImportNameWindow>(owner); DialogAction(owner, "Importer avec ce nom");
            await pending;
            Assert.Equal("existant", File.ReadAllText(destination));
            Assert.Equal("nouveau", File.ReadAllText(source));
            Assert.Contains("1 échec", owner.FindControl<TextBlock>("ImportBatchSummary")!.Text);
            Assert.Single(Directory.GetFiles(directory));
        }
        finally { owner.Close(); }
    });
}
