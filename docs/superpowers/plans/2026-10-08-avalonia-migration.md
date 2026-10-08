# Avalonia Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver a .NET 10 Avalonia Windows application that preserves all MS3DPRINT Manager functions and data while replacing the WPF interface with the validated dark immersive console.

**Architecture:** Keep file-system business rules in `MS3DPRINT.Manager.Core`; create a cross-platform Avalonia UI library in `MS3DPRINT.Manager.Desktop`; isolate Windows-only actions and hardware discovery in `MS3DPRINT.Manager.Windows`; and start the Windows release from `MS3DPRINT.Manager.Windows.App`. Keep the WPF application operational until the Avalonia application passes the complete parity and packaging checklist.

**Tech Stack:** .NET 10, Avalonia, xUnit, Avalonia.Controls.PdfViewer/PDFium, Ab4d.SharpEngine.AvaloniaUI, Ab4d.SharpEngine.Assimp, Windows self-contained `win-x64` publishing.

**Spec:** `docs/superpowers/specs/2026-10-08-avalonia-migration-design.md`

## Global Constraints

- Core targets exactly `net10.0` and has no WPF, Avalonia or Windows dependency.
- Windows is the only release platform in this migration; the shared UI project targets `net10.0`.
- The application has one dark theme only; no light-theme setting is exposed.
- Existing `data`, `.ms3dprint-manager`, managed folders and user documents are read and written without conversion.
- Every destructive operation keeps the current confirmation and no-overwrite guarantees.
- All current Windows functions, including image, PDF, STL and OBJ previews, are present before replacing the WPF delivery.
- The delivered file is the sole executable `sources/MS3DPRINT Manager.exe`; published and delivered SHA256 values must match.
- Do not force-close a running application during delivery.

---

## File Structure

| Path | Responsibility |
| --- | --- |
| `src/MS3DPRINT.Manager.Core` | Existing platform-neutral data, filesystem and business services, retargeted to .NET 10. |
| `src/MS3DPRINT.Manager.Desktop` | Avalonia application, dark design system, navigation, view models and file-preview abstractions. |
| `src/MS3DPRINT.Manager.Windows` | Windows implementations for shell opening and hardware information. |
| `src/MS3DPRINT.Manager.Windows.App` | Windows executable that composes the shared Avalonia library and Windows adapters. |
| `tests/MS3DPRINT.Manager.Desktop.Tests` | xUnit tests for view models, theme resources and navigation contracts. |
| `tests/MS3DPRINT.Manager.Windows.Tests` | xUnit tests for Windows adapters and their safe failure modes. |
| `tests/MS3DPRINT.Manager.Core.Tests` | Existing Core regression suite retargeted to .NET 10. |
| `src/MS3DPRINT.Manager.Desktop/Styles` | Semantic dark-theme tokens and common control styles. |
| `src/MS3DPRINT.Manager.Desktop/Services` | UI-level adapters for shell actions, notifications and previews. |
| `src/MS3DPRINT.Manager.Desktop/Views` | Avalonia screens and dialogs. |
| `src/MS3DPRINT.Manager.Desktop/ViewModels` | Testable UI state and commands. |

### Task 1: Retarget the solution and create Avalonia project boundaries

**Files:**
- Modify: `MS3DPRINT.Manager.sln`
- Modify: `src/MS3DPRINT.Manager.Core/MS3DPRINT.Manager.Core.csproj`
- Modify: `tests/MS3DPRINT.Manager.Core.Tests/MS3DPRINT.Manager.Core.Tests.csproj`
- Create: `src/MS3DPRINT.Manager.Desktop/MS3DPRINT.Manager.Desktop.csproj`
- Create: `src/MS3DPRINT.Manager.Desktop/DesktopApplication.cs`
- Create: `src/MS3DPRINT.Manager.Desktop/App.axaml`
- Create: `src/MS3DPRINT.Manager.Desktop/App.axaml.cs`
- Create: `src/MS3DPRINT.Manager.Desktop/MainWindow.axaml`
- Create: `src/MS3DPRINT.Manager.Desktop/MainWindow.axaml.cs`
- Create: `src/MS3DPRINT.Manager.Windows/MS3DPRINT.Manager.Windows.csproj`
- Create: `src/MS3DPRINT.Manager.Windows.App/MS3DPRINT.Manager.Windows.App.csproj`
- Create: `src/MS3DPRINT.Manager.Windows.App/Program.cs`
- Create: `tests/MS3DPRINT.Manager.Desktop.Tests/MS3DPRINT.Manager.Desktop.Tests.csproj`
- Create: `tests/MS3DPRINT.Manager.Windows.Tests/MS3DPRINT.Manager.Windows.Tests.csproj`

**Interfaces:**
- Consumes: `MS3DPRINT.Manager.Core` public services.
- Produces: `MS3DPRINT.Manager.Desktop`, a reusable Avalonia UI assembly, `MS3DPRINT.Manager.Windows`, a Windows-only adapter assembly, and `MS3DPRINT.Manager.Windows.App`, the runnable Windows application.

- [ ] **Step 1: Write failing project-boundary tests**

Create `tests/MS3DPRINT.Manager.Desktop.Tests/ProjectBoundaryTests.cs`:

