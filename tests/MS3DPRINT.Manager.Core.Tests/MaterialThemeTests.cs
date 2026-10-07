using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MS3DPRINT.Manager.App;
using MS3DPRINT.Manager.App.Views;
using MS3DPRINT.Manager.Core.Storage;

namespace MS3DPRINT.Manager.Core.Tests;

[Collection("Responsive layout UI")]
public sealed class MaterialThemeTests
{
    public static IEnumerable<object[]> AccentCombinations()
    {
        foreach (var theme in new[] { ThemePreference.Light, ThemePreference.Dark, ThemePreference.Amoled, ThemePreference.Paper })
            foreach (var accent in Enum.GetValues<AccentPreference>())
                yield return [theme, accent];
    }

    [Theory]
    [MemberData(nameof(AccentCombinations))]
    public void AccentColors_KeepActionsAndColoredTextReadable(ThemePreference preference, AccentPreference accent)
    {
        ThemeTestResources.RunSta(() =>
        {
            var resources = ThemeTestResources.Load();
            ThemeManager.Apply(new ThemeAppearance(preference, accent), resources);
            foreach (var key in new[] { "SurfaceBrush", "CardHoverBrush" })
                Assert.True(Contrast((Brush)resources["AccentForegroundBrush"], (Brush)resources[key]) >= 4.5,
                    $"L’accent {accent} se confond avec le fond {key} du thème {preference}.");
            Assert.True(Contrast((Brush)resources["CalendarSelectedTextBrush"], (Brush)resources["AccentBrush"]) >= 4.5);

            foreach (var style in new[] { (Style)resources[typeof(Button)], (Style)resources["SecondaryButton"] })
            {
                var button = new Button { Content = "Action", Resources = resources, Style = style };
                button.Measure(new Size(180, 44));
                button.Arrange(new Rect(0, 0, 180, 44));
                button.UpdateLayout();
                var label = Descendants(button).OfType<TextBlock>().Single(text => text.Text == "Action");
                label.Style = (Style)resources[typeof(TextBlock)];
                Assert.True(Contrast(label.Foreground, button.Background) >= 4.5,
                    $"Le libellé du bouton manque de contraste : {preference}, {accent}.");
            }
        });
    }

    [Fact]
    public void DarkMode_UsesNeutralGreyInsteadOfBlueGrey()
    {
        ThemeTestResources.RunSta(() =>
        {
            var resources = ThemeTestResources.Load();
            ThemeManager.Apply(ThemePreference.Dark, resources);
            foreach (var key in new[] { "BackgroundBrush", "SurfaceBrush", "HeaderBrush" })
            {
                var color = ((SolidColorBrush)resources[key]).Color;
                Assert.Equal(color.R, color.G);
                Assert.Equal(color.G, color.B);
                Assert.True(color.R > 0, "Le mode gris doit rester distinct du noir AMOLED.");
            }
        });
    }

    [Theory]
    [InlineData("Amoled")]
    [InlineData("Paper")]
    public void AdditionalThemes_ProvideBlackOrWarmPaperSurfaces(string name)
    {
        Assert.True(Enum.TryParse<ThemePreference>(name, out var preference), $"Thème absent : {name}.");
        ThemeTestResources.RunSta(() =>
        {
            var resources = ThemeTestResources.Load();
            ThemeManager.Apply(preference, resources);
            var color = ((SolidColorBrush)resources["BackgroundBrush"]).Color;
            if (name == "Amoled")
            {
                foreach (var key in new[] { "BackgroundBrush", "SurfaceBrush", "HeaderBrush" })
                    Assert.Equal(Colors.Black, ((SolidColorBrush)resources[key]).Color);
            }
            else
                Assert.True(color.R >= color.G && color.G > color.B && color.G > 230, "Le fond papier doit être clair et légèrement jaune.");
        });
    }

