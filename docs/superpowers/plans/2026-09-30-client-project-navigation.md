# Clients and Projects Navigation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (\`- [ ]\`) syntax for tracking.

**Goal:** Transform MS3DPRINT Manager into a low-resource workspace application where clients, projects, statuses and project files are managed from the application.

**Architecture:** All business rules remain in MS3DPRINT.Manager.Core. A provider-neutral workspace root contains .ms3dprint-manager with one atomic JSON profile per client or project; physical folders remain authoritative for business documents. WPF adds a lazy navigation shell and focused Client/Project pages.

**Tech Stack:** .NET 8, C#, WPF, System.Text.Json, xUnit, local Windows file APIs; no cloud SDK, database, polling service or paid dependency.

**Spec:** docs/superpowers/specs/2026-09-30-client-project-navigation-design.md

## Global Constraints

- Keep net8.0-windows and the current dependency set.
- Work through a selected local workspace root only; cloud products are synchronization hosts, not APIs.
- Store shared management data under <workspace>/.ms3dprint-manager; leave themes in the local application settings location.
- Do not rename, replace or delete business folders/files implicitly. File moves keep explicit confirmation and refuse an existing destination.
- Write metadata only after explicit Save, using a unique temporary file, flush, history backup and File.Replace.
- Do not add timers, polling, recursive root scans or GPU work.
- Keep all visible text in French and bind controls to existing dynamic theme resources.

---

## File Structure

- Create src/MS3DPRINT.Manager.Core/Workspace/WorkspaceMetadataPaths.cs.
- Create src/MS3DPRINT.Manager.Core/Clients/{ClientKind,PrimaryContact,ClientProfile,ClientFolder,ClientSummary,ClientProfileStore,ClientCatalog,ClientCreationService}.cs.
- Create src/MS3DPRINT.Manager.Core/Projects/{ProjectStatus,ProjectProfile,ProjectFolder,ProjectSummary,ProjectProfileStore,ProjectCatalog}.cs.
- Create src/MS3DPRINT.Manager.Core/Files/{ProjectFileEntry,ProjectFileBrowser}.cs.
- Create client, project, workspace and file-browser tests under tests/MS3DPRINT.Manager.Core.Tests.
- Create src/MS3DPRINT.Manager.App/ViewModels/{NavigationSection,ClientsViewModel,ClientDetailViewModel,ProjectsViewModel,ProjectDetailViewModel}.cs.
- Create src/MS3DPRINT.Manager.App/Views/{ClientsView,ClientDetailView,ProjectsView,ProjectDetailView}.xaml and .xaml.cs.
- Modify MainWindow.xaml, MainWindow.xaml.cs, MainViewModel.cs, CreateClientWindow.*, CreateProjectWindow.*, CreateProjectViewModel.cs, ProjectCreationService.cs, UiErrorMessages.cs and AppMarkupTests.cs.

### Task 1: Add provider-neutral workspace metadata paths

**Files:**
- Create: src/MS3DPRINT.Manager.Core/Workspace/WorkspaceMetadataPaths.cs
- Test: tests/MS3DPRINT.Manager.Core.Tests/Workspace/WorkspaceMetadataPathsTests.cs

**Interfaces:** WorkspaceMetadataPaths(string rootPath) exposes RootPath, MetadataDirectory, ClientsDirectory, ProjectsDirectory, HistoryDirectory and EnsureMetadataDirectories().

- [ ] **Step 1: Write the failing tests**

~~~csharp
[Fact]
public void Constructor_UsesMetadataFolderBelowWorkspace()
{
    var paths = new WorkspaceMetadataPaths(_root);
    Assert.Equal(Path.Combine(_root, ".ms3dprint-manager"), paths.MetadataDirectory);
    Assert.Equal(Path.Combine(paths.MetadataDirectory, "clients"), paths.ClientsDirectory);
}