```csharp
[Fact]
public void DesktopProject_targets_net10_without_windows_suffix()
{
    var project = File.ReadAllText(ProjectFile.Find("src/MS3DPRINT.Manager.Desktop/MS3DPRINT.Manager.Desktop.csproj"));
    Assert.Contains("<TargetFramework>net10.0</TargetFramework>", project);
    Assert.DoesNotContain("UseWPF", project);
}

[Fact]
public void CoreProject_has_no_ui_package_reference()
{
    var project = File.ReadAllText(ProjectFile.Find("src/MS3DPRINT.Manager.Core/MS3DPRINT.Manager.Core.csproj"));
    Assert.DoesNotContain("Avalonia", project);
    Assert.DoesNotContain("Wpf", project, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 2: Run the boundary tests to verify they fail**

Run: `dotnet test tests/MS3DPRINT.Manager.Desktop.Tests --filter FullyQualifiedName~ProjectBoundaryTests`

Expected: FAIL because the Desktop project does not exist.

- [ ] **Step 3: Create the projects and retarget Core**

Use these project properties:

```xml
<!-- MS3DPRINT.Manager.Desktop.csproj -->
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
  <ApplicationIcon>Assets\app-icon.ico</ApplicationIcon>
</PropertyGroup>
<ItemGroup>
  <ProjectReference Include="..\MS3DPRINT.Manager.Core\MS3DPRINT.Manager.Core.csproj" />
  <PackageReference Include="Avalonia.Desktop" Version="12.*" />
</ItemGroup>
```

Target Core with `net10.0`; target the existing WPF application, its tests, Windows adapters and the Windows host with `net10.0-windows`. Add all projects to the solution and pin SDK `10.0.401` with `global.json`.

- [ ] **Step 4: Implement the minimum Avalonia startup**

`DesktopApplication.BuildAvaloniaApp()` configures the common `App`. `MS3DPRINT.Manager.Windows.App.Program.Main` calls `BuildAvaloniaApp().StartWithClassicDesktopLifetime(args)`. `App` loads `MainWindow` when the desktop lifetime starts. `MainWindow` displays the text `MS3DPRINT Manager` only; styling follows Task 2.

- [ ] **Step 5: Run build and tests**

Run: `dotnet test MS3DPRINT.Manager.sln`

Expected: PASS; the WPF project, Core and empty Avalonia shell build on the installed .NET 10 SDK.

- [ ] **Step 6: Commit**

```powershell
git add global.json MS3DPRINT.Manager.sln src/MS3DPRINT.Manager.Core src/MS3DPRINT.Manager.App tests/MS3DPRINT.Manager.Core.Tests src/MS3DPRINT.Manager.Desktop src/MS3DPRINT.Manager.Windows src/MS3DPRINT.Manager.Windows.App
git commit -m "build: add .NET 10 Avalonia solution boundary"
```

### Task 2: Build the dark console design system

**Files:**
- Create: `src/MS3DPRINT.Manager.Desktop/Styles/Theme.axaml`
- Create: `src/MS3DPRINT.Manager.Desktop/Styles/Controls.axaml`
- Create: `src/MS3DPRINT.Manager.Desktop/Controls/SectionPanel.axaml`
- Create: `src/MS3DPRINT.Manager.Desktop/Controls/SectionPanel.axaml.cs`
- Modify: `src/MS3DPRINT.Manager.Desktop/App.axaml`
- Test: `tests/MS3DPRINT.Manager.Desktop.Tests/ThemeMarkupTests.cs`

**Interfaces:**
- Consumes: Avalonia resource dictionaries.
- Produces: resource keys `CanvasBrush`, `SurfaceBrush`, `RaisedSurfaceBrush`, `AccentBrush`, `TextBrush`, `MutedTextBrush`, `DangerBrush`, `FocusBorderBrush` and reusable `SectionPanel`.

- [ ] **Step 1: Write failing resource tests**

```csharp
[Theory]
[InlineData("CanvasBrush")]
[InlineData("SurfaceBrush")]
[InlineData("AccentBrush")]
[InlineData("FocusBorderBrush")]
public void Theme_declares_required_semantic_resource(string key)
{
    var markup = File.ReadAllText(ProjectFile.Find("src/MS3DPRINT.Manager.Desktop/Styles/Theme.axaml"));
    Assert.Contains($"x:Key=\"{key}\"", markup);
}

