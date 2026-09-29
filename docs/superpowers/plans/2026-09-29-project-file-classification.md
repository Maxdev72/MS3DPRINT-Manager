# Project File Classification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move a chosen local file into an existing client project with a safe, project-referenced name and no duplicate at the source.

**Architecture:** Core owns extension routing, final-name generation, and guarded movement. WPF selects the source and project, exposes the proposed folder for adjustment, asks confirmation, then transfers asynchronously.

**Tech Stack:** .NET 8, C#, WPF, xUnit, Windows `OpenFileDialog`.

**Spec:** `docs/superpowers/specs/2026-09-29-file-classification-design.md`

## Global Constraints

- Move once; do not leave a source duplicate after a successful move.
- Name files `NOM_ORIGINAL__CODECLIENT-AAAA-XXX.ext`, preserving extension spelling.
- Map PDF, STEP/STP, STL, 3MF, and other files according to the specification; let the user choose another allowed folder.
- Never overwrite, replace, or delete an existing destination.
- Keep every destination inside an existing project below `01_CLIENTS`.
- Obtain explicit confirmation before a move.

---

### Task 1: Classify names and project folders

**Files:**
- Create: `src/MS3DPRINT.Manager.Core/Files/ProjectFileCategory.cs`
- Create: `src/MS3DPRINT.Manager.Core/Files/ProjectFileDestination.cs`
- Create: `src/MS3DPRINT.Manager.Core/Files/ProjectFileClassifier.cs`
- Create: `tests/MS3DPRINT.Manager.Core.Tests/Files/ProjectFileClassifierTests.cs`

**Interfaces:**
- Produces `enum ProjectFileCategory { Documents, Step, Stl, ThreeMf, ClientFiles }`.
- Produces `record ProjectFileDestination(ProjectFileCategory Category, string RelativeDirectory, string FileName)`.
- Produces `ProjectFileClassifier.Suggest(string sourceFileName, string projectFolderName) : ProjectFileDestination`.

- [ ] **Step 1: Write the failing classifier test**

```csharp
[Theory]
[InlineData("DEV2026-05.pdf", "MPO-2026-001_OUTILLAGE", "01_DEVIS_FACTURES", "DEV2026-05__MPO-2026-001.pdf")]
[InlineData("MPO-STEP-25-GRADE-A.step", "MPO-2026-001_OUTILLAGE", "03_CAO_3D\\02_STEP", "MPO-STEP-25-GRADE-A__MPO-2026-001.step")]
[InlineData("piece.STL", "MPO-2026-001_OUTILLAGE", "03_CAO_3D\\03_STL", "piece__MPO-2026-001.STL")]
public void Suggest_UsesOriginalStemProjectReferenceAndExtension(string source, string project, string folder, string expected)
{
    var result = ProjectFileClassifier.Suggest(source, project);
    Assert.Equal(folder, result.RelativeDirectory);
    Assert.Equal(expected, result.FileName);
}
```

- [ ] **Step 2: Run the test and verify red**

Run: `dotnet test tests/MS3DPRINT.Manager.Core.Tests/MS3DPRINT.Manager.Core.Tests.csproj --filter FullyQualifiedName~ProjectFileClassifierTests`

Expected: FAIL because `ProjectFileClassifier` does not exist.

- [ ] **Step 3: Implement the minimal classifier**

```csharp
public static ProjectFileDestination Suggest(string sourceFileName, string projectFolderName)
{
    var extension = Path.GetExtension(sourceFileName);
    var stem = Path.GetFileNameWithoutExtension(sourceFileName);
    var reference = projectFolderName.Split('_', 2, StringSplitOptions.None)[0];
    if (!Regex.IsMatch(reference, "^[A-Z0-9]+-[0-9]{4}-[0-9]{3}$"))
        throw new ArgumentException("La référence projet est invalide.", nameof(projectFolderName));
    return new(CategoryFor(extension), DirectoryFor(extension), stem + "__" + reference + extension);
}
```

`CategoryFor` and `DirectoryFor` compare extensions case-insensitively. The returned name retains the original extension string.

- [ ] **Step 4: Run the test and verify green**

Run: `dotnet test tests/MS3DPRINT.Manager.Core.Tests/MS3DPRINT.Manager.Core.Tests.csproj --filter FullyQualifiedName~ProjectFileClassifierTests --no-restore`

Expected: PASS.

