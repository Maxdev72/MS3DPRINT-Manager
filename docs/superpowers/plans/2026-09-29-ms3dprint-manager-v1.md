# MS3DPRINT Manager V1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (- [ ]) syntax for tracking.

**Goal:** Build a portable Windows WPF application that safely creates and verifies MS3DPRINT folder structures.

**Architecture:** A .NET 8 Core library owns normalization, references, templates, code persistence and disk operations. A WPF executable uses that library through focused view models and modal forms. A JSON registry beside the executable persists client codes; client folders remain the source of truth for project numbering.

**Tech Stack:** C# 12, .NET 8, WPF, System.Text.Json, xUnit and explorer.exe.

**Spec:** docs/superpowers/specs/2026-09-29-ms3dprint-manager-v1-design.md

## Global Constraints

- Target net8.0 for Core/tests and net8.0-windows with WPF for the app.
- Publish a self-contained single-file win-x64 executable with no paid or runtime dependency.
- Default root is C:\MS3DPRINT\Nextcloud\MS3DPRINT.
- Normalize names to uppercase ASCII; accents are removed and separator runs become one underscore.
- Never delete or overwrite user files or directories; display French errors for conflicts.
- Assemble every new tree in a sibling staging folder then move it only when complete.
- Use no third-party UI package; all user-facing text is French.

---

## File Structure

    MS3DPRINT.Manager.sln
    Directory.Build.props
    src/MS3DPRINT.Manager.Core/
      Naming/NameNormalizer.cs
      Naming/ClientCodeSuggester.cs
      Projects/ProjectReference.cs
      Projects/ProjectReferenceGenerator.cs
      Templates/FolderTemplates.cs
      Storage/FolderConflictException.cs
      Storage/FolderCreationResult.cs
      Storage/FolderTreeService.cs
      Storage/ClientCodeRegistry.cs
    src/MS3DPRINT.Manager.App/
      App.xaml, App.xaml.cs, MainWindow.xaml, MainWindow.xaml.cs
      ViewModels/ObservableObject.cs, MainViewModel.cs, CreateClientViewModel.cs, CreateProjectViewModel.cs
      Views/CreateClientWindow.xaml, CreateProjectWindow.xaml, CreateNamedItemWindow.xaml
      Properties/PublishProfiles/PortableWinX64.pubxml
    tests/MS3DPRINT.Manager.Core.Tests/
      Naming/NameNormalizerTests.cs, ClientCodeSuggesterTests.cs
      Projects/ProjectReferenceGeneratorTests.cs
      Storage/FolderTreeServiceTests.cs, ClientCodeRegistryTests.cs
    README.md

### Task 1: Create the solution and release profile

**Files:**
- Create: solution, Directory.Build.props, README, the Core library, WPF application, xUnit project and publish profile in the file structure above.

**Interfaces:** Produces a buildable solution. Core is referenced by the WPF application and test project.

- [ ] **Step 1: Scaffold the projects**

    dotnet new sln --name MS3DPRINT.Manager
    dotnet new classlib --name MS3DPRINT.Manager.Core --output src/MS3DPRINT.Manager.Core --framework net8.0
    dotnet new wpf --name MS3DPRINT.Manager.App --output src/MS3DPRINT.Manager.App --framework net8.0
    dotnet new xunit --name MS3DPRINT.Manager.Core.Tests --output tests/MS3DPRINT.Manager.Core.Tests --framework net8.0
    dotnet sln MS3DPRINT.Manager.sln add src/MS3DPRINT.Manager.Core/MS3DPRINT.Manager.Core.csproj src/MS3DPRINT.Manager.App/MS3DPRINT.Manager.App.csproj tests/MS3DPRINT.Manager.Core.Tests/MS3DPRINT.Manager.Core.Tests.csproj
    dotnet add src/MS3DPRINT.Manager.App/MS3DPRINT.Manager.App.csproj reference src/MS3DPRINT.Manager.Core/MS3DPRINT.Manager.Core.csproj
    dotnet add tests/MS3DPRINT.Manager.Core.Tests/MS3DPRINT.Manager.Core.Tests.csproj reference src/MS3DPRINT.Manager.Core/MS3DPRINT.Manager.Core.csproj

- [ ] **Step 2: Add compile and publish settings**

