# Résilience des données et de la navigation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Empêcher les doublons de fiches projet et conserver une application utilisable lorsqu'un catalogue Nextcloud est temporairement indisponible.

**Architecture:** `ProjectProfileStore` devient l'unique barrière contre deux fiches associées au même dossier projet. Les vues Clients et Projets encapsulent leur rafraîchissement dans un chemin sûr : elles affichent l'erreur métier dans leur propre écran au lieu de laisser une exception WPF remonter jusqu'au processus.

**Tech Stack:** .NET 8, WPF, xUnit.

**Spec:** `docs/superpowers/specs/2026-10-01-global-audit-design.md`

## Global Constraints

- Ne supprimer ni écraser aucun fichier utilisateur.
- Conserver la publication Windows x64 autonome.
- Ajouter un test qui échoue avant chaque correction de comportement.
- Conserver les messages en français et utiliser `UiErrorMessages` pour la traduction des exceptions.

---

### Task 1: Unicité d'une fiche projet

**Files:**
- Modify: `src/MS3DPRINT.Manager.Core/Projects/ProjectProfileStore.cs`
- Modify: `tests/MS3DPRINT.Manager.Core.Tests/Projects/ProjectProfileStoreTests.cs`

**Interfaces:**
- Consumes: `ProjectProfileStore.Create(ProjectProfile profile)`.
- Produces: un `InvalidOperationException` si `FolderName` ou `Reference` appartient déjà à une autre fiche projet.

- [x] **Step 1: Write the failing test**

```csharp
[Fact]
public void Create_RejectsASecondProfileForTheSameProjectFolder()
{
    var store = new ProjectProfileStore(new WorkspaceMetadataPaths(_root));
    var profile = CreateProfile();
    store.Create(profile);

    Assert.Throws<InvalidOperationException>(() =>
        store.Create(profile with { Id = Guid.NewGuid() }));
}
```

- [x] **Step 2: Run test to verify it fails**

Run: `dotnet test tests\MS3DPRINT.Manager.Core.Tests\MS3DPRINT.Manager.Core.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~ProjectProfileStoreTests.Create_RejectsASecondProfileForTheSameProjectFolder"`

Expected: FAIL because the current store only checks the JSON file id.

- [x] **Step 3: Write minimal implementation**

```csharp
if (File.Exists(Path(profile.Id)) || LoadAll().Any(existing =>
        string.Equals(existing.FolderName, profile.FolderName, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(existing.Reference, profile.Reference, StringComparison.OrdinalIgnoreCase)))
{
    throw new InvalidOperationException("Une fiche projet existe déjà pour ce dossier.");
}
```

- [x] **Step 4: Run test to verify it passes**

Run the command from Step 2. Expected: PASS.

- [x] **Step 5: Commit**

```powershell
git add src/MS3DPRINT.Manager.Core/Projects/ProjectProfileStore.cs tests/MS3DPRINT.Manager.Core.Tests/Projects/ProjectProfileStoreTests.cs
git commit -m "fix: prevent duplicate project profiles"
```

### Task 2: Rafraîchissement sûr des catalogues

**Files:**
- Modify: `src/MS3DPRINT.Manager.App/Views/ClientsView.xaml`
- Modify: `src/MS3DPRINT.Manager.App/Views/ClientsView.xaml.cs`
- Modify: `src/MS3DPRINT.Manager.App/Views/ProjectsView.xaml`
- Modify: `src/MS3DPRINT.Manager.App/Views/ProjectsView.xaml.cs`
- Modify: `tests/MS3DPRINT.Manager.Core.Tests/AppMarkupTests.cs`

**Interfaces:**
- Consumes: `ClientsViewModel.Refresh()` et `ProjectsViewModel.Refresh()` qui peuvent lever une exception de stockage.
- Produces: un message visible dans la page, sans exception non gérée dans l'événement WPF `Loaded` ou le bouton `Actualiser`.

- [x] **Step 1: Write the failing markup test**

```csharp
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
```

- [x] **Step 2: Run test to verify it fails**

Run: `dotnet test tests\MS3DPRINT.Manager.Core.Tests\MS3DPRINT.Manager.Core.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~CatalogViews_ExposeAnInPageLoadErrorArea"`

Expected: FAIL because neither page contains the error area.

- [x] **Step 3: Write minimal implementation**

```csharp
private void RefreshSafely()
{
    try
    {
        _viewModel.Refresh();
        LoadErrorText.Text = string.Empty;
    }
    catch (Exception exception)
    {
        LoadErrorText.Text = UiErrorMessages.For(exception);
    }
}
```

Use this method from each page's `Loaded` and `Actualiser` handler; the projects page calls `PopulateFilters()` only after a successful refresh.

- [x] **Step 4: Run test to verify it passes**

Run the command from Step 2. Expected: PASS.

- [x] **Step 5: Commit**

```powershell
git add src/MS3DPRINT.Manager.App/Views tests/MS3DPRINT.Manager.Core.Tests/AppMarkupTests.cs
git commit -m "fix: keep catalog errors inside their views"
```

### Task 3: Vérification de livraison

**Files:**
- Modify: `docs/superpowers/specs/2026-10-01-global-audit-design.md`

**Interfaces:**
- Consumes: solution and published executable.
- Produces: verification record for this resilience batch.

- [x] **Step 1: Run the complete test suite**

Run: `dotnet test MS3DPRINT.Manager.sln -c Release --no-restore`

Expected: zero failed tests.

- [x] **Step 2: Build the complete solution**

Run: `dotnet build MS3DPRINT.Manager.sln -c Release --no-restore`

Expected: zero warnings and zero errors.

- [x] **Step 3: Publish and verify the portable executable**

Run: `dotnet publish src\MS3DPRINT.Manager.App\MS3DPRINT.Manager.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true --no-restore -o outputs\MS3DPRINT-Manager`

Expected: `outputs\MS3DPRINT-Manager\MS3DPRINT.Manager.App.exe` exists.

- [x] **Step 4: Commit audit records**

```powershell
git add docs/superpowers/specs/2026-10-01-global-audit-design.md docs/superpowers/plans/2026-10-01-resilience-navigation.md
git commit -m "docs: record global audit remediation plan"
```