[Fact]
public void EnsureMetadataDirectories_DoesNotCreateBusinessFolders()
{
    new WorkspaceMetadataPaths(_root).EnsureMetadataDirectories();
    Assert.True(Directory.Exists(Path.Combine(_root, ".ms3dprint-manager", "projects")));
    Assert.False(Directory.Exists(Path.Combine(_root, "01_CLIENTS")));
}
~~~

- [ ] **Step 2: Run the test to verify it fails**

Run: dotnet test tests/MS3DPRINT.Manager.Core.Tests --filter FullyQualifiedName~WorkspaceMetadataPathsTests

Expected: FAIL because WorkspaceMetadataPaths is absent.

- [ ] **Step 3: Implement the path object**

~~~csharp
public sealed class WorkspaceMetadataPaths
{
    public WorkspaceMetadataPaths(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        RootPath = Path.GetFullPath(rootPath);
        MetadataDirectory = Path.Combine(RootPath, ".ms3dprint-manager");
        ClientsDirectory = Path.Combine(MetadataDirectory, "clients");
        ProjectsDirectory = Path.Combine(MetadataDirectory, "projects");
        HistoryDirectory = Path.Combine(MetadataDirectory, "history");
    }
    public string RootPath { get; }
    public string MetadataDirectory { get; }
    public string ClientsDirectory { get; }
    public string ProjectsDirectory { get; }
    public string HistoryDirectory { get; }
    public void EnsureMetadataDirectories()
    {
        Directory.CreateDirectory(ClientsDirectory);
        Directory.CreateDirectory(ProjectsDirectory);
        Directory.CreateDirectory(HistoryDirectory);
    }
}
~~~

- [ ] **Step 4: Re-run the focused test**

Run: dotnet test tests/MS3DPRINT.Manager.Core.Tests --filter FullyQualifiedName~WorkspaceMetadataPathsTests

Expected: PASS.

- [ ] **Step 5: Commit**

~~~powershell
git add src/MS3DPRINT.Manager.Core/Workspace/WorkspaceMetadataPaths.cs tests/MS3DPRINT.Manager.Core.Tests/Workspace/WorkspaceMetadataPathsTests.cs
git commit -m "feat: add workspace metadata paths"
~~~

### Task 2: Persist synchronized client profiles

**Files:**
- Create: src/MS3DPRINT.Manager.Core/Clients/ClientKind.cs, PrimaryContact.cs, ClientProfile.cs, ClientProfileStore.cs
- Test: tests/MS3DPRINT.Manager.Core.Tests/Clients/ClientProfileStoreTests.cs

**Interfaces:** ClientKind has Professional and Individual. ClientProfile is a record with Id, Kind, FolderName, ClientCode, CompanyName, FirstName, LastName, Address, Notes, PrimaryContact, CreatedAt and UpdatedAt. ClientProfileStore exposes LoadAll(), Create(ClientProfile) and Update(ClientProfile).

- [ ] **Step 1: Write the failing persistence tests**

~~~csharp
[Fact]
public void Create_PersistsOneProfileForANewStore()
{
    _store.Create(_professionalProfile);
    var profile = new ClientProfileStore(_paths).LoadAll().Single();
    Assert.Equal("MPO", profile.ClientCode);
}

[Fact]
public void Update_ArchivesPriorJsonBeforeReplacingIt()
{
    _store.Create(_professionalProfile);
    _store.Update(_professionalProfile with { Notes = "Relancer lundi" });
    Assert.Single(Directory.GetFiles(_paths.HistoryDirectory, "*.json"));
    Assert.Equal("Relancer lundi", _store.LoadAll().Single().Notes);
}
~~~

- [ ] **Step 2: Run the test to verify it fails**

Run: dotnet test tests/MS3DPRINT.Manager.Core.Tests --filter FullyQualifiedName~ClientProfileStoreTests

Expected: FAIL because client profile types are absent.

- [ ] **Step 3: Implement profiles and atomic JSON writes**

