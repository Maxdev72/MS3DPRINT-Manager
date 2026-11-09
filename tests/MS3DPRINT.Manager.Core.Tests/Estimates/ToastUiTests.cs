using Avalonia.Controls;
using Avalonia;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace MS3DPRINT.Manager.Core.Tests.Estimates;

public sealed partial class EstimateUiTests
{
    [Fact]
    public Task Notifications_are_at_the_bottom_left_away_from_page_actions_and_side_panel() => Run(async () =>
    {
        var owner = await Open(); owner.Show();
        try
        {
            foreach (var width in new[] { 960d, 1400d })
            {
                owner.Width = width; owner.UpdateLayout();
                Invoke(owner, "Notify", "Nom du projet copié.", false);
                await Task.Delay(180); Dispatcher.UIThread.RunJobs(); owner.UpdateLayout();
                var host = owner.FindControl<StackPanel>("ToastHost")!;
                var origin = host.TranslatePoint(default, owner)!.Value;
                Assert.Equal(VerticalAlignment.Bottom, host.VerticalAlignment);
                Assert.Equal(HorizontalAlignment.Left, host.HorizontalAlignment);
                // Clear of the page header (title, search, actions) and of the right-hand side panel.
                Assert.True(origin.Y > owner.Bounds.Height / 2);
                Assert.True(origin.X + host.Bounds.Width < owner.Bounds.Width - 420);
                CaptureDraftUi(owner, "notifications-bottom-left-" + width);
            }
        }
        finally { owner.Close(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Notifications_have_an_opaque_background_so_the_list_below_never_shows_through(bool error) => Run(async () =>
    {
        var owner = await Open(); owner.Show();
        try
        {
            Invoke(owner, "Notify", "Estimation enregistrée. Vous pouvez préparer la suivante.", error);
            Dispatcher.UIThread.RunJobs();
            var toast = Assert.Single(owner.FindControl<StackPanel>("ToastHost")!.Children.OfType<Border>());
            var background = Assert.IsAssignableFrom<ISolidColorBrush>(toast.Background);
            Assert.Equal(255, background.Color.A);
            Assert.Equal(1.0, background.Opacity);
            Assert.True(toast.MaxWidth <= 420 && toast.Padding.Left > 0);
        }
        finally { owner.Close(); }
    });
}