[Fact]
public void Theme_does_not_declare_a_light_theme_switch()
{
    var markup = File.ReadAllText(ProjectFile.Find("src/MS3DPRINT.Manager.Desktop/Styles/Theme.axaml"));
    Assert.DoesNotContain("Light", markup, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/MS3DPRINT.Manager.Desktop.Tests --filter FullyQualifiedName~ThemeMarkupTests`

Expected: FAIL because no theme dictionary exists.

- [ ] **Step 3: Add semantic tokens and common states**

Define blue-black canvas `#090D14`, main surface `#0D1420`, raised surface `#141F30`, blue accent `#6698F5`, readable body text `#E9EFFB` and muted text `#94A7C4`. Create styles for buttons, text boxes, list rows and tabs with visible keyboard focus using `FocusBorderBrush`. Use a single font stack beginning with `Inter` then `Segoe UI`.

- [ ] **Step 4: Create SectionPanel**

Expose these styled properties:

```csharp
public static readonly StyledProperty<object?> HeaderProperty;
public static readonly StyledProperty<object?> ContentProperty;
public static readonly StyledProperty<object?> FooterProperty;
```

The control renders a 16-pixel-radius panel with optional header and footer; its content presenter has `MinWidth="0"` so lists never force horizontal overflow.

- [ ] **Step 5: Run tests and a visual smoke build**

Run: `dotnet test tests/MS3DPRINT.Manager.Desktop.Tests --filter FullyQualifiedName~ThemeMarkupTests`

Expected: PASS.

Run: `dotnet build src/MS3DPRINT.Manager.Desktop`

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add src/MS3DPRINT.Manager.Desktop/Styles src/MS3DPRINT.Manager.Desktop/Controls src/MS3DPRINT.Manager.Desktop/App.axaml tests/MS3DPRINT.Manager.Desktop.Tests/ThemeMarkupTests.cs
git commit -m "feat: add dark immersive Avalonia design system"
```

### Task 3: Establish navigation, dashboard and command search

**Files:**
- Create: `src/MS3DPRINT.Manager.Desktop/Navigation/AppPage.cs`
- Create: `src/MS3DPRINT.Manager.Desktop/Navigation/NavigationItem.cs`
- Create: `src/MS3DPRINT.Manager.Desktop/ViewModels/ShellViewModel.cs`
- Create: `src/MS3DPRINT.Manager.Desktop/ViewModels/DashboardViewModel.cs`
- Create: `src/MS3DPRINT.Manager.Desktop/Views/DashboardView.axaml`
- Create: `src/MS3DPRINT.Manager.Desktop/Views/DashboardView.axaml.cs`
- Create: `src/MS3DPRINT.Manager.Desktop/Views/CommandPalette.axaml`
- Create: `src/MS3DPRINT.Manager.Desktop/Views/CommandPalette.axaml.cs`
- Modify: `src/MS3DPRINT.Manager.Desktop/MainWindow.axaml`
- Modify: `src/MS3DPRINT.Manager.Desktop/MainWindow.axaml.cs`
- Test: `tests/MS3DPRINT.Manager.Desktop.Tests/ShellViewModelTests.cs`
- Test: `tests/MS3DPRINT.Manager.Desktop.Tests/DashboardViewModelTests.cs`

**Interfaces:**
- Consumes: `DashboardSnapshot.Create`, `GlobalSearchService`, `ClientCatalog`, `ProjectCatalog` from Core.
- Produces: `ShellViewModel.Navigate(AppPage page)`, `ShellViewModel.IsCommandPaletteOpen`, `DashboardViewModel.RefreshAsync(CancellationToken)`.

- [ ] **Step 1: Write failing shell tests**

```csharp
[Fact]
public void Navigate_updates_the_selected_page()
{
    var shell = new ShellViewModel();
    shell.Navigate(AppPage.Projects);
    Assert.Equal(AppPage.Projects, shell.SelectedPage);
}

[Fact]
public async Task Dashboard_refreshes_counts_and_recent_projects()
{
    var viewModel = new DashboardViewModel(new FakeDashboardReader(snapshot));
    await viewModel.RefreshAsync(CancellationToken.None);
    Assert.Equal(snapshot.ProjectCount, viewModel.ProjectCount);
    Assert.Equal(snapshot.RecentProjects, viewModel.RecentProjects);
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/MS3DPRINT.Manager.Desktop.Tests --filter "FullyQualifiedName~ShellViewModelTests|FullyQualifiedName~DashboardViewModelTests"`

Expected: FAIL because the shell and dashboard view models do not exist.

- [ ] **Step 3: Implement navigation contracts**

Create `enum AppPage { Dashboard, Projects, Clients, Collections, Filaments, Trash, Settings, Search }`. `ShellViewModel` exposes `ObservableCollection<NavigationItem> NavigationItems`, `AppPage SelectedPage`, `object? CurrentContent`, `bool IsCommandPaletteOpen`, `Navigate(AppPage)` and `ToggleCommandPalette()`. `MainWindow` binds the rail to these items and binds `Ctrl+K` to `ToggleCommandPalette`.

- [ ] **Step 4: Implement the overview dashboard**

Create an `IDashboardReader`:

```csharp
public interface IDashboardReader
{
    Task<DashboardSnapshot> LoadAsync(string storageRoot, CancellationToken cancellationToken);
}
```

The production reader runs catalog loading on a background task. The view shows counts, recent projects, a quick-create button and an activity panel. It sets `ErrorMessage` on failure and always resets `IsLoading` in `finally`.

- [ ] **Step 5: Implement the command palette**

The palette displays the same command list as the rail plus `Create project`, `Create client`, `Import file` and `Open 3D viewer`. Typing a query uses `GlobalSearchService` only after the user invokes the palette; no background indexing is added.

- [ ] **Step 6: Run focused tests and build**

Run: `dotnet test tests/MS3DPRINT.Manager.Desktop.Tests --filter "FullyQualifiedName~ShellViewModelTests|FullyQualifiedName~DashboardViewModelTests"`

Expected: PASS.

Run: `dotnet build src/MS3DPRINT.Manager.Desktop`

Expected: PASS.

- [ ] **Step 7: Commit**

```powershell
git add src/MS3DPRINT.Manager.Desktop/Navigation src/MS3DPRINT.Manager.Desktop/ViewModels src/MS3DPRINT.Manager.Desktop/Views src/MS3DPRINT.Manager.Desktop/MainWindow.axaml src/MS3DPRINT.Manager.Desktop/MainWindow.axaml.cs tests/MS3DPRINT.Manager.Desktop.Tests
git commit -m "feat: add Avalonia shell dashboard and command palette"
```

### Task 4: Isolate Windows integration behind testable adapters

**Files:**
- Create: `src/MS3DPRINT.Manager.Desktop/Services/IExternalFileOpener.cs`
- Create: `src/MS3DPRINT.Manager.Desktop/Services/IHardwareInfoProvider.cs`
- Create: `src/MS3DPRINT.Manager.Windows/WindowsExternalFileOpener.cs`
- Create: `src/MS3DPRINT.Manager.Windows/WindowsHardwareInfoProvider.cs`
- Create: `src/MS3DPRINT.Manager.Windows/WindowsServiceRegistration.cs`
- Test: `tests/MS3DPRINT.Manager.Windows.Tests/WindowsExternalFileOpenerTests.cs`
- Test: `tests/MS3DPRINT.Manager.Windows.Tests/WindowsHardwareInfoProviderTests.cs`

**Interfaces:**
- Consumes: existing WPF `ExplorerService` and `Hardware` behavior as the behavioral reference.
- Produces: `Task OpenAsync(string path, CancellationToken)` and `Task<HardwareProfile> GetAsync(CancellationToken)`.

- [ ] **Step 1: Write failing adapter tests**

```csharp
[Fact]
public async Task OpenAsync_rejects_an_empty_path_before_starting_a_process()
{
    var opener = new WindowsExternalFileOpener(new FakeProcessStarter());
    await Assert.ThrowsAsync<ArgumentException>(() => opener.OpenAsync("", CancellationToken.None));
}

[Fact]
public async Task GetAsync_returns_a_profile_when_the_reader_returns_a_snapshot()
{
    var provider = new WindowsHardwareInfoProvider(new FakeHardwareSnapshotReader(snapshot));
    var profile = await provider.GetAsync(CancellationToken.None);
    Assert.Equal(snapshot.ProcessorName, profile.ProcessorName);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/MS3DPRINT.Manager.Windows.Tests --filter "FullyQualifiedName~WindowsExternalFileOpenerTests|FullyQualifiedName~WindowsHardwareInfoProviderTests"`

Expected: FAIL because the adapters do not exist.

- [ ] **Step 3: Implement adapters**

`WindowsExternalFileOpener` uses `ProcessStartInfo { FileName = path, UseShellExecute = true }`; wrap process-launch exceptions in `ExternalFileOpenException` with the source path. Port the existing hardware snapshot reader and factory without exposing `System.Management` types outside the Windows project. Register both services in `WindowsServiceRegistration.AddWindowsServices(IServiceCollection services)`.

- [ ] **Step 4: Update dependent Desktop code**

Replace direct Explorer and hardware calls with the interfaces. Convert exceptions to the existing French user-facing messages in a single `UiErrorFormatter` class.

- [ ] **Step 5: Run tests**

Run: `dotnet test tests/MS3DPRINT.Manager.Windows.Tests`

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add src/MS3DPRINT.Manager.Desktop/Services src/MS3DPRINT.Manager.Windows tests/MS3DPRINT.Manager.Windows.Tests
git commit -m "feat: isolate Windows desktop integrations"
```

### Task 5: Port lists, search results and entity creation flows

**Files:**
- Create: `src/MS3DPRINT.Manager.Desktop/ViewModels/ClientsViewModel.cs`
- Create: `src/MS3DPRINT.Manager.Desktop/ViewModels/ProjectsViewModel.cs`
- Create: `src/MS3DPRINT.Manager.Desktop/ViewModels/CollectionsViewModel.cs`
- Create: `src/MS3DPRINT.Manager.Desktop/ViewModels/FilamentsViewModel.cs`
- Create: `src/MS3DPRINT.Manager.Desktop/Views/ClientsView.axaml`
- Create: `src/MS3DPRINT.Manager.Desktop/Views/ProjectsView.axaml`
- Create: `src/MS3DPRINT.Manager.Desktop/Views/CollectionsView.axaml`
- Create: `src/MS3DPRINT.Manager.Desktop/Views/FilamentsView.axaml`
- Create: `src/MS3DPRINT.Manager.Desktop/Views/CreateClientDialog.axaml`
- Create: `src/MS3DPRINT.Manager.Desktop/Views/CreateProjectDialog.axaml`
- Create: `src/MS3DPRINT.Manager.Desktop/Views/CreateCollectionDialog.axaml`
- Create: `src/MS3DPRINT.Manager.Desktop/Views/FilamentDialog.axaml`
- Test: `tests/MS3DPRINT.Manager.Desktop.Tests/EntityListViewModelTests.cs`
- Test: `tests/MS3DPRINT.Manager.Desktop.Tests/EntityCreationViewModelTests.cs`

**Interfaces:**
- Consumes: `ClientCatalog`, `ProjectCatalog`, `CollectionCatalog`, `FilamentStore`, `ClientCreationService`, `ProjectCreationService`, `FolderTreeService` and the existing Core validation exceptions.
- Produces: `Task RefreshAsync(CancellationToken)`, `Task CreateAsync(CancellationToken)` and `string? ErrorMessage` on each entity view model.

- [ ] **Step 1: Write failing list and creation tests**

```csharp
[Fact]
public async Task Projects_refresh_sorts_items_by_the_existing_catalog_order()
{
    var viewModel = new ProjectsViewModel(new FakeProjectCatalog(projects), root);
    await viewModel.RefreshAsync(CancellationToken.None);
    Assert.Equal(projects, viewModel.Items);
}

[Fact]
public async Task CreateClient_keeps_the_generated_code_registered()
{
    var viewModel = new CreateClientViewModel(service, root);
    viewModel.CompanyName = "Atelier Nord";
    await viewModel.CreateAsync(CancellationToken.None);
    Assert.NotNull(viewModel.CreatedClient);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/MS3DPRINT.Manager.Desktop.Tests --filter "FullyQualifiedName~EntityListViewModelTests|FullyQualifiedName~EntityCreationViewModelTests"`

Expected: FAIL because Avalonia entity view models do not exist.

- [ ] **Step 3: Implement lists and compact tables**

Use virtualized Avalonia list controls where available. Every list has a search/filter field, keyboard selection, an empty state and a primary `Create` action. Preserve current filters: client/project search, collection category and filament brand/material/abrasiveness.

- [ ] **Step 4: Implement creation/editing dialogs**

Port all fields and validation from the WPF windows. Bind validation messages beside their inputs and disable the submit action while required data is invalid. Call Core services directly; do not reproduce naming, code-registration or path-validation logic in the UI.

- [ ] **Step 5: Run tests and smoke build**

Run: `dotnet test tests/MS3DPRINT.Manager.Desktop.Tests --filter "FullyQualifiedName~EntityListViewModelTests|FullyQualifiedName~EntityCreationViewModelTests"`

Expected: PASS.

Run: `dotnet build src/MS3DPRINT.Manager.Desktop`

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add src/MS3DPRINT.Manager.Desktop/ViewModels src/MS3DPRINT.Manager.Desktop/Views tests/MS3DPRINT.Manager.Desktop.Tests
git commit -m "feat: port Avalonia entity lists and creation flows"
```

### Task 6: Port detail workspaces, managed files and trash

**Files:**
- Create: `src/MS3DPRINT.Manager.Desktop/ViewModels/ProjectWorkspaceViewModel.cs`
- Create: `src/MS3DPRINT.Manager.Desktop/ViewModels/ClientWorkspaceViewModel.cs`
- Create: `src/MS3DPRINT.Manager.Desktop/ViewModels/CollectionWorkspaceViewModel.cs`
- Create: `src/MS3DPRINT.Manager.Desktop/ViewModels/TrashViewModel.cs`
- Create: `src/MS3DPRINT.Manager.Desktop/Views/ProjectWorkspaceView.axaml`
- Create: `src/MS3DPRINT.Manager.Desktop/Views/ClientWorkspaceView.axaml`
- Create: `src/MS3DPRINT.Manager.Desktop/Views/CollectionWorkspaceView.axaml`
- Create: `src/MS3DPRINT.Manager.Desktop/Views/TrashView.axaml`
- Create: `src/MS3DPRINT.Manager.Desktop/Views/ConfirmDestructiveActionDialog.axaml`
- Test: `tests/MS3DPRINT.Manager.Desktop.Tests/ProjectWorkspaceViewModelTests.cs`
- Test: `tests/MS3DPRINT.Manager.Desktop.Tests/TrashViewModelTests.cs`

**Interfaces:**
- Consumes: `ProjectFileBrowser`, `ProjectFileTransferService`, `ManagedFileService`, `EntityManagementService`, `FileOpenRouting` and `TrashEntry` from Core.
- Produces: `ProjectFileEntry? SelectedFile`, `Task ImportAsync(IEnumerable<string>, CancellationToken)`, `Task MoveAsync(string destination, CancellationToken)` and `Task RestoreAsync(CancellationToken)`.

- [ ] **Step 1: Write failing workspace tests**

```csharp
[Fact]
public async Task Selecting_a_file_updates_the_selected_file()
{
    var viewModel = new ProjectWorkspaceViewModel(browser, project);
    await viewModel.LoadAsync(CancellationToken.None);
    viewModel.Select(viewModel.Files.Single());
    Assert.Equal(viewModel.Files.Single().Path, viewModel.SelectedFile!.Path);
}

[Fact]
public async Task Restore_keeps_the_entry_when_the_original_path_is_occupied()
{
    var viewModel = new TrashViewModel(service);
    await viewModel.RestoreAsync(entry, CancellationToken.None);
    Assert.Contains(entry, viewModel.Items);
    Assert.NotNull(viewModel.ErrorMessage);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/MS3DPRINT.Manager.Desktop.Tests --filter "FullyQualifiedName~ProjectWorkspaceViewModelTests|FullyQualifiedName~TrashViewModelTests"`

Expected: FAIL because the workspace view models do not exist.

- [ ] **Step 3: Implement the validated project workspace**

Use the visual structure accepted in the design: breadcrumb and project metadata, a central file list with `Fichiers`, `Fiche projet` and `Activité` tabs, plus a right context panel. At narrow desktop width, hide the right panel and expose its preview through `Open preview`.

- [ ] **Step 4: Implement management actions**

Wire create folder, import, rename, move, delete and open commands to existing Core services. Before delete or move, show the confirmation dialog with the exact affected name and destination. Refresh the current workspace after a successful operation and retain selection when its path still exists.

- [ ] **Step 5: Implement trash and unsaved-edit guard**

Port the current restore rule and path-conflict message. Define `IUnsavedChanges` with `bool HasUnsavedChanges` and `Task<bool> SaveAsync(CancellationToken)`; Shell navigation asks save/discard/cancel before replacing a dirty workspace.

- [ ] **Step 6: Run focused tests**

Run: `dotnet test tests/MS3DPRINT.Manager.Desktop.Tests --filter "FullyQualifiedName~ProjectWorkspaceViewModelTests|FullyQualifiedName~TrashViewModelTests"`

Expected: PASS.

- [ ] **Step 7: Commit**

```powershell
git add src/MS3DPRINT.Manager.Desktop/ViewModels src/MS3DPRINT.Manager.Desktop/Views tests/MS3DPRINT.Manager.Desktop.Tests
git commit -m "feat: port Avalonia workspaces and managed files"
```

### Task 7: Add image and PDF previews with safe fallback

**Files:**
- Create: `src/MS3DPRINT.Manager.Desktop/Preview/PreviewKind.cs`
- Create: `src/MS3DPRINT.Manager.Desktop/Preview/PreviewRequest.cs`
- Create: `src/MS3DPRINT.Manager.Desktop/Preview/IPreviewService.cs`
- Create: `src/MS3DPRINT.Manager.Desktop/Preview/ImagePreviewService.cs`
- Create: `src/MS3DPRINT.Manager.Desktop/Preview/PdfPreviewService.cs`
- Create: `src/MS3DPRINT.Manager.Desktop/Views/FilePreviewPane.axaml`
- Modify: `src/MS3DPRINT.Manager.Desktop/ViewModels/ProjectWorkspaceViewModel.cs`
- Modify: `src/MS3DPRINT.Manager.Desktop/Views/ProjectWorkspaceView.axaml`
- Modify: `src/MS3DPRINT.Manager.Desktop/MS3DPRINT.Manager.Desktop.csproj`
- Test: `tests/MS3DPRINT.Manager.Desktop.Tests/PreviewRequestTests.cs`
- Test: `tests/MS3DPRINT.Manager.Desktop.Tests/ImagePreviewServiceTests.cs`

**Interfaces:**
- Consumes: `PreviewFileSupport`, `FileOpenRouting` and `IExternalFileOpener`.
- Produces: `Task<PreviewResult> LoadAsync(PreviewRequest request, CancellationToken cancellationToken)`.

- [ ] **Step 1: Write failing preview-routing tests**

```csharp
[Theory]
[InlineData("part.png", PreviewKind.Image)]
[InlineData("document.pdf", PreviewKind.Pdf)]
[InlineData("part.stl", PreviewKind.ThreeD)]
[InlineData("archive.zip", PreviewKind.External)]
public void PreviewRequest_uses_the_existing_file_support_rules(string name, PreviewKind expected)
{
    Assert.Equal(expected, PreviewRequest.FromPath(name).Kind);
}

[Fact]
public async Task Image_service_returns_failure_without_throwing_for_a_corrupt_file()
{
    var result = await service.LoadAsync(PreviewRequest.FromPath(corruptPng), CancellationToken.None);
    Assert.False(result.IsSuccess);
    Assert.NotEmpty(result.UserMessage);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/MS3DPRINT.Manager.Desktop.Tests --filter "FullyQualifiedName~PreviewRequestTests|FullyQualifiedName~ImagePreviewServiceTests"`

Expected: FAIL because preview types do not exist.

- [ ] **Step 3: Implement preview contracts and image decoding**

Define:

```csharp
public sealed record PreviewResult(bool IsSuccess, object? Content, string UserMessage);
public interface IPreviewService
{
    PreviewKind Kind { get; }
    Task<PreviewResult> LoadAsync(PreviewRequest request, CancellationToken cancellationToken);
}
```

`ImagePreviewService` decodes PNG, JPEG, BMP, GIF and TIFF using Avalonia APIs. It catches decoder and IO exceptions and returns an unsuccessful `PreviewResult`; it never deletes or alters the source file.

- [ ] **Step 4: Add PDF viewer**

Add `Avalonia.Controls.PdfViewer`, its required theme resources and the Windows PDFium package. `PdfPreviewService` validates extension and file existence, then returns the document source for `FilePreviewPane`. Add the PDFium BSD 3-Clause text to `THIRD-PARTY-NOTICES.md`.

- [ ] **Step 5: Implement fallback rendering**

When `PreviewResult.IsSuccess` is false, show filename, message and `Open with Windows`. The button calls `IExternalFileOpener.OpenAsync`; no failed preview blocks file management.

- [ ] **Step 6: Run tests and build**

Run: `dotnet test tests/MS3DPRINT.Manager.Desktop.Tests --filter "FullyQualifiedName~PreviewRequestTests|FullyQualifiedName~ImagePreviewServiceTests"`

Expected: PASS.

Run: `dotnet build src/MS3DPRINT.Manager.Desktop`

Expected: PASS.

- [ ] **Step 7: Commit**

```powershell
git add src/MS3DPRINT.Manager.Desktop/Preview src/MS3DPRINT.Manager.Desktop/Views/FilePreviewPane.axaml src/MS3DPRINT.Manager.Desktop/MS3DPRINT.Manager.Desktop.csproj THIRD-PARTY-NOTICES.md tests/MS3DPRINT.Manager.Desktop.Tests
git commit -m "feat: add Avalonia image and PDF previews"
```

### Task 8: Port STL and OBJ previewing to SharpEngine

**Files:**
- Create: `src/MS3DPRINT.Manager.Desktop/Preview/IModelPreviewService.cs`
- Create: `src/MS3DPRINT.Manager.Desktop/Preview/SharpEngineModelPreviewService.cs`
- Create: `src/MS3DPRINT.Manager.Desktop/Preview/ModelPreviewViewModel.cs`
- Create: `src/MS3DPRINT.Manager.Desktop/Views/ModelPreviewPane.axaml`
- Create: `src/MS3DPRINT.Manager.Desktop/Views/ModelPreviewWindow.axaml`
- Modify: `src/MS3DPRINT.Manager.Desktop/MS3DPRINT.Manager.Desktop.csproj`
- Test: `tests/MS3DPRINT.Manager.Desktop.Tests/ModelPreviewViewModelTests.cs`
- Test: `tests/MS3DPRINT.Manager.Desktop.Tests/SharpEngineModelPreviewServiceTests.cs`

**Interfaces:**
- Consumes: `ThreeDFileSupport`, `ModelDimensions`, `WallThicknessAnalyzer`, `IExternalFileOpener` and files selected by `PreviewRequest`.
- Produces: `Task<ModelPreviewResult> LoadAsync(string path, CancellationToken)` and `Task<WallThicknessResult> AnalyzeWallsAsync(double scaleMillimeters, double thresholdMillimeters, CancellationToken)`.

- [ ] **Step 1: Write failing 3D tests**

```csharp
[Theory]
[InlineData("model.stl")]
[InlineData("model.obj")]
public async Task LoadAsync_accepts_supported_model_files(string fileName)
{
    var result = await service.LoadAsync(Path.Combine(fixtures, fileName), CancellationToken.None);
    Assert.True(result.IsSuccess);
}

[Fact]
public async Task AnalyzeWalls_refuses_a_non_manifold_mesh()
{
    var result = await service.AnalyzeWallsAsync(nonManifoldModel, 1, .8, CancellationToken.None);
    Assert.False(result.CanAnalyze);
    Assert.NotEmpty(result.UserMessage);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/MS3DPRINT.Manager.Desktop.Tests --filter "FullyQualifiedName~ModelPreviewViewModelTests|FullyQualifiedName~SharpEngineModelPreviewServiceTests"`

Expected: FAIL because SharpEngine preview services do not exist.

- [ ] **Step 3: Integrate SharpEngine and importers**

Add `Ab4d.SharpEngine`, `Ab4d.SharpEngine.AvaloniaUI` and `Ab4d.SharpEngine.Assimp`. Load only STL and OBJ in the application UI, create the camera and scene after file validation, and dispose every scene/view resource when its pane closes. Keep the engine behind `IModelPreviewService` so no view model references SharpEngine types.

Define the result contracts in `IModelPreviewService.cs`:

```csharp
public sealed record ModelPreviewResult(
    bool IsSuccess,
    ModelDimensions? Dimensions,
    string UserMessage);

public sealed record WallThicknessResult(
    bool CanAnalyze,
    WallAnalysisResult? Analysis,
    string UserMessage);
```

- [ ] **Step 4: Preserve model behavior**

Expose orbit, zoom, fit-to-view, model dimensions and explicit scale/thickness inputs. Reuse or move the pure geometry code from WPF preview classes into Core only if it contains no UI framework type; retain the existing refusal for open, non-manifold or inconsistent meshes.

- [ ] **Step 5: Add graceful rendering failure**

If Vulkan or model import initialization fails, return a failed `ModelPreviewResult`; `FilePreviewPane` offers `Open with Windows` and preserves the selected file. A GPU rendering failure must not crash the shell.

- [ ] **Step 6: Run tests and manual model smoke test**

Run: `dotnet test tests/MS3DPRINT.Manager.Desktop.Tests --filter "FullyQualifiedName~ModelPreviewViewModelTests|FullyQualifiedName~SharpEngineModelPreviewServiceTests"`

Expected: PASS.

Run: `dotnet run --project src/MS3DPRINT.Manager.Desktop`

Expected: a supplied STL and OBJ can be opened, rotated, zoomed and framed; a non-manifold test mesh reports that wall analysis is unavailable.

- [ ] **Step 7: Commit**

```powershell
git add src/MS3DPRINT.Manager.Desktop/Preview src/MS3DPRINT.Manager.Desktop/ViewModels/ModelPreviewViewModel.cs src/MS3DPRINT.Manager.Desktop/Views/ModelPreviewPane.axaml src/MS3DPRINT.Manager.Desktop/Views/ModelPreviewWindow.axaml src/MS3DPRINT.Manager.Desktop/MS3DPRINT.Manager.Desktop.csproj tests/MS3DPRINT.Manager.Desktop.Tests
git commit -m "feat: port 3D previews to Avalonia SharpEngine"
```

### Task 9: Complete parity, regression coverage and accessibility validation

**Files:**
- Create: `tests/MS3DPRINT.Manager.Desktop.Tests/NavigationGuardTests.cs`
- Create: `tests/MS3DPRINT.Manager.Desktop.Tests/WorkspaceParityTests.cs`
- Create: `tests/MS3DPRINT.Manager.Desktop.Tests/AccessibilityMarkupTests.cs`
- Create: `tests/MS3DPRINT.Manager.Desktop.Tests/CompatibilityFixture.cs`
- Modify: `README.md`
- Modify: `THIRD-PARTY-NOTICES.md`

**Interfaces:**
- Consumes: all Desktop services, Core test fixture patterns and the existing WPF behavior as the parity oracle.
- Produces: repeatable tests for old-data compatibility, destructive-operation safeguards, visible focus and accessible names.

- [ ] **Step 1: Write failing compatibility tests**

```csharp
[Fact]
public async Task Existing_workspace_data_is_loaded_without_writing_a_conversion_file()
{
    using var fixture = CompatibilityFixture.FromCopyOf("Fixtures/ExistingWorkspace");
    await desktop.LoadWorkspaceAsync(fixture.Root, CancellationToken.None);
    Assert.True(File.Exists(Path.Combine(fixture.Root, "data", "client-codes.json")));
    Assert.False(File.Exists(Path.Combine(fixture.Root, "data", "migration.json")));
}

[Fact]
public void MainWindow_marks_icon_only_actions_with_accessible_names()
{
    var markup = File.ReadAllText(ProjectFile.Find("src/MS3DPRINT.Manager.Desktop/MainWindow.axaml"));
    Assert.Contains("AutomationProperties.Name", markup);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/MS3DPRINT.Manager.Desktop.Tests --filter "FullyQualifiedName~WorkspaceParityTests|FullyQualifiedName~AccessibilityMarkupTests"`

Expected: FAIL until compatibility fixture and accessibility markup are complete.

- [ ] **Step 3: Finish remaining commands and messages**

Exercise every operation in Core through the Desktop UI: create, edit, rename, move, import, open, delete, restore and search. Map all exceptions through `UiErrorFormatter`; show a recoverable message without exposing a stack trace. Ensure only explicit deletes show a destructive confirmation.

- [ ] **Step 4: Add accessibility and scaling checks**

Give every icon-only control an `AutomationProperties.Name`, preserve keyboard focus, and verify tab navigation. Manually test 125% and 150% Windows display scaling at 1280×720 and 1920×1080; text, search and project file rows must remain readable and no essential action may clip.

- [ ] **Step 5: Run the full test suite**

Run: `dotnet test MS3DPRINT.Manager.sln`

Expected: PASS with Core, Desktop and Windows test projects.

- [ ] **Step 6: Commit**

```powershell
git add tests/MS3DPRINT.Manager.Desktop.Tests README.md THIRD-PARTY-NOTICES.md
git commit -m "test: verify Avalonia parity and accessibility"
```

### Task 10: Publish, verify and replace the Windows delivery

**Files:**
- Create: `src/MS3DPRINT.Manager.Desktop/Properties/PublishProfiles/PortableWinX64.pubxml`
- Create: `scripts/Publish-AvaloniaWindows.ps1`
- Modify: `README.md`
- Test: `tests/MS3DPRINT.Manager.Desktop.Tests/PublishContractTests.cs`

**Interfaces:**
- Consumes: the Desktop publish output and `sources/AGENTS.md` delivery rules.
- Produces: `outputs/MS3DPRINT-Manager/MS3DPRINT.Manager.Desktop.exe` and delivery `sources/MS3DPRINT Manager.exe` with an identical SHA256.

- [ ] **Step 1: Write failing publish-contract tests**

```csharp
[Fact]
public void Publish_script_names_the_user_delivery_exactly_once()
{
    var script = File.ReadAllText(ProjectFile.Find("scripts/Publish-AvaloniaWindows.ps1"));
    Assert.Contains("MS3DPRINT Manager.exe", script);
    Assert.Contains("Get-FileHash", script);
    Assert.DoesNotContain("Material", script);
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/MS3DPRINT.Manager.Desktop.Tests --filter FullyQualifiedName~PublishContractTests`

Expected: FAIL because the publishing script does not exist.

- [ ] **Step 3: Implement self-contained Windows publishing**

Set `RuntimeIdentifier` to `win-x64`, `SelfContained` to `true`, `PublishSingleFile` to `true` and `IncludeNativeLibrariesForSelfExtract` to `true`. The script publishes into `outputs/MS3DPRINT-Manager`, verifies the application is not running by attempting a non-destructive open of the delivery file, copies the internal executable to `sources/MS3DPRINT Manager.exe`, computes both SHA256 hashes and stops with an error on mismatch. It does not delete `data`, `.ms3dprint-manager` or user documents.

- [ ] **Step 4: Run publish-contract test and publish**

Run: `dotnet test tests/MS3DPRINT.Manager.Desktop.Tests --filter FullyQualifiedName~PublishContractTests`

Expected: PASS.

Run: `powershell -ExecutionPolicy Bypass -File scripts/Publish-AvaloniaWindows.ps1`

Expected: one `MS3DPRINT Manager.exe` in `sources/`, equal SHA256 values printed, and no additional delivery executable.

- [ ] **Step 5: Run final regression**

Run: `dotnet test MS3DPRINT.Manager.sln`

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add src/MS3DPRINT.Manager.Desktop/Properties/PublishProfiles scripts/Publish-AvaloniaWindows.ps1 README.md tests/MS3DPRINT.Manager.Desktop.Tests/PublishContractTests.cs
git commit -m "build: publish Avalonia Windows delivery"
```

## Plan Review

| Specification requirement | Implemented by |
| --- | --- |
| .NET 10 platform-neutral Core | Task 1 |
| Avalonia shell and dark console design | Tasks 1–3 |
| Windows-specific integrations | Task 4 |
| All business screens and editing flows | Tasks 5–6 |
| Images, PDF, STL and OBJ previews | Tasks 7–8 |
| Existing-data compatibility, safety and accessibility | Task 9 |
| Single-executable Windows delivery and SHA256 validation | Task 10 |

The plan contains complete implementation instructions, uses the same type names at every producer and consumer boundary, and leaves WPF intact until the final parity check passes.
