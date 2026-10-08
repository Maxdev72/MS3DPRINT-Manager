using System.Windows.Controls;
using MS3DPRINT.Manager.App.Views;
using MS3DPRINT.Manager.Core.Filaments;

namespace MS3DPRINT.Manager.Core.Tests.Filaments;

[Collection("Responsive layout UI")]
public sealed class FilamentViewsTests
{
    [Fact]
    public void Editor_PreservesThreeDecimalPriceWhenOnlyNameChanges()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var now = DateTimeOffset.UtcNow;
                var profile = new FilamentProfile(Guid.NewGuid(), "Prusa", "Carbon", "PETG", 24.991m, true, now, now);
                FilamentProfile? saved = null;
                var window = new FilamentEditorWindow(profile, value => saved = value);
                try
                {
                    Assert.Equal("24,991", Assert.IsType<TextBox>(window.FindName("PriceBox")).Text);
                    Assert.IsType<TextBox>(window.FindName("NameBox")).Text = "Nouveau nom";
                    Assert.IsType<Button>(window.FindName("SaveButton")).RaiseEvent(new System.Windows.RoutedEventArgs(Button.ClickEvent));
                    Assert.NotNull(saved);
                    Assert.Equal(24.991m, saved.PricePerKg);
                }
                finally { window.Close(); }
            }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        if (failure is not null) throw failure;
    }

    [Fact]
    public void Editor_InitializesExistingValuesAndDoesNotSaveBeforeValidation()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var now = DateTimeOffset.UtcNow;
                var profile = new FilamentProfile(Guid.NewGuid(), "Prusa", "Carbon", "PETG", 24.90m, true, now, now);
                var saved = false;
                var window = new FilamentEditorWindow(profile, _ => saved = true);
                try
                {
                    Assert.Equal("24,90", Assert.IsType<TextBox>(window.FindName("PriceBox")).Text);
                    Assert.Equal("Prusa", Assert.IsType<TextBox>(window.FindName("BrandBox")).Text);
                    Assert.Null(window.Result);
                    Assert.IsType<TextBox>(window.FindName("PriceBox")).Text = "2 4,90";
                    Assert.IsType<Button>(window.FindName("SaveButton")).RaiseEvent(new System.Windows.RoutedEventArgs(Button.ClickEvent));
                    Assert.False(saved);
                    Assert.Null(window.Result);
                    Assert.NotEmpty(Assert.IsType<TextBlock>(window.FindName("ErrorText")).Text);
                }
                finally { window.Close(); }
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        if (failure is not null) throw failure;
    }
}
