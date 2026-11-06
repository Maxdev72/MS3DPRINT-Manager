using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using MS3DPRINT.Manager.Core.Migration;
using MS3DPRINT.Manager.Core.Remote;
using MS3DPRINT.Manager.Core.Collections;
using MS3DPRINT.Manager.Core.Storage;
using MS3DPRINT.Manager.Core.Tests.Estimates;
using MS3DPRINT.Manager.Desktop;
namespace MS3DPRINT.Manager.Core.Tests;
[Collection("WorkspaceEnvironment")]
public sealed class GuidedWorkspaceMigrationUiTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task One_login_prepares_missing_collections_compares_and_waits_for_final_confirmation_before_opening_server(bool pcChangesDuringApply)
    {
        var temporary = Directory.CreateTempSubdirectory("simple-sync-ui-").FullName;
        var previous = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE");
        var previousWindow = Environment.GetEnvironmentVariable("MS3DPRINT_WINDOW_FILE");
        var pc = Path.Combine(temporary, "pc");
        Directory.CreateDirectory(Path.Combine(pc, "01_CLIENTS"));
        File.WriteAllText(Path.Combine(pc, "01_CLIENTS", "example.txt"), "document");
        File.WriteAllText(Path.Combine(pc, "local-only.txt"), "keep local");
        var profiles = Directory.CreateDirectory(Path.Combine(pc, ".ms3dprint-manager", "collections")).FullName;
        var id = Guid.NewGuid();
        var profile = new CollectionProfile(id, "02_MODELES_3D", @"02_MODELES_3D\Missing", "Missing", null, null, null, null, null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var original = JsonSerializer.SerializeToUtf8Bytes(profile);
        var profilePath = Path.Combine(profiles, id + ".json"); File.WriteAllBytes(profilePath, original);
        var archive = Path.Combine(temporary, "fixture.zip");
        var hash = new WorkspaceBackupService().Create(pc, archive, true).ArchiveSha256;
        var prepared = Path.Combine(temporary, "fixture");
        Assert.True(new PortableWorkspaceMigrationService().Prepare(archive, prepared, hash, new HashSet<Guid> { id }).Succeeded);
        var reconciliation = new WorkspaceReconciliationService();
        var portable = reconciliation.Capture(Path.Combine(prepared, "workspace"));
        var handler = new Server(reconciliation.Compare(portable, portable), completeApply: true,
            afterApply: pcChangesDuringApply ? () => File.WriteAllText(Path.Combine(pc, "01_CLIENTS", "example.txt"), "new PC edit") : null);
        Environment.SetEnvironmentVariable("MS3DPRINT_WORKSPACE", pc);
        Environment.SetEnvironmentVariable("MS3DPRINT_WINDOW_FILE", Path.Combine(temporary, "window.json"));
        var mode = Path.Combine(temporary, "mode.txt"); File.WriteAllText(mode, "local");
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EstimateTestApplication));
        try
        {
            await session.Dispatch(async () =>
            {
                var window = new MainWindow(new HttpClient(handler), new RemoteWorkspaceCache(Path.Combine(temporary, "cache.json")), mode); window.Show();
                var type = typeof(MainWindow).GetNestedType("GuidedMigrationPanel", BindingFlags.NonPublic)!;
                var dialog = (DialogWindow)Activator.CreateInstance(type, [pc, (Func<bool>)(() => true), new HttpClient(handler), Path.Combine(temporary, "private")])!;
                var showing = dialog.ShowDialog<bool>(window);
                try
                {
                    await Task.Yield(); Dispatcher.UIThread.RunJobs();
                    Find<TextBox>(window, "MigrationAccount").Text = "owner"; Find<TextBox>(window, "MigrationPassword").Text = "fake password";
                    await Next(dialog);
                    Assert.Equal(1, handler.Logins); Assert.Equal(1, handler.ChoiceSaves); Assert.Equal(0, handler.Applies);
                    Assert.Equal("Confirmer la synchronisation", Find<Button>(window, "MigrationNext").Content);
                    var body = (Control)type.GetField("_body", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(dialog)!;
                    Assert.Empty(body.GetLogicalDescendants().OfType<CheckBox>());
                    Assert.Contains(window.GetLogicalDescendants().OfType<Expander>(), e => Equals(e.Header, "Ces éléments restent uniquement sur ce PC"));
                    Assert.Equal(original, File.ReadAllBytes(profilePath));
                    var annex = Assert.Single(Directory.EnumerateFiles(Path.Combine(temporary, "private"), id + ".json", SearchOption.AllDirectories).Where(p => p.Contains("inactive-collections")));
                    Assert.Equal(original, File.ReadAllBytes(annex));
                    typeof(DialogWindow).GetMethod("HandleKey", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(dialog, [new KeyEventArgs { Key = Key.Enter }]);
                    Assert.Equal(0, handler.Applies);
                    await Next(dialog); Assert.Equal(1, handler.Applies);
                    Assert.Equal(!pcChangesDuringApply, (bool)type.GetProperty("CanOpenServer")!.GetValue(dialog)!);
                    if (pcChangesDuringApply)
                    {
                        Assert.False(showing.IsCompleted); Assert.Null(type.GetProperty("SwitchSession")!.GetValue(dialog));
                        Assert.Contains("PC a changé", Find<TextBlock>(window, "MigrationStatus").Text);
                        Assert.Contains(window.GetLogicalDescendants().OfType<Button>(), b => Equals(b.Content, "Nouvelle préparation") && b.IsEffectivelyVisible);
                        dialog.Close(false); Assert.False(await showing);
                    }
                    else { Assert.True(await showing); Assert.NotNull(type.GetProperty("SwitchSession")!.GetValue(dialog)); }
                    Assert.Equal(original, File.ReadAllBytes(profilePath)); Assert.Equal("keep local", File.ReadAllText(Path.Combine(pc, "local-only.txt")));
                }
                finally { dialog.Close(false); window.Close(); }
                return true;
            }, CancellationToken.None);
        }
        finally { Environment.SetEnvironmentVariable("MS3DPRINT_WORKSPACE", previous); Environment.SetEnvironmentVariable("MS3DPRINT_WINDOW_FILE", previousWindow); Directory.Delete(temporary, true); }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Native_review_requires_explicit_choices_locks_final_selection_and_keeps_Enter_safe(bool chooseAllPc)
    {
        var temporary = Path.Combine(Path.GetTempPath(), "migration-ui-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temporary);
        var previous = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE"); var previousWindow = Environment.GetEnvironmentVariable("MS3DPRINT_WINDOW_FILE");
        Environment.SetEnvironmentVariable("MS3DPRINT_WORKSPACE", Path.Combine(temporary, "pc")); Environment.SetEnvironmentVariable("MS3DPRINT_WINDOW_FILE", Path.Combine(temporary, "window.json"));
        Directory.CreateDirectory(Path.Combine(temporary, "pc", "01_CLIENTS")); File.WriteAllText(Path.Combine(temporary, "pc", "01_CLIENTS", "exemple.txt"), "PC content");
        Directory.CreateDirectory(Path.Combine(temporary, "server", "01_CLIENTS")); File.WriteAllText(Path.Combine(temporary, "server", "01_CLIENTS", "exemple.txt"), "server content");
        var service = new WorkspaceReconciliationService(); var plan = service.Compare(service.Capture(Path.Combine(temporary, "pc")), service.Capture(Path.Combine(temporary, "server")));
        var mode = Path.Combine(temporary, "mode.txt"); File.WriteAllText(mode, "local");
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EstimateTestApplication));
        try
        {
            await session.Dispatch(async () =>
            {
                var handler = new Server(plan); var window = new MainWindow(new HttpClient(handler), new RemoteWorkspaceCache(Path.Combine(temporary, "cache.json")), mode); window.Show();
                var migrationFlag = typeof(MainWindow).GetField("_migrationOpen", BindingFlags.NonPublic | BindingFlags.Instance)!;
                migrationFlag.SetValue(window, true);
                typeof(MainWindow).GetMethod("RequestMobileSnapshot", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, [true]);
                Assert.Equal(0, (int)typeof(MainWindow).GetField("_mobilePublishing", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!);
                Assert.False(Directory.Exists(Path.Combine(temporary, "pc", "Echanges mobile")));
                migrationFlag.SetValue(window, false);
                var type = typeof(MainWindow).GetNestedType("GuidedMigrationPanel", BindingFlags.NonPublic)!;
                var dialog = (DialogWindow)Activator.CreateInstance(type, [Path.Combine(temporary, "pc"), (Func<bool>)(() => true), new HttpClient(handler), Path.Combine(temporary, "private")])!;
                var showing = dialog.ShowDialog<bool>(window);
                try
                {
                    await Task.Yield(); Dispatcher.UIThread.RunJobs();
                    Find<TextBox>(window, "MigrationAccount").Text = "owner"; Find<TextBox>(window, "MigrationPassword").Text = "fake password";
                    var enter = new KeyEventArgs { Key = Key.Enter }; typeof(DialogWindow).GetMethod("HandleKey", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(dialog, [enter]); Assert.Equal(0, handler.Logins);
                    await Next(dialog); Assert.Equal(1, handler.Logins); Assert.Equal("", ((TextBox)type.GetField("_password", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(dialog)!).Text);
                    var choices = window.GetLogicalDescendants().OfType<ComboBox>().Where(c => c.ItemsSource is string[] strings && strings.Contains("Conserver PC")).ToArray();
                    var choice = Assert.Single(choices); Assert.Equal(0, choice.SelectedIndex);
                    Assert.Equal(0, handler.Applies);
                    Assert.Contains(window.GetLogicalDescendants().OfType<Button>(), b => b.Name == "MigrationUsePcForAll");
                    await Next(dialog); Assert.Equal(0, handler.ChoiceSaves); Assert.Contains("chaque conflit", Find<TextBlock>(window, "MigrationStatus").Text);
                    if (chooseAllPc)
                    {
                        Find<Button>(window, "MigrationUsePcForAll").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        await (Task)type.GetMethod("WaitForIdleAsync")!.Invoke(dialog, null)!;
                    }
                    else { choice.SelectedIndex = 1; await Next(dialog); }
                    Assert.Equal(1, handler.ChoiceSaves); Assert.False(choice.IsEnabled);
                    Assert.Same(Find<Button>(window, "MigrationCancel"), window.FocusManager!.GetFocusedElement());
                    typeof(DialogWindow).GetMethod("HandleKey", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(dialog, [new KeyEventArgs { Key = Key.Enter }]); Assert.Equal(0, handler.Applies);
                    var output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../outputs/guided-workspace-migration", Guid.NewGuid().ToString("N"))); Directory.CreateDirectory(output);
                    foreach (var width in new[] { 1400, 960 })
                    {
                        window.Width = width; window.Height = 900; window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                        using var image = window.CaptureRenderedFrame(); Assert.NotNull(image); image.Save(Path.Combine(output, $"review-{width}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                    }
                    var oldJournal = Assert.Single(Directory.EnumerateFiles(Path.Combine(temporary, "private"), "journal.json", SearchOption.AllDirectories));
                    await Next(dialog); Assert.Equal(1, handler.Applies);
                    Assert.DoesNotContain(window.GetLogicalDescendants().OfType<Button>(), b => Equals(b.Content, "Nouvelle préparation") && b.IsEffectivelyVisible);
                    var oldIntent = File.ReadAllBytes(oldJournal);
                    await Next(dialog); Assert.Equal(1, handler.StatusReads); Assert.Equal(1, handler.Applies);
                    var restart = window.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Content, "Nouvelle préparation") && b.IsEffectivelyVisible);
                    restart.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.Equal(oldIntent, File.ReadAllBytes(oldJournal)); Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(oldJournal)!, "original.zip")));
                    Find<TextBox>(window, "MigrationPassword").Text = "fake password"; await Next(dialog);
                    Assert.Equal(2, Directory.EnumerateFiles(Path.Combine(temporary, "private"), "journal.json", SearchOption.AllDirectories).Count());
                    Assert.Equal(1, handler.Applies); Assert.Equal(oldIntent, File.ReadAllBytes(oldJournal));
                    dialog.Close(false); Assert.False(await showing); Assert.Equal("PC content", File.ReadAllText(Path.Combine(temporary, "pc", "01_CLIENTS", "exemple.txt")));
                }
                finally { dialog.Close(false); window.Close(); }
                return true;
            }, CancellationToken.None);
        }
        finally { Environment.SetEnvironmentVariable("MS3DPRINT_WORKSPACE", previous); Environment.SetEnvironmentVariable("MS3DPRINT_WINDOW_FILE", previousWindow); Directory.Delete(temporary, true); }
    }
    private static T Find<T>(Control root, string name) where T : Control => root.GetLogicalDescendants().OfType<T>().Single(c => c.Name == name);
    private static async Task Next(DialogWindow dialog) => await (Task)dialog.GetType().GetMethod("AdvanceAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(dialog, null)!;
    private sealed class Server(WorkspaceReconciliationPlan plan, bool completeApply = false, Action? afterApply = null) : HttpMessageHandler
    {
        public int Logins, ChoiceSaves, Applies, StatusReads; private WorkspaceMigrationStatus? _status;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("login")) { Logins++; return Response(new { accessToken = "fake", expiresIn = 3600 }); }
            if (path.EndsWith("capabilities")) return Response(new WorkspaceMigrationCapabilities(1, 524288, 4294967296));
            if (path.EndsWith("/operations")) { var command = (await request.Content!.ReadFromJsonAsync<StartWorkspaceMigration>(token))!; _status = new(command.CommandId, WorkspaceMigrationPhase.Uploading, command.Bytes, command.Sha256, 524288, [], null, [], null, null, null, []); return Response(_status); }
            if (path.Contains("/parts/")) { _status = _status! with { Phase = WorkspaceMigrationPhase.Uploaded, ReceivedParts = [0] }; return Response(_status); }
            if (path.EndsWith("/prepare")) { _status = _status! with { Phase = WorkspaceMigrationPhase.Prepared, Plan = plan }; return Response(_status); }
            if (path.EndsWith("/choices")) { ChoiceSaves++; var choices = (await request.Content!.ReadFromJsonAsync<WorkspaceReconciliationChoice[]>(Wire, token))!; _status = _status! with { Choices = choices, CandidateFingerprint = plan.PcFingerprint, ReviewHash = new WorkspaceReconciliationService().ReviewHash(plan, choices) }; return Response(_status); }
            if (path.EndsWith("/apply"))
            {
                if (completeApply)
                {
                    Applies++;
                    var apply = (await request.Content!.ReadFromJsonAsync<ApplyWorkspaceMigration>(Wire, token))!;
                    _status = _status! with { Phase = WorkspaceMigrationPhase.Applied,
                        Receipt = new(_status.OperationId, apply.CommandId, _status.CandidateFingerprint!, DateTimeOffset.UtcNow, plan.PcFingerprint, plan.ServerFingerprint, true) };
                    afterApply?.Invoke();
                    return Response(_status);
                }
                Applies++; _status = _status! with { Phase = WorkspaceMigrationPhase.Blocked, Issues = [new("ServerChanged", "", "Le serveur a changé avant l’activation.")] };
                return new(HttpStatusCode.Conflict);
            }
            if (request.Method == HttpMethod.Get && path.Contains("/operations/")) { StatusReads++; return Response(_status!); }
            return new(HttpStatusCode.NotFound);
        }
        private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
        private static HttpResponseMessage Response(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value, options: Wire) };
    }
}
