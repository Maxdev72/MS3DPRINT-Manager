using System.Xml.Linq;

namespace MS3DPRINT.Manager.Core.Tests;

public sealed class AppMarkupTests
{
    [Theory]
    [InlineData("CreateClientWindow.xaml")]
    [InlineData("CreateTrackedProjectWindow.xaml")]
    public void CreationDialogs_UseShortOpeningFade(string fileName)
    {
        var document = LoadMarkup("src", "MS3DPRINT.Manager.App", "Views", fileName);
        Assert.Contains(document.Descendants(), element => element.Name.LocalName == "DoubleAnimation" && (string?)element.Attribute("To") == "1");
    }

    [Fact]
    public void MainWindow_PrioritizesClientAndProjectCreation()
    {
        var document = LoadMarkup("src", "MS3DPRINT.Manager.App", "MainWindow.xaml");

        var actions = Assert.Single(document.Descendants().Where(element =>
            element.Name.LocalName == "WrapPanel" && element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "PrimaryDashboardActions")));
        Assert.Contains(actions.Elements(), element => element.Name.LocalName == "Button" && (string?)element.Attribute("Content") == "Nouveau client");
        Assert.Contains(actions.Elements(), element => element.Name.LocalName == "Button" && (string?)element.Attribute("Content") == "Nouveau projet");
        Assert.Contains(document.Descendants(), element => element.Name.LocalName == "TextBlock" && (string?)element.Attribute("Text") == "Projets récents");
        Assert.Contains(document.Descendants(), element => element.Name.LocalName == "TextBlock" && (string?)element.Attribute("Text") == "À surveiller");
    }

    [Fact]
    public void Dashboard_KeepsCardsCompactInAWideWindow()
    {
        var document = LoadMarkup("src", "MS3DPRINT.Manager.App", "MainWindow.xaml");
        var dashboard = Assert.Single(document.Descendants().Where(element =>
            element.Name.LocalName == "ScrollViewer" &&
            element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "DashboardPage")));
        var content = Assert.Single(dashboard.Elements().Where(element => element.Name.LocalName == "StackPanel"));

        Assert.Equal("Center", (string?)content.Attribute("HorizontalAlignment"));
        Assert.InRange(double.Parse(Assert.IsType<string>((string?)content.Attribute("MaxWidth"))), 1100, 1250);

        var panels = content.Descendants().Where(element => element.Name.LocalName == "ResponsiveCardPanel").ToArray();
        Assert.Equal(2, panels.Length);
        Assert.All(panels, panel => Assert.Equal("0", (string?)panel.Attribute("ItemHeightRatio")));
        Assert.InRange(double.Parse(Assert.IsType<string>((string?)panels[0].Attribute("MinimumItemHeight"))), 80, 100);
        Assert.InRange(double.Parse(Assert.IsType<string>((string?)panels[1].Attribute("MinimumItemHeight"))), 130, 160);
    }

    [Fact]
    public void MainWindow_CentersEveryPageWithinTheDashboardWidth()
    {
        var document = LoadMarkup("src", "MS3DPRINT.Manager.App", "MainWindow.xaml");
        var pageHost = Assert.Single(document.Descendants().Where(element =>
            element.Name.LocalName == "ContentControl" &&
            element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "PageHost")));

        Assert.Equal("Center", (string?)pageHost.Attribute("HorizontalAlignment"));
        Assert.Equal("1200", (string?)pageHost.Attribute("MaxWidth"));
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
        Assert.Contains("WindowLaunchSize.ForMainWindow", source);
        Assert.Contains("WindowState = WindowState.Normal", source);
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
    public void ProjectDetailView_UsesCompactLeftAlignedTabs()
    {
        var document = LoadMarkup("src", "MS3DPRINT.Manager.App", "Views", "ProjectDetailView.xaml");
        foreach (var header in new[] { "Fichiers", "Informations" })
        {
            var tab = Assert.Single(document.Descendants().Where(element => element.Name.LocalName == "TabItem" && (string?)element.Attribute("Header") == header));
            Assert.Equal("150", (string?)tab.Attribute("Width"));
        }
    }

    [Fact]
    public void ClientDetailView_UsesCompactLeftAlignedTabs()
    {
        var document = LoadMarkup("src", "MS3DPRINT.Manager.App", "Views", "ClientDetailView.xaml");
        foreach (var header in new[] { "Fichiers", "Informations", "Projets" })
        {
            var tab = Assert.Single(document.Descendants().Where(element => element.Name.LocalName == "TabItem" && (string?)element.Attribute("Header") == header));
            Assert.Equal("150", (string?)tab.Attribute("Width"));
        }
    }

    [Theory]
    [InlineData("CollectionDetailView.xaml")]
    [InlineData("LegacyEntityDetailView.xaml")]
    public void RemainingDetailViews_UseCompactLeftAlignedTabs(string fileName)
    {
        var document = LoadMarkup("src", "MS3DPRINT.Manager.App", "Views", fileName);
        foreach (var header in new[] { "Fichiers", "Informations" })
        {
            var tab = Assert.Single(document.Descendants().Where(element => element.Name.LocalName == "TabItem" && (string?)element.Attribute("Header") == header));
            Assert.Equal("150", (string?)tab.Attribute("Width"));
        }
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
        var buttonStyle = document.Descendants().Single(element => element.Name.LocalName == "Style" && (string?)element.Attribute("TargetType") == "Button" && !element.Attributes().Any(attribute => attribute.Name.LocalName == "Key"));
        Assert.Contains(buttonStyle.Descendants(), element =>
            element.Name.LocalName == "Trigger" &&
            (string?)element.Attribute("Property") == "IsEnabled" &&
            (string?)element.Attribute("Value") == "False");

        var comboItemStyle = document.Descendants().Single(element => element.Name.LocalName == "Style" && (string?)element.Attribute("TargetType") == "ComboBoxItem");
        Assert.Contains(comboItemStyle.Descendants(), element => element.Name.LocalName == "MultiTrigger");
    }


    [Fact]
    public void LegacyDetails_OfferExplicitCompletionWithoutRequiringItForFiles()
    {
        var document = LoadMarkup("src", "MS3DPRINT.Manager.App", "Views", "LegacyEntityDetailView.xaml");
        Assert.Contains(document.Descendants(), element => element.Name.LocalName == "Button" && (string?)element.Attribute("Click") == "Complete_Click");
        Assert.Contains(document.Descendants(), element => element.Name.LocalName == "ContentControl" && element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "FilesHost"));
    }

    [Fact]
    public void LegacyDetails_ExplainWhyRenamingAndMovingRequireCompletion()
    {
        var document = LoadMarkup("src", "MS3DPRINT.Manager.App", "Views", "LegacyEntityDetailView.xaml");
        foreach (var content in new[] { "Renommer…", "Déplacer…" })
        {
            var button = Assert.Single(document.Descendants().Where(element => element.Name.LocalName == "Button" && (string?)element.Attribute("Content") == content));
            Assert.Equal("False", (string?)button.Attribute("IsEnabled"));
            Assert.Contains("Complétez", (string?)button.Attribute("ToolTip"));
        }
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
        Assert.Contains("ShowProjectDialog(client)", source);
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
            element.Name.LocalName == "ListView" &&
            element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "ProjectsList"));
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "TextBlock" &&
            element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "ProjectsLoadingText"));
        foreach (var header in new[] { "Référence", "Projet", "Statut" })
            Assert.Contains(document.Descendants(), element => element.Name.LocalName == "GridViewColumn" && (string?)element.Attribute("Header") == header);
        Assert.Contains(document.Descendants(), element => element.Name.LocalName == "Button" && (string?)element.Attribute("Click") == "OpenProject_Click");

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
    public void FileDetailViews_ProvideAThreeDimensionalPreviewAction()
    {
        foreach (var fileName in new[] { "ProjectDetailView.xaml", "CollectionDetailView.xaml" })
        {
            var document = LoadMarkup("src", "MS3DPRINT.Manager.App", "Views", fileName);
            Assert.Contains(document.Descendants(), element =>
                element.Name.LocalName == "Button" &&
                element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "Preview3DButton") &&
                (string?)element.Attribute("Click") == "Preview3D_Click");
        }

        var viewer = LoadMarkup("src", "MS3DPRINT.Manager.App", "Views", "ModelPreviewWindow.xaml");
        Assert.Contains(viewer.Descendants(), element => element.Name.LocalName == "HelixViewport3D");
    }

    [Fact]
    public void GpuViewer_ProvidesBoundedViewportAndDisplayControls()
    {
        var viewer = LoadMarkup("src", "MS3DPRINT.Manager.App", "Views", "GpuModelPreviewWindow.xaml");
        Assert.Contains(viewer.Descendants(), element => element.Name.LocalName == "Viewport3DX");
        Assert.Contains(viewer.Descendants(), element => element.Name.LocalName == "ComboBox" &&
            element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "RenderModeBox"));
        Assert.Contains(viewer.Descendants(), element => element.Name.LocalName == "TextBox" &&
            element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "ColorHexBox"));
    }

    [Fact]
    public void MainWindow_OffersTheThreeDimensionalViewerOutsideProjects()
    {
        var document = LoadMarkup("src", "MS3DPRINT.Manager.App", "MainWindow.xaml");
        var entryPoints = document.Descendants().Where(element =>
            element.Name.LocalName == "Button" &&
            (string?)element.Attribute("Click") == "Open3DViewer_Click").ToArray();

        Assert.Equal(2, entryPoints.Length);
        Assert.Contains(entryPoints, element => (string?)element.Attribute("Content") == "Visualiseur 3D");
        Assert.Contains(entryPoints, element => element.Descendants().Any(child =>
            child.Name.LocalName == "TextBlock" && (string?)child.Attribute("Text") == "Visualiser un fichier 3D"));
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
