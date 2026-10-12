namespace MS3DPRINT.Manager.Core.Tests;

public sealed class AvaloniaProjectBoundaryTests
{
    [Fact]
    public void DesktopProject_targets_net10_without_a_windows_suffix()
    {
        var projectPath = Path.Combine(RepositoryRoot(), "src", "MS3DPRINT.Manager.Desktop", "MS3DPRINT.Manager.Desktop.csproj");

        Assert.True(File.Exists(projectPath), "The Avalonia desktop project must exist.");

        var project = File.ReadAllText(projectPath);
        Assert.Contains("<TargetFramework>net10.0</TargetFramework>", project);
        Assert.DoesNotContain("UseWPF", project, StringComparison.Ordinal);
    }

    [Fact]
    public void CoreProject_has_no_ui_framework_package_reference()
    {
        var projectPath = Path.Combine(RepositoryRoot(), "src", "MS3DPRINT.Manager.Core", "MS3DPRINT.Manager.Core.csproj");
        var project = File.ReadAllText(projectPath);

        Assert.DoesNotContain("Avalonia", project, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Wpf", project, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DesktopProject_does_not_reference_the_windows_adapter()
    {
        var projectPath = Path.Combine(RepositoryRoot(), "src", "MS3DPRINT.Manager.Desktop", "MS3DPRINT.Manager.Desktop.csproj");
        var project = File.ReadAllText(projectPath);

        Assert.DoesNotContain("MS3DPRINT.Manager.Windows", project, StringComparison.Ordinal);
    }

    [Fact]
    public void Repository_pins_the_stable_dotnet_10_sdk()
    {
        var globalJson = Path.Combine(RepositoryRoot(), "global.json");

        Assert.True(File.Exists(globalJson), "global.json must pin the stable .NET 10 SDK.");
        var json = File.ReadAllText(globalJson);
        Assert.Contains("\"version\": \"10.0.401\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsHost_composes_the_shared_desktop_and_windows_adapters()
    {
        var projectPath = Path.Combine(RepositoryRoot(), "src", "MS3DPRINT.Manager.Windows.App", "MS3DPRINT.Manager.Windows.App.csproj");

        Assert.True(File.Exists(projectPath), "The Windows host project must exist.");

        var project = File.ReadAllText(projectPath);
        Assert.Contains("<TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>", project, StringComparison.Ordinal);
        Assert.Contains("MS3DPRINT.Manager.Desktop", project, StringComparison.Ordinal);
        Assert.Contains("MS3DPRINT.Manager.Windows", project, StringComparison.Ordinal);
    }

    [Fact]
    public void Sources_contain_no_wpf_reference()
    {
        var root = Path.Combine(RepositoryRoot(), "src");
        var offenders = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(path => path.EndsWith(".cs", StringComparison.Ordinal) || path.EndsWith(".csproj", StringComparison.Ordinal) || path.EndsWith(".xaml", StringComparison.Ordinal))
            .Where(path => path.EndsWith(".xaml", StringComparison.Ordinal) ||
                           File.ReadAllText(path) is var text && (text.Contains("UseWPF", StringComparison.Ordinal) || text.Contains("System.Windows.", StringComparison.Ordinal) ||
                                                                  text.Contains("MaterialDesign", StringComparison.Ordinal) || text.Contains("HelixToolkit", StringComparison.Ordinal)))
            .ToArray();

        Assert.True(offenders.Length == 0, "WPF references found: " + string.Join(", ", offenders));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MS3DPRINT.Manager.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