Require a company for Professional and a last name for Individual; normalize/validate FolderName and ClientCode with NameNormalizer. Reject duplicate ids and folder names on Create; reject unknown ids on Update. Serialize a Guid-suffixed .tmp file, flush it, copy the old JSON to history/<id>-<UTC ticks>.json, then use File.Replace. Let malformed JSON throw JsonException without changing it.

~~~csharp
public void Update(ClientProfile profile)
{
    var activePath = ProfilePath(profile.Id);
    var temporaryPath = activePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
    using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
    {
        JsonSerializer.Serialize(stream, profile, _jsonOptions);
        stream.Flush(flushToDisk: true);
    }
    File.Copy(activePath, HistoryPath(profile.Id), overwrite: false);
    File.Replace(temporaryPath, activePath, destinationBackupFileName: null);
}
~~~

- [ ] **Step 4: Re-run the focused test**

Run: dotnet test tests/MS3DPRINT.Manager.Core.Tests --filter FullyQualifiedName~ClientProfileStoreTests

Expected: PASS, including duplicate and malformed JSON tests added with Step 1.

- [ ] **Step 5: Commit**

~~~powershell
git add src/MS3DPRINT.Manager.Core/Clients tests/MS3DPRINT.Manager.Core.Tests/Clients
git commit -m "feat: persist synchronized client profiles"
~~~

### Task 3: Reconcile folders, legacy client codes and profiles

**Files:**
- Create: src/MS3DPRINT.Manager.Core/Clients/ClientFolder.cs, ClientSummary.cs, ClientCatalog.cs
- Modify: src/MS3DPRINT.Manager.Core/Storage/ClientCodeRegistry.cs
- Test: tests/MS3DPRINT.Manager.Core.Tests/Clients/ClientCatalogTests.cs

**Interfaces:** ClientCatalog.Load(string workspaceRoot) returns sorted ClientSummary values. A summary contains ClientPath, FolderName, DisplayName, ClientCode, ClientKind?, ClientProfile?, ProjectCount and IsProfileMissing.

- [ ] **Step 1: Write the failing catalog tests**

~~~csharp
[Fact]
public void Load_ShowsExistingFolderWithoutProfileAsNeedingCompletion()
{
    Directory.CreateDirectory(Path.Combine(_root, "01_CLIENTS", "DUPONT"));
    var client = _catalog.Load(_root).Single();
    Assert.True(client.IsProfileMissing);
    Assert.Equal("DUPONT", client.DisplayName);
}

[Fact]
public void Load_ExcludesSystemFoldersAndCountsOnlyProjectReferences()
{
    CreateDirectories("01_CLIENTS/MPO/00_CLIENT", "01_CLIENTS/MPO/99_ARCHIVES",
        "01_CLIENTS/MPO/MPO-2026-001_OUTILLAGE");
    Assert.Equal(1, _catalog.Load(_root).Single().ProjectCount);
}
~~~

- [ ] **Step 2: Run the test to verify it fails**

Run: dotnet test tests/MS3DPRINT.Manager.Core.Tests --filter FullyQualifiedName~ClientCatalogTests

Expected: FAIL because ClientCatalog is absent.

- [ ] **Step 3: Implement shallow reconciliation**

Enumerate only non-hidden direct children of 01_CLIENTS. Merge profiles by normalized folder name and do not create metadata while reading. Count only immediate folders matching ^[A-Z0-9]+-[0-9]{4}-[0-9]{3}(?:_|$). Read ClientCodeRegistry as a legacy fallback only when a profile does not exist; profiles are the synchronized source for new work.

- [ ] **Step 4: Re-run the focused test**

Run: dotnet test tests/MS3DPRINT.Manager.Core.Tests --filter FullyQualifiedName~ClientCatalogTests

Expected: PASS.

- [ ] **Step 5: Commit**

