using System.Globalization;
using System.Text.RegularExpressions;
using MS3DPRINT.Manager.Desktop;

namespace MS3DPRINT.Manager.Core.Tests;

/// <summary>Keeps the interface on one scale: the seven text sizes and the 4 px spacing grid of <see cref="Ui"/>.</summary>
public sealed partial class DesignTokenTests
{
    // Every margin, padding and gap is on the grid since 9 November 2026: none may stray again.
    private const int OffGridSpacingBaseline = 0;

    [Fact]
    public void Theme_type_scale_matches_the_code_scale()
    {
        var theme = File.ReadAllText(Path.Combine(DesktopSource(), "Styles", "Theme.axaml"));
        double Token(string key) => double.Parse(Regex.Match(theme, $"x:Key=\"{key}\">([\\d.]+)<").Groups[1].Value, CultureInfo.InvariantCulture);
        Assert.Equal(
            [Ui.Text.Micro, Ui.Text.Caption, Ui.Text.Body, Ui.Text.Section, Ui.Text.Title, Ui.Text.Page, Ui.Text.Display],
            new[] { "Micro", "Caption", "Body", "Section", "Title", "Page", "Display" }.Select(name => Token("FontSize" + name)));
    }

    [Fact]
    public void Text_sizes_come_only_from_the_scale()
    {
        var offenders = SourceFiles().Where(file => Path.GetFileName(file) != "Ui.cs")
            .SelectMany(file => LiteralFontSize().Matches(File.ReadAllText(file)).Select(match => Path.GetFileName(file) + ": " + match.Value))
            .ToArray();
        Assert.True(offenders.Length == 0, "Taille de police écrite en dur — utilisez Ui.Text ou {DynamicResource FontSize…} :\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void Off_grid_spacing_never_grows()
    {
        var count = SourceFiles().Sum(file => OffGridValues(file).Count());
        Assert.True(count <= OffGridSpacingBaseline,
            $"{count} marges ou écarts hors de la grille de 4 px (maximum {OffGridSpacingBaseline}). Utilisez Ui.Space : 4, 8, 12, 16, 24, 32, 48.");
    }

    [Fact]
    public void Screens_use_only_the_two_corner_radii_and_pills()
    {
        var allowed = new[] { 0, Ui.Radius.Control, Ui.Radius.Card, Ui.Radius.Pill };
        var offenders = SourceFiles()
            .SelectMany(file => (file.EndsWith(".axaml", StringComparison.Ordinal) ? AxamlRadius() : CodeRadius()).Matches(File.ReadAllText(file))
                .SelectMany(match => match.Groups[1].Value.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries))
                .Where(value => !allowed.Contains(double.Parse(value, CultureInfo.InvariantCulture)))
                .Select(value => Path.GetFileName(file) + ": " + value))
            .ToArray();
        Assert.True(offenders.Length == 0, "Arrondi hors échelle — 6 pour les contrôles, 10 pour les cartes :\n" + string.Join("\n", offenders));
    }

    private static IEnumerable<string> OffGridValues(string file)
    {
        var text = File.ReadAllText(file);
        var patterns = file.EndsWith(".axaml", StringComparison.Ordinal) ? new[] { AxamlSpacing() } : new[] { CodeThickness(), CodeSpacing() };
        return patterns.SelectMany(pattern => pattern.Matches(text))
            .SelectMany(match => match.Groups[1].Value.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries))
            .Select(value => Math.Abs(double.Parse(value, CultureInfo.InvariantCulture)))
            // Beyond the largest step, large offsets stay on an 8 px multiple.
            .Where(value => !Ui.Space.Steps.Contains(value) && !(value > Ui.Space.S7 && value % 8 == 0))
            .Select(value => value.ToString(CultureInfo.InvariantCulture));
    }

    private static IEnumerable<string> SourceFiles() => Directory.EnumerateFiles(DesktopSource(), "*.*", SearchOption.AllDirectories)
        .Where(file => (file.EndsWith(".cs", StringComparison.Ordinal) || file.EndsWith(".axaml", StringComparison.Ordinal))
            && !file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar));

    private static string DesktopSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MS3DPRINT.Manager.sln"))) directory = directory.Parent;
        return Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found."), "src", "MS3DPRINT.Manager.Desktop");
    }

    [GeneratedRegex(@"FontSize\s*=\s*\d|FontSize=""\d|Property=""FontSize"" Value=""\d")]
    private static partial Regex LiteralFontSize();
    // Attributes on controls and Setter values in the style sheets.
    [GeneratedRegex(@"(?:\b(?:Margin|Padding|Spacing|ColumnSpacing|RowSpacing|ItemSpacing|LineSpacing)=|Property=""(?:Margin|Padding|Spacing|ColumnSpacing|RowSpacing|ItemSpacing|LineSpacing)"" Value=)""([-\d., ]+)""")]
    private static partial Regex AxamlSpacing();
    [GeneratedRegex(@"(?:\bCornerRadius=|Property=""CornerRadius"" Value=|x:Key=""\w*CornerRadius"">)""?([\d., ]+)[""<]")]
    private static partial Regex AxamlRadius();
    [GeneratedRegex(@"new (?:Avalonia\.)?CornerRadius\(([\d., ]+)\)")]
    private static partial Regex CodeRadius();
    [GeneratedRegex(@"new (?:Avalonia\.)?Thickness\(([-\d., ]+)\)")]
    private static partial Regex CodeThickness();
    [GeneratedRegex(@"\b(?:Spacing|ColumnSpacing|RowSpacing|ItemSpacing|LineSpacing)\s*=\s*(-?\d+(?:\.\d+)?)\b")]
    private static partial Regex CodeSpacing();
}
