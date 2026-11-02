using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Desktop;
using MS3DPRINT.Manager.Desktop.Dashboard;

namespace MS3DPRINT.Manager.Core.Tests.Estimates;

public sealed partial class EstimateUiTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Client_search_filters_names_and_codes_and_requires_a_real_selection(bool moving) => Run(async () =>
    {
        new WorkspaceClientCreator().CreateIndividual(_root, "Élodie", "Dupont", "ED");
        new WorkspaceClientCreator().CreateProfessional(_root, "Atelier Martin", "AM");
        var clients = new WorkspaceClientReader().Load(_root);
        var expected = clients.Single(client => client.ClientCode == "ED");
        var owner = await Open(); owner.Show();
        try
        {
            DialogWindow dialog = moving ? new ClientPickerWindow(clients) : new CreateProjectWindow(clients);
            var search = Assert.Single(((Control)dialog.Content!).GetLogicalDescendants().OfType<AutoCompleteBox>());
            ListBox? results = null;
            search.TemplateApplied += (_, e) => results = e.NameScope.Find<ListBox>("PART_SelectingItemsControl");
            var pending = dialog.ShowDialog<bool>(owner); await WaitForDialog<DialogWindow>(owner);
            await Task.Delay(280);
            var input = search.GetVisualDescendants().OfType<TextBox>().Single();
            if (!moving) owner.FindControl<Border>("SidePanelBody")!.GetVisualDescendants().OfType<TextBox>().Single(box => box.PlaceholderText == "ex. Support de casque").Text = "Support";
            input.Focus(); owner.KeyTextInput("elodie"); await Task.Delay(100); Dispatcher.UIThread.RunJobs();
            Assert.NotNull(results); Assert.Equal(expected, Assert.Single(results.Items.OfType<ClientSummary>()));
            Assert.Null(moving ? ((ClientPickerWindow)dialog).SelectedClient : ((CreateProjectWindow)dialog).Client);
            foreach (var width in new[] { 960d, 1600d })
            {
                owner.Width = width; await Task.Delay(160); owner.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                Assert.False(search.IsDropDownOpen); Assert.Equal("elodie", input.Text);
                search.IsDropDownOpen = true; await Task.Delay(50); owner.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var target = input.PointToScreen(new Avalonia.Point(0, input.Bounds.Height));
                var origin = results.PointToScreen(default);
                Assert.True(Math.Abs(target.X - origin.X) <= 24, "target=" + target + " popup=" + origin);
                CaptureDraftUi(owner, "client-search-" + moving + "-" + width);
            }
            owner.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); owner.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Dispatcher.UIThread.RunJobs();
            Assert.False(search.IsDropDownOpen); Assert.False(pending.IsCompleted);
            input.Text = "ED"; await Task.Delay(100); Dispatcher.UIThread.RunJobs();
            if (moving)
            {
                owner.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var row = (Control)results.ContainerFromIndex(0)!;
                var top = TopLevel.GetTopLevel(results)!;
                var point = row.TranslatePoint(new Point(16, row.Bounds.Height / 2), top)!.Value;
                top.MouseDown(point, MouseButton.Left); top.MouseUp(point, MouseButton.Left);
            }
            else
            {
                owner.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null); owner.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
                owner.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); owner.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            }
            Dispatcher.UIThread.RunJobs();
            Assert.False(pending.IsCompleted);
            Assert.Equal(expected, moving ? ((ClientPickerWindow)dialog).SelectedClient : ((CreateProjectWindow)dialog).Client);
            input.Text = "client inconnu";
            DialogAction(owner, moving ? "Déplacer" : "Créer le projet"); Dispatcher.UIThread.RunJobs();
            Assert.False(pending.IsCompleted); Assert.Contains("form-invalid", search.Classes);
            input.Text = "ED"; await Task.Delay(100); Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain("form-invalid", search.Classes);
            Assert.Equal(expected, Assert.Single(results.Items.OfType<ClientSummary>()));
            search.SelectedItem = expected;
            DialogAction(owner, moving ? "Déplacer" : "Créer le projet"); Assert.True(await pending);
            Assert.Equal(expected, moving ? ((ClientPickerWindow)dialog).SelectedClient : ((CreateProjectWindow)dialog).Client);
        }
        finally { owner.Close(); }
    });

    [Fact]
    public Task Client_search_keeps_the_locked_client_when_opened_from_its_sheet() => Run(async () =>
    {
        new WorkspaceClientCreator().CreateIndividual(_root, "Camille", "Client", "CC");
        var client = Assert.Single(new WorkspaceClientReader().Load(_root));
        var owner = await Open(); owner.Show();
        try
        {
            var dialog = new CreateProjectWindow(new[] { client }, client);
            var pending = dialog.ShowDialog<bool>(owner); await WaitForDialog<CreateProjectWindow>(owner);
            var search = Assert.Single(owner.FindControl<Border>("SidePanelBody")!.GetVisualDescendants().OfType<AutoCompleteBox>());
            Assert.False(search.IsEnabled); Assert.Equal(client, dialog.Client);
            Assert.Contains("Camille", search.Text); Assert.Contains("CC", search.Text);
            DialogAction(owner, "Annuler"); Assert.False(await pending);
        }
        finally { owner.Close(); }
    });
}