Write Directory.Build.props with Nullable enabled, ImplicitUsings enabled and TreatWarningsAsErrors enabled. Publish profile properties must be Configuration=Release, RuntimeIdentifier=win-x64, SelfContained=true, PublishSingleFile=true, IncludeNativeLibrariesForSelfExtract=true and PublishTrimmed=false.

- [ ] **Step 3: Add usage instructions**

README must give these exact commands and state that data\client-codes.json must travel with the executable:

    dotnet test MS3DPRINT.Manager.sln
    dotnet run --project src/MS3DPRINT.Manager.App/MS3DPRINT.Manager.App.csproj
    dotnet publish src/MS3DPRINT.Manager.App/MS3DPRINT.Manager.App.csproj -p:PublishProfile=PortableWinX64 -o outputs/MS3DPRINT-Manager

- [ ] **Step 4: Verify and commit**

Run: dotnet build MS3DPRINT.Manager.sln -warnaserror
Expected: success with zero warnings.

    git add MS3DPRINT.Manager.sln Directory.Build.props src tests README.md
    git commit -m "chore: scaffold MS3DPRINT Manager solution"

### Task 2: Implement normalization and project references with tests

**Files:**
- Create: Core Naming/NameNormalizer.cs and ClientCodeSuggester.cs.
- Create: Core Projects/ProjectReference.cs and ProjectReferenceGenerator.cs.
- Create: corresponding Naming and Projects test files.

**Interfaces:** NameNormalizer.Normalize(string), ClientCodeSuggester.Suggest(string), ProjectReferenceGenerator.Create(string clientCode, int year, IEnumerable<string> existingNames, string projectName). ProjectReference exposes ClientCode, Year, Sequence, NormalizedProjectName and FolderName.

- [ ] **Step 1: Write the failing naming tests**

    [Theory]
    [InlineData(" Société Dupont & Fils ", "SOCIETE_DUPONT_FILS")]
    [InlineData("déjà---vu", "DEJA_VU")]
    [InlineData("___", "")]
    public void Normalize_ReturnsSafeDirectorySegment(string input, string expected)
        => Assert.Equal(expected, NameNormalizer.Normalize(input));

    [Theory]
    [InlineData("MAIRIE_PARIS_OUEST", "MPO")]
    [InlineData("DUPONT", "DUPON")]
    public void Suggest_ReturnsInitialsOrFiveCharacters(string input, string expected)
        => Assert.Equal(expected, ClientCodeSuggester.Suggest(input));

- [ ] **Step 2: Run test to verify failure**

Run: dotnet test tests/MS3DPRINT.Manager.Core.Tests --filter FullyQualifiedName~Naming -warnaserror
Expected: FAIL because the production types do not exist.

- [ ] **Step 3: Implement the minimum code**

Decompose with FormD, skip NonSpacingMark, retain A-Z and 0-9, and collapse other runs to _. For a multiword normalized client name return initials; otherwise return five characters maximum; empty input returns empty.

- [ ] **Step 4: Write failing reference tests**

    [Fact]
    public void Create_UsesNextSequenceForSameClientAndYear()
    {
        var names = new[] { "MPO-2026-001_ANCIEN", "MPO-2026-007_AUTRE", "MPO-2025-999_OLD", "SDIS72-2026-011_OTHER" };
        var result = ProjectReferenceGenerator.Create("MPO", 2026, names, "Outillage monocuvette");
        Assert.Equal(8, result.Sequence);
        Assert.Equal("MPO-2026-008_OUTILLAGE_MONOCUVETTE", result.FolderName);
    }

    [Fact]
    public void Create_RejectsEmptyNormalizedProjectName()
        => Assert.Throws<ArgumentException>(() => ProjectReferenceGenerator.Create("MPO", 2026, [], "---"));

- [ ] **Step 5: Run test to verify failure**

Run: dotnet test tests/MS3DPRINT.Manager.Core.Tests --filter FullyQualifiedName~Projects -warnaserror
Expected: FAIL because the generator does not exist.

- [ ] **Step 6: Implement generator**

Escape the normalized code and match immediate folders with the equivalent of ^CODE-AAAA-(\d{3})_. Use the maximum matching sequence plus one, format with D3 and reject blank code/name or number above 999.

