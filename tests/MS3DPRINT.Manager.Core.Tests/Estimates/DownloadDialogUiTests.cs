using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MS3DPRINT.Manager.Core.ModelSearch;
using MS3DPRINT.Manager.Desktop;
using MS3DPRINT.Manager.Desktop.ModelSearch;

namespace MS3DPRINT.Manager.Core.Tests.Estimates;

public sealed partial class EstimateUiTests
{
    [Fact]
    public Task Download_actions_reenable_after_a_new_file_arrives_during_the_open_delay() => Run(async () =>
    {
        var owner=await Open();owner.Show();
        var dialog=new DownloadDialog(()=>Task.FromResult<string?>(null));
        // A disappeared file takes the no-launch path, retaining the normal three-second delay.
        dialog.Add([new(Path.Combine(_root,"disparu.stl"),10)]);dialog.Show(owner);
        try
        {
            var opening=(Task)typeof(DownloadDialog).GetMethod("OpenAsync",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(dialog,null)!;
            var file=Path.Combine(_root,"nouveau.stl");File.WriteAllText(file,"solid new\nendsolid");
            dialog.Add([new(file,new FileInfo(file).Length)]);
            var store=(Button)typeof(DownloadDialog).GetField("_store",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(dialog)!;
            var openWith=(Button)typeof(DownloadDialog).GetField("_openWith",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(dialog)!;
            Assert.False(store.IsEnabled);Assert.False(openWith.IsEnabled);
            await opening;
            Assert.True(store.IsEnabled);Assert.True(openWith.IsEnabled);
        }
        finally{dialog.Close(false);owner.Close();}
    });

    [Fact]
    public Task Download_picker_failure_restores_the_offer_without_moving_files() => Run(async () =>
    {
        var file=Path.Combine(_root,"modele.stl");File.WriteAllText(file,"solid first\nendsolid");
        var owner=await Open();owner.Show();
        var dialog=new DownloadDialog(()=>Task.FromException<string?>(new IOException("Dossier indisponible")));
        dialog.Add([new(file,new FileInfo(file).Length)]);dialog.Show(owner);
        try
        {
            await (Task)typeof(DownloadDialog).GetMethod("StoreAsync",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(dialog,null)!;
            Dispatcher.UIThread.RunJobs();dialog.UpdateLayout();
            Assert.True(dialog.IsVisible);Assert.True(File.Exists(file));
            Assert.Contains(dialog.GetVisualDescendants().OfType<TextBlock>(),t=>t.Text?.Contains("Dossier indisponible")==true);
        }
        finally{dialog.Close(false);owner.Close();}
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Download_window_yields_to_folder_picker_and_restores_selection(bool accept) => Run(async () =>
    {
        var source=Path.Combine(_root,"tampon");var destination=Path.Combine(_root,"destination");
        Directory.CreateDirectory(source);Directory.CreateDirectory(destination);
        var first=Path.Combine(source,"premier.stl");var second=Path.Combine(source,"second.stl");
        File.WriteAllText(first,"solid first\nendsolid");File.WriteAllText(second,"solid second\nendsolid");
        var owner=await Open();owner.Show();
        var dialog=new DownloadDialog(()=>(Task<string?>)Invoke(owner,"PickWorkspaceFolderAsync",destination,"Ranger les modèles reçus",null)!);
        dialog.Add([new(first,new FileInfo(first).Length),new(second,new FileInfo(second).Length)]);
        dialog.Show(owner);Dispatcher.UIThread.RunJobs();dialog.UpdateLayout();
        try
        {
            dialog.GetVisualDescendants().OfType<CheckBox>().Last().IsChecked=false;
            var storing=(Task)typeof(DownloadDialog).GetMethod("StoreAsync",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(dialog,null)!;
            await WaitForDialog<FolderPickerWindow>(owner);
            Assert.False(dialog.IsVisible);
            var third=Path.Combine(source,"troisieme.stl");File.WriteAllText(third,"solid third\nendsolid");
            dialog.Add([new(third,new FileInfo(third).Length)]);
            Assert.False(dialog.IsVisible);
            DialogAction(owner,accept?"Déplacer ici":"Annuler");
            await storing;Dispatcher.UIThread.RunJobs();dialog.UpdateLayout();
            Assert.True(dialog.IsVisible);
            Assert.True(File.Exists(second));Assert.True(File.Exists(third));
            Assert.Equal(accept,File.Exists(Path.Combine(destination,"premier.stl")));
            Assert.Equal(!accept,File.Exists(first));
            Assert.Equal(accept?2:3,dialog.Paths.Count);
            Assert.False(dialog.GetVisualDescendants().OfType<CheckBox>().First(c=>c.IsChecked==false).IsChecked);
        }
        finally{dialog.Close(false);CloseInvoiceTestPanel(owner);owner.Close();}
    });
}
