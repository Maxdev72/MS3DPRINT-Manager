using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using MS3DPRINT.Manager.Core.Tests.Estimates;
using MS3DPRINT.Manager.Native;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Remote;
using MS3DPRINT.Manager.Core.Clients;
using Avalonia.Input;

namespace MS3DPRINT.Manager.Core.Tests;

[Collection("WorkspaceEnvironment")]
public sealed class RemoteWorkspaceUiTests
{
    [Fact]
    public async Task Late_logout_from_closed_view_cannot_delete_reopened_views_command_journal()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sync-late-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "copy.json");
        var cache = new RemoteWorkspaceCache(path);
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EstimateTestApplication));
        try
        {
            await session.Dispatch(async () =>
            {
                var handler = new FakeServer { WithProject = true, LoseMutationResponse = true, LogoutGate = new() };
                using var first = new RemoteWorkspaceView(new HttpClient(handler), cache: cache);
                var firstWindow = new Window { Content = first }; firstWindow.Show();
                Field(first, "RemoteName").Text = "owner"; Field(first, "RemotePassword").Text = "secret";
                await Click(first, "RemoteLogin");
                Find<Button>(first, "RemoteLogout").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await handler.LogoutStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                firstWindow.Close(); first.Dispose();
                using var second = new RemoteWorkspaceView(new HttpClient(handler), cache: new RemoteWorkspaceCache(path));
                var window = new Window { Content = second }; window.Show();
                try
                {
                    Field(second, "RemotePassword").Text = "secret"; await Click(second, "RemoteLogin");
                    Find<ComboBox>(second, "RemoteClients").SelectedIndex = 0;
                    Find<ComboBox>(second, "RemoteProjects").SelectedIndex = 0; await Idle(second);
                    Field(second, "RemoteTaskTitle").Text = "Nouvelle commande incertaine";
                    await Click(second, "RemoteSaveTask");
                    var command = cache.Load()!.PendingCommand;
                    handler.LogoutGate.SetResult(); await Idle(first);
                    Assert.NotNull(command);
                    Assert.Equal(command, cache.Load()!.PendingCommand);
                }
                finally { handler.LogoutGate.TrySetResult(); window.Close(); }
                return true;
            }, CancellationToken.None);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Temporarily_empty_task_editor_still_keeps_its_original_version()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EstimateTestApplication));
        await session.Dispatch(async () =>
        {
            var handler = new FakeServer { WithProject = true };
            handler.Tasks.Add(new(Guid.NewGuid(), "Texte initial", null));
            using var view = new RemoteWorkspaceView(new HttpClient(handler));
            var window = new Window { Content = view }; window.Show();
            try
            {
                Field(view, "RemoteName").Text = "owner"; Field(view, "RemotePassword").Text = "secret";
                await Click(view, "RemoteLogin");
                Find<ComboBox>(view, "RemoteClients").SelectedIndex = 0;
                Find<ComboBox>(view, "RemoteProjects").SelectedIndex = 0; await Idle(view);
                await LabelClick(view, "Modifier"); Field(view, "RemoteTaskTitle").Text = "";
                handler.ChangeVersion(); handler.Tasks[0] = handler.Tasks[0] with { Title = "Texte modifié sur le web" };
                await view.SynchronizeServerAsync();
                Field(view, "RemoteTaskTitle").Text = "Mon remplacement";
                await Click(view, "RemoteSaveTask");
                Assert.Contains("Conflit", Find<TextBlock>(view, "RemoteStatus").Text);
                Assert.Equal("Texte modifié sur le web", handler.Tasks[0].Title);
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Second_view_cannot_replace_or_remove_another_instances_pending_command()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sync-owner-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "copy.json");
        var cache = new RemoteWorkspaceCache(path);
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EstimateTestApplication));
        try
        {
            await session.Dispatch(async () =>
            {
                var handler = new FakeServer { WithProject = true, LoseMutationResponse = true };
                using var first = new RemoteWorkspaceView(new HttpClient(handler), cache: cache);
                var window = new Window { Content = first }; window.Show();
                try
                {
                    Field(first, "RemoteName").Text = "owner"; Field(first, "RemotePassword").Text = "secret";
                    await Click(first, "RemoteLogin");
                    using var second = new RemoteWorkspaceView(new HttpClient(handler), cache: new RemoteWorkspaceCache(path));
                    Assert.False(Find<Button>(second, "RemoteLogin").IsEnabled);
                    Find<ComboBox>(first, "RemoteClients").SelectedIndex = 0;
                    Find<ComboBox>(first, "RemoteProjects").SelectedIndex = 0; await Idle(first);
                    Field(first, "RemoteTaskTitle").Text = "Envoi incertain";
                    await Click(first, "RemoteSaveTask");
                    await second.SynchronizeServerAsync();
                    Assert.Equal(handler.LastCommandId, cache.Load()!.PendingCommand);
                    first.Dispose();
                    using var third = new RemoteWorkspaceView(new HttpClient(handler), cache: new RemoteWorkspaceCache(path));
                    Assert.True(Find<Button>(third, "RemoteReceipt").IsVisible);
                    Assert.True(Find<Button>(third, "RemoteLogin").IsEnabled);
                }
                finally { window.Close(); }
                return true;
            }, CancellationToken.None);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Invalid_copy_blocks_recovery_without_crashing_and_invalid_server_snapshot_keeps_last_view()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sync-invalid-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "copy.json");
        Directory.CreateDirectory(directory); File.WriteAllText(path, "{broken");
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EstimateTestApplication));
        try
        {
            await session.Dispatch(async () =>
            {
                using (var invalid = new RemoteWorkspaceView(new HttpClient(new FakeServer()), cache: new RemoteWorkspaceCache(path)))
                {
                    Assert.False(Find<Button>(invalid, "RemoteLogin").IsEnabled);
                    Assert.Contains("illisible", Find<TextBlock>(invalid, "RemoteStatus").Text);
                    Assert.Equal("{broken", File.ReadAllText(path));
                }
                var handler = new FakeServer { WithProject = true };
                using var view = new RemoteWorkspaceView(new HttpClient(handler));
                var window = new Window { Content = view }; window.Show();
                try
                {
                    Field(view, "RemoteName").Text = "owner"; Field(view, "RemotePassword").Text = "secret";
                    await Click(view, "RemoteLogin");
                    Find<ComboBox>(view, "RemoteClients").SelectedIndex = 0;
                    handler.ProjectTitle = "";
                    await view.SynchronizeServerAsync();
                    Assert.Contains("Projet fictif", Find<ComboBox>(view, "RemoteProjects").Items.Cast<ComboBoxItem>().Single().Content!.ToString());
                    Assert.False(Find<Button>(view, "RemoteCreateClient").IsEnabled);
                    Assert.Contains("invalide", Find<TextBlock>(view, "RemoteStatus").Text);
                }
                finally { window.Close(); }
                return true;
            }, CancellationToken.None);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Reauthentication_read_failure_keeps_last_copy_and_logout_still_forgets_session_when_disk_is_locked()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sync-login-" + Guid.NewGuid().ToString("N"));
        var cachePath = Path.Combine(directory, "copy.json");
        var cache = new RemoteWorkspaceCache(cachePath);
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EstimateTestApplication));
        try
        {
            await session.Dispatch(async () =>
            {
                var handler = new FakeServer { WithProject = true };
                using (var initial = new RemoteWorkspaceView(new HttpClient(handler), cache: cache))
                {
                    var window = new Window { Content = initial }; window.Show();
                    Field(initial, "RemoteName").Text = "owner"; Field(initial, "RemotePassword").Text = "secret";
                    await Click(initial, "RemoteLogin"); window.Close();
                }
                using var view = new RemoteWorkspaceView(new HttpClient(handler), cache: cache);
                var reopened = new Window { Content = view }; reopened.Show();
                try
                {
                    handler.FailReads = true;
                    Field(view, "RemotePassword").Text = "secret"; await Click(view, "RemoteLogin");
                    Assert.Single(Find<ComboBox>(view, "RemoteClients").Items);
                    Assert.False(Find<Button>(view, "RemoteCreateClient").IsEnabled);
                    using var locked = new FileStream(cachePath, FileMode.Open, FileAccess.Read, FileShare.None);
                    await Click(view, "RemoteLogout");
                    Assert.True(Find<Button>(view, "RemoteLogin").IsEnabled);
                    Assert.False(Find<Button>(view, "RemoteLogout").IsEnabled);
                }
                finally { reopened.Close(); }
                return true;
            }, CancellationToken.None);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Connected_visible_view_polls_without_manual_refresh_and_disposal_stops_synchronization()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EstimateTestApplication));
        await session.Dispatch(async () =>
        {
            var handler = new FakeServer { WithProject = true };
            using var view = new RemoteWorkspaceView(new HttpClient(handler));
            var window = new Window { Content = view }; window.Show();
            try
            {
                Field(view, "RemoteName").Text = "owner"; Field(view, "RemotePassword").Text = "secret";
                await Click(view, "RemoteLogin");
                Find<ComboBox>(view, "RemoteClients").SelectedIndex = 0;
                handler.ProjectTitle = "Reçu automatiquement";
                for (var i = 0; i < 350 && !Find<ComboBox>(view, "RemoteProjects").Items.Cast<ComboBoxItem>().Single().Content!.ToString()!.Contains("Reçu automatiquement"); i++)
                    await Task.Delay(100);
                Assert.Contains("Reçu automatiquement", Find<ComboBox>(view, "RemoteProjects").Items.Cast<ComboBoxItem>().Single().Content!.ToString());
                var requests = handler.RequestCount;
                view.Dispose(); handler.ProjectTitle = "Après fermeture";
                await view.SynchronizeServerAsync();
                Assert.Equal(requests, handler.RequestCount);
                Assert.Equal(0, handler.MutationCount);
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Unwritable_command_journal_prevents_network_mutation()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sync-write-" + Guid.NewGuid().ToString("N"));
        var cachePath = Path.Combine(directory, "copy.json");
        var cache = new RemoteWorkspaceCache(cachePath);
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EstimateTestApplication));
        try
        {
            await session.Dispatch(async () =>
            {
                var handler = new FakeServer { WithProject = true };
                using var view = new RemoteWorkspaceView(new HttpClient(handler), cache: cache);
                var window = new Window { Content = view }; window.Show();
                try
                {
                    Field(view, "RemoteName").Text = "owner"; Field(view, "RemotePassword").Text = "secret";
                    await Click(view, "RemoteLogin");
                    Find<ComboBox>(view, "RemoteClients").SelectedIndex = 0;
                    Find<ComboBox>(view, "RemoteProjects").SelectedIndex = 0; await Idle(view);
                    Field(view, "RemoteTaskTitle").Text = "Ne pas envoyer";
                    using var locked = new FileStream(cachePath, FileMode.Open, FileAccess.Read, FileShare.None);
                    await Click(view, "RemoteSaveTask");
                    Assert.Equal(0, handler.MutationCount);
                    Assert.False(Find<Button>(view, "RemoteSaveTask").IsEnabled);
                    Assert.Equal("Ne pas envoyer", Field(view, "RemoteTaskTitle").Text);
                }
                finally { window.Close(); }
                return true;
            }, CancellationToken.None);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Ordinary_logout_forgets_persisted_copy_but_uncertain_receipt_blocks_another_account()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sync-logout-" + Guid.NewGuid().ToString("N"));
        var cache = new RemoteWorkspaceCache(Path.Combine(directory, "copy.json"));
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EstimateTestApplication));
        try
        {
            await session.Dispatch(async () =>
            {
                var handler = new FakeServer { WithProject = true };
                using var view = new RemoteWorkspaceView(new HttpClient(handler), cache: cache);
                var window = new Window { Content = view }; window.Show();
                try
                {
                    Field(view, "RemoteName").Text = "owner"; Field(view, "RemotePassword").Text = "secret";
                    await Click(view, "RemoteLogin"); Assert.NotNull(cache.Load());
                    await Click(view, "RemoteLogout"); Assert.Null(cache.Load());
                    Field(view, "RemotePassword").Text = "secret"; await Click(view, "RemoteLogin");
                    Find<ComboBox>(view, "RemoteClients").SelectedIndex = 0;
                    Find<ComboBox>(view, "RemoteProjects").SelectedIndex = 0; await Idle(view);
                    handler.LoseMutationResponse = true;
                    Field(view, "RemoteTaskTitle").Text = "Résultat inconnu"; await Click(view, "RemoteSaveTask");
                    await Click(view, "RemoteLogout"); Assert.NotNull(cache.Load()!.PendingCommand);
                    Field(view, "RemoteName").Text = "someone else";
                    var requests = handler.RequestCount;
                    await Click(view, "RemoteLogin");
                    Assert.Equal(requests, handler.RequestCount);
                    Assert.Contains("compte", Find<TextBlock>(view, "RemoteStatus").Text);
                }
                finally { window.Close(); }
                return true;
            }, CancellationToken.None);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Automatic_read_updates_remote_changes_and_reconnects_without_erasing_draft()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EstimateTestApplication));
        await session.Dispatch(async () =>
        {
            var handler = new FakeServer { WithProject = true };
            using var view = new RemoteWorkspaceView(new HttpClient(handler));
            var window = new Window { Content = view }; window.Show();
            try
            {
                Field(view, "RemoteName").Text = "owner"; Field(view, "RemotePassword").Text = "secret";
                await Click(view, "RemoteLogin");
                Find<ComboBox>(view, "RemoteClients").SelectedIndex = 0;
                Find<ComboBox>(view, "RemoteProjects").SelectedIndex = 0; await Idle(view);
                Field(view, "RemoteProjectName").Text = "Brouillon de création";
                handler.ProjectTitle = "Modifié sur le web";
                await view.SynchronizeServerAsync();
                Assert.Contains("Modifié sur le web", Find<ComboBox>(view, "RemoteProjects").Items.Cast<ComboBoxItem>().Single().Content!.ToString());
                handler.Offline = true; await view.SynchronizeServerAsync();
                Assert.False(Find<Button>(view, "RemoteCreateProject").IsEnabled);
                Assert.Single(Find<ComboBox>(view, "RemoteClients").Items);
                handler.Offline = false; await view.SynchronizeServerAsync();
                Assert.True(Find<Button>(view, "RemoteCreateProject").IsEnabled);
                Assert.Equal("Brouillon de création", Field(view, "RemoteProjectName").Text);
                Assert.Equal(0, handler.MutationCount);
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Automatic_read_does_not_rebase_task_draft_onto_a_concurrent_version()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EstimateTestApplication));
        await session.Dispatch(async () =>
        {
            var handler = new FakeServer { WithProject = true };
            using var view = new RemoteWorkspaceView(new HttpClient(handler));
            var window = new Window { Content = view }; window.Show();
            try
            {
                Field(view, "RemoteName").Text = "owner"; Field(view, "RemotePassword").Text = "secret";
                await Click(view, "RemoteLogin");
                Find<ComboBox>(view, "RemoteClients").SelectedIndex = 0;
                Find<ComboBox>(view, "RemoteProjects").SelectedIndex = 0; await Idle(view);
                Field(view, "RemoteTaskTitle").Text = "Brouillon ancien";
                handler.ChangeVersion();
                await view.SynchronizeServerAsync();
                await Click(view, "RemoteSaveTask");
                Assert.Contains("Conflit", Find<TextBlock>(view, "RemoteStatus").Text);
                Assert.Empty(handler.Tasks);
                Assert.Equal("Brouillon ancien", Field(view, "RemoteTaskTitle").Text);
                await view.RefreshServerAsync();
                await Click(view, "RemoteSaveTask");
                Assert.Equal("Brouillon ancien", Assert.Single(handler.Tasks).Title);
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Reopened_PC_copy_is_read_only_and_pending_command_is_checked_without_resending()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sync-ui-" + Guid.NewGuid().ToString("N"));
        var cache = new RemoteWorkspaceCache(Path.Combine(directory, "copy.json"));
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EstimateTestApplication));
        try
        {
            await session.Dispatch(async () =>
            {
                var handler = new FakeServer { WithProject = true, LoseMutationResponse = true, UncertainReceipt = true };
                handler.BeforeMutation = () => Assert.NotNull(cache.Load()!.PendingCommand);
                Guid command;
                using (var first = new RemoteWorkspaceView(new HttpClient(handler), cache: cache))
                {
                    var window = new Window { Content = first }; window.Show();
                    Field(first, "RemoteName").Text = "owner"; Field(first, "RemotePassword").Text = "secret";
                    await Click(first, "RemoteLogin");
                    Find<ComboBox>(first, "RemoteClients").SelectedIndex = 0;
                    Find<ComboBox>(first, "RemoteProjects").SelectedIndex = 0; await Idle(first);
                    Field(first, "RemoteTaskTitle").Text = "Commande perdue";
                    await Click(first, "RemoteSaveTask"); command = handler.LastCommandId;
                    window.Close();
                }
                using var reopened = new RemoteWorkspaceView(new HttpClient(handler), cache: cache);
                var second = new Window { Content = reopened }; second.Show();
                try
                {
                    Assert.Single(Find<ComboBox>(reopened, "RemoteClients").Items);
                    Assert.Equal("OWNER", Field(reopened, "RemoteName").Text);
                    Assert.Equal("", Field(reopened, "RemotePassword").Text);
                    Assert.False(Find<Button>(reopened, "RemoteCreateClient").IsEnabled);
                    Assert.True(Find<Button>(reopened, "RemoteReceipt").IsVisible);
                    Assert.False(Field(reopened, "RemoteServer").IsEnabled);
                    Field(reopened, "RemotePassword").Text = "secret"; await Click(reopened, "RemoteLogin");
                    await reopened.SynchronizeServerAsync();
                    Assert.False(Find<Button>(reopened, "RemoteCreateClient").IsEnabled);
                    await Click(reopened, "RemoteReceipt");
                    Assert.Equal(command, handler.LastReceiptId);
                    Assert.Equal(1, handler.MutationCount);
                    Assert.Equal(command, cache.Load()!.PendingCommand);
                }
                finally { second.Close(); }
                return true;
            }, CancellationToken.None);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Uncertain_command_survives_reauthentication_and_explicit_logout(bool logout)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EstimateTestApplication));
        await session.Dispatch(async () =>
        {
            var handler = new FakeServer { WithProject = true, LoseMutationResponse = true, UncertainReceipt = true };
            using var view = new RemoteWorkspaceView(new HttpClient(handler));
            var window = new Window { Content = view, Width = 480, Height = 800 }; window.Show();
            try
            {
                Field(view, "RemoteName").Text = "owner"; Field(view, "RemotePassword").Text = "secret";
                await Click(view, "RemoteLogin");
                Find<ComboBox>(view, "RemoteClients").SelectedIndex = 0; Find<ComboBox>(view, "RemoteProjects").SelectedIndex = 0; await Idle(view);
                Field(view, "RemoteTaskTitle").Text = "Brouillon dont le résultat est inconnu";
                await Click(view, "RemoteSaveTask");
                var id = handler.LastCommandId;
                Assert.NotEqual(Guid.Empty, id); Assert.True(Find<Button>(view, "RemoteReceipt").IsVisible);
                if (logout) await Click(view, "RemoteLogout");
                else { handler.ReceiptUnauthorized = true; await Click(view, "RemoteReceipt"); }
                Assert.True(Find<Button>(view, "RemoteLogin").IsEnabled);
                Assert.False(Field(view, "RemoteServer").IsEnabled);
                handler.ReceiptUnauthorized = false;
                Field(view, "RemotePassword").Text = "secret"; await Click(view, "RemoteLogin");
                Assert.True(Find<Button>(view, "RemoteReceipt").IsVisible);
                Assert.False(Field(view, "RemoteServer").IsEnabled);
                Assert.Equal("Brouillon dont le résultat est inconnu", Field(view, "RemoteTaskTitle").Text);
                Find<ComboBox>(view, "RemoteClients").SelectedIndex = 0; Find<ComboBox>(view, "RemoteProjects").SelectedIndex = 0; await Idle(view);
                await Click(view, "RemoteRefresh"); Assert.False(Find<Button>(view, "RemoteSaveTask").IsEnabled);
                await Click(view, "RemoteSaveTask"); Assert.Equal(1, handler.MutationCount);
                await Click(view, "RemoteReceipt"); Assert.Equal(id, handler.LastReceiptId);
                Assert.False(Find<Button>(view, "RemoteSaveTask").IsEnabled);
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(0, ClientKind.Professional)]
    [InlineData(1, ClientKind.Individual)]
    public async Task Creates_client_and_its_project_in_remote_mode(int kind, ClientKind expected)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EstimateTestApplication));
        await session.Dispatch(async () =>
        {
            var handler = new FakeServer();
            using var view = new RemoteWorkspaceView(new HttpClient(handler));
            var window = new Window { Content = view, Width = 390, Height = 844 }; window.Show();
            try
            {
                Field(view, "RemoteName").Text = "owner"; Field(view, "RemotePassword").Text = "secret";
                await Click(view, "RemoteLogin");
                Find<ComboBox>(view, "RemoteClientKind").SelectedIndex = kind;
                Field(view, "RemoteClientCode").Text = "CF";
                Field(view, "RemoteCompany").Text = "Entreprise fictive";
                Field(view, "RemoteFirstName").Text = "Camille"; Field(view, "RemoteLastName").Text = "Fictif";
                await Click(view, "RemoteCreateClient");
                Assert.Equal(expected, handler.ClientCreation!.Kind);
                Assert.Equal("CF", handler.ClientCreation.ClientCode);
                Assert.NotNull(Find<ComboBox>(view, "RemoteClients").SelectedItem);
                Field(view, "RemoteProjectName").Text = "Projet créé à distance";
                await Click(view, "RemoteCreateProject");
                Assert.Equal("Projet créé à distance", handler.ProjectCreation!.ProjectName);
                Assert.NotNull(Find<ComboBox>(view, "RemoteProjects").SelectedItem);
                Assert.True(Find<Button>(view, "RemoteSaveTask").IsEnabled);
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Tasks_use_server_versions_preserve_conflicting_draft_and_require_cancel_first_removal()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EstimateTestApplication));
        await session.Dispatch(async () =>
        {
            var handler = new FakeServer { WithProject = true };
            using var view = new RemoteWorkspaceView(new HttpClient(handler));
            var window = new Window { Content = view, Width = 480, Height = 800 }; window.Show();
            try
            {
                Field(view, "RemoteName").Text = "owner"; Field(view, "RemotePassword").Text = "secret";
                await Click(view, "RemoteLogin");
                Find<ComboBox>(view, "RemoteClients").SelectedIndex = 0;
                Find<ComboBox>(view, "RemoteProjects").SelectedIndex = 0;
                await Idle(view);
                Field(view, "RemoteTaskTitle").Text = "Préparer la pièce"; Field(view, "RemoteTaskNotes").Text = "Mes notes";
                await Click(view, "RemoteSaveTask");
                Assert.Equal("Préparer la pièce", Assert.Single(handler.Tasks).Title);
                await LabelClick(view, "Modifier");
                Field(view, "RemoteTaskTitle").Text = "Brouillon concurrent";
                handler.Conflict = true;
                await Click(view, "RemoteSaveTask");
                Assert.Equal("Brouillon concurrent", Field(view, "RemoteTaskTitle").Text);
                Assert.False(Find<Button>(view, "RemoteSaveTask").IsEnabled);
                Assert.Contains("Conflit", Find<TextBlock>(view, "RemoteStatus").Text);
                handler.Conflict = false;
                await Click(view, "RemoteRefresh");
                await Click(view, "RemoteSaveTask");
                Assert.Equal("Brouillon concurrent", Assert.Single(handler.Tasks).Title);
                await LabelClick(view, "Terminer"); Assert.True(Assert.Single(handler.Tasks).IsCompleted);
                await LabelClick(view, "Rouvrir"); Assert.False(Assert.Single(handler.Tasks).IsCompleted);
                Field(view, "RemoteTaskTitle").Text = "Deuxième tâche";
                await Click(view, "RemoteSaveTask");
                await LabelClick(view, "Monter", last: true);
                Assert.Equal("Deuxième tâche", handler.Tasks[0].Title);
                await LabelClick(view, "Retirer…");
                Assert.True(Find<Button>(view, "RemoteCancelRemoval").IsFocused);
                Find<Button>(view, "RemoteConfirmRemoval").RaiseEvent(new KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Key.Enter });
                Assert.Equal(2, handler.Tasks.Count);
                await Click(view, "RemoteCancelRemoval"); Assert.Equal(2, handler.Tasks.Count);
                await LabelClick(view, "Retirer…");
                await Click(view, "RemoteConfirmRemoval");
                Assert.Single(handler.Tasks);
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Login_refresh_and_network_loss_keep_last_view_and_draft_but_disable_writes()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EstimateTestApplication));
        await session.Dispatch(async () =>
        {
            var handler = new FakeServer();
            var view = new RemoteWorkspaceView(new HttpClient(handler));
            var window = new Window { Content = view, Width = 480, Height = 800 }; window.Show();
            try
            {
                Field(view, "RemoteName").Text = "owner";
                Field(view, "RemotePassword").Text = "secret";
                await Click(view, "RemoteLogin");
                Assert.Contains("Client fictif", Find<ComboBox>(view, "RemoteClients").Items.Cast<ComboBoxItem>().Single().Content!.ToString());
                Find<ComboBox>(view, "RemoteClients").SelectedIndex = 0;
                Field(view, "RemoteProjectName").Text = "Brouillon conservé";
                Assert.True(Find<Button>(view, "RemoteCreateProject").IsEnabled);
                handler.Offline = true;
                await Click(view, "RemoteRefresh");
                Assert.Equal("Brouillon conservé", Field(view, "RemoteProjectName").Text);
                Assert.Single(Find<ComboBox>(view, "RemoteClients").Items);
                Assert.False(Find<Button>(view, "RemoteCreateProject").IsEnabled);
                Assert.Contains("injoignable", Find<TextBlock>(view, "RemoteStatus").Text);
                handler.Offline = false;
                await Click(view, "RemoteRefresh");
                Assert.True(Find<Button>(view, "RemoteCreateProject").IsEnabled);
                Assert.Equal("Brouillon conservé", Field(view, "RemoteProjectName").Text);
            }
            finally { window.Close(); view.Dispose(); }
            return true;
        }, CancellationToken.None);
    }

    private static T Find<T>(Control control, string name) where T : Control => control.GetLogicalDescendants().OfType<T>().Single(c => c.Name == name);
    private static TextBox Field(Control control, string name) => Find<TextBox>(control, name);
    private static async Task Click(Control control, string name)
    {
        Find<Button>(control, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Idle(control);
    }
    private static async Task LabelClick(Control control, string label, bool last = false)
    {
        var buttons = control.GetLogicalDescendants().OfType<Button>().Where(b => Equals(b.Content, label));
        (last ? buttons.Last() : buttons.First()).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Idle(control);
    }
    private static async Task Idle(Control control)
    {
        for (var i = 0; i < 50 && Find<Button>(control, "RemoteLogin").IsEnabled == false && Find<Button>(control, "RemoteRefresh").IsEnabled == false; i++) await Task.Delay(10);
    }
    private sealed class FakeServer : HttpMessageHandler
    {
        private readonly Guid _clientId = Guid.NewGuid();
        private readonly Guid _projectId = Guid.NewGuid();
        private DateTimeOffset _version = DateTimeOffset.UtcNow;
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
        public List<ProjectTask> Tasks { get; } = [];
        public string ProjectTitle { get; set; } = "Projet fictif";
        public Action? BeforeMutation { get; set; }
        public void ChangeVersion() => _version = _version.AddMinutes(1);
        public bool Offline { get; set; }
        public bool FailReads { get; set; }
        public TaskCompletionSource? LogoutGate { get; set; }
        public TaskCompletionSource LogoutStarted { get; } = new();
        public bool WithProject { get; set; }
        public bool Conflict { get; set; }
        public bool LoseMutationResponse { get; set; }
        public bool UncertainReceipt { get; set; }
        public bool ReceiptUnauthorized { get; set; }
        public Guid LastCommandId { get; private set; }
        public Guid LastReceiptId { get; private set; }
        public int MutationCount { get; private set; }
        public int RequestCount { get; private set; }
        public CreateClientCommand? ClientCreation { get; set; }
        public CreateProjectCommand? ProjectCreation { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            if (Offline) throw new HttpRequestException();
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("logout") && LogoutGate is not null) { LogoutStarted.TrySetResult(); await LogoutGate.Task; }
            if (FailReads && request.Method == HttpMethod.Get) throw new HttpRequestException();
            if (path.Contains("/commands/"))
            {
                LastReceiptId = Guid.Parse(path.Split('/').Last());
                if (ReceiptUnauthorized) return new(HttpStatusCode.Unauthorized);
                return Response(new CommandReceiptResponse(LastReceiptId, UncertainReceipt ? "Uncertain" : "Completed", UncertainReceipt ? null : 409, null));
            }
            if (path.EndsWith("clients") && request.Method == HttpMethod.Post)
            {
                var json = await request.Content!.ReadAsStringAsync(cancellationToken);
                Assert.Contains("\"kind\":\"", json);
                ClientCreation = JsonSerializer.Deserialize<CreateClientCommand>(json, Json)!;
                return Response(new ClientDetailResponse(_clientId, ClientCreation.Kind, "Fictif", ClientCreation.ClientCode, ClientCreation.CompanyName, ClientCreation.FirstName, ClientCreation.LastName, ClientCreation.Address, ClientCreation.Notes, ClientCreation.Contact, _version, _version, null));
            }
            if (path.EndsWith("projects") && request.Method == HttpMethod.Post)
            {
                ProjectCreation = await request.Content!.ReadFromJsonAsync<CreateProjectCommand>(Json, cancellationToken);
                Assert.Equal(_clientId, ProjectCreation!.ClientId);
                WithProject = true;
                return Response(Detail());
            }
            if (path.Contains("/tasks") && request.Method != HttpMethod.Get)
            {
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                var input = json.RootElement;
                BeforeMutation?.Invoke();
                LastCommandId = input.GetProperty("commandId").GetGuid(); MutationCount++;
                if (Conflict) return new(HttpStatusCode.Conflict);
                if (_version != input.GetProperty("expectedUpdatedAt").GetDateTimeOffset()) return new(HttpStatusCode.Conflict);
                Assert.NotEqual(Guid.Empty, input.GetProperty("commandId").GetGuid());
                var segments = path.Split('/');
                if (segments.Length == 6) Tasks.Add(new(Guid.NewGuid(), input.GetProperty("title").GetString()!, input.GetProperty("notes").GetString()));
                else
                {
                    var id = Guid.Parse(segments[6]); var index = Tasks.FindIndex(t => t.Id == id); var task = Tasks[index];
                    if (path.EndsWith("remove")) { Assert.True(input.GetProperty("confirmed").GetBoolean()); Tasks.RemoveAt(index); }
                    else if (path.EndsWith("complete")) Tasks[index] = task with { IsCompleted = input.GetProperty("completed").GetBoolean() };
                    else if (path.EndsWith("move")) { var target = index + input.GetProperty("direction").GetInt32(); (Tasks[index], Tasks[target]) = (Tasks[target], Tasks[index]); }
                    else Tasks[index] = task with { Title = input.GetProperty("title").GetString()!, Notes = input.GetProperty("notes").GetString() };
                }
                _version = _version.AddSeconds(1);
                if (LoseMutationResponse) throw new HttpRequestException("Response lost after commit");
                return Response(Detail());
            }
            object result = path.EndsWith("login")
                ? new { accessToken = "token", expiresIn = 3600 }
                : path.EndsWith("clients")
                    ? new[] { new { id = _clientId, kind = "Individual", clientCode = "CF", displayName = "Client fictif", updatedAt = DateTimeOffset.UtcNow } }
                    : path.EndsWith("projects")
                        ? WithProject ? new[] { new ProjectResponse(_projectId, _clientId, "CF-2026-001", ProjectTitle, "InProgress", null, "None", _version, Tasks) } : Array.Empty<ProjectResponse>()
                        : Detail();
            return Response(result);
        }
        private ProjectDetailResponse Detail() => new(_projectId, _clientId, "CF", "CF-2026-001", "Projet", ProjectTitle, ProjectStatus.InProgress, _version, null, null, null, _version, ProjectPriority.None, Tasks.ToArray());
        private static HttpResponseMessage Response(object result) => new(HttpStatusCode.OK) { Content = JsonContent.Create(result, options: Json) };
    }
}
