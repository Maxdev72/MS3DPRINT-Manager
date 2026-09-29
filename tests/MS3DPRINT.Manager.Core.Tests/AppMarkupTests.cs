using System.Xml.Linq;

namespace MS3DPRINT.Manager.Core.Tests;

public sealed class AppMarkupTests
{
    [Fact]
    public void MainWindow_UsesAnAdaptiveWrappingActionCardLayout()
    {
        var document = LoadMarkup("src", "MS3DPRINT.Manager.App", "MainWindow.xaml");

        Assert.Contains(document.Descendants(), element => element.Name.LocalName == "WrapPanel");
        Assert.Contains(document.Descendants().Where(element => element.Name.LocalName == "Button"),
            element => (string?)element.Attribute("Style") == "{StaticResource ActionCardButton}" &&
                       element.Attribute("Height") is null);
    }

    [Fact]
    public void NamedItemDialog_KeepsItsContentScrollable()
    {
        var document = LoadMarkup("src", "MS3DPRINT.Manager.App", "Views", "CreateNamedItemWindow.xaml");

        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "ScrollViewer" &&
            (string?)element.Attribute("VerticalScrollBarVisibility") == "Auto");
    }

    [Fact]
    public void ApplicationStyles_KeepComboBoxesReadableInTheDarkTheme()
    {
        var document = LoadMarkup("src", "MS3DPRINT.Manager.App", "App.xaml");

        var styles = document.Descendants().Where(element => element.Name.LocalName == "Style").ToList();
        var comboBoxStyle = Assert.Single(styles.Where(element => (string?)element.Attribute("TargetType") == "ComboBox"));
        var comboBoxItemStyle = Assert.Single(styles.Where(element => (string?)element.Attribute("TargetType") == "ComboBoxItem"));

        Assert.Contains(comboBoxStyle.Elements(), element =>
            (string?)element.Attribute("Property") == "Background" &&
            (string?)element.Attribute("Value") == "{DynamicResource InputBrush}");
        Assert.Contains(comboBoxStyle.Elements(), element =>
            (string?)element.Attribute("Property") == "Foreground" &&
            (string?)element.Attribute("Value") == "{DynamicResource TextBrush}");
        Assert.Contains(comboBoxItemStyle.Elements(), element =>
            (string?)element.Attribute("Property") == "Background" &&
            (string?)element.Attribute("Value") == "{DynamicResource SurfaceBrush}");
        Assert.Contains(comboBoxItemStyle.Elements(), element =>
            (string?)element.Attribute("Property") == "Foreground" &&
            (string?)element.Attribute("Value") == "{DynamicResource TextBrush}");
    }

    [Fact]
    public void MainWindow_UsesThemeResourcesForHeaderAndStatusBar()
    {
        var document = LoadMarkup("src", "MS3DPRINT.Manager.App", "MainWindow.xaml");
        var borders = document.Descendants().Where(element => element.Name.LocalName == "Border").ToList();

        Assert.Contains(borders, element =>
            (string?)element.Attribute("Background") == "{DynamicResource HeaderBrush}");
        Assert.Contains(borders, element =>
            (string?)element.Attribute("Background") == "{DynamicResource SurfaceBrush}" &&
            (string?)element.Attribute("BorderBrush") == "{DynamicResource BorderBrush}");
    }

    [Fact]
    public void ActionCards_UseAControlledHoverTemplate()
    {
        var document = LoadMarkup("src", "MS3DPRINT.Manager.App", "MainWindow.xaml");
        var actionCardStyle = document.Descendants()
            .Single(element => element.Name.LocalName == "Style" &&
                               element.Attributes().Any(attribute => attribute.Name.LocalName == "Key" && attribute.Value == "ActionCardButton"));

        Assert.Contains(actionCardStyle.Descendants(), element => element.Name.LocalName == "ControlTemplate");
        Assert.Contains(actionCardStyle.Descendants(), element =>
            element.Name.LocalName == "Setter" &&
            (string?)element.Attribute("TargetName") == "CardBorder" &&
            (string?)element.Attribute("Value") == "{DynamicResource CardHoverBrush}");
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