~~~powershell
git add src/MS3DPRINT.Manager.Core/Clients src/MS3DPRINT.Manager.Core/Storage/ClientCodeRegistry.cs tests/MS3DPRINT.Manager.Core.Tests/Clients/ClientCatalogTests.cs
git commit -m "feat: list reconciled workspace clients"
~~~

### Task 4: Create client folders and client profiles safely

**Files:**
- Create: src/MS3DPRINT.Manager.Core/Clients/ClientCreationService.cs
- Test: tests/MS3DPRINT.Manager.Core.Tests/Clients/ClientCreationServiceTests.cs

**Interfaces:** ClientCreationService(FolderTreeService, ClientProfileStore, ClientCodeRegistry).Create(ClientProfile draft) returns the stored ClientProfile.

- [ ] **Step 1: Write the failing workflow tests**

~~~csharp
[Fact]
public void Create_CreatesClientTreeProfileAndCode()
{
    var created = _service.Create(_draft);
    Assert.True(Directory.Exists(Path.Combine(_root, "01_CLIENTS", "MPO", "00_CLIENT")));
    Assert.Equal("MPO", _store.LoadAll().Single().ClientCode);
    Assert.Equal("MPO", _registry.GetCode("MPO"));
}

[Fact]
public void Create_RefusesExistingFolderWithoutChangingIt()
{
    Directory.CreateDirectory(Path.Combine(_root, "01_CLIENTS", "MPO"));
    Assert.Throws<FolderConflictException>(() => _service.Create(_draft));
}
~~~

- [ ] **Step 2: Run the test to verify it fails**

Run: dotnet test tests/MS3DPRINT.Manager.Core.Tests --filter FullyQualifiedName~ClientCreationServiceTests

Expected: FAIL because ClientCreationService is absent.

- [ ] **Step 3: Implement recoverable creation**

Validate first, call FolderTreeService.CreateTree, persist the profile, then register the code. If profile or code persistence fails after a tree was created, surface the created path and allow a retry of metadata only; never delete that tree automatically.

- [ ] **Step 4: Re-run the focused test**

Run: dotnet test tests/MS3DPRINT.Manager.Core.Tests --filter FullyQualifiedName~ClientCreationServiceTests

Expected: PASS.

- [ ] **Step 5: Commit**

~~~powershell
git add src/MS3DPRINT.Manager.Core/Clients/ClientCreationService.cs tests/MS3DPRINT.Manager.Core.Tests/Clients/ClientCreationServiceTests.cs
git commit -m "feat: create client profiles with folder trees"
~~~

### Task 5: Build client list, search and detail view models

**Files:**
- Create: src/MS3DPRINT.Manager.App/ViewModels/ClientsViewModel.cs, ClientDetailViewModel.cs, ClientKindOption.cs
- Test: tests/MS3DPRINT.Manager.Core.Tests/ViewModels/ClientsViewModelTests.cs, ClientDetailViewModelTests.cs

**Interfaces:** ClientsViewModel has Refresh(), SearchText, SelectedKind and VisibleClients. ClientDetailViewModel maps editable fields to ClientProfileStore.Update through explicit Save().

- [ ] **Step 1: Write failing filter and save tests**

~~~csharp
[Fact]
public void VisibleClients_MatchesCodeContactAndSelectedKind()
{
    var viewModel = CreateViewModelWithMpoAndJulien();
    viewModel.SearchText = "marie";
    viewModel.SelectedKind = ClientKind.Professional;
    Assert.Equal("MPO", Assert.Single(viewModel.VisibleClients).ClientCode);
}

[Fact]
public void Save_UpdatesOnlySelectedProfile()
{
    var viewModel = new ClientDetailViewModel(_profile, _store);
    viewModel.Notes = "Client prioritaire";
    viewModel.Save();
    Assert.Equal("Client prioritaire", _store.LoadAll().Single().Notes);
}
~~~

- [ ] **Step 2: Run the test to verify it fails**

