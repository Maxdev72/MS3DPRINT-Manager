# Hardware Diagnostics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (- [ ]) syntax for tracking.

**Goal:** Display a cached, read-only CPU, thread, memory, and GPU hardware snapshot in Settings.

**Architecture:** Windows-specific hardware discovery remains in the WPF application layer behind a small query abstraction. A service converts query results to an immutable profile with user-readable fallbacks, then Settings loads it once asynchronously after its initial UI is visible.

**Tech Stack:** .NET 8 WPF, System.Management, xUnit.

**Spec:** docs/superpowers/specs/2026-09-29-storage-and-performance-settings-design.md

## Global Constraints

- The diagnostic is informational; no current file operation gets GPU acceleration or extra parallelism.
- Read hardware data once per Settings window and never benchmark or poll.
- Do not collect, display, store, or transmit serial numbers, network identifiers, or device IDs.
- A failed CPU, memory, or GPU query displays Non détecté and never prevents Settings from opening.
- Keep the UI readable in light and dark themes.

---

### Task 1: Hardware profile model and fallback normalization

**Files:**
- Create: src/MS3DPRINT.Manager.App/Hardware/HardwareProfile.cs
- Create: src/MS3DPRINT.Manager.App/Hardware/HardwareProfileFactory.cs
- Create: tests/MS3DPRINT.Manager.Core.Tests/Hardware/HardwareProfileFactoryTests.cs

**Interfaces:**
- Produces: sealed record HardwareProfile(string ProcessorName, int LogicalProcessorCount, string TotalMemory, IReadOnlyList<string> GraphicsAdapters).
- Produces: HardwareProfile HardwareProfileFactory.Create(string? processorName, int logicalProcessorCount, ulong? totalMemoryBytes, IEnumerable<string?> graphicsAdapters).

- [ ] **Step 1: Write the failing tests**

~~~
[Fact]
public void Create_NormalizesMissingHardwareFields()
{
    var profile = HardwareProfileFactory.Create(null, 0, null, [null, "  "]);

    Assert.Equal("Non détecté", profile.ProcessorName);
    Assert.Equal(1, profile.LogicalProcessorCount);
    Assert.Equal("Non détecté", profile.TotalMemory);
    Assert.Equal(["Non détecté"], profile.GraphicsAdapters);
}

[Fact]
public void Create_FormatsMemoryAndKeepsDetectedGraphicsAdapters()
{
    var profile = HardwareProfileFactory.Create("CPU Test", 16, 34_359_738_368, ["GPU A", "GPU B"]);

    Assert.Equal("CPU Test", profile.ProcessorName);
    Assert.Equal(16, profile.LogicalProcessorCount);
    Assert.Equal("32 Go", profile.TotalMemory);
    Assert.Equal(["GPU A", "GPU B"], profile.GraphicsAdapters);
}
~~~

- [ ] **Step 2: Run tests to verify they fail**

Run: dotnet test tests/MS3DPRINT.Manager.Core.Tests/MS3DPRINT.Manager.Core.Tests.csproj --filter "FullyQualifiedName~HardwareProfileFactoryTests"

Expected: FAIL because the profile model and factory do not exist.

- [ ] **Step 3: Write minimal implementation**

~~~
public static HardwareProfile Create(string? processorName, int logicalProcessorCount, ulong? totalMemoryBytes, IEnumerable<string?> graphicsAdapters)
{
    var adapters = graphicsAdapters.Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name!.Trim()).ToArray();
    return new HardwareProfile(
        string.IsNullOrWhiteSpace(processorName) ? "Non détecté" : processorName.Trim(),
        Math.Max(1, logicalProcessorCount),
        totalMemoryBytes is > 0 ? $"{Math.Round(totalMemoryBytes.Value / 1_073_741_824d):0} Go" : "Non détecté",
        adapters.Length == 0 ? ["Non détecté"] : adapters);
}
~~~

- [ ] **Step 4: Run tests to verify they pass**

Run: dotnet test tests/MS3DPRINT.Manager.Core.Tests/MS3DPRINT.Manager.Core.Tests.csproj --filter "FullyQualifiedName~HardwareProfileFactoryTests"

Expected: PASS.

- [ ] **Step 5: Commit**

~~~
git add src/MS3DPRINT.Manager.App/Hardware tests/MS3DPRINT.Manager.Core.Tests/Hardware/HardwareProfileFactoryTests.cs
git commit -m "feat: add normalized hardware profile"
~~~

### Task 2: Windows hardware query and per-window cache

**Files:**
- Create: src/MS3DPRINT.Manager.App/Hardware/IHardwareInfoProvider.cs
- Create: src/MS3DPRINT.Manager.App/Hardware/WindowsHardwareInfoProvider.cs
- Modify: src/MS3DPRINT.Manager.App/MS3DPRINT.Manager.App.csproj
- Create: tests/MS3DPRINT.Manager.Core.Tests/Hardware/WindowsHardwareInfoProviderTests.cs

**Interfaces:**
- Produces: interface IHardwareInfoProvider { HardwareProfile GetHardwareProfile(); }.
- Produces: sealed class WindowsHardwareInfoProvider : IHardwareInfoProvider.
- Consumes: the profile factory from Task 1.

