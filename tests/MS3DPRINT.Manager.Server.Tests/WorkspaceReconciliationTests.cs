using MS3DPRINT.Manager.Core.Migration;
namespace MS3DPRINT.Manager.Server.Tests;
public sealed class WorkspaceReconciliationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "reconciliation-" + Guid.NewGuid().ToString("N"));
    private readonly WorkspaceReconciliationService service = new();
    public WorkspaceReconciliationTests() => Directory.CreateDirectory(root);
    private string Workspace(string name) { var p = Path.Combine(root, name); Directory.CreateDirectory(Path.Combine(p, "02_MODELES_3D")); return p; }
    private static void Write(string p, string file, string text) { var full = Path.Combine(p, "02_MODELES_3D", file); Directory.CreateDirectory(Path.GetDirectoryName(full)!); File.WriteAllText(full, text); }
    [Fact] public void DatesDoNotChangeContentComparison()
    {
        var pc = Workspace("pc"); var server = Workspace("server"); Write(pc,"a.txt","same"); Write(server,"a.txt","same");
        File.SetLastWriteTimeUtc(Path.Combine(pc,"02_MODELES_3D/a.txt"), DateTime.UtcNow.AddYears(-1));
        var a = service.Capture(pc); var b = service.Capture(server);
        Assert.Equal(a.Fingerprint,b.Fingerprint); Assert.Equal(WorkspaceDifferenceKind.Same,Assert.Single(service.Compare(a,b).Files).Kind);
    }
    [Fact] public void ExplicitChoicesPreserveSelectedBytesAndServerOnlyFiles()
    {
        var pc = Workspace("pc"); var server = Workspace("server"); Write(pc,"a.txt","pc"); Write(server,"a.txt","server"); Write(pc,"pc.txt","new"); Write(server,"server.txt","keep");
        var plan = service.Compare(service.Capture(pc),service.Capture(server));
        Assert.Throws<InvalidDataException>(()=>service.BuildCandidate(pc,server,Path.Combine(root,"missing"),plan,[]));
        var result = service.BuildCandidate(pc,server,Path.Combine(root,"candidate"),plan,[new("02_MODELES_3D/a.txt",WorkspaceConflictChoice.Pc)]);
        Assert.True(result.Succeeded, string.Join(";",result.Issues.Select(i=>i.Message)));
        Assert.Equal("pc", File.ReadAllText(Path.Combine(root,"candidate/02_MODELES_3D/a.txt")));
        Assert.True(File.Exists(Path.Combine(root,"candidate/02_MODELES_3D/server.txt")));
        Assert.True(File.Exists(Path.Combine(root,"candidate/02_MODELES_3D/pc.txt")));
        Assert.Equal("server", File.ReadAllText(Path.Combine(server,"02_MODELES_3D/a.txt")));
    }
    [Fact] public void ChangedSnapshotRefusesCandidate()
    {
        var pc=Workspace("pc"); var server=Workspace("server"); var plan=service.Compare(service.Capture(pc),service.Capture(server)); Write(server,"late.txt","late");
        Assert.Throws<InvalidDataException>(()=>service.BuildCandidate(pc,server,Path.Combine(root,"candidate"),plan,[]));
        Assert.False(Directory.Exists(Path.Combine(root,"candidate")));
    }
    [Fact] public void UntrustedSnapshotsAndChoicesCannotEscapeOrAlterReviewedPlan()
    {
        var pc=Workspace("pc"); var server=Workspace("server"); Write(pc,"a.txt","pc"); Write(server,"a.txt","server");
        var snapshot=service.Capture(pc); var target=service.Capture(server); var plan=service.Compare(snapshot,target);
        Assert.Throws<InvalidDataException>(()=>service.Compare(snapshot with { Fingerprint=new string('0',64) },target));
        Assert.Throws<InvalidDataException>(()=>service.Compare(snapshot with { Files=[snapshot.Files[0] with { Path="../escape" }] },target));
        Assert.Throws<InvalidDataException>(()=>service.Compare(snapshot with { Issues=[null!] },target));
        Assert.Throws<InvalidDataException>(()=>service.ReviewHash(plan with { PcFingerprint=null! },[new("02_MODELES_3D/a.txt",WorkspaceConflictChoice.Pc)]));
        Assert.Throws<InvalidDataException>(()=>service.ReviewHash(plan,[new("02_MODELES_3D/a.txt",(WorkspaceConflictChoice)99)]));
        Assert.Throws<InvalidDataException>(()=>service.ReviewHash(plan,[new("02_MODELES_3D/a.txt",WorkspaceConflictChoice.Pc),new("02_MODELES_3D/a.txt",WorkspaceConflictChoice.Server)]));
        Assert.Throws<InvalidDataException>(()=>service.ReviewHash(plan,[new("unknown",WorkspaceConflictChoice.Pc)]));
        Assert.Throws<InvalidDataException>(()=>service.BuildCandidate(pc,server,Path.Combine(pc,"nested"),plan,[new("02_MODELES_3D/a.txt",WorkspaceConflictChoice.Pc)]));
        Assert.Throws<InvalidDataException>(()=>service.BuildCandidate(pc,server,Path.Combine(root,"candidate"),plan with { Files=[] },[]));
    }
    [Theory] [InlineData("Folder","folder")] [InlineData("folder","folder/child")]
    public void CaseAndFileDirectoryUnionCollisionsAreRejected(string left,string right)
    {
        var pc=Workspace("pc"); var server=Workspace("server"); Write(pc,left,"pc"); Write(server,right,"server");
        Assert.Throws<InvalidDataException>(()=>service.Compare(service.Capture(pc),service.Capture(server)));
    }
    [Fact] public void InvalidProfileVersionsAreBlocking()
    {
        var pc=Workspace("pc"); var id=Guid.NewGuid(); Directory.CreateDirectory(Path.Combine(pc,"01_CLIENTS/Client"));
        Directory.CreateDirectory(Path.Combine(pc,".ms3dprint-manager/clients"));
        File.WriteAllText(Path.Combine(pc,".ms3dprint-manager/clients",id+".json"),"""
            {"Id":"ID","Kind":0,"FolderName":"Client","ClientCode":"C001","PrimaryContact":{},"CreatedAt":"0001-01-01T00:00:00+00:00","UpdatedAt":"0001-01-01T00:00:00+00:00"}
            """.Replace("ID",id.ToString()));
        Assert.Contains(service.Capture(pc).Issues,i=>i.Blocking && i.Code=="InvalidProfileVersion");
    }
    [Fact] public void ImportedProfilesRenewOnlyTechnicalVersionAndPreserveUnknownFieldsAndSources()
    {
        var pc=Workspace("pc"); var server=Workspace("server"); var id=Guid.NewGuid();
        var relative=".ms3dprint-manager/clients/"+id+".json";
        var original="""
            {"Id":"ID","Kind":0,"FolderName":"Client","ClientCode":"C001","PrimaryContact":{},"CreatedAt":"2020-01-01T00:00:00+00:00","UpdatedAt":"2021-01-01T00:00:00+00:00","CompanyName":"PC","Unknown":{"Tag":"Keep"}}
            """.Replace("ID",id.ToString());
        foreach(var workspace in new[]{pc,server}) { Directory.CreateDirectory(Path.Combine(workspace,"01_CLIENTS/Client")); Directory.CreateDirectory(Path.Combine(workspace,".ms3dprint-manager/clients")); }
        File.WriteAllText(Path.Combine(pc,relative),original);
        var serverOriginal=original.Replace("\"PC\"","\"Server\"").Replace("2021-01-01","2099-01-01"); File.WriteAllText(Path.Combine(server,relative),serverOriginal);
        var plan=service.Compare(service.Capture(pc),service.Capture(server));
        Assert.Equal("Client « PC »",Assert.Single(plan.Files).DisplayName);
        var result=service.BuildCandidate(pc,server,Path.Combine(root,"candidate"),plan,[new(relative,WorkspaceConflictChoice.Pc)]);
        Assert.True(result.Succeeded); var version=Assert.Single(result.VersionChanges);
        Assert.True(version.UpdatedAt>DateTimeOffset.Parse("2099-01-01T00:00:00Z"));
        var json=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(root,"candidate",relative)))!;
        Assert.Equal("Keep",json["Unknown"]!["Tag"]!.GetValue<string>()); Assert.Equal("PC",json["CompanyName"]!.GetValue<string>());
        Assert.Equal(DateTimeOffset.Parse("2020-01-01T00:00:00Z"),json["CreatedAt"]!.GetValue<DateTimeOffset>());
        Assert.Equal(original,File.ReadAllText(Path.Combine(pc,relative))); Assert.Equal(serverOriginal,File.ReadAllText(Path.Combine(server,relative)));
        var kept=service.BuildCandidate(pc,server,Path.Combine(root,"kept"),plan,[new(relative,WorkspaceConflictChoice.Server)]);
        Assert.Empty(kept.VersionChanges); Assert.Equal(serverOriginal,File.ReadAllText(Path.Combine(root,"kept",relative)));
    }
    [Fact] public void UnionIsAuditedForDuplicateCollectionFolders()
    {
        var pc=Workspace("pc"); var server=Workspace("server");
        foreach(var workspace in new[]{pc,server})
        {
            Directory.CreateDirectory(Path.Combine(workspace,"02_MODELES_3D/Collection")); Directory.CreateDirectory(Path.Combine(workspace,".ms3dprint-manager/collections"));
            var id=Guid.NewGuid(); var profile=new MS3DPRINT.Manager.Core.Collections.CollectionProfile(id,"02_MODELES_3D","02_MODELES_3D/Collection","Name",null,null,null,null,null,null,null,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow);
            File.WriteAllBytes(Path.Combine(workspace,".ms3dprint-manager/collections",id+".json"),System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(profile));
        }
        var plan=service.Compare(service.Capture(pc),service.Capture(server));
        var result=service.BuildCandidate(pc,server,Path.Combine(root,"candidate"),plan,[]);
        Assert.False(result.Succeeded); Assert.Contains(result.Issues,i=>i.Code=="InvalidCollection" && i.Blocking);
    }
    public void Dispose() => Directory.Delete(root,true);
}