Run: dotnet test tests/MS3DPRINT.Manager.Core.Tests --filter "FullyQualifiedName~ClientsViewModelTests|FullyQualifiedName~ClientDetailViewModelTests"

Expected: FAIL because the view models are absent.

- [ ] **Step 3: Implement cached search and explicit save**

Load a catalog list on Refresh only. Filter case-insensitively across code, identity, company, contact name, phone and email. Preserve typed values while changing Professional/Individual in the editor; Save validates in core, changes no folder name and emits a French success/error message.

- [ ] **Step 4: Re-run the focused test**

Run: dotnet test tests/MS3DPRINT.Manager.Core.Tests --filter "FullyQualifiedName~ClientsViewModelTests|FullyQualifiedName~ClientDetailViewModelTests"

Expected: PASS.

- [ ] **Step 5: Commit**

~~~powershell
git add src/MS3DPRINT.Manager.App/ViewModels tests/MS3DPRINT.Manager.Core.Tests/ViewModels
git commit -m "feat: add client list and detail view models"
~~~

### Task 6: Deliver the responsive navigation shell and Clients UI

**Files:**
- Create: src/MS3DPRINT.Manager.App/ViewModels/NavigationSection.cs, Views/ClientsView.xaml, Views/ClientsView.xaml.cs, Views/ClientDetailView.xaml, Views/ClientDetailView.xaml.cs
- Modify: src/MS3DPRINT.Manager.App/MainWindow.xaml, MainWindow.xaml.cs, ViewModels/MainViewModel.cs, Views/CreateClientWindow.*, ViewModels/CreateClientViewModel.cs, tests/MS3DPRINT.Manager.Core.Tests/AppMarkupTests.cs

**Interfaces:** NavigationSection contains Dashboard, Clients, Projects, Models, Products, Suppliers and Settings. MainViewModel.CurrentSection drives PageHost. ClientsView raises ClientSelected and CreateRequested; ClientDetailView raises BackRequested and CreateProjectRequested.

- [ ] **Step 1: Write failing markup/form tests**

~~~csharp
[Fact]
public void MainWindow_HostsNavigationAndPageContent()
{
    var document = LoadMarkup("src", "MS3DPRINT.Manager.App", "MainWindow.xaml");
    Assert.Contains(document.Descendants(), e => e.Name.LocalName == "ContentControl" &&
        (string?)e.Attribute("Name") == "PageHost");
    Assert.Contains(document.Descendants(), e => e.Name.LocalName == "Button" &&
        (string?)e.Attribute("Content") == "Clients");
}

[Fact]
public void ClientForm_ShowsKindAndPrimaryContact()
{
    var document = LoadMarkup("src", "MS3DPRINT.Manager.App", "Views", "CreateClientWindow.xaml");
    Assert.Contains(document.Descendants(), e => (string?)e.Attribute("Name") == "KindBox");
    Assert.Contains(document.Descendants(), e => (string?)e.Attribute("Name") == "ContactEmailBox");
}
~~~

- [ ] **Step 2: Run the test to verify it fails**

Run: dotnet test tests/MS3DPRINT.Manager.Core.Tests --filter "FullyQualifiedName~MainWindow_HostsNavigationAndPageContent|FullyQualifiedName~ClientForm_ShowsKindAndPrimaryContact"

Expected: FAIL because the host, navigation and fields are absent.

- [ ] **Step 3: Implement WPF navigation and client forms**

Keep adaptive non-maximized startup, dynamic theme resources and status bar. Add a compact left rail and PageHost; instantiate ClientsView only when selected. The create form adapts between business and individual fields, creates through ClientCreationService, then returns to a refreshed list instead of offering Explorer. Put Explorer only as a secondary detail-page action.

- [ ] **Step 4: Run markup tests and build**

Run: dotnet test tests/MS3DPRINT.Manager.Core.Tests --filter FullyQualifiedName~AppMarkupTests; dotnet build MS3DPRINT.Manager.sln -c Release --no-restore

