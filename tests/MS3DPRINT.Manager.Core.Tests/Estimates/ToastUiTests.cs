using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace MS3DPRINT.Manager.Core.Tests.Estimates;

public sealed partial class EstimateUiTests
{
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