- [ ] **Step 7: Verify and commit**

    dotnet test tests/MS3DPRINT.Manager.Core.Tests -warnaserror
    git add src/MS3DPRINT.Manager.Core/Naming src/MS3DPRINT.Manager.Core/Projects tests/MS3DPRINT.Manager.Core.Tests/Naming tests/MS3DPRINT.Manager.Core.Tests/Projects
    git commit -m "feat: add naming and project reference rules"

Expected: tests pass before commit.

### Task 3: Implement templates, safe storage and client-code persistence

**Files:**
- Create: Core Templates/FolderTemplates.cs.
- Create: Core Storage/FolderConflictException.cs, FolderCreationResult.cs, FolderTreeService.cs and ClientCodeRegistry.cs.
- Create: Storage test files.

**Interfaces:** FolderTemplates.Main, Client, Project, Model, Product and Supplier are IReadOnlyList<string>. FolderTreeService.EnsureMainStructure(string) and CreateTree(string, IReadOnlyList<string>) return FolderCreationResult. ClientCodeRegistry.GetCode(string) and Add(string, string) use data/client-codes.json beside the executable.

- [ ] **Step 1: Write failing filesystem tests**

    [Fact]
    public void EnsureMainStructure_CreatesEveryRequiredFolder()
    {
        var root = CreateTemporaryRoot();
        Directory.CreateDirectory(Path.Combine(root, "01_CLIENTS"));
        _service.EnsureMainStructure(root);
        Assert.All(FolderTemplates.Main, f => Assert.True(Directory.Exists(Path.Combine(root, f))));
    }

    [Fact]
    public void CreateTree_ThrowsBeforeWritingWhenDestinationExists()
    {
        var target = Path.Combine(CreateTemporaryRoot(), "EXISTANT");
        Directory.CreateDirectory(target);
        Assert.Throws<FolderConflictException>(() => _service.CreateTree(target, FolderTemplates.Model));
        Assert.Empty(Directory.GetDirectories(target));
    }

Also assert project creation includes 03_CAO_3D\01_MASTER and 04_IMPRESSION_3D\03_PARAMETRES.

- [ ] **Step 2: Verify failure and implement templates**

Run: dotnet test tests/MS3DPRINT.Manager.Core.Tests --filter FullyQualifiedName~Storage -warnaserror
Expected: FAIL before templates exist.

Copy all six folder templates exactly from the approved specification.

- [ ] **Step 3: Implement safe folder creation**

EnsureMainStructure adds only missing root segments. CreateTree rejects an existing destination, creates .MS3DPRINT-STAGING-GUID in its parent, constructs every template folder, then calls Directory.Move(staging, destination). A target that appears during the move raises FolderConflictException; it is never replaced. Delete only an empty staging directory made by the current operation after a failure.

- [ ] **Step 4: Write failing registry tests**

    [Fact]
    public void Add_PersistsAndCanBeReadByANewRegistry()
    {
        new ClientCodeRegistry(_temporaryDataDirectory).Add("DUPONT", "DUPONT");
        Assert.Equal("DUPONT", new ClientCodeRegistry(_temporaryDataDirectory).GetCode("DUPONT"));
    }

    [Fact]
    public void Add_RejectsExistingClientWithoutChangingCode()
    {
        _registry.Add("DUPONT", "DUPONT");
        Assert.Throws<FolderConflictException>(() => _registry.Add("DUPONT", "OTHER"));
        Assert.Equal("DUPONT", _registry.GetCode("DUPONT"));
    }

- [ ] **Step 5: Implement append-only JSON updates**

Serialize a dictionary through System.Text.Json. Reject duplicate normalized names before writing. Write a client-codes.GUID.tmp file, preserve an existing JSON as client-codes.previous.json, then activate the new JSON. V1 has no code-edit action.

- [ ] **Step 6: Verify and commit**

    dotnet test MS3DPRINT.Manager.sln -warnaserror
    git add src/MS3DPRINT.Manager.Core/Templates src/MS3DPRINT.Manager.Core/Storage tests/MS3DPRINT.Manager.Core.Tests/Storage
    git commit -m "feat: add safe folder creation and client registry"

Expected: all Core tests pass before commit.

### Task 4: Build the French WPF workflow

