# Fast 3D Viewer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a GPU-backed STL/OBJ viewer with wireframe and color controls, while preserving the legacy viewer as a fallback and improving interaction smoothness.

**Architecture:** Keep `ModelPreviewWindow` as the WPF compatibility viewer. Introduce an independent SharpDX scene loader and `GpuModelPreviewWindow`; route existing entry points through a single launcher. UI-only mode and color changes reuse loaded geometry. Measure dense-model behavior before installing the portable executable.

**Tech Stack:** .NET 8 WPF, HelixToolkit.Wpf 3.1.2, HelixToolkit.Wpf.SharpDX 3.1.2, xUnit.

**Spec:** `docs/superpowers/specs/2026-10-01-fast-3d-viewer-design.md`

## Global Constraints

- Windows x64 single-file self-contained portable executable; no paid dependencies.
- STL/OBJ originals are read-only and never rewritten.
- An unhandled viewer failure must not close the manager; the WPF viewer remains available.
- UI mode/color changes must not reload the model file or reset the camera.
- Only the latest requested model may replace the displayed scene.

---

### Task 1: GPU scene import and metrics

**Files:**
- Modify: `src/MS3DPRINT.Manager.App/MS3DPRINT.Manager.App.csproj`
- Create: `src/MS3DPRINT.Manager.App/Preview/GpuModelLoader.cs`
- Create: `src/MS3DPRINT.Manager.App/Preview/GpuPreviewScene.cs`
- Create: `tests/MS3DPRINT.Manager.Core.Tests/Preview/GpuModelLoaderTests.cs`

**Interfaces:**
- Produces: `GpuModelLoader.Load(string path)` returning `GpuPreviewScene`; `GpuPreviewScene` exposes `Parts`, `Bounds`, and `TriangleCount`.
- Consumes: HelixToolkit SharpDX `StLReader`, `ObjReader`, `MeshGeometry3D` and existing `ThreeDFileSupport`.

- [ ] **Step 1: Write failing tests** for one-triangle ASCII STL, binary STL, OBJ, malformed input and triangle count. Build fixtures in isolated temporary directories and assert dimensions/triangle count from literal expected values. The first test should call `GpuModelLoader.Load(path)` and expect exactly one triangle.
- [ ] **Step 2: Run the focused tests** with `dotnet test tests/MS3DPRINT.Manager.Core.Tests --filter FullyQualifiedName~GpuModelLoaderTests --no-restore`; verify the missing API is the only failure, then add `HelixToolkit.Wpf.SharpDX` version `3.1.2` to the app project and restore.
- [ ] **Step 3: Implement the loader**: select reader by case-insensitive extension, open with `FileShare.ReadWrite`, reject empty geometry, collect mesh parts and calculate triangle count as the sum of `TriangleIndices.Count / 3`. Calculate bounds from actual positions, not from the file name or STL header. Preserve OBJ submeshes and transformations; source files remain untouched.
- [ ] **Step 4: Re-run focused tests**, then `dotnet test MS3DPRINT.Manager.sln -c Release --no-restore`. Commit loader and tests.

### Task 2: GPU viewer with safe entry points

**Files:**
- Create: `src/MS3DPRINT.Manager.App/Views/GpuModelPreviewWindow.xaml`
- Create: `src/MS3DPRINT.Manager.App/Views/GpuModelPreviewWindow.xaml.cs`
- Create: `src/MS3DPRINT.Manager.App/Preview/ModelPreviewLauncher.cs`
- Modify: `src/MS3DPRINT.Manager.App/MainWindow.xaml.cs`
- Modify: `src/MS3DPRINT.Manager.App/Views/ProjectDetailView.xaml.cs`
- Modify: `src/MS3DPRINT.Manager.App/Views/CollectionDetailView.xaml.cs`
- Create: `tests/MS3DPRINT.Manager.Core.Tests/Preview/GpuPreviewWindowSmokeTests.cs`

**Interfaces:**
- Consumes: `GpuModelLoader.Load(string)`, `GpuPreviewScene` from Task 1.
- Produces: `ModelPreviewLauncher.Show(string? path, Window owner)`; all three existing preview entry points call it.

