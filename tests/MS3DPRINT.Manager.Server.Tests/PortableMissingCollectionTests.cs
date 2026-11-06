using System.Security.Cryptography;
using System.Text.Json;
using MS3DPRINT.Manager.Core.Migration;
using MS3DPRINT.Manager.Core.Storage;
using MS3DPRINT.Manager.Core.Collections;
namespace MS3DPRINT.Manager.Server.Tests;
public sealed class PortableMissingCollectionTests
{
    [Fact] public void MissingCollectionRequiresExplicitValidatedAnnexAndPreservesOriginalBytes()
    {
        var root=Path.Combine(Path.GetTempPath(),"annex-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try {
            var workspace=Path.Combine(root,"pc"); Directory.CreateDirectory(Path.Combine(workspace,".ms3dprint-manager/collections"));
            var id=Guid.NewGuid(); var profile=new CollectionProfile(id,"02_MODELES_3D","02_MODELES_3D/Lost","Lost",null,null,null,null,null,null,null,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow);
            var bytes=JsonSerializer.SerializeToUtf8Bytes(profile); var file=Path.Combine(workspace,".ms3dprint-manager/collections",id+".json"); File.WriteAllBytes(file,bytes);
            Directory.CreateDirectory(Path.Combine(workspace,"02_MODELES_3D")); File.WriteAllText(Path.Combine(workspace,"02_MODELES_3D/keep.txt"),"keep"); var archive=Path.Combine(root,"source.zip"); var hash=new WorkspaceBackupService().Create(workspace,archive,true).ArchiveSha256; var service=new PortableWorkspaceMigrationService();
            Assert.False(service.Prepare(archive,Path.Combine(root,"blocked"),hash).Succeeded);
            Assert.False(service.Prepare(archive,Path.Combine(root,"unknown"),hash,new HashSet<Guid>{Guid.NewGuid()}).Succeeded);
            var result=service.Prepare(archive,Path.Combine(root,"prepared"),hash,new HashSet<Guid>{id});
            Assert.True(result.Succeeded,JsonSerializer.Serialize(result.Issues));
            Assert.False(File.Exists(Path.Combine(root,"prepared/workspace/.ms3dprint-manager/collections",id+".json")));
            Assert.Equal(bytes,File.ReadAllBytes(Path.Combine(root,"prepared/inactive-collections",id+".json")));
            using var manifest=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root,"prepared/inactive-collections/manifest.json")));
            var entry=Assert.Single(manifest.RootElement.EnumerateArray()); Assert.Equal(id,entry.GetProperty("Id").GetGuid());
            Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)),entry.GetProperty("Sha256").GetString());
            Assert.Equal(bytes,File.ReadAllBytes(file)); Assert.Equal(hash,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(archive))));
        } finally { Directory.Delete(root,true); }
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void ExistingFolderOrFileCannotBeHiddenInAnnex(bool isFile)
    {
        var root=Path.Combine(Path.GetTempPath(),"annex-existing-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var workspace=Path.Combine(root,"pc"); Directory.CreateDirectory(Path.Combine(workspace,".ms3dprint-manager/collections")); Directory.CreateDirectory(Path.Combine(workspace,"02_MODELES_3D"));
            var target=Path.Combine(workspace,"02_MODELES_3D/Present"); if(isFile) File.WriteAllText(target,"preserve"); else Directory.CreateDirectory(target);
            var id=Guid.NewGuid(); var profile=new CollectionProfile(id,"02_MODELES_3D","02_MODELES_3D/Present","Present",null,null,null,null,null,null,null,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow);
            var bytes=JsonSerializer.SerializeToUtf8Bytes(profile); var original=Path.Combine(workspace,".ms3dprint-manager/collections",id+".json"); File.WriteAllBytes(original,bytes);
            var archive=Path.Combine(root,"source.zip"); var hash=new WorkspaceBackupService().Create(workspace,archive,true).ArchiveSha256;
            var report=new PortableWorkspaceMigrationService().Prepare(archive,Path.Combine(root,"prepared"),hash,new HashSet<Guid>{id});
            Assert.False(report.Succeeded); Assert.False(Directory.Exists(Path.Combine(root,"prepared/inactive-collections"))); Assert.Equal(bytes,File.ReadAllBytes(original));
            Assert.True(File.Exists(Path.Combine(root,"prepared/workspace/.ms3dprint-manager/collections",id+".json")));
        }
        finally { Directory.Delete(root,true); }
    }
    [Fact] public void WrongArchiveHashNeverProducesPortableCandidate()
    {
        var root=Path.Combine(Path.GetTempPath(),"annex-corrupt-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var workspace=Path.Combine(root,"pc"); Directory.CreateDirectory(Path.Combine(workspace,"02_MODELES_3D")); File.WriteAllText(Path.Combine(workspace,"02_MODELES_3D/keep.txt"),"keep");
            var archive=Path.Combine(root,"source.zip"); var hash=new WorkspaceBackupService().Create(workspace,archive,true).ArchiveSha256;
            var report=new PortableWorkspaceMigrationService().Prepare(archive,Path.Combine(root,"prepared"),new string('0',64),new HashSet<Guid>());
            Assert.False(report.Succeeded); Assert.Null(report.PortableArchiveSha256); Assert.False(Directory.Exists(Path.Combine(root,"prepared/workspace")));
            Assert.Equal(hash,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(archive))));
        }
        finally { Directory.Delete(root,true); }
    }
}
