# File Preview and 3D Usability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prévisualiser images et PDF dans le logiciel et rendre le visualiseur STL/OBJ utilisable par glisser-déposer avec dimensions.

**Architecture:** `PreviewFileSupport` choisit le type de fichier dans Core. `DocumentPreviewWindow` charge des images WPF ou rend une page PDF avec `Windows.Data.Pdf`. `ModelPreviewWindow` accepte les fichiers déposés et affiche `Model3D.Bounds`.

**Tech Stack:** .NET 8, WPF, Windows.Data.Pdf, HelixToolkit.Wpf, xUnit.

**Spec:** `docs/superpowers/specs/2026-10-01-file-preview-and-3d-usability-design.md`

## Global Constraints

- Ne jamais modifier les fichiers ouverts en aperçu.
- Conserver un seul exécutable Windows x64 portable au chemin de livraison habituel.
- Ne pas ajouter 3MF à ce lot.

---

### Task 1: Classification et rendu de documents

**Files:**
- Create: `src/MS3DPRINT.Manager.Core/Files/PreviewFileSupport.cs`
- Create: `src/MS3DPRINT.Manager.App/Views/DocumentPreviewWindow.xaml`
- Create: `src/MS3DPRINT.Manager.App/Views/DocumentPreviewWindow.xaml.cs`
- Create: `src/MS3DPRINT.Manager.App/Preview/PdfPageRenderer.cs`
- Modify: `src/MS3DPRINT.Manager.App/MS3DPRINT.Manager.App.csproj`
- Modify: `tests/MS3DPRINT.Manager.Core.Tests/MS3DPRINT.Manager.Core.Tests.csproj`
- Test: `tests/MS3DPRINT.Manager.Core.Tests/Files/PreviewFileSupportTests.cs`
- Test: `tests/MS3DPRINT.Manager.Core.Tests/Preview/PdfPageRendererTests.cs`

**Interfaces:** `PreviewFileSupport.GetKind(string path)` returns `PreviewFileKind.None/Image/Pdf`. `PdfPageRenderer.LoadAsync(string path)` returns a PDF document wrapper with `PageCount` and `RenderPageAsync(int index)` returning PNG bytes. `DocumentPreviewWindow(string path)` displays one image or page and handles previous/next.

- [x] Write tests for case-insensitive image/PDF classification, unsupported extensions, a one-page PDF fixture, and invalid page index.
- [x] Run `dotnet test MS3DPRINT.Manager.sln -c Release --no-restore` and confirm these tests fail for the missing APIs.
- [x] Implement Core classification, Windows API target TFM and the PDF renderer, then the WPF preview window. Keep rendering asynchronous and bound image memory limited.
- [x] Run the same test command and confirm all tests pass.

### Task 2: Wire all file entry points

**Files:**
- Modify: `src/MS3DPRINT.Manager.App/MainWindow.xaml.cs`
- Modify: `src/MS3DPRINT.Manager.App/Views/ProjectDetailView.xaml.cs`
- Modify: `src/MS3DPRINT.Manager.App/Views/CollectionDetailView.xaml.cs`

**Interfaces:** On selecting an image/PDF result, construct `DocumentPreviewWindow(path)` with the main window as owner; retain the existing 3D viewer for STL/OBJ and Windows open for other formats.

- [x] Add a WPF smoke/integration test for the supported preview entry path where practicable, or a focused executable behavior test of the routing helper.
- [x] Verify it fails before routing is added.
- [x] Integrate the three entry points, catching preview failures in the existing UI error handling.
- [x] Run tests and build with zero errors and warnings.

### Task 3: 3D file selection and dimensions

**Files:**
- Modify: `src/MS3DPRINT.Manager.App/Views/ModelPreviewWindow.xaml`
- Modify: `src/MS3DPRINT.Manager.App/Views/ModelPreviewWindow.xaml.cs`
- Modify: `src/MS3DPRINT.Manager.App/MainWindow.xaml.cs`
- Create: `src/MS3DPRINT.Manager.App/Preview/ModelFileSelection.cs`
- Create: `src/MS3DPRINT.Manager.App/Preview/ModelDimensions.cs`
- Test: `tests/MS3DPRINT.Manager.Core.Tests/Preview/ModelFileSelectionTests.cs`
- Test: `tests/MS3DPRINT.Manager.Core.Tests/Preview/ModelDimensionsTests.cs`

**Interfaces:** `ModelPreviewWindow()` opens empty, `ModelPreviewWindow(string path)` opens a file, and drop/browse both use `ModelFileSelection.Select` to validate one existing STL/OBJ. `ModelDimensions.FromBounds(Rect3D)` returns X/Y/Z lengths for the label.

- [x] Write tests for valid/invalid drop, missing file and geometry bounds; run them red.
- [x] Implement selection and dimensions, then wire drop/browse and empty launch from the main menu.
- [x] Run tests and build green; STA smoke test verifies window construction. Interactive visual review was unavailable.

### Task 4: Publication

**Files:**
- Modify: `README.md`

- [x] Document previewable formats, Windows version floor and the unitless dimension label.
- [x] Run `dotnet test MS3DPRINT.Manager.sln -c Release --no-restore` and `dotnet build MS3DPRINT.Manager.sln -c Release --no-restore`.
- [x] Publish self-contained single-file win-x64, verify the app is closed, copy to `C:\MS3DPRINT\Nextcloud\MS3DPRINT\MS3DPRINT.Manager.App.exe`, compare SHA256 hashes, and commit the source changes.