Expected: PASS with zero errors.

- [ ] **Step 5: Commit**

~~~powershell
git add src/MS3DPRINT.Manager.App/MainWindow.xaml src/MS3DPRINT.Manager.App/MainWindow.xaml.cs src/MS3DPRINT.Manager.App/Views src/MS3DPRINT.Manager.App/ViewModels tests/MS3DPRINT.Manager.Core.Tests/AppMarkupTests.cs
git commit -m "feat: add client navigation and profiles"
~~~

### Task 7: Persist project profiles and create profile-aware projects

**Files:**
- Create: src/MS3DPRINT.Manager.Core/Projects/ProjectStatus.cs, ProjectProfile.cs, ProjectFolder.cs, ProjectSummary.cs, ProjectProfileStore.cs, ProjectCatalog.cs
- Modify: src/MS3DPRINT.Manager.Core/Projects/ProjectCreationService.cs, src/MS3DPRINT.Manager.App/Views/CreateProjectWindow.xaml.cs, ViewModels/CreateProjectViewModel.cs
- Test: tests/MS3DPRINT.Manager.Core.Tests/Projects/ProjectProfileStoreTests.cs, ProjectCatalogTests.cs, ProjectCreationServiceTests.cs

**Interfaces:** ProjectStatus has Quote, InProgress and Completed, displayed as DEVIS, EN_COURS and TERMINE. ProjectProfile contains client profile id/code, reference, folder name, description, notes, CreatedAt, DueDate and Status. ProjectCreationService.Create(ClientSummary client, int year, string projectName, DateOnly? dueDate, string? notes) returns (ProjectReference Reference, ProjectProfile Profile).

- [ ] **Step 1: Write failing profile, discovery and creation tests**

~~~csharp
[Fact]
public void Catalog_RecognizesReferenceFoldersButNotSystemFolders()
{
    CreateDirectories("01_CLIENTS/MPO/00_CLIENT", "01_CLIENTS/MPO/MPO-2026-001_OUTILLAGE");
    Assert.Equal("MPO-2026-001", _catalog.Load(_root).Single().Reference);
}

[Fact]
public void Create_CreatesQuoteProfileForSelectedClient()
{
    var result = _service.Create(_client, 2026, "Outillage", null, "Prototype");
    Assert.Equal(ProjectStatus.Quote, result.Profile.Status);
    Assert.True(Directory.Exists(Path.Combine(_client.ClientPath, result.Reference.FolderName)));
}
~~~

- [ ] **Step 2: Run the test to verify it fails**

Run: dotnet test tests/MS3DPRINT.Manager.Core.Tests --filter "FullyQualifiedName~ProjectProfileStoreTests|FullyQualifiedName~ProjectCatalogTests|FullyQualifiedName~ProjectCreationServiceTests"

Expected: FAIL because project profiles and profile-aware creation are absent.

- [ ] **Step 3: Implement profiles, catalog and creation integration**

Mirror client JSON atomic behavior in metadata/projects. Parse only immediate project folders matching ^[A-Z0-9]+-[0-9]{4}-[0-9]{3}(?:_|$); missing profiles stay visible without a status. Use the synchronized client profile code first, then ClientCodeRegistry only for legacy clients. Preserve the current per-client mutex and project-number conflict protection. If metadata fails after tree creation, report the tree and permit metadata retry without deleting it.

- [ ] **Step 4: Re-run the focused test**

Run: dotnet test tests/MS3DPRINT.Manager.Core.Tests --filter "FullyQualifiedName~ProjectProfileStoreTests|FullyQualifiedName~ProjectCatalogTests|FullyQualifiedName~ProjectCreationServiceTests"

Expected: PASS.

- [ ] **Step 5: Commit**

