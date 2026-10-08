using System.Diagnostics;
using System.Text;
using MS3DPRINT.Manager.Core.Files;

namespace MS3DPRINT.Manager.Core.Tests.Files;

public sealed class PathLinkSafetyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ms3d-links-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("destination")]
    [InlineData("project")]
    [InlineData("source")]
    public void Move_RejectsJunctionsAndPreservesSource(string location)
    {
        var project = Directory.CreateDirectory(Path.Combine(_root, "project")).FullName;
        var external = Directory.CreateDirectory(Path.Combine(_root, "external")).FullName;
        var input = Directory.CreateDirectory(Path.Combine(_root, "input")).FullName;
        var source = Path.Combine(input, "source.txt");
        File.WriteAllText(source, "source");
        string link;
        if (location == "project")
        {
            link = Path.Combine(_root, "linked-project"); Junction(link, project); project = link;
            Directory.CreateDirectory(Path.Combine(project, "documents"));
        }
        else if (location == "source")
        {
            link = Path.Combine(_root, "linked-input"); Junction(link, input); source = Path.Combine(link, "source.txt");
            Directory.CreateDirectory(Path.Combine(project, "documents"));
        }
        else { link = Path.Combine(project, "documents"); Junction(link, external); }
        try
        {
            Assert.ThrowsAny<IOException>(() => new ProjectFileTransferService().Move(source, project, new(ProjectFileCategory.ClientFiles, "documents", "target.txt")));
            Assert.Equal("source", File.ReadAllText(source));
            Assert.False(File.Exists(Path.Combine(project, "documents", "target.txt")));
        }
        finally { Directory.Delete(link, false); }
    }

    [Fact]
    public void List_DoesNotExposeLinkedEntriesAndRefusesTraversal()
    {
        var project = Directory.CreateDirectory(Path.Combine(_root, "project")).FullName;
        var external = Directory.CreateDirectory(Path.Combine(_root, "external")).FullName;
        File.WriteAllText(Path.Combine(external, "secret.txt"), "external");
        var link = Path.Combine(project, "linked"); Junction(link, external);
        try
        {
            Assert.DoesNotContain(new ProjectFileBrowser().List(project, project), entry => entry.Name == "linked");
            Assert.ThrowsAny<IOException>(() => new ProjectFileBrowser().List(project, link));
            Assert.ThrowsAny<IOException>(() => new ProjectFileBrowser().GetParentDirectory(project, link));
        }
        finally { Directory.Delete(link, false); }
    }

    private static void Junction(string path, string target)
    {
        static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
        var script = "$ErrorActionPreference='Stop'; New-Item -ItemType Junction -Path " + Quote(path) + " -Target " + Quote(target) + " | Out-Null";
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEnd(); process.WaitForExit(); Assert.True(process.ExitCode == 0, error);
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
