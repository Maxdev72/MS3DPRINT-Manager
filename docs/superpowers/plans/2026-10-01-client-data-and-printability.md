# Client Data and Printability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver professional-only autocomplete, automatic refresh, readable calendars, actionable classification errors, and advisory wall analysis in the portable Windows app.

**Architecture:** Keep cloud API adapters and geometry calculations independent from WPF. Let WPF trigger debounced/cancellable work and consume typed results. Refresh only the visible view on changes; keep file moves non-overwriting and non-destructive on error.

**Tech Stack:** C#/.NET 8, WPF, HelixToolkit SharpDX, xUnit.

**Spec:** `docs/superpowers/specs/2026-10-01-client-data-and-printability-design.md`

## Global Constraints

- Storage root remains `C:\MS3DPRINT\Nextcloud\MS3DPRINT` by default.
- No paid dependency and no Python runtime.
- No overwrite or deletion of an existing user file without confirmation.
- Existing profile JSON must remain readable.
- Wall analysis must state its limits and never claim guaranteed printability.

---

### Task 1: Professional client completion

**Files:** `src/MS3DPRINT.Manager.Core/Clients/ClientProfile.cs`, new lookup adapter files in `src/MS3DPRINT.Manager.App/Clients/`, `src/MS3DPRINT.Manager.App/Views/CreateClientWindow.xaml*`, `src/MS3DPRINT.Manager.App/ViewModels/CreateClientViewModel.cs`, relevant client tests.

**Interfaces:** Produce an optional SIRET field in the profile and an async company/address suggestion provider consumed by the creation dialog.

- [ ] Add failing tests: legacy profile JSON loads; individual profiles omit professional-only data; parsed company suggestions retain name/SIRET/address.
- [ ] Run focused tests and confirm missing behavior fails.
- [ ] Implement typed public-API lookup with debounce/cancellation, manual fallback, and professional-only controls.
- [ ] Run focused tests and full tests.

### Task 2: Refresh and calendar

**Files:** `src/MS3DPRINT.Manager.App/MainWindow.xaml.cs`, visible catalog/detail views, `src/MS3DPRINT.Manager.App/App.xaml`, relevant WPF tests.

**Interfaces:** A debounced storage-change notification refreshes the visible page and dashboard; successful dialogs do the same.

- [ ] Add failing tests for refresh decisions and popup calendar contrast states.
- [ ] Run focused tests to verify failure.
- [ ] Implement event-driven/debounced refresh and explicit calendar colors.
- [ ] Run focused tests and full tests.

### Task 3: Classification diagnostics

**Files:** `src/MS3DPRINT.Manager.Core/Files/ProjectFileTransferService.cs`, `src/MS3DPRINT.Manager.App/Views/ClassifyFileWindow.xaml.cs`, relevant file tests.

**Interfaces:** A typed transfer failure carries operation and source/destination paths; successful move still returns target path.

- [ ] Reproduce source/destination denial using isolated test directories or injected file operations, then add a failing diagnostic test.
- [ ] Run focused tests to verify failure.
- [ ] Implement precise error reporting without copy-delete fallback or overwrite.
- [ ] Run focused tests and full tests.

### Task 4: Advisory wall analysis

**Files:** new mesh-analysis files in `src/MS3DPRINT.Manager.App/Preview/` or `Core`, `src/MS3DPRINT.Manager.App/Views/GpuModelPreviewWindow.xaml*`, relevant geometry tests.

**Interfaces:** Accept loaded mesh geometry, millimeter scale and minimum thickness; return mesh-validity flags and sampled thin regions. Analysis is cancellable and runs outside the UI thread.

- [ ] Add failing tests for known thick/thin closed meshes, open mesh limitations, and cancellation.
- [ ] Run focused tests to verify failure.
- [ ] Implement bounded-cost mesh analysis and advisory UI; do not modify model files.
- [ ] Run focused tests and full tests.

### Task 5: Integration and delivery

**Files:** affected tests, documentation and portable publish output only.

- [ ] Run full Release tests and inspect the complete output.
- [ ] Run UI smoke checks for dialogs/viewer on Windows and inspect screenshot evidence.
- [ ] Publish self-contained single-file portable executable, then verify its path and version.
- [ ] Review the full diff and preserve unrelated user files.