- [ ] **Step 5: Commit**

Run: `git add src/MS3DPRINT.Manager.Core/Files tests/MS3DPRINT.Manager.Core.Tests/Files && git commit -m "feat: classify project file destinations"`

### Task 2: Move files without overwrite

**Files:**
- Create: `src/MS3DPRINT.Manager.Core/Files/ProjectFileTransferService.cs`
- Create: `tests/MS3DPRINT.Manager.Core.Tests/Files/ProjectFileTransferServiceTests.cs`

**Interfaces:**
- Consumes `ProjectFileDestination`.
- Produces `ProjectFileTransferService.Move(string sourcePath, string projectPath, ProjectFileDestination destination) : string`.

- [ ] **Step 1: Write the failing move tests with actual temporary files**

```csharp
[Fact]
public void Move_MovesFileAndRemovesTheSource()
{
    File.WriteAllText(_sourcePath, "indy document");
    Directory.CreateDirectory(Path.Combine(_projectPath, "01_DEVIS_FACTURES"));
    var destination = new ProjectFileDestination(ProjectFileCategory.Documents, "01_DEVIS_FACTURES", "DEV2026-05__MPO-2026-001.pdf");

    var result = new ProjectFileTransferService().Move(_sourcePath, _projectPath, destination);

    Assert.False(File.Exists(_sourcePath));
    Assert.Equal("indy document", File.ReadAllText(result));
}

[Fact]
public void Move_RejectsExistingDestinationAndKeepsSource()
{
    File.WriteAllText(_sourcePath, "source");
    var target = Path.Combine(_projectPath, "01_DEVIS_FACTURES", "DEV2026-05__MPO-2026-001.pdf");
    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
    File.WriteAllText(target, "existing");

    Assert.Throws<FolderConflictException>(() => new ProjectFileTransferService().Move(_sourcePath, _projectPath, _destination));
    Assert.True(File.Exists(_sourcePath));
}
```

- [ ] **Step 2: Run the tests and verify red**

Run: `dotnet test tests/MS3DPRINT.Manager.Core.Tests/MS3DPRINT.Manager.Core.Tests.csproj --filter FullyQualifiedName~ProjectFileTransferServiceTests`

Expected: FAIL because `ProjectFileTransferService` does not exist.

- [ ] **Step 3: Implement the guarded move**

```csharp
public string Move(string sourcePath, string projectPath, ProjectFileDestination destination)
{
    var source = Path.GetFullPath(sourcePath);
    var project = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectPath));
    var targetDirectory = ResolveInsideProject(project, destination.RelativeDirectory);
    var target = Path.Combine(targetDirectory, destination.FileName);
    if (!File.Exists(source)) throw new FileNotFoundException("Le fichier source est introuvable.", source);
    if (!Directory.Exists(targetDirectory)) throw new DirectoryNotFoundException("Le dossier projet cible est introuvable.");
    if (File.Exists(target) || Directory.Exists(target)) throw new FolderConflictException($"Un fichier existe déjà : {target}");
    File.Move(source, target);
    return target;
}
```

`ResolveInsideProject` uses `Path.GetFullPath` and `Path.GetRelativePath`, rejecting rooted directory values and relative paths escaping through `..`.

- [ ] **Step 4: Run the tests and verify green**

Run: `dotnet test tests/MS3DPRINT.Manager.Core.Tests/MS3DPRINT.Manager.Core.Tests.csproj --filter FullyQualifiedName~ProjectFileTransferServiceTests --no-restore`

Expected: PASS.

- [ ] **Step 5: Commit**

Run: `git add src/MS3DPRINT.Manager.Core/Files tests/MS3DPRINT.Manager.Core.Tests/Files && git commit -m "feat: move project files without overwrite"`

### Task 3: Build the classification dialog

**Files:**
- Create: `src/MS3DPRINT.Manager.App/ViewModels/ClassifyFileViewModel.cs`
- Create: `src/MS3DPRINT.Manager.App/Views/ClassifyFileWindow.xaml`
- Create: `src/MS3DPRINT.Manager.App/Views/ClassifyFileWindow.xaml.cs`
- Modify: `src/MS3DPRINT.Manager.App/MainWindow.xaml`
- Modify: `src/MS3DPRINT.Manager.App/MainWindow.xaml.cs`
- Create: `tests/MS3DPRINT.Manager.Core.Tests/ViewModels/ClassifyFileViewModelTests.cs`
- Modify: `tests/MS3DPRINT.Manager.Core.Tests/AppMarkupTests.cs`

