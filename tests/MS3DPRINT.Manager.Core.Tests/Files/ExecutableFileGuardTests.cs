using MS3DPRINT.Manager.Core.Files;

namespace MS3DPRINT.Manager.Core.Tests.Files;

public sealed class ExecutableFileGuardTests
{
    [Theory]
    [InlineData(@"C:\clients\a\setup.exe")]
    [InlineData("script.PS1")]
    [InlineData("lien.lnk")]
    [InlineData("macro.vbs")]
    [InlineData("setup.exe. ")]
    public void Programs_and_scripts_are_recognised(string path) => Assert.True(ExecutableFileGuard.IsExecutable(path));

    [Theory]
    [InlineData("piece.stl")]
    [InlineData("devis.pdf")]
    [InlineData("notes.txt")]
    [InlineData("sans-extension")]
    [InlineData("archive.exe.txt")]
    public void Ordinary_documents_are_not_flagged(string path) => Assert.False(ExecutableFileGuard.IsExecutable(path));
}
