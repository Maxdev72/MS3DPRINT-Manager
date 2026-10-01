using System.Windows;
using System.Windows.Controls;
using MS3DPRINT.Manager.App.Views;
using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Storage;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Tests.Clients;

[CollectionDefinition("Client dialog UI", DisableParallelization = true)]
public sealed class ClientDialogUiCollection { }

[Collection("Client dialog UI")]
public sealed class CreateClientWindowSmokeTests
{
    [Fact]
    public void IndividualSelection_HidesCompanyAndDuplicateContactControls()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var root = Path.Combine(Path.GetTempPath(), "ms3dprint-dialog-" + Guid.NewGuid().ToString("N"));
                var metadata = new WorkspaceMetadataPaths(root);
                var window = new CreateClientWindow(root, new FolderTreeService(), new ClientCodeRegistry(Path.Combine(root, "data")), new ClientProfileStore(metadata));
                try
                {
                    ((ComboBox)window.FindName("KindBox")).SelectedIndex = 1;
                    Assert.Equal(Visibility.Collapsed, ((StackPanel)window.FindName("ProfessionalPanel")).Visibility);
                    Assert.Equal(Visibility.Collapsed, ((StackPanel)window.FindName("ContactIdentityPanel")).Visibility);
                    Assert.Equal(Visibility.Visible, ((StackPanel)window.FindName("IndividualPanel")).Visibility);
                }
                finally { window.Close(); }
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        Assert.Null(failure);
    }
}