    [Fact]
    public void SettingsWindow_RestoresTheSelectedAccent()
    {
        ThemeTestResources.RunSta(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "ms3d-appearance-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                File.WriteAllText(Path.Combine(directory, "settings.json"), "{\"Theme\":1,\"Accent\":5}");
                var window = new SettingsWindow(new ThemeSettingsStore(directory), directory);
                try
                {
                    var accents = Assert.IsType<ComboBox>(window.FindName("AccentBox"));
                    Assert.Equal(AccentPreference.Yellow, accents.SelectedValue);
                }
                finally { window.Close(); }
            }
            finally { Directory.Delete(directory, recursive: true); }
        });
    }

    [Theory]
    [InlineData(ThemePreference.Light, AccentPreference.Blue)]
    [InlineData(ThemePreference.Dark, AccentPreference.Violet)]
    [InlineData(ThemePreference.Amoled, AccentPreference.Yellow)]
    [InlineData(ThemePreference.Paper, AccentPreference.Blue)]
    public void ClosedAccentSelector_KeepsTheSelectedLabelReadable(ThemePreference preference, AccentPreference accent)
    {
        ThemeTestResources.RunSta(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "ms3d-selector-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new ThemeSettingsStore(directory);
                store.Save(new ThemeAppearance(preference, accent));
                var resources = ThemeTestResources.Load();
                ThemeManager.Apply(store.LoadAppearance(), resources);
                var window = new SettingsWindow(store, directory);
                window.Resources.MergedDictionaries.Add(resources);
                try
                {
                    var content = (FrameworkElement)window.Content;
                    content.Measure(new Size(660, 780));
                    content.Arrange(new Rect(0, 0, 660, 780));
                    content.UpdateLayout();
                    var selector = Assert.IsType<ComboBox>(window.FindName("AccentBox"));
                    var labelRoot = selector.SelectionBoxItem is VisualBrush clone ? clone.Visual : selector;
                    var label = Descendants(labelRoot).OfType<TextBlock>().First(text => !string.IsNullOrWhiteSpace(text.Text));
                    Assert.True(Contrast(label.Foreground, selector.Background) >= 4.5,
                        $"La couleur choisie est illisible dans le sélecteur fermé : {preference}, {accent}.");
                }
                finally { window.Close(); }
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
        });
    }

    [Theory]
    [InlineData(ThemePreference.Light)]
    [InlineData(ThemePreference.Dark)]
    public void ChangingPreference_UpdatesMaterialControlsAndApplicationColorsTogether(ThemePreference preference)
    {
        ThemeTestResources.RunSta(() =>
        {
            var resources = ThemeTestResources.Load();
            ThemeManager.Apply(preference == ThemePreference.Light ? ThemePreference.Dark : ThemePreference.Light, resources);
            ThemeManager.Apply(preference, resources);

            foreach (var (material, application) in new[]
            {
                ("MaterialDesign.Brush.Primary", "AccentBrush"),
                ("MaterialDesign.Brush.Background", "BackgroundBrush"),
                ("MaterialDesign.Brush.Foreground", "TextBrush")
            })
            {
                Assert.True(resources.Contains(material), $"Couleur Material Design absente : {material}.");
                Assert.Equal(((SolidColorBrush)resources[application]).Color, ((SolidColorBrush)resources[material]).Color);
            }
        });
    }

    [Fact]
    public void MaterialControls_KeepInputsAndActionsCompact()
    {
        ThemeTestResources.RunSta(() =>
        {
            var resources = ThemeTestResources.Load();
            Assert.True(resources.Contains("MaterialDesignRaisedButton"), "Le thème Material Design n’est pas chargé.");
            foreach (var control in new Control[] { new Button { Content = "Enregistrer" }, new TextBox { Text = "Client" }, new ComboBox { ItemsSource = new[] { "Clair", "Sombre" }, SelectedIndex = 1 } })
            {
                control.Resources = resources;
                control.Style = (Style)resources[control.GetType()];
                control.Measure(new Size(220, double.PositiveInfinity));
                Assert.InRange(control.DesiredSize.Height, 30, 44);
            }
        });
    }

    [Theory]
    [InlineData(ThemePreference.Light)]
    [InlineData(ThemePreference.Dark)]
    public void NavigationLabel_RemainsReadableOnTheSidebar(ThemePreference preference)
    {
        ThemeTestResources.RunSta(() =>
        {
            var resources = ThemeTestResources.Load();
            ThemeManager.Apply(preference, resources);
            var window = new MainWindow();
            window.Resources.MergedDictionaries.Add(resources);
            try
            {
                var content = (FrameworkElement)window.Content;
                content.Measure(new Size(700, 720));
                content.Arrange(new Rect(0, 0, 700, 720));
                content.UpdateLayout();
                var navigation = Descendants(content).OfType<Button>().Single(button => button.Content is string label && label == "Tableau de bord");
                var label = Descendants(navigation).OfType<TextBlock>().Single(text => text.Text == "Tableau de bord");
                // Application-level implicit TextBlock styles also reach generated button labels.
                label.Style = (Style)resources[typeof(TextBlock)];
                Assert.True(Contrast(label.Foreground, (Brush)resources["HeaderBrush"]) >= 4.5, "Le libellé de navigation se confond avec le fond.");
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void DialogCancel_RemainsReadableInDarkMode()
    {
        ThemeTestResources.RunSta(() =>
        {
            var resources = ThemeTestResources.Load();
            ThemeManager.Apply(ThemePreference.Dark, resources);
            var window = new CreateNamedItemWindow("Nouveau modèle", Path.GetTempPath(), [], new FolderTreeService());
            window.Resources.MergedDictionaries.Add(resources);
            try
            {
                var content = (FrameworkElement)window.Content;
                content.Measure(new Size(540, 500));
                content.Arrange(new Rect(0, 0, 540, 500));
                content.UpdateLayout();
                var cancel = Descendants(content).OfType<Button>().Single(button => button.Content is string label && label == "Annuler");
                Assert.True(Contrast(cancel.Foreground, cancel.Background) >= 4.5, "L’action Annuler manque de contraste.");
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData(ThemePreference.Light, true)]
    [InlineData(ThemePreference.Dark, true)]
    [InlineData(ThemePreference.Light, false)]
    [InlineData(ThemePreference.Dark, false)]
    public void PrimaryActionLabel_InheritsReadableButtonColors(ThemePreference preference, bool enabled)
    {
        ThemeTestResources.RunSta(() =>
        {
            var resources = ThemeTestResources.Load();
            ThemeManager.Apply(preference, resources);
            var button = new Button { Content = "Enregistrer", IsEnabled = enabled, Resources = resources, Style = (Style)resources[typeof(Button)] };
            button.Measure(new Size(180, 44));
            button.Arrange(new Rect(0, 0, 180, 44));
            button.UpdateLayout();
            var label = Descendants(button).OfType<TextBlock>().Single(text => text.Text == "Enregistrer");
            label.Style = (Style)resources[typeof(TextBlock)];
            Assert.True(Contrast(label.Foreground, button.Background) >= 4.5, "Le texte de l’action manque de contraste sur le bouton.");
        });
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static double Contrast(Brush foreground, Brush background)
    {
        static double Luminance(Brush brush)
        {
            var color = ((SolidColorBrush)brush).Color;
            static double Linear(byte channel)
            {
                var value = channel / 255.0;
                return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
            }
            return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
        }
        var first = Luminance(foreground);
        var second = Luminance(background);
        return (Math.Max(first, second) + .05) / (Math.Min(first, second) + .05);
    }
}
