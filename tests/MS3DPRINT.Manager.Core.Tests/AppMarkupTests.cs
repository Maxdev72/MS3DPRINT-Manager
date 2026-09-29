using System.Xml.Linq;

namespace MS3DPRINT.Manager.Core.Tests;

public sealed class AppMarkupTests
{
    [Fact]
    public void MainWindow_UsesAResponsiveActionCardLayout()
    {
        var document = LoadMarkup("src", "MS3DPRINT.Manager.App", "MainWindow.xaml");

        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "ResponsiveCardPanel" &&
            (string?)element.Attribute("MaxColumns") == "4" &&
            (string?)element.Attribute("ItemHeightRatio") == "0.42");
        Assert.Contains(document.Descendants().Where(element => element.Name.LocalName == "Button"),
            element => (string?)element.Attribute("Style") == "{StaticResource ActionCardButton}" &&
                       element.Attribute("Height") is null &&
                       element.Attribute("Width") is null);
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
        Assert.Contains(comboBoxStyle.Descendants(), element =>
            element.Name.LocalName == "ContentPresenter" &&
            element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "SelectionPresenter") &&
            (string?)element.Attribute("TextElement.Foreground") == "{TemplateBinding Foreground}");
        Assert.Contains(comboBoxStyle.Descendants(), element =>
            element.Name.LocalName == "Trigger" &&
            (string?)element.Attribute("Property") == "IsEnabled" &&
            (string?)element.Attribute("Value") == "False");
        Assert.Contains(comboBoxStyle.Descendants(), element =>
            element.Name.LocalName == "Border" &&
            element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "DropDownToggleBorder"));
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

    [Fact]
    public void MainWindow_UsesAnAdaptiveNormalStartupSize()
    {
        var document = LoadMarkup("src", "MS3DPRINT.Manager.App", "MainWindow.xaml");

        Assert.NotEqual("Maximized", (string?)document.Root?.Attribute("WindowState"));

        var source = LoadSource("src", "MS3DPRINT.Manager.App", "MainWindow.xaml.cs");
        Assert.Contains("Width = Math.Min(1200, SystemParameters.WorkArea.Width * 0.84);", source);
        Assert.Contains("Height = Math.Min(850, SystemParameters.WorkArea.Height * 0.85);", source);
    }

    [Fact]
    public void MainWindow_PlacesStructureVerificationBesideTheStorageFolder()
    {
        var document = LoadMarkup("src", "MS3DPRINT.Manager.App", "MainWindow.xaml");

        var button = Assert.Single(document.Descendants().Where(element =>
            element.Name.LocalName == "Button" && (string?)element.Attribute("Click") == "VerifyStructure_Click"));

        Assert.Equal("{StaticResource SecondaryActionButton}", (string?)button.Attribute("Style"));
        Assert.Equal("Vérifier", (string?)button.Attribute("Content"));
        Assert.Equal("1", (string?)button.Attribute("Grid.Column"));
        Assert.Equal("Grid", button.Parent?.Name.LocalName);
    }

    [Fact]
    public void ClassifyFileWindow_ProvidesSourceProjectDestinationAndMoveControls()
    {
        var document = LoadMarkup("src", "MS3DPRINT.Manager.App", "Views", "ClassifyFileWindow.xaml");

        Assert.Contains(document.Descendants(), element => element.Name.LocalName == "Button" && (string?)element.Attribute("Click") == "Browse_Click");
        Assert.Contains(document.Descendants(), element => element.Name.LocalName == "ComboBox" && element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "ProjectBox"));
        Assert.Contains(document.Descendants(), element => element.Name.LocalName == "ComboBox" && element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "DestinationBox"));
        Assert.Contains(document.Descendants(), element => element.Name.LocalName == "Button" && (string?)element.Attribute("Content") == "Déplacer le fichier");
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

    private static string LoadSource(params string[] relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MS3DPRINT.Manager.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine([directory!.FullName, .. relativePath]));
    }
}
