using System.Xml.Linq;

namespace MS3DPRINT.Manager.Core.Tests;

public sealed class AppMarkupTests
{
    [Fact]
    public void MainWindow_UsesATwoColumnActionCardGrid()
    {
        var document = LoadMarkup("src", "MS3DPRINT.Manager.App", "MainWindow.xaml");

        Assert.Contains(document.Descendants().Where(element => element.Name.LocalName == "UniformGrid"),
            element => (string?)element.Attribute("Columns") == "2");
    }

    [Fact]
    public void NamedItemDialog_KeepsItsContentScrollable()
    {
        var document = LoadMarkup("src", "MS3DPRINT.Manager.App", "Views", "CreateNamedItemWindow.xaml");

        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "ScrollViewer" &&
            (string?)element.Attribute("VerticalScrollBarVisibility") == "Auto");
    }

    private static XDocument LoadMarkup(params string[] relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MS3DPRINT.Manager.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return XDocument.Load(Path.Combine([directory!.FullName, .. relativePath]));
    }
}
