# Stockages et performances Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add placeholder storage-provider cards to Settings while retaining the active Nextcloud root.

**Architecture:** A small immutable catalog in Core defines storage providers and their availability. The Settings window consumes this catalog only for display; no cloud account, token, API call, or active-root change is introduced.

**Tech Stack:** .NET 8, WPF, xUnit, local SVG resources.

**Spec:** `docs/superpowers/specs/2026-09-29-storage-and-performance-settings-design.md`

## Global Constraints

- Keep `C:\MS3DPRINT\Nextcloud\MS3DPRINT` as the active root and do not add cloud authentication.
- Nextcloud is the sole active provider; Google Drive, Dropbox, and Dossier local are non-interactive “Bientôt disponible” cards.
- Bundle only locally stored provider logos from their official brand assets and retain their attribution/source record.
- Preserve the light and dark contrast rules.
- Never poll the filesystem or a cloud service from the Settings window.

---

### Task 1: Storage provider catalog

**Files:**
- Create: `src/MS3DPRINT.Manager.Core/Storage/StorageProviderKind.cs`
- Create: `src/MS3DPRINT.Manager.Core/Storage/StorageProviderDefinition.cs`
- Create: `src/MS3DPRINT.Manager.Core/Storage/StorageProviderCatalog.cs`
- Create: `tests/MS3DPRINT.Manager.Core.Tests/Storage/StorageProviderCatalogTests.cs`

**Interfaces:**
- Produces: `enum StorageProviderKind { Nextcloud, GoogleDrive, Dropbox, LocalFolder }`.
- Produces: `sealed record StorageProviderDefinition(StorageProviderKind Kind, string DisplayName, bool IsAvailable, string? RootPath)`.
- Produces: `IReadOnlyList<StorageProviderDefinition> StorageProviderCatalog.Create(string nextcloudRoot)`.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public void Create_ExposesNextcloudAsTheOnlyAvailableProvider()
{
    var providers = StorageProviderCatalog.Create(@"C:\MS3DPRINT\Nextcloud\MS3DPRINT");

    Assert.Collection(providers,
        provider => Assert.Equal(new(StorageProviderKind.Nextcloud, "Nextcloud", true, @"C:\MS3DPRINT\Nextcloud\MS3DPRINT"), provider),
        provider => Assert.Equal(new(StorageProviderKind.GoogleDrive, "Google Drive", false, null), provider),
        provider => Assert.Equal(new(StorageProviderKind.Dropbox, "Dropbox", false, null), provider),
        provider => Assert.Equal(new(StorageProviderKind.LocalFolder, "Dossier local", false, null), provider));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/MS3DPRINT.Manager.Core.Tests/MS3DPRINT.Manager.Core.Tests.csproj --filter "FullyQualifiedName~StorageProviderCatalogTests"`

Expected: FAIL because `StorageProviderCatalog` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
public static class StorageProviderCatalog
{
    public static IReadOnlyList<StorageProviderDefinition> Create(string nextcloudRoot) =>
    [
        new(StorageProviderKind.Nextcloud, "Nextcloud", true, nextcloudRoot),
        new(StorageProviderKind.GoogleDrive, "Google Drive", false, null),
        new(StorageProviderKind.Dropbox, "Dropbox", false, null),
        new(StorageProviderKind.LocalFolder, "Dossier local", false, null)
    ];
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/MS3DPRINT.Manager.Core.Tests/MS3DPRINT.Manager.Core.Tests.csproj --filter "FullyQualifiedName~StorageProviderCatalogTests"`

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/MS3DPRINT.Manager.Core/Storage tests/MS3DPRINT.Manager.Core.Tests/Storage/StorageProviderCatalogTests.cs
git commit -m "feat: add storage provider catalog"
```

### Task 2: Settings storage cards and packaged branding

**Files:**
- Create: `src/MS3DPRINT.Manager.App/Assets/Storage/nextcloud.svg`
- Create: `src/MS3DPRINT.Manager.App/Assets/Storage/google-drive.svg`
- Create: `src/MS3DPRINT.Manager.App/Assets/Storage/dropbox.svg`
- Create: `src/MS3DPRINT.Manager.App/Assets/Storage/SOURCES.md`
- Modify: `src/MS3DPRINT.Manager.App/MS3DPRINT.Manager.App.csproj`
- Modify: `src/MS3DPRINT.Manager.App/Views/SettingsWindow.xaml`
- Modify: `src/MS3DPRINT.Manager.App/Views/SettingsWindow.xaml.cs`
- Modify: `tests/MS3DPRINT.Manager.Core.Tests/AppMarkupTests.cs`

**Interfaces:**
- Consumes: `StorageProviderCatalog.Create(string nextcloudRoot)`.
- Produces: a scrollable Settings window with a non-interactive storage-card region.

- [ ] **Step 1: Write the failing markup test**

```csharp
[Fact]
public void SettingsWindow_ShowsAvailableAndPlannedStorageProviders()
{
    var document = LoadMarkup("src", "MS3DPRINT.Manager.App", "Views", "SettingsWindow.xaml");

    Assert.Contains(document.Descendants(), element => element.Name.LocalName == "TextBlock" && (string?)element.Attribute("Text") == "Stockages");
    Assert.Contains(document.Descendants(), element => element.Name.LocalName == "ItemsControl" && (string?)element.Attribute("Name") == "StorageProviders");
    Assert.Contains(document.Descendants(), element => element.Name.LocalName == "ScrollViewer" && (string?)element.Attribute("VerticalScrollBarVisibility") == "Auto");
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/MS3DPRINT.Manager.Core.Tests/MS3DPRINT.Manager.Core.Tests.csproj --filter "FullyQualifiedName~SettingsWindow_ShowsAvailableAndPlannedStorageProviders"`

Expected: FAIL because the storage section is absent.

- [ ] **Step 3: Add local branded assets and implement the cards**

Use the official Nextcloud, Google Drive, and Dropbox vector assets. Record the direct source URL and retrieval date in `SOURCES.md`. Mark the assets as `Resource` in the app project. Bind an `ItemsControl` named `StorageProviders` to the catalog. Render Nextcloud with its root path and “Actif”; render the three unavailable providers with “Bientôt disponible”, `IsHitTestVisible="False"`, and theme resources.

- [ ] **Step 4: Run the focused test and build**

Run: `dotnet test tests/MS3DPRINT.Manager.Core.Tests/MS3DPRINT.Manager.Core.Tests.csproj --filter "FullyQualifiedName~SettingsWindow_ShowsAvailableAndPlannedStorageProviders"; dotnet build MS3DPRINT.Manager.sln -c Release --no-restore`

Expected: test PASS and build succeeds.

- [ ] **Step 5: Commit**

```bash
git add src/MS3DPRINT.Manager.App tests/MS3DPRINT.Manager.Core.Tests/AppMarkupTests.cs
git commit -m "feat: show planned storage providers in settings"
```