~~~powershell
git add src/MS3DPRINT.Manager.Core/Projects src/MS3DPRINT.Manager.App/Views/CreateProjectWindow.xaml.cs src/MS3DPRINT.Manager.App/ViewModels/CreateProjectViewModel.cs tests/MS3DPRINT.Manager.Core.Tests/Projects
git commit -m "feat: create synchronized project profiles"
~~~

### Task 8: Deliver Projects list, filters and details

**Files:**
- Create: src/MS3DPRINT.Manager.App/ViewModels/ProjectsViewModel.cs, ProjectDetailViewModel.cs, Views/ProjectsView.xaml, Views/ProjectsView.xaml.cs, Views/ProjectDetailView.xaml, Views/ProjectDetailView.xaml.cs
- Modify: src/MS3DPRINT.Manager.App/MainWindow.xaml.cs, Views/ClientDetailView.*, tests/MS3DPRINT.Manager.Core.Tests/AppMarkupTests.cs
- Test: tests/MS3DPRINT.Manager.Core.Tests/ViewModels/ProjectsViewModelTests.cs, ProjectDetailViewModelTests.cs

**Interfaces:** ProjectsViewModel has Refresh(), SearchText, SelectedStatus, SelectedClient, SelectedYear and VisibleProjects. ProjectDetailViewModel.Save() updates only status, due date, description and notes. Project pages open from the side rail or a Client detail.

- [ ] **Step 1: Write failing filter and markup tests**

~~~csharp
[Fact]
public void VisibleProjects_CombinesTextStatusClientAndYearFilters()
{
    var viewModel = CreateProjectsViewModel();
    viewModel.SearchText = "outillage";
    viewModel.SelectedStatus = ProjectStatus.Quote;
    viewModel.SelectedYear = 2026;
    Assert.Equal("MPO-2026-001", Assert.Single(viewModel.VisibleProjects).Reference);
}

[Fact]
public void ProjectsView_ContainsSearchAndTrackingFilters()
{
    var document = LoadMarkup("src", "MS3DPRINT.Manager.App", "Views", "ProjectsView.xaml");
    Assert.Contains(document.Descendants(), e => (string?)e.Attribute("Name") == "StatusFilterBox");
    Assert.Contains(document.Descendants(), e => (string?)e.Attribute("Name") == "YearFilterBox");
}
~~~

- [ ] **Step 2: Run the test to verify it fails**

Run: dotnet test tests/MS3DPRINT.Manager.Core.Tests --filter "FullyQualifiedName~ProjectsViewModelTests|FullyQualifiedName~ProjectsView_ContainsSearchAndTrackingFilters"

Expected: FAIL because ProjectsView and its view model are absent.

- [ ] **Step 3: Implement filters and responsive project UI**

Cache catalog data on Refresh and apply concrete filters conjunctively. Show profile-missing projects only under all statuses. Use accessible status badges in light/dark themes, normal vertical scrolling at small heights and asynchronous directory reads with dispatcher continuation. The direct Explorer action is secondary.

- [ ] **Step 4: Re-run tests and build**

Run: dotnet test tests/MS3DPRINT.Manager.Core.Tests --filter "FullyQualifiedName~ProjectsViewModelTests|FullyQualifiedName~ProjectDetailViewModelTests|FullyQualifiedName~AppMarkupTests"; dotnet build MS3DPRINT.Manager.sln -c Release --no-restore

Expected: PASS with zero errors.

- [ ] **Step 5: Commit**

~~~powershell
git add src/MS3DPRINT.Manager.App/Views src/MS3DPRINT.Manager.App/ViewModels src/MS3DPRINT.Manager.App/MainWindow.xaml.cs tests/MS3DPRINT.Manager.Core.Tests
git commit -m "feat: manage projects and statuses"
~~~

### Task 9: Browse project files on demand, verify and publish