**Files:**
- Create: App ViewModels/ObservableObject.cs, MainViewModel.cs, CreateClientViewModel.cs and CreateProjectViewModel.cs.
- Create: App Views/CreateClientWindow.xaml and code-behind, CreateProjectWindow.xaml and code-behind, CreateNamedItemWindow.xaml and code-behind.
- Modify: App.xaml, App.xaml.cs, MainWindow.xaml and MainWindow.xaml.cs.

**Interfaces:** Consumes Task 2-3 Core types. Produces all seven V1 commands and an open-folder action after successful creation.

- [ ] **Step 1: Add commands and main window**

Implement INotifyPropertyChanged. Set StorageRoot to C:\MS3DPRINT\Nextcloud\MS3DPRINT. Use a resizable WPF window with dark-blue title header, neutral background, status line, seven 44px vertical buttons, 12px spacing and visible keyboard focus. Buttons: Vérifier l’arborescence; Nouveau client; Nouveau projet client; Nouveau modèle 3D; Nouveau produit MS3DPRINT; Nouveau fournisseur; Ouvrir le dossier MS3DPRINT.

- [ ] **Step 2: Implement client and project dialogs**

Client dialog binds name, readonly normalized preview and editable suggested code; preserve manually edited code; create client tree then register code; retain values with specific French errors on conflicts, validation, IO or access failure.

Project dialog lists sorted non-hidden folders in 01_CLIENTS; loads client code; explicitly confirms saving a supplied absent code; enumerates immediate project directories; shows live CODE-AAAA-001_NOM preview; creates only valid project trees.

- [ ] **Step 3: Implement reusable named-item dialog**

CreateNamedItemWindow accepts French title, target root and template. It displays name input plus normalized preview, validates, creates the tree and offers to open it. Reuse it for model, product and supplier.

- [ ] **Step 4: Wire Explorer, verify and commit**

    Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"\"{path}\"", UseShellExecute = true });

The root button calls EnsureMainStructure before Explorer. Build, then run:

    dotnet build MS3DPRINT.Manager.sln -warnaserror
    dotnet run --project src/MS3DPRINT.Manager.App/MS3DPRINT.Manager.App.csproj

Expected manual checks: all actions visible; Société Dupont previews SOCIETE_DUPONT; code can be changed; a project previews DUPON-2026-001_*; every requested subfolder exists; repeat creation shows a French conflict; Explorer opens root and created folder.

    git add src/MS3DPRINT.Manager.App
    git commit -m "feat: add MS3DPRINT Manager WPF workflow"

### Task 5: Publish and verify the portable release

**Files:**
- Modify: README.md.
- Create: outputs/MS3DPRINT-Manager/MS3DPRINT.Manager.App.exe (generated; do not commit).

**Interfaces:** Uses Task 1 release profile and produces the portable delivery folder.

- [ ] **Step 1: Run all tests**

Run: dotnet test MS3DPRINT.Manager.sln -warnaserror
Expected: all tests pass with zero warnings.

- [ ] **Step 2: Publish**

    Remove-Item -LiteralPath 'outputs\MS3DPRINT-Manager' -Recurse -Force -ErrorAction SilentlyContinue
    dotnet publish src/MS3DPRINT.Manager.App/MS3DPRINT.Manager.App.csproj -p:PublishProfile=PortableWinX64 -o outputs/MS3DPRINT-Manager
    Get-ChildItem outputs/MS3DPRINT-Manager

Expected: MS3DPRINT.Manager.App.exe is present and needs no installed .NET runtime.

- [ ] **Step 3: Smoke-test without production writes**

Run: Start-Process -FilePath 'outputs\MS3DPRINT-Manager\MS3DPRINT.Manager.App.exe'.
Verify the main window opens, then close it. Use a disposable root override for creation tests, never production Nextcloud folders.

- [ ] **Step 4: Finish delivery instructions and commit source only**

Add Livraison instructions: copy the complete outputs\MS3DPRINT-Manager folder, including data\client-codes.json after it is first created.

    git add README.md
    git commit -m "docs: add portable delivery instructions"

## Plan Self-Review

- **Coverage:** Tasks 2-4 cover normalization, codes, references, every requested folder type, root verification, Explorer access, French UI and conflicts. Task 5 verifies portable publishing.
- **Placeholders:** Every task has concrete files, commands, APIs, tests and expected outcomes.
- **Consistency:** Types and members consumed by Task 4 are defined in Tasks 2-3.