- [ ] **Step 1: Write a failing STA smoke test** that constructs and closes `GpuModelPreviewWindow`, plus markup/route tests verifying every preview entry point uses `ModelPreviewLauncher`.
- [ ] **Step 2: Run focused tests** to see missing GPU viewer/route failures.
- [ ] **Step 3: Create the GPU window** with a `Viewport3DX`, `DefaultEffectsManager`, one ambient and one directional light, a fixed default camera, and scene items created from loaded meshes. Disable unnecessary hit-testing, shadows and maximal MSAA. Load the scene through `Task.Run`, discard stale results using a monotonically increasing request version, show triangle count/dimensions/loading time, and release DirectX resources when the window closes. Recenter from tested model bounds; avoid relying on the reported `ZoomExtents` regression.
- [ ] **Step 4: Make the launcher** create the GPU viewer, catch startup/render errors and offer the existing WPF viewer as compatibility mode. Update the MainWindow, project detail and collection detail entry points; retain drag-and-drop and file picker in the GPU viewer.
- [ ] **Step 5: Re-run focused tests and the complete suite**, then commit.

### Task 3: Display modes and color without reload

**Files:**
- Create: `src/MS3DPRINT.Manager.App/Preview/PreviewDisplaySettings.cs`
- Modify: `src/MS3DPRINT.Manager.App/Views/GpuModelPreviewWindow.xaml`
- Modify: `src/MS3DPRINT.Manager.App/Views/GpuModelPreviewWindow.xaml.cs`
- Create: `tests/MS3DPRINT.Manager.Core.Tests/Preview/PreviewDisplaySettingsTests.cs`

**Interfaces:**
- Produces: `PreviewDisplaySettings.TryParseHexColor(string, out Color)` and `PreviewDisplaySettings.Apply(MeshGeometryModel3D, bool wireframe, Color color)`.
- Consumes: GPU meshes from Tasks 1–2.

- [ ] **Step 1: Write failing tests** for `#RRGGBB`, invalid input, mode switch and material color application. Verify the same geometry object remains attached after mode/color changes.
- [ ] **Step 2: Run focused tests** and verify expected failures.
- [ ] **Step 3: Add compact controls** (`Solide` / `Filaire`, preset swatches and editable `#RRGGBB` field). Set SharpDX `FillMode` and material color in place, with a validation message on invalid hex; do not call `GpuModelLoader.Load` and do not change camera state in these handlers.
- [ ] **Step 4: Re-run focused tests and the complete suite**, then commit.

### Task 4: Benchmark, regression checks and portable delivery

**Files:**
- Modify: `tests/MS3DPRINT.Manager.Core.Tests/Preview/GpuModelLoaderTests.cs` only if the synthetic geometry reveals a correctness regression.
- Generated: `outputs/MS3DPRINT-Manager/MS3DPRINT.Manager.App.exe` (ignored, not committed).

**Interfaces:**
- Consumes: completed GPU viewer and existing publish profile.
- Produces: measured load/render observations and a verified installed executable.

- [ ] **Step 1: Measure baseline** with a generated dense STL (at least 250,000 triangles) in the existing WPF viewer; record loading time, CPU usage and interactive FPS/latency. Do not turn nondeterministic timing into a CI pass/fail threshold.
- [ ] **Step 2: Measure the same file** in the GPU viewer, including solid and wireframe rotation/zoom. If the GPU path is not materially smoother, do not replace the installed executable; diagnose the bottleneck and add a bounded preview-quality option before retrying.
- [ ] **Step 3: Verify** `dotnet test MS3DPRINT.Manager.sln -c Release --no-restore`, `dotnet build MS3DPRINT.Manager.sln -c Release --no-restore`, and portable `dotnet publish` with win-x64 self-contained single-file settings. Smoke-launch the published executable and test a missing/bad file without manager crash.
- [ ] **Step 4: Check no installed app process is running**, save the prior installed executable outside Nextcloud, copy the new one to `C:\MS3DPRINT\Nextcloud\MS3DPRINT\MS3DPRINT.Manager.App.exe`, and compare SHA-256 hashes. Commit source changes and report measured results plus any untested real-model caveat.