**Files:**
- Create: src/MS3DPRINT.Manager.Core/Files/ProjectFileEntry.cs, ProjectFileBrowser.cs
- Modify: src/MS3DPRINT.Manager.App/ViewModels/ProjectDetailViewModel.cs, Views/ProjectDetailView.*, UiErrorMessages.cs
- Test: tests/MS3DPRINT.Manager.Core.Tests/Files/ProjectFileBrowserTests.cs, tests/MS3DPRINT.Manager.Core.Tests/AppMarkupTests.cs
- Output: outputs/MS3DPRINT-Manager/MS3DPRINT.Manager.App.exe

**Interfaces:** ProjectFileBrowser.List(string projectRoot, string currentDirectory) returns direct child ProjectFileEntry(Name, FullPath, IsDirectory, Length, LastWriteTime). It rejects a current directory outside projectRoot. ProjectDetailViewModel opens a file using ExplorerService and launches ClassifyFileWindow with the project preselected.

- [ ] **Step 1: Write failing file-browser tests**

~~~csharp
[Fact]
public void List_ReturnsImmediateEntriesDirectoriesFirst()
{
    CreateDirectories("project/03_CAO_3D", "project/03_CAO_3D/01_MASTER");
    File.WriteAllText(Path.Combine(_root, "project", "brief.pdf"), "x");
    var entries = _browser.List(Path.Combine(_root, "project"), Path.Combine(_root, "project"));
    Assert.Equal(new[] { "03_CAO_3D", "brief.pdf" }, entries.Select(x => x.Name));
}

[Fact]
public void List_RejectsDirectoryOutsideProject()
{
    Assert.Throws<UnauthorizedAccessException>(() => _browser.List(_projectRoot, _outsideRoot));
}
~~~

- [ ] **Step 2: Run the test to verify it fails**

Run: dotnet test tests/MS3DPRINT.Manager.Core.Tests --filter FullyQualifiedName~ProjectFileBrowserTests

Expected: FAIL because ProjectFileBrowser is absent.

- [ ] **Step 3: Implement secure lazy browsing and UI actions**

Resolve both paths with Path.GetFullPath and verify the current directory begins with the project root plus separator. Never recurse. List folders before files, show a parent action only within the project, open files through ExplorerService, and preserve the current list on an access/sync error while displaying a French inline error. Keep file classification confirmation-based.

- [ ] **Step 4: Run full verification and publish**

Run: dotnet test MS3DPRINT.Manager.sln -c Release --no-restore; dotnet build MS3DPRINT.Manager.sln -c Release --no-restore; dotnet restore src/MS3DPRINT.Manager.App/MS3DPRINT.Manager.App.csproj -r win-x64; dotnet publish src/MS3DPRINT.Manager.App/MS3DPRINT.Manager.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true --no-restore -o outputs/MS3DPRINT-Manager

Expected: all tests PASS, build has zero errors, and outputs/MS3DPRINT-Manager/MS3DPRINT.Manager.App.exe exists.

- [ ] **Step 5: Smoke-test and install only with the app closed**

Create a professional and an individual in a temporary workspace; reopen the app; create/filter a project through all three statuses; browse a folder; verify a duplicate file destination remains blocked. Test normal and narrow windows plus light/dark/automatic themes, an access-denied root and a missing root. Before copying to C:\MS3DPRINT\Nextcloud\MS3DPRINT\MS3DPRINT.Manager.App.exe, confirm Get-Process MS3DPRINT.Manager.App returns nothing. Then compare source/installed SHA-256 values with Get-FileHash.

- [ ] **Step 6: Commit**

~~~powershell
git add src/MS3DPRINT.Manager.Core/Files src/MS3DPRINT.Manager.App/ViewModels/ProjectDetailViewModel.cs src/MS3DPRINT.Manager.App/Views/ProjectDetailView.xaml src/MS3DPRINT.Manager.App/Views/ProjectDetailView.xaml.cs src/MS3DPRINT.Manager.App/UiErrorMessages.cs tests/MS3DPRINT.Manager.Core.Tests
git commit -m "feat: browse project files in the manager"
~~~