**Interfaces:**
- Consumes the classifier and move service from Tasks 1–2.
- Produces `ClassifyFileWindow` implementing `ICreatedFolderDialog`, with `CreatedPath` set to the moved file path.

- [ ] **Step 1: Write failing view-model and dialog tests**

```csharp
[Fact]
public void SelectingPdfAndProject_ProposesInvoiceFolderAndReferencedName()
{
    var viewModel = CreateViewModelWithProject("MPO-2026-001_OUTILLAGE");
    viewModel.SourcePath = Path.Combine(_root, "DEV2026-05.pdf");
    viewModel.SelectedProject = "MPO-2026-001_OUTILLAGE";

    Assert.Equal("01_DEVIS_FACTURES", viewModel.SelectedRelativeDirectory);
    Assert.Equal("DEV2026-05__MPO-2026-001.pdf", viewModel.FinalFileName);
}
```

Assert in markup that the dialog contains a source-file selection button, a project `ComboBox`, an editable destination `ComboBox`, a final-name preview, and `Déplacer le fichier`.

- [ ] **Step 2: Run the tests and verify red**

Run: `dotnet test tests/MS3DPRINT.Manager.Core.Tests/MS3DPRINT.Manager.Core.Tests.csproj --filter "FullyQualifiedName~ClassifyFileViewModelTests|FullyQualifiedName~ClassifyFileWindow"`

Expected: FAIL because the view model and dialog do not exist.

- [ ] **Step 3: Implement preview and guarded asynchronous transfer**

Use `Microsoft.Win32.OpenFileDialog` for the source. Enumerate client/project folders from `01_CLIENTS`, apply `ProjectFileClassifier.Suggest` when both selections exist, and restrict the editable destination list to existing project folders.

Before `Task.Run(() => transferService.Move(...))`, display:

```text
Déplacer ce fichier ?

Source : {source path}
Destination : {destination path}

Le fichier ne sera plus présent à son emplacement d’origine.
```

Proceed only after `MessageBoxResult.Yes`; leave the dialog open and display `UiErrorMessages.For(exception)` if transfer fails. Add a `Classer un fichier` card and click handler that opens the dialog through `Run`.

- [ ] **Step 4: Run the tests and verify green**

Run: `dotnet test tests/MS3DPRINT.Manager.Core.Tests/MS3DPRINT.Manager.Core.Tests.csproj --filter "FullyQualifiedName~ClassifyFileViewModelTests|FullyQualifiedName~ClassifyFileWindow" --no-restore`

Expected: PASS.

- [ ] **Step 5: Commit**

Run: `git add src/MS3DPRINT.Manager.App src/MS3DPRINT.Manager.Core.Tests && git commit -m "feat: add safe project file classification dialog"`

### Task 4: Verify and deliver

**Files:**
- Modify: `outputs/MS3DPRINT-Manager/MS3DPRINT.Manager.App.exe` (generated)

**Interfaces:**
- Consumes Tasks 1–3.
- Produces `C:\MS3DPRINT\Nextcloud\MS3DPRINT\MS3DPRINT.Manager.App.exe`.

- [ ] **Step 1: Run the full suite**

Run: `dotnet test tests/MS3DPRINT.Manager.Core.Tests/MS3DPRINT.Manager.Core.Tests.csproj --no-restore`

Expected: PASS with zero failures.

- [ ] **Step 2: Publish the portable executable**

Run: `dotnet restore src\MS3DPRINT.Manager.App\MS3DPRINT.Manager.App.csproj -r win-x64` then `dotnet publish src\MS3DPRINT.Manager.App\MS3DPRINT.Manager.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o outputs\MS3DPRINT-Manager --no-restore`.

- [ ] **Step 3: Verify one success and one collision manually**

Move a disposable PDF into an existing test project and verify its source disappears and its referenced target exists. Repeat with an existing target name and verify the source remains in place.

- [ ] **Step 4: Install after the app is closed**

Check `Get-Process -Name MS3DPRINT.Manager.App`; when no process exists, copy EXE and PDB files to `C:\MS3DPRINT\Nextcloud\MS3DPRINT` and compare SHA-256 hashes.

- [ ] **Step 5: Report delivery**

Report the verified test count, installed executable path, and explicit confirmation before every move.
