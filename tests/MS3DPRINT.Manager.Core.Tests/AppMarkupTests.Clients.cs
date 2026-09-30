using System.Xml.Linq;

namespace MS3DPRINT.Manager.Core.Tests;

public sealed class ClientsViewMarkupTests
{
    [Fact]
    public void ClientsView_ProvidesSearchTypeFilterAndClientList()
    {
        var root = FindRoot();
        var document = XDocument.Load(Path.Combine(root, "src", "MS3DPRINT.Manager.App", "Views", "ClientsView.xaml"));

        Assert.Contains(document.Descendants(), element => element.Name.LocalName == "TextBox" && HasName(element, "SearchBox"));
        Assert.Contains(document.Descendants(), element => element.Name.LocalName == "ComboBox" && HasName(element, "KindFilterBox"));
        Assert.Contains(document.Descendants(), element => element.Name.LocalName == "ListView" && HasName(element, "ClientsList"));
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MS3DPRINT.Manager.sln"))) directory = directory.Parent;
        return Assert.IsType<string>(directory?.FullName);
    }

    private static bool HasName(XElement element, string name)
        => element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == name);
}