- [ ] **Step 1: Write the failing cache test**

~~~
[Fact]
public void GetHardwareProfile_QueriesWindowsOnlyOnce()
{
    var query = new FakeHardwareQuery("CPU Test", 8, 17_179_869_184, ["GPU Test"]);
    var provider = new WindowsHardwareInfoProvider(query);

    _ = provider.GetHardwareProfile();
    _ = provider.GetHardwareProfile();

    Assert.Equal(1, query.QueryCount);
}
~~~

- [ ] **Step 2: Run test to verify it fails**

Run: dotnet test tests/MS3DPRINT.Manager.Core.Tests/MS3DPRINT.Manager.Core.Tests.csproj --filter "FullyQualifiedName~WindowsHardwareInfoProviderTests"

Expected: FAIL because the provider and query seam do not exist.

- [ ] **Step 3: Implement a narrow Windows query seam**

Add the free System.Management package to the WPF app project. Define an internal IWindowsHardwareQuery returning processor name, total physical memory and display-adapter names from Win32_Processor, Win32_ComputerSystem, and Win32_VideoController. Keep no device IDs. Wrap each WMI query so a failure returns a null or empty value. Pass Environment.ProcessorCount to the factory. Cache the completed profile in a Lazy<HardwareProfile>.

- [ ] **Step 4: Run focused tests and build**

Run: dotnet test tests/MS3DPRINT.Manager.Core.Tests/MS3DPRINT.Manager.Core.Tests.csproj --filter "FullyQualifiedName~Hardware"; dotnet build MS3DPRINT.Manager.sln -c Release --no-restore

Expected: all focused tests PASS and the application builds with the new package reference.

- [ ] **Step 5: Commit**

~~~
git add src/MS3DPRINT.Manager.App/Hardware src/MS3DPRINT.Manager.App/MS3DPRINT.Manager.App.csproj tests/MS3DPRINT.Manager.Core.Tests/Hardware
git commit -m "feat: detect cached Windows hardware profile"
~~~

### Task 3: Settings performance diagnostics

**Files:**
- Modify: src/MS3DPRINT.Manager.App/Views/SettingsWindow.xaml
- Modify: src/MS3DPRINT.Manager.App/Views/SettingsWindow.xaml.cs
- Modify: tests/MS3DPRINT.Manager.Core.Tests/AppMarkupTests.cs

**Interfaces:**
- Consumes: IHardwareInfoProvider.GetHardwareProfile().
- Produces: a read-only Performance section with CPU, thread, memory, GPU, and future-acceleration explanation.

- [ ] **Step 1: Write the failing markup test**

~~~
[Fact]
public void SettingsWindow_ShowsReadOnlyPerformanceDiagnostics()
{
    var document = LoadMarkup("src", "MS3DPRINT.Manager.App", "Views", "SettingsWindow.xaml");

    Assert.Contains(document.Descendants(), element => element.Name.LocalName == "TextBlock" && (string?)element.Attribute("Text") == "Performances");
    Assert.Contains(document.Descendants(), element => element.Name.LocalName == "ItemsControl" && (string?)element.Attribute("Name") == "GraphicsAdapters");
    Assert.Contains(document.Descendants(), element => element.Name.LocalName == "TextBlock" && ((string?)element.Attribute("Text") ?? string.Empty).Contains("GPU"));
}
~~~

- [ ] **Step 2: Run test to verify it fails**

Run: dotnet test tests/MS3DPRINT.Manager.Core.Tests/MS3DPRINT.Manager.Core.Tests.csproj --filter "FullyQualifiedName~SettingsWindow_ShowsReadOnlyPerformanceDiagnostics"

Expected: FAIL because the performance section is absent.

- [ ] **Step 3: Implement non-blocking display**

Inject IHardwareInfoProvider into SettingsWindow. After InitializeComponent, display static settings immediately, then load the cached profile with await Task.Run(provider.GetHardwareProfile). Bind ProcessorName, LogicalProcessorCount, TotalMemory, and an ItemsControl named GraphicsAdapters. Display Automatique — préparation pour futurs traitements and explain that current file operations do not use GPU acceleration. On an unexpected load failure, bind the factory fallback profile.

- [ ] **Step 4: Run focused test and full test suite**

Run: dotnet test tests/MS3DPRINT.Manager.Core.Tests/MS3DPRINT.Manager.Core.Tests.csproj --filter "FullyQualifiedName~SettingsWindow_ShowsReadOnlyPerformanceDiagnostics"; dotnet test MS3DPRINT.Manager.sln -c Release --no-restore

Expected: focused test PASS and the full suite PASS.

- [ ] **Step 5: Commit**

~~~
git add src/MS3DPRINT.Manager.App/Views/SettingsWindow.xaml src/MS3DPRINT.Manager.App/Views/SettingsWindow.xaml.cs tests/MS3DPRINT.Manager.Core.Tests/AppMarkupTests.cs
git commit -m "feat: show hardware diagnostics in settings"
~~~

