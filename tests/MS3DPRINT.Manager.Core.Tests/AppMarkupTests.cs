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
    public void ApplicationStyles_KeepDatePickersAndCalendarsReadableInTheDarkTheme()
    {
        var document = LoadMarkup("src", "MS3DPRINT.Manager.App", "App.xaml");
        var styles = document.Descendants().Where(element => element.Name.LocalName == "Style").ToList();

        var datePickerStyle = Assert.Single(styles.Where(element => (string?)element.Attribute("TargetType") == "DatePicker"));
        var calendarStyle = Assert.Single(styles.Where(element => (string?)element.Attribute("TargetType") == "Calendar"));
        var dayButtonStyle = Assert.Single(styles.Where(element => (string?)element.Attribute("TargetType") == "CalendarDayButton"));

        Assert.Contains(datePickerStyle.Elements(), element =>
            (string?)element.Attribute("Property") == "Foreground" &&
            (string?)element.Attribute("Value") == "{DynamicResource TextBrush}");
        Assert.Contains(datePickerStyle.Elements(), element =>
            (string?)element.Attribute("Property") == "Background" &&
            (string?)element.Attribute("Value") == "{DynamicResource InputBrush}");
        Assert.Contains(calendarStyle.Elements(), element =>
            (string?)element.Attribute("Property") == "Background" &&
            (string?)element.Attribute("Value") == "{DynamicResource SurfaceBrush}");
        Assert.Contains(dayButtonStyle.Descendants(), element =>
            element.Name.LocalName == "Trigger" &&
            (string?)element.Attribute("Property") == "IsSelected" &&
            (string?)element.Attribute("Value") == "True");
    }

    [Fact]
    public void ApplicationStyles_UseDiscreetThemeAwareScrollBars()
    {
        var document = LoadMarkup("src", "MS3DPRINT.Manager.App", "App.xaml");
        var scrollBarStyle = Assert.Single(document.Descendants().Where(element =>
            element.Name.LocalName == "Style" &&
            (string?)element.Attribute("TargetType") == "ScrollBar"));

        Assert.Contains(scrollBarStyle.Elements(), element =>
            (string?)element.Attribute("Property") == "Width" &&
            (string?)element.Attribute("Value") == "10");
        Assert.Contains(scrollBarStyle.Descendants(), element =>
            element.Name.LocalName == "Track" &&
            element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "PART_Track"));
        Assert.Contains(scrollBarStyle.Descendants(), element =>
            element.Name.LocalName == "Thumb" &&
            element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "ScrollThumb"));
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
    public void MainWindow_HostsPersistentNavigationAndClientsPage()
    {
        var document = LoadMarkup("src", "MS3DPRINT.Manager.App", "MainWindow.xaml");

        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "ContentControl" &&
            element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "PageHost"));
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "Button" && (string?)element.Attribute("Content") == "Clients");
    }

    [Fact]
    public void TrackedProjectDialog_UsesAProfiledClientAndProjectFields()
    {
        var document = LoadMarkup("src", "MS3DPRINT.Manager.App", "Views", "CreateTrackedProjectWindow.xaml");

        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "ComboBox" &&
            element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "ClientBox"));
        Assert.Contains(document.Descendants(), element => element.Name.LocalName == "DatePicker");
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "TextBox" &&
            element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "ProjectNameBox"));
    }

    [Fact]
    public void ProjectsView_ProvidesClientAndYearFiltersAndSelection()
    {
        var document = LoadMarkup("src", "MS3DPRINT.Manager.App", "Views", "ProjectsView.xaml");

        Assert.Contains(document.Descendants(), element => element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "ClientFilterBox"));
        Assert.Contains(document.Descendants(), element => element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "YearFilterBox"));
        Assert.Contains(document.Descendants(), element => element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "ProjectsList"));
    }

    [Fact]
    public void CatalogViews_ExposeAnInPageLoadErrorArea()
    {
        var clients = LoadMarkup("src", "MS3DPRINT.Manager.App", "Views", "ClientsView.xaml");
        var projects = LoadMarkup("src", "MS3DPRINT.Manager.App", "Views", "ProjectsView.xaml");

        foreach (var document in new[] { clients, projects })
        {
            Assert.Contains(document.Descendants(), element =>
                element.Name.LocalName == "TextBlock" &&
                element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "LoadErrorText"));
        }
    }

    [Fact]
    public void CatalogViews_ShowLoadingStateAndRefreshOutsideTheUiThread()
    {
        foreach (var fileName in new[] { "ClientsView", "ProjectsView" })
        {
            var document = LoadMarkup("src", "MS3DPRINT.Manager.App", "Views", fileName + ".xaml");
            Assert.Contains(document.Descendants(), element =>
                element.Name.LocalName == "TextBlock" &&
                element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "LoadingText"));

            var source = LoadSource("src", "MS3DPRINT.Manager.App", "Views", fileName + ".xaml.cs");
            Assert.Contains("Task.Run(_viewModel.LoadCatalog)", source);
        }
    }

    [Fact]
    public void ProjectDetailView_ProvidesOnDemandFileNavigation()
    {
        var document = LoadMarkup("src", "MS3DPRINT.Manager.App", "Views", "ProjectDetailView.xaml");

        Assert.Contains(document.Descendants(), element => element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "FilesList"));
        Assert.Contains(document.Descendants(), element => element.Name.LocalName == "Button" && (string?)element.Attribute("Content") == "Remonter");
        Assert.Contains(document.Descendants(), element => element.Name.LocalName == "Button" && (string?)element.Attribute("Content") == "Classer un fichier…");
    }

    [Fact]
    public void FilterViews_AssignTheirViewModelsBeforeLoadingXamlEvents()
    {
        foreach (var fileName in new[] { "ClientsView.xaml.cs", "ProjectsView.xaml.cs" })
        {
            var source = LoadSource("src", "MS3DPRINT.Manager.App", "Views", fileName);
            Assert.True(source.IndexOf("_viewModel = viewModel", StringComparison.Ordinal) < source.IndexOf("InitializeComponent();", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void ClassifyFileWindow_UsesOneWayBindingForItsGeneratedFileName()
    {
        var document = LoadMarkup("src", "MS3DPRINT.Manager.App", "Views", "ClassifyFileWindow.xaml");
        var fileNameBox = Assert.Single(document.Descendants().Where(element =>
            element.Name.LocalName == "TextBox" &&
            ((string?)element.Attribute("Text") ?? string.Empty).Contains("FinalFileName")));

        Assert.Contains("Mode=OneWay", (string?)fileNameBox.Attribute("Text"));
    }

    [Fact]
    public void ApplicationStyles_PreserveContrastForDisabledButtonsAndSelectedComboItems()
    {
        var document = LoadMarkup("src", "MS3DPRINT.Manager.App", "App.xaml");
        var buttonStyle = document.Descendants().Single(element => element.Name.LocalName == "Style" && (string?)element.Attribute("TargetType") == "Button");
        Assert.Contains(buttonStyle.Descendants(), element =>
            element.Name.LocalName == "Trigger" &&
            (string?)element.Attribute("Property") == "IsEnabled" &&
            (string?)element.Attribute("Value") == "False");

        var comboItemStyle = document.Descendants().Single(element => element.Name.LocalName == "Style" && (string?)element.Attribute("TargetType") == "ComboBoxItem");
        Assert.Contains(comboItemStyle.Descendants(), element => element.Name.LocalName == "MultiTrigger");
    }

    [Fact]
    public void ClientAndProjectLists_UseReadableBusinessLabels()
    {
        var clients = LoadMarkup("src", "MS3DPRINT.Manager.App", "Views", "ClientsView.xaml");
        var projects = LoadMarkup("src", "MS3DPRINT.Manager.App", "Views", "ProjectsView.xaml");

        Assert.DoesNotContain(clients.Descendants(), element => ((string?)element.Attribute("Text") ?? string.Empty).Contains("IsProfileMissing"));
        Assert.Contains(clients.Descendants(), element => ((string?)element.Attribute("Text") ?? string.Empty).Contains("ProfileLabel"));
        Assert.Contains(projects.Descendants(), element => ((string?)element.Attribute("Text") ?? string.Empty).Contains("StatusLabel"));
        Assert.Contains(clients.Descendants(), element =>
            element.Name.LocalName == "Setter" &&
            (string?)element.Attribute("Property") == "HorizontalContentAlignment" &&
            (string?)element.Attribute("Value") == "Stretch");
    }

    [Fact]
    public void MainWindow_OpensTheCompletionFormForExistingClientsWithoutProfiles()
    {
        var source = LoadSource("src", "MS3DPRINT.Manager.App", "MainWindow.xaml.cs");

        Assert.Contains("new CreateClientWindow(_viewModel.StorageRoot, _folders, _codes, _clientProfiles, client)", source);
    }

    [Fact]
    public void MainWindow_OpensTheCompletionFormForExistingProjectsWithoutProfiles()
    {
        var source = LoadSource("src", "MS3DPRINT.Manager.App", "MainWindow.xaml.cs");

        Assert.Contains("new CreateTrackedProjectWindow(_viewModel.StorageRoot, _folders, _clientCatalog, _projectProfiles, project)", source);
    }

    [Fact]
    public void ClientDetailView_ProvidesAProjectCreationAction()
    {
        var document = LoadMarkup("src", "MS3DPRINT.Manager.App", "Views", "ClientDetailView.xaml");
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "Button" &&
            (string?)element.Attribute("Content") == "Nouveau projet" &&
            (string?)element.Attribute("Click") == "CreateProject_Click");

        var source = LoadSource("src", "MS3DPRINT.Manager.App", "MainWindow.xaml.cs");
        Assert.Contains("preselectedClient: client", source);
    }

    [Fact]
    public void Dashboard_ProvidesBusinessCountersLoadedOutsideTheUiThread()
    {
        var document = LoadMarkup("src", "MS3DPRINT.Manager.App", "MainWindow.xaml");
        foreach (var name in new[] { "DashboardClientsCount", "DashboardProjectsCount", "DashboardQuotesCount", "DashboardInProgressCount", "DashboardCompletedCount" })
        {
            Assert.Contains(document.Descendants(), element =>
                element.Name.LocalName == "TextBlock" &&
                element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == name));
        }

        var source = LoadSource("src", "MS3DPRINT.Manager.App", "MainWindow.xaml.cs");
        Assert.Contains("Task.Run(LoadDashboardSnapshot)", source);
    }

    [Fact]
    public void ClientDetailView_ListsAndOpensItsTrackedProjectsWithoutBlockingTheUi()
    {
        var document = LoadMarkup("src", "MS3DPRINT.Manager.App", "Views", "ClientDetailView.xaml");
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "ListBox" &&
            element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "ProjectsList"));
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "TextBlock" &&
            element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "ProjectsLoadingText"));
        var projectItemStyle = Assert.Single(document.Descendants().Where(element =>
            element.Name.LocalName == "Style" && (string?)element.Attribute("TargetType") == "ListBoxItem"));
        Assert.Contains(projectItemStyle.Elements(), element =>
            element.Name.LocalName == "Setter" &&
            (string?)element.Attribute("Property") == "Foreground" &&
            (string?)element.Attribute("Value") == "{DynamicResource TextBrush}");

        var source = LoadSource("src", "MS3DPRINT.Manager.App", "Views", "ClientDetailView.xaml.cs");
        Assert.Contains("Task.Run(_viewModel.LoadProjects)", source);
        Assert.Contains("ProjectSelected", source);
    }

    [Fact]
    public void MainWindow_ActivatesTheThreePlannedFolderCatalogs()
    {
        var document = LoadMarkup("src", "MS3DPRINT.Manager.App", "MainWindow.xaml");
        foreach (var content in new[] { "Modèles 3D", "Produits", "Fournisseurs" })
        {
            var button = Assert.Single(document.Descendants().Where(element =>
                element.Name.LocalName == "Button" && (string?)element.Attribute("Content") == content));
            Assert.NotEqual("False", (string?)button.Attribute("IsEnabled"));
            Assert.NotNull(button.Attribute("Click"));
        }

        var view = LoadMarkup("src", "MS3DPRINT.Manager.App", "Views", "CollectionView.xaml");
        Assert.Contains(view.Descendants(), element => element.Name.LocalName == "ListView" &&
            element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "ItemsList") &&
            (string?)element.Attribute("SelectionChanged") == "Item_SelectionChanged");

        var detail = LoadMarkup("src", "MS3DPRINT.Manager.App", "Views", "CollectionDetailView.xaml");
        Assert.Contains(detail.Descendants(), element => element.Name.LocalName == "ListView" &&
            element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "FilesList"));
    }

    [Fact]
    public void ClientForm_ConnectsItsTypeSelectorOnlyAfterTheNamedPanelsExist()
    {
        var document = LoadMarkup("src", "MS3DPRINT.Manager.App", "Views", "CreateClientWindow.xaml");
        var selector = Assert.Single(document.Descendants().Where(element =>
            element.Name.LocalName == "ComboBox" &&
            element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "KindBox")));

        Assert.Null(selector.Attribute("SelectionChanged"));

        var source = LoadSource("src", "MS3DPRINT.Manager.App", "Views", "CreateClientWindow.xaml.cs");
        Assert.True(source.IndexOf("InitializeComponent();", StringComparison.Ordinal) < source.IndexOf("KindBox.SelectionChanged +=", StringComparison.Ordinal));
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

    [Fact]
    public void SettingsWindow_ShowsAvailableAndPlannedStorageProviders()
    {
        var document = LoadMarkup("src", "MS3DPRINT.Manager.App", "Views", "SettingsWindow.xaml");

        Assert.Contains(document.Descendants(), element => element.Name.LocalName == "TextBlock" && (string?)element.Attribute("Text") == "Stockages");
        Assert.Contains(document.Descendants(), element => element.Name.LocalName == "ItemsControl" && element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "StorageProviders"));
        Assert.Contains(document.Descendants(), element => element.Name.LocalName == "ScrollViewer" && (string?)element.Attribute("VerticalScrollBarVisibility") == "Auto");
        Assert.Contains(document.Descendants(), element => element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "NextcloudLogo"));
        Assert.Contains(document.Descendants(), element => element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "GoogleDriveLogo"));
        Assert.Contains(document.Descendants(), element => element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "DropboxLogo"));
    }

    [Fact]
    public void SettingsWindow_ShowsReadOnlyPerformanceDiagnostics()
    {
        var document = LoadMarkup("src", "MS3DPRINT.Manager.App", "Views", "SettingsWindow.xaml");

        Assert.Contains(document.Descendants(), element => element.Name.LocalName == "TextBlock" && (string?)element.Attribute("Text") == "Performances");
        Assert.Contains(document.Descendants(), element => element.Name.LocalName == "ItemsControl" && element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "GraphicsAdapters"));
        Assert.Contains(document.Descendants(), element => element.Name.LocalName == "TextBlock" && ((string?)element.Attribute("Text") ?? string.Empty).Contains("GPU"));
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
