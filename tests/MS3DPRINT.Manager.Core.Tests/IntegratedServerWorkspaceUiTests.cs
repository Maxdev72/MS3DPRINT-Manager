using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using MS3DPRINT.Manager.Core.Remote;
using MS3DPRINT.Manager.Core.Tests.Estimates;
using MS3DPRINT.Manager.Desktop;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Projects;
using System.Text.Json;
using Avalonia.Threading;

namespace MS3DPRINT.Manager.Core.Tests;

[Collection("WorkspaceEnvironment")]
public sealed class IntegratedServerWorkspaceUiTests
{
    [Fact]
    public async Task Native_shell_starts_on_server_and_keeps_pc_pages_without_server_requests()
    {
        var temporary = Path.Combine(Path.GetTempPath(), "integrated-shell-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        using var isolation = new IsolatedPreferences(temporary);
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EstimateTestApplication));
        try
        {
            await session.Dispatch(async () =>
            {
                var constructor = typeof(MainWindow).GetConstructor([typeof(HttpClient), typeof(RemoteWorkspaceCache), typeof(string)]);
                Assert.NotNull(constructor);
                var handler = new Server();
                var window = (MainWindow)constructor.Invoke([new HttpClient(handler), new RemoteWorkspaceCache(Path.Combine(temporary, "copy.json")), Path.Combine(temporary, "mode.txt")]);
                window.Show();
                try
                {
                    await window.PreloadAsync();
                    Assert.Equal("Atelier serveur", window.FindControl<TextBlock>("WorkspaceNameText")!.Text);
                    Assert.NotNull(window.FindControl<TextBox>("ServerAccount"));
                    // Pages not hosted by the server stay reachable on this PC, without server requests or a mode switch.
                    await Invoke(window, "NavigateToWorkspaceAsync", "Achats");
                    Assert.DoesNotContain("indisponible", window.FindControl<TextBlock>("ServerConnectionStatus")!.Text);
                    Assert.Equal(0, handler.Requests);
                    Assert.False(window.FindControl<Button>("WorkspaceModeButton")!.IsVisible);
                    // A local dashboard or client list must never overwrite the server pages.
                    var start = typeof(MainWindow).GetMethod("StartWorkspaceLoad", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object[] { "clients", temporary, false });
                    Assert.False((bool)typeof(MainWindow).GetMethod("IsWorkspaceLoadCurrent", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new[] { start })!);
                }
                finally { window.Close(); }
                return true;
            }, CancellationToken.None);
        }
        finally { Directory.Delete(temporary, true); }
    }

    [Fact]
    public async Task Login_renders_native_clients_projects_and_persists_explicit_local_mode()
    {
        var temporary = Path.Combine(Path.GetTempPath(), "integrated-login-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        using var isolation = new IsolatedPreferences(temporary);
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EstimateTestApplication));
        try
        {
            await session.Dispatch(async () =>
            {
                var constructor = typeof(MainWindow).GetConstructor([typeof(HttpClient), typeof(RemoteWorkspaceCache), typeof(string)]);
                Assert.NotNull(constructor);
                var handler = new Server();
                var window = (MainWindow)constructor.Invoke([new HttpClient(handler), new RemoteWorkspaceCache(Path.Combine(temporary, "copy.json")), Path.Combine(temporary, "mode.txt")]);
                window.Show();
                try
                {
                    await window.PreloadAsync();
                    Capture(window, "login");
                    window.FindControl<TextBox>("ServerAccount")!.Text = "owner";
                    window.FindControl<TextBox>("ServerPassword")!.Text = "secret";
                    await Invoke(window, "LoginServerAsync");
                    Assert.Equal("", window.FindControl<TextBox>("ServerPassword")!.Text);
                    Capture(window, "home");
                    await Invoke(window, "NavigateToWorkspaceAsync", "Clients");
                    Assert.True(window.FindControl<Border>("ClientsWorkspacePanel")!.IsVisible);
                    Capture(window, "clients");
                    Assert.Contains(window.FindControl<StackPanel>("ClientList")!.GetLogicalDescendants().OfType<TextBlock>(), x => x.Text == "Client serveur");
                    await Invoke(window, "NavigateToWorkspaceAsync", "Projets");
                    Assert.Contains(window.FindControl<StackPanel>("ProjectList")!.GetLogicalDescendants().OfType<TextBlock>(), x => x.Text == "Projet serveur");
                    Capture(window, "projects");
                    await Invoke(window, "OpenServerProjectAsync", handler.Project);
                    Capture(window, "detail");
                    // PC pages and global search follow the server copy in server mode.
                    var choices = (IReadOnlyList<MS3DPRINT.Manager.Core.Projects.ProjectSummary>)typeof(MainWindow).GetMethod("WithServerProjects", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(window, [Array.Empty<MS3DPRINT.Manager.Core.Projects.ProjectSummary>()])!;
                    Assert.Contains(choices, p => p.ProjectName == "Projet serveur");
                    var found = ((IEnumerable<MS3DPRINT.Manager.Core.Search.GlobalSearchResult>)typeof(MainWindow).GetMethod("ServerSearch", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(window, ["serveur"])!).ToArray();
                    Assert.Contains(found, r => r.Kind == MS3DPRINT.Manager.Core.Search.GlobalSearchResultKind.Client && r.Title == "Client serveur");
                    Assert.Contains(found, r => r.Kind == MS3DPRINT.Manager.Core.Search.GlobalSearchResultKind.Project && r.Title == "Projet serveur");
                    var healthyCopy = File.ReadAllBytes(Path.Combine(temporary, "copy.json"));
                    handler.InvalidSnapshot = true;
                    await Invoke(window, "SynchronizeServerAsync");
                    Assert.Equal(healthyCopy, File.ReadAllBytes(Path.Combine(temporary, "copy.json")));
                    Assert.Contains("invalides", window.FindControl<TextBlock>("ServerConnectionStatus")!.Text);
                    Assert.Contains(window.FindControl<StackPanel>("ProjectList")!.GetLogicalDescendants().OfType<TextBlock>(), x => x.Text == "Projet serveur");
                    handler.InvalidSnapshot = false;
                    handler.Offline = true;
                    await Invoke(window, "SynchronizeServerAsync");
                    Assert.Contains(window.FindControl<StackPanel>("ProjectList")!.GetLogicalDescendants().OfType<TextBlock>(), x => x.Text == "Projet serveur");
                    Assert.Contains("hors connexion", window.FindControl<TextBlock>("ServerConnectionStatus")!.Text, StringComparison.OrdinalIgnoreCase);
                    Assert.All(window.FindControl<StackPanel>("ProjectList")!.GetLogicalDescendants().OfType<Button>().Where(b => b.Content as string == "Modifier la fiche"), b => Assert.False(b.IsEnabled));
                    Assert.False(File.Exists(Path.Combine(temporary, "mode.txt")));
                    Assert.False(Directory.Exists(Path.Combine(temporary, "local-workspace")));
                }
                finally { window.Close(); }
                return true;
            }, CancellationToken.None);
        }
        finally { Directory.Delete(temporary, true); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Restored_revoked_and_unavailable_vault_sessions_keep_the_native_shell(bool revoked, bool unavailable)
    {
        var temporary = Path.Combine(Path.GetTempPath(), "integrated-restore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        using var isolation = new IsolatedPreferences(temporary);
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EstimateTestApplication));
        try
        {
            await session.Dispatch(async () =>
            {
                var handler = new Server { Revoked = revoked };
                var window = new MainWindow(new HttpClient(handler), new RemoteWorkspaceCache(Path.Combine(temporary, "copy.json")), Path.Combine(temporary, "mode.txt"), new Sessions(unavailable));
                window.Show();
                try
                {
                    await window.PreloadAsync();
                    Assert.True(window.IsServerWorkspace);
                    Assert.Equal(revoked || unavailable, window.FindControl<StackPanel>("ServerLoginPanel")!.IsVisible);
                    if (unavailable) Assert.Contains("1312", window.FindControl<TextBlock>("ServerConnectionStatus")!.Text);
                    Assert.Empty(window.OwnedWindows);
                }
                finally { window.Close(); }
                return true;
            }, CancellationToken.None);
        }
        finally { Directory.Delete(temporary, true); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Native_forms_send_commands_once_and_recover_without_losing_drafts(bool lostResponse, bool conflict)
    {
        var temporary = Path.Combine(Path.GetTempPath(), "integrated-forms-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        using var isolation = new IsolatedPreferences(temporary);
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EstimateTestApplication));
        try
        {
            await session.Dispatch(async () =>
            {
                var handler = new Server { LoseWriteResponse = lostResponse, ConflictWrite = conflict };
                var window = new MainWindow(new HttpClient(handler), new RemoteWorkspaceCache(Path.Combine(temporary, "copy.json")), Path.Combine(temporary, "mode.txt"), new Sessions(false));
                window.Show();
                try
                {
                    await window.PreloadAsync();
                    Assert.Contains("connecté", window.FindControl<TextBlock>("ServerConnectionStatus")!.Text);
                    var remote = (RemoteWorkspaceConnection)typeof(MainWindow).GetField("_serverConnection", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                    Assert.True(remote.CanWrite, window.FindControl<TextBlock>("ServerConnectionStatus")!.Text);
                    var editing = Invoke(window, "EditServerClientAsync", new object?[] { null }!);
                    await Task.Yield(); Dispatcher.UIThread.RunJobs();
                    if (editing.IsCompleted) await editing;
                    var form = (Control)window;
                    Control Field(string name) => form.GetLogicalDescendants().OfType<Control>().Single(c => c.Name == name);
                    ((TextBox)Field("ServerClientCode")).Text = "NEW";
                    ((TextBox)Field("ServerCompany")).Text = "Nouveau client test";
                    ((Button)Field("ServerSave")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Task.Yield(); Dispatcher.UIThread.RunJobs();
                    if (lostResponse || conflict)
                    {
                        Assert.False(editing.IsCompleted);
                        Assert.Equal("Nouveau client test", ((TextBox)Field("ServerCompany")).Text);
                        Assert.Equal(1, handler.Writes);
                        ((Button)Field("ServerRecover")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        if (conflict)
                        {
                            await Task.Yield(); Dispatcher.UIThread.RunJobs();
                            Assert.Equal("Nouveau client test", ((TextBox)Field("ServerCompany")).Text);
                            handler.ConflictWrite = false;
                            ((Button)Field("ServerSave")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        }
                    }
                    try { await editing.WaitAsync(TimeSpan.FromSeconds(5)); }
                    catch (TimeoutException) { Assert.Fail("writes=" + handler.Writes + "; status=" + window.FindControl<TextBlock>("ServerConnectionStatus")!.Text + "; form=" + string.Join(" | ", form.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text))); }
                    Assert.Equal(conflict ? 2 : 1, handler.Writes);
                    Assert.Empty(window.OwnedWindows);
                    handler.LoseWriteResponse = false;
                    var projectEditing = Invoke(window, "EditServerProjectAsync", new object?[] { null, null }!);
                    await Task.Yield(); Dispatcher.UIThread.RunJobs();
                    form = window;
                    ((TextBox)Field("ServerProjectName")).Text = "Nouveau projet test";
                    ((Button)Field("ServerSave")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    try { await projectEditing.WaitAsync(TimeSpan.FromSeconds(5)); }
                    catch (TimeoutException) { Assert.Fail("project writes=" + handler.Writes + "; status=" + window.FindControl<TextBlock>("ServerConnectionStatus")!.Text + "; form=" + string.Join(" | ", form.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text))); }
                    Assert.Equal(conflict ? 3 : 2, handler.Writes);
                    var updating = Invoke(window, "EditServerProjectAsync", new object?[] { handler.Project, null }!);
                    await Task.Yield(); Dispatcher.UIThread.RunJobs();
                    form = window;
                    Assert.True(((TextBox)Field("ServerProjectName")).IsReadOnly);
                    ((TextBox)Field("ServerProjectNotes")).Text = "Notes modifiées";
                    ((Button)Field("ServerSave")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await updating.WaitAsync(TimeSpan.FromSeconds(5));
                    Assert.Equal(conflict ? 4 : 3, handler.Writes);
                    var project = handler.ProjectRow;
                    var adding = Invoke(window, "EditServerTaskAsync", new object?[] { project, null }!);
                    await Task.Yield(); Dispatcher.UIThread.RunJobs();
                    form = window;
                    ((TextBox)Field("ServerTaskTitle")).Text = "Imprimer la pièce";
                    var frozenVersion = project.UpdatedAt;
                    handler.Version = handler.Version.AddMinutes(1);
                    await Invoke(window, "SynchronizeServerAsync");
                    Assert.Equal("Imprimer la pièce", ((TextBox)Field("ServerTaskTitle")).Text);
                    ((Button)Field("ServerSave")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await adding.WaitAsync(TimeSpan.FromSeconds(5));
                    Assert.Equal(frozenVersion, handler.LastExpectedVersion);
                    Assert.Equal(conflict ? 5 : 4, handler.Writes);
                    Assert.False(Directory.Exists(Path.Combine(temporary, "local-workspace")));
                    Directory.CreateDirectory(Path.Combine(temporary, "local-workspace"));
                    await Invoke(window, "ChangeWorkspaceModeAsync");
                    Assert.False(window.IsServerWorkspace);
                    Assert.Equal("local", File.ReadAllText(Path.Combine(temporary, "mode.txt")));
                    var taskBusy = typeof(MainWindow).GetField("_projectTaskBusy", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    taskBusy.SetValue(window, true);
                    await Invoke(window, "ChangeWorkspaceModeAsync");
                    Assert.False(window.IsServerWorkspace);
                    Assert.Equal("local", File.ReadAllText(Path.Combine(temporary, "mode.txt")));
                    taskBusy.SetValue(window, false);
                    await Invoke(window, "ChangeWorkspaceModeAsync");
                    Assert.True(window.IsServerWorkspace);
                    Assert.Equal("server", File.ReadAllText(Path.Combine(temporary, "mode.txt")));
                }
                finally { foreach (var form in window.OwnedWindows.ToArray()) form.Close(); window.Close(); }
                return true;
            }, CancellationToken.None);
        }
        finally { Directory.Delete(temporary, true); }
    }

    [Fact]
    public async Task Existing_PC_data_remains_local_until_an_explicit_server_choice()
    {
        var temporary = Path.Combine(Path.GetTempPath(), "integrated-preserve-pc-" + Guid.NewGuid().ToString("N"));
        var local = Path.Combine(temporary, "local-workspace");
        var project = Path.Combine(local, "01_CLIENTS", "Client PC", "PC-2026-001 Pièce récente");
        Directory.CreateDirectory(project);
        var source = Path.Combine(project, "pièce-récente.stl");
        var bytes = new byte[] { 3, 2, 1, 42 };
        File.WriteAllBytes(source, bytes);
        using var isolation = new IsolatedPreferences(temporary);
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EstimateTestApplication));
        try
        {
            Assert.True(WorkspaceStartupMode.UseLocal(local, null, explicitLocal: false));
            Assert.False(WorkspaceStartupMode.UseLocal(local, "server", explicitLocal: false));
            Assert.True(WorkspaceStartupMode.UseLocal(local, "local", explicitLocal: false));
            Assert.False(WorkspaceStartupMode.UseLocal(Path.Combine(temporary, "empty"), null, explicitLocal: false));
            await session.Dispatch(async () =>
            {
                var mode = Path.Combine(temporary, "mode.txt");
                var handler = new Server();
                var constructor = typeof(MainWindow).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single(c => c.GetParameters().Length == 5);
                var window = (MainWindow)constructor.Invoke([new HttpClient(handler), new RemoteWorkspaceCache(Path.Combine(temporary, "copy.json")), mode, null, true]);
                // Constructing the shell must not preload or migrate the existing workshop.
                try
                {
                    Assert.False(window.IsServerWorkspace);
                    Assert.Contains("Données PC conservées", window.FindControl<TextBlock>("WorkspaceSourceText")!.Text);
                    Assert.Contains("ne les transfère pas", window.FindControl<TextBlock>("WorkspaceSourceText")!.Text);
                    Assert.Equal(0, handler.Requests);
                    Assert.False(File.Exists(mode));
                    Assert.Equal(bytes, File.ReadAllBytes(source));
                    window.Show();
                    await window.PreloadAsync();
                    var change = Invoke(window, "ChangeWorkspaceModeAsync");
                    await Task.Yield(); Dispatcher.UIThread.RunJobs();
                    var confirmation = window.FindControl<Border>("SidePanelBody")!;
                    Assert.Contains(confirmation.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text?.Contains("plus récents") == true);
                    var cancel = confirmation.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == "Rester sur ce PC");
                    Assert.Same(cancel, window.FocusManager!.GetFocusedElement());
                    cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await change;
                    Assert.False(window.IsServerWorkspace);
                    Assert.False(File.Exists(mode));
                    Assert.Equal(0, handler.Requests);
                    Assert.Equal(bytes, File.ReadAllBytes(source));
                    var busy = typeof(MainWindow).GetField("_mobilePublishing", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    change = Invoke(window, "ChangeWorkspaceModeAsync");
                    await Task.Yield(); Dispatcher.UIThread.RunJobs();
                    busy.SetValue(window, 1);
                    window.FindControl<Border>("SidePanelBody")!.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "OpenOlderServer").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await change;
                    Assert.False(window.IsServerWorkspace);
                    Assert.False(File.Exists(mode));
                    busy.SetValue(window, 0);
                    change = Invoke(window, "ChangeWorkspaceModeAsync");
                    await Task.Yield(); Dispatcher.UIThread.RunJobs();
                    window.FindControl<Border>("SidePanelBody")!.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == "Rester sur ce PC").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await change;
                    Assert.False(window.IsServerWorkspace);
                    Assert.Equal(0, handler.Requests);
                    Assert.Equal(bytes, File.ReadAllBytes(source));
                }
                finally { window.Close(); }
                return true;
            }, CancellationToken.None);
        }
        finally { Directory.Delete(temporary, true); }
    }

    [Fact]
    public void Unavailable_saved_workshop_and_files_only_workshop_keep_local_priority()
    {
        var temporary = Path.Combine(Path.GetTempPath(), "startup-local-priority-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(temporary, "saved-workshop");
        Directory.CreateDirectory(root);
        var previous = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE");
        try
        {
            Environment.SetEnvironmentVariable("MS3DPRINT_WORKSPACE", null);
            var location = new MS3DPRINT.Manager.Core.Storage.WorkspaceLocation(Path.Combine(temporary, "config"));
            location.Save(root);
            Directory.Delete(root);
            Assert.Equal(root, location.Resolve());
            Assert.False(location.NeedsSetup);
            var unavailable = !Directory.Exists(location.Resolve()) && !location.NeedsSetup;
            Assert.True(WorkspaceStartupMode.UseLocal(location.Resolve(), null, false, unavailable));
            Assert.False(WorkspaceStartupMode.UseLocal(location.Resolve(), "server", false, unavailable));
            Assert.False(WorkspaceStartupMode.UseLocal(Path.Combine(temporary, "fresh-unknown"), null, false));
            var emptyManaged = Path.Combine(temporary, "empty-managed");
            foreach (var name in MS3DPRINT.Manager.Core.Templates.FolderTemplates.Main) Directory.CreateDirectory(Path.Combine(emptyManaged, name));
            Directory.CreateDirectory(Path.Combine(emptyManaged, "98_DEVELOPPEMENT"));
            File.WriteAllText(Path.Combine(emptyManaged, "98_DEVELOPPEMENT", "unrelated-source.cs"), "ignored development file");
            Assert.False(WorkspaceStartupMode.UseLocal(emptyManaged, null, false));
            var files = Path.Combine(temporary, "files-only");
            Directory.CreateDirectory(Path.Combine(files, "02_MODELES_3D"));
            var file = Path.Combine(files, "02_MODELES_3D", "pièce.stl");
            File.WriteAllText(file, "PC newer file");
            Assert.True(WorkspaceStartupMode.UseLocal(files, null, false));
            Assert.Equal("PC newer file", File.ReadAllText(file));
            var collection = Path.Combine(temporary, "collections-only");
            Directory.CreateDirectory(Path.Combine(collection, ".ms3dprint-manager", "collections"));
            File.WriteAllText(Path.Combine(collection, ".ms3dprint-manager", "collections", "fiche.json"), "{}");
            Assert.True(WorkspaceStartupMode.UseLocal(collection, null, false));
        }
        finally
        {
            Environment.SetEnvironmentVariable("MS3DPRINT_WORKSPACE", previous);
            Directory.Delete(temporary, true);
        }
    }

    private sealed class Sessions(bool unavailable) : IRemoteSessionStore
    {
        public RemoteDeviceSession? Load() => unavailable ? throw new System.ComponentModel.Win32Exception(1312, "Coffre indisponible 1312") : new("https://ms3dforge.fr", "owner", "test-only-token", DateTimeOffset.UtcNow.AddHours(1));
        public void Save(RemoteDeviceSession session) { }
        public void Clear() { }
    }

    private static void Capture(MainWindow window, string page)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "MS3DPRINT.Manager.sln"))) root = root.Parent!;
        var output = Path.Combine(root.FullName, "outputs", "integrated-server-workspace");
        Directory.CreateDirectory(output);
        foreach (var width in new[] { 1400, 960 })
        {
            window.Width = width; window.Height = 900;
            window.UpdateLayout(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var bitmap = window.CaptureRenderedFrame(); Assert.NotNull(bitmap);
            bitmap.Save(Path.Combine(output, $"{page}-{width}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        }
    }

    private sealed class IsolatedPreferences : IDisposable
    {
        private readonly string? _window = Environment.GetEnvironmentVariable("MS3DPRINT_WINDOW_FILE");
        private readonly string? _workspace = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE");
        public IsolatedPreferences(string temporary)
        {
            Environment.SetEnvironmentVariable("MS3DPRINT_WINDOW_FILE", Path.Combine(temporary, "window.json"));
            Environment.SetEnvironmentVariable("MS3DPRINT_WORKSPACE", Path.Combine(temporary, "local-workspace"));
        }
        public void Dispose()
        {
            Environment.SetEnvironmentVariable("MS3DPRINT_WINDOW_FILE", _window);
            Environment.SetEnvironmentVariable("MS3DPRINT_WORKSPACE", _workspace);
        }
    }

    internal static async Task Invoke(MainWindow window, string method, params object[] args)
        => await (Task)typeof(MainWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args)!;

    private sealed class Server : HttpMessageHandler
    {
        private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
        public int Requests;
        public bool Offline;
        public bool Revoked;
        public bool InvalidSnapshot;
        public bool LoseWriteResponse;
        public bool ConflictWrite;
        public int Writes;
        private Guid _lastCommand;
        public DateTimeOffset Version = DateTimeOffset.Parse("2026-11-01T12:00:00Z");
        public DateTimeOffset? LastExpectedVersion;
        public ProjectResponse ProjectRow => new(_project, _client, "2026_SERV_001", "Projet serveur", "InProgress", null, "None", Version, []);
        private ClientDetailResponse ClientDetail => new(_client, ClientKind.Professional, "SERV", "SERV", "Client serveur", null, null, null, null, new(null, null, null, null, null, null), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null);
        private ProjectDetailResponse ProjectDetail => new(_project, _client, "SERV", "2026_SERV_001", "project", "Projet serveur", ProjectStatus.InProgress, DateTimeOffset.UtcNow, null, null, null, ProjectRow.UpdatedAt, ProjectPriority.None, []);
        public Guid Project => _project;
        private readonly Guid _client = Guid.NewGuid(), _project = Guid.NewGuid();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            if (Offline) throw new HttpRequestException("offline");
            if (Revoked) return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method != HttpMethod.Get && path != "/api/v1/auth/login")
            {
                Writes++;
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                _lastCommand = body.RootElement.GetProperty("commandId").GetGuid();
                if (body.RootElement.TryGetProperty("expectedUpdatedAt", out var expected)) LastExpectedVersion = expected.GetDateTimeOffset();
                if (ConflictWrite) return new HttpResponseMessage(HttpStatusCode.Conflict);
                if (LoseWriteResponse) throw new HttpRequestException("response lost after commit");
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(path.Contains("clients") ? (object)ClientDetail : ProjectDetail, options: WireJson) };
            }
            if (path.StartsWith("/api/v1/commands/")) return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new CommandReceiptResponse(_lastCommand, "Completed", ConflictWrite ? 409 : 200, null)) };
            if (path == "/api/v1/clients/" + _client) return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(ClientDetail, options: WireJson) };
            if (path == "/api/v1/projects/" + _project) return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(ProjectDetail, options: WireJson) };
            object response = path switch
            {
                "/api/v1/auth/login" => new { AccessToken = "test-only-token", ExpiresIn = 28800 },
                "/api/v1/clients" => new[] { new ClientResponse(_client, "Professional", "SERV", "Client serveur", DateTimeOffset.UtcNow) },
                "/api/v1/projects" => new[] { InvalidSnapshot ? ProjectRow with { ClientId = Guid.NewGuid() } : ProjectRow },
                _ => Array.Empty<object>()
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(response) };
        }
    }
}
