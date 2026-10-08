using System.Windows;
using System.Windows.Controls;
using MS3DPRINT.Manager.App.Views;
using MS3DPRINT.Manager.Core.Collections;

namespace MS3DPRINT.Manager.Core.Tests.Collections;

[Collection("Responsive layout UI")]
public sealed class CollectionEditorWindowTests
{
    [Fact]
    public void SaveProfile_RebasesOwnSavesAndRejectsExternalChangesWithoutLosingDraft()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "ms3d-collection-conflict-" + Guid.NewGuid().ToString("N"));
            try
            {
                var path = Directory.CreateDirectory(Path.Combine(root, "06_FOURNISSEURS", "ACME")).FullName;
                var now = DateTimeOffset.UtcNow;
                var profile = new CollectionProfile(Guid.NewGuid(), "06_FOURNISSEURS", "06_FOURNISSEURS/ACME", "Acme", null, null, null, null, null, null, null, now, now);
                var store = new CollectionProfileStore(root);
                store.Create(profile);
                var window = new CollectionEditorWindow(root, new("Acme", path, now), "06_FOURNISSEURS");
                try
                {
                    ((TextBox)window.FindName("NameBox")).Text = "Premier nom";
                    window.SaveProfile();
                    ((TextBox)window.FindName("NameBox")).Text = "Deuxième nom";
                    window.SaveProfile();
                    var latest = store.Load(profile.Id)!;
                    store.Update(latest with { Address = "Adresse externe", UpdatedAt = latest.UpdatedAt.AddSeconds(1) });
                    ((TextBox)window.FindName("NotesBox")).Text = "Brouillon local";
                    Assert.ThrowsAny<InvalidOperationException>(() => window.SaveProfile());
                    Assert.Equal("Adresse externe", store.Load(profile.Id)!.Address);
                    Assert.Equal("Brouillon local", ((TextBox)window.FindName("NotesBox")).Text);
                    Assert.Equal("Deuxième nom", store.Load(profile.Id)!.Name);
                }
                finally { window.Close(); }
            }
            catch (Exception error) { failure = error; }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        if (failure is not null) throw failure;
    }

    [Theory]
    [InlineData("06_FOURNISSEURS", Visibility.Visible)]
    [InlineData("02_MODELES_3D", Visibility.Collapsed)]
    [InlineData("03_PRODUITS_MS3DPRINT", Visibility.Collapsed)]
    public void Constructor_LoadsSavedFieldsAndShowsSupplierFieldsOnlyForSuppliers(string category, Visibility visibility)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "MS3DPRINT-collection-editor-" + Guid.NewGuid().ToString("N"));
            try
            {
                var path = Directory.CreateDirectory(Path.Combine(root, category, "LEGACY")).FullName;
                var profile = new CollectionProfile(Guid.NewGuid(), category, category + "/LEGACY", "Nom enregistré", "Description", "Contact", "Adresse", "0123", "email", "site", "Notes", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
                new CollectionProfileStore(root).Create(profile);
                var window = new CollectionEditorWindow(root, new CollectionItemSummary("LEGACY", path, DateTimeOffset.UtcNow), category);
                try
                {
                    Assert.Equal("Nom enregistré", ((TextBox)window.FindName("NameBox")).Text);
                    Assert.Equal("Description", ((TextBox)window.FindName("DescriptionBox")).Text);
                    Assert.Equal("Notes", ((TextBox)window.FindName("NotesBox")).Text);
                    Assert.Equal(visibility, ((StackPanel)window.FindName("SupplierPanel")).Visibility);
                    Assert.Null(window.SavedProfile);
                    ((TextBox)window.FindName("NameBox")).Text = "Nom modifié";
                    var saved = window.SaveProfile();
                    Assert.Equal(profile.Id, saved.Id);
                    Assert.Equal(profile.CreatedAt, saved.CreatedAt);
                    Assert.Equal("Nom modifié", new CollectionProfileStore(root).Load(profile.Id)!.Name);
                    Assert.True(Directory.Exists(path));
                }
                finally { window.Close(); }
            }
            catch (Exception exception) { failure = exception; }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        if (failure is not null) throw failure;
    }
}
