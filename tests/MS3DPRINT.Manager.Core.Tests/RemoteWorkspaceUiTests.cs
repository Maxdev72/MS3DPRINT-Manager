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

public sealed class RemoteWorkspaceUiTests
{
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
        public bool Offline { get; set; }
        public bool WithProject { get; set; }
        public bool Conflict { get; set; }
        public bool LoseMutationResponse { get; set; }
        public bool UncertainReceipt { get; set; }
        public bool ReceiptUnauthorized { get; set; }
        public Guid LastCommandId { get; private set; }
        public Guid LastReceiptId { get; private set; }
        public int MutationCount { get; private set; }
        public CreateClientCommand? ClientCreation { get; set; }
        public CreateProjectCommand? ProjectCreation { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Offline) throw new HttpRequestException();
            var path = request.RequestUri!.AbsolutePath;
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
                LastCommandId = input.GetProperty("commandId").GetGuid(); MutationCount++;
                if (Conflict) return new(HttpStatusCode.Conflict);
                Assert.Equal(_version, input.GetProperty("expectedUpdatedAt").GetDateTimeOffset());
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
                        ? WithProject ? new[] { new ProjectResponse(_projectId, _clientId, "CF-2026-001", "Projet fictif", "InProgress", null, "None", _version, Tasks) } : Array.Empty<ProjectResponse>()
                        : Detail();
            return Response(result);
        }
        private ProjectDetailResponse Detail() => new(_projectId, _clientId, "CF", "CF-2026-001", "Projet", "Projet fictif", ProjectStatus.InProgress, _version, null, null, null, _version, ProjectPriority.None, Tasks.ToArray());
        private static HttpResponseMessage Response(object result) => new(HttpStatusCode.OK) { Content = JsonContent.Create(result, options: Json) };
    }
}
