using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Threading;
using MS3DPRINT.Manager.Core.Printers;
using MS3DPRINT.Manager.Core.Tests.Estimates;
using MS3DPRINT.Manager.Desktop.Printers;

namespace MS3DPRINT.Manager.Core.Tests.Printers;

[Collection("WorkspaceEnvironment")]
public sealed class PrinterLiveCardTests
{
    [Fact]
    public void Finish_time_names_today_tomorrow_or_the_date()
    {
        var now = new DateTime(2026, 11, 25, 10, 0, 0);
        Assert.Equal("Fin 14:32", PrinterLiveCard.FinishTime(now.Date.AddHours(14).AddMinutes(32), now));
        Assert.Equal("Fin demain 07:46", PrinterLiveCard.FinishTime(now.Date.AddDays(1).AddHours(7).AddMinutes(46), now));
        Assert.Equal("Fin le 27/11 09:10", PrinterLiveCard.FinishTime(now.Date.AddDays(2).AddHours(9).AddMinutes(10), now));
    }

    [Fact]
    public async Task Cards_render_printing_idle_and_offline_states()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EstimateTestApplication));
        await session.Dispatch(() =>
        {
            PrinterLiveCard Card(string model, Action<PrinterLiveCard> state)
            {
                var now = DateTimeOffset.UtcNow;
                var card = new PrinterLiveCard(new PrinterProfile(Guid.NewGuid(), "Bambu Lab", model, "Bambu Lab " + model, 0, "", true, now, now), new("192.0.2.1", "X", "x"), connect: false);
                state(card);
                return card;
            }
            void Show(PrinterLiveCard card, BambuStatus status) => typeof(PrinterLiveCard).GetMethod("Show", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(card, [status]);
            var printing = Card("H2D", c => Show(c, new BambuStatus("RUNNING", 57, 89, 77, 465, "Système de récupération d'eau - 90mm.stl_1 + Système de récupération d'eau - 90mm.stl_2",
                244, 245, 70, 70, 38, 2, [new("PLA", "F28C38"), new("PETG", "FFFFFF"), new("PP-GF", "FFFFFF"), new("PLA-S", "1E1E1E")])));
            var idle = Card("A1", c => Show(c, new BambuStatus("FINISH", 100, 0, 200, 200, "Support", 26, 0, 24, 0, null, 0, [new("PLA", "3D7BD9")])));
            var offline = Card("H2S", c => typeof(PrinterLiveCard).GetMethod("ShowOffline", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(c, ["Imprimante injoignable : vérifiez l’adresse et qu’elle est allumée."]));
            var window = new Window { Width = 1440, Height = 760, Content = new UniformGrid { Columns = 3, Margin = new(24), Children = { printing, idle, offline } } };
            window.Show();
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame.Save(Path.Combine(Path.GetTempPath(), "ms3dprint-surveillance.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            Assert.Equal("Hors ligne", offline.GetVisualDescendantsText().First(t => t is "Hors ligne"));
            foreach (var card in new[] { printing, idle, offline }) card.Dispose();
            window.Close();
            return true;
        }, CancellationToken.None);
    }
}

file static class TextExtensions
{
    public static IEnumerable<string> GetVisualDescendantsText(this Control control) =>
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(control).OfType<TextBlock>().Select(t => t.Text ?? "");
}
