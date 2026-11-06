using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MS3DPRINT.Manager.Core.Migration;
using MS3DPRINT.Manager.Core.Remote;
using MS3DPRINT.Manager.Core.Workspace;
using Avalonia.Threading;

namespace MS3DPRINT.Manager.Desktop;
public partial class MainWindow
{
    private bool _migrationOpen;
    private Border BuildMigrationSettingsCard()
    {
        var action = new Button { Name = "StartGuidedWorkspaceMigration", Content = "Reprendre les données de ce PC", IsEnabled = !IsServerWorkspace };
        action.Click += async (_, _) => await OpenGuidedMigrationAsync();
        return new Border { Classes = { "card-raised" }, Padding = new Avalonia.Thickness(22, 20), Child = new StackPanel { Spacing = 14, Children = {
            SettingsCardHeader("IconSave", "Reprise PC vers serveur", "Sauvegarder ce PC, comparer avec le serveur et choisir chaque conflit avant toute activation."), action } } };
    }
    private async Task OpenGuidedMigrationAsync()
    {
        if (IsServerWorkspace || _migrationOpen || _busy != 0 || _panel is not null || _pendingOperations.Count != 0 || _estimateDraftAutoSaving || _mobilePublishing != 0 || _projectTaskBusy || _backupPickerActive) return;
        if (!FlushEstimateDraft()) return;
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(WorkspaceRoot));
        var generation = _serverGeneration;
        _migrationOpen = true;
        var panel = new GuidedMigrationPanel(root, () => !_serverClosed && !IsServerWorkspace && _serverGeneration == generation
            && Path.TrimEndingDirectorySeparator(Path.GetFullPath(WorkspaceRoot)).Equals(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
        try
        {
            var openServer = await panel.ShowDialog<bool>(this);
            var enabled = RootGrid.IsEnabled;
            RootGrid.IsEnabled = false;
            try { await panel.WaitForIdleAsync(); }
            finally { RootGrid.IsEnabled = enabled; }
            if (openServer && panel.CanOpenServer && !_serverClosed && _serverGeneration == generation && WorkspaceRoot == root)
            {
                _migrationOpen = false;
                await ChangeWorkspaceModeCoreAsync(panel.SwitchSession);
            }
        }
        finally { _migrationOpen = false; }
    }

    internal sealed class GuidedMigrationPanel : DialogWindow
    {
        private readonly string _root;
        private readonly Func<bool> _current;
        private readonly CancellationTokenSource _lifetime = new();
        private readonly HttpClient _http;
        private readonly string _privateRoot;
        private readonly StackPanel _body = new() { Spacing = 14 };
        private readonly TextBlock _status = new() { Name = "MigrationStatus", TextWrapping = TextWrapping.Wrap, Classes = { "muted" } };
        private readonly Button _next = new() { Name = "MigrationNext", Content = "Se connecter et vérifier le serveur" };
        private readonly Button _cancel = new() { Name = "MigrationCancel", Content = "Fermer et conserver la reprise" };
        private readonly TextBox _address = new() { Name = "MigrationServer", Text = "https://ms3dforge.fr", PlaceholderText = "Adresse HTTPS" };
        private readonly TextBox _account = new() { Name = "MigrationAccount", PlaceholderText = "Compte" };
        private readonly TextBox _password = new() { Name = "MigrationPassword", PlaceholderText = "Mot de passe", PasswordChar = '●' };
        private readonly List<(string Path, ComboBox Choice)> _choices = [];
        private readonly List<(Guid Id, CheckBox Choice)> _annex = [];
        private readonly CheckBox _excluded = new() { Content = "Ces entrées restent uniquement sur ce PC ; elles ne sont ni sauvegardées ni transférées." };
        private RemoteWorkspaceClient? _client;
        private GuidedWorkspaceMigration? _migration;
        private WorkspaceMigrationStatus? _remote;
        private int _step;
        private bool _busy, _closed, _finalSourceVerified, _startNew;
        private readonly Button _newPreparation = new() { Content = "Nouvelle préparation", IsVisible = false };
        private TaskCompletionSource _idle = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task WaitForIdleAsync() => _busy ? _idle.Task : Task.CompletedTask;
        private string PrivateRoot => _privateRoot;
        public RemoteDeviceSession? SwitchSession { get; private set; }
        public bool CanOpenServer => _migration?.Journal.Receipt is not null && _finalSourceVerified && _current();
        private bool CanStartNewPreparation => _migration is { } migration &&
            (!migration.Journal.ApplyRequested || migration.Journal.Receipt is not null ||
             _remote is { Phase: WorkspaceMigrationPhase.Blocked, Receipt: null } blocked
             && blocked.OperationId == migration.Journal.OperationId && blocked.ArchiveBytes == migration.Journal.ArchiveBytes
             && string.Equals(blocked.ArchiveSha256, migration.Journal.ArchiveSha256, StringComparison.OrdinalIgnoreCase));
        public GuidedMigrationPanel(string root, Func<bool> current, HttpClient? http = null, string? privateRoot = null)
        {
            _root = root; _current = current; Width = 600;
            _http = http ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromMinutes(30) };
            _privateRoot = privateRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify), "MS3DPRINT Manager", "workspace-migrations");
            _body.Children.Add(new TextBlock { Text = "Serveur HTTPS", Classes = { "caption" } }); _body.Children.Add(_address);
            _body.Children.Add(new TextBlock { Text = "Compte", Classes = { "caption" } }); _body.Children.Add(_account);
            _body.Children.Add(new TextBlock { Text = "Mot de passe · conservé uniquement en mémoire", TextWrapping = TextWrapping.Wrap, Classes = { "caption" } }); _body.Children.Add(_password);
            _next.Click += async (_, _) => await AdvanceAsync();
            _cancel.Click += (_, _) => Close(false);
            _newPreparation.Click += (_, _) =>
            {
                if (_busy || !CanStartNewPreparation) return;
                _migration?.Dispose(); _migration = null; _client?.ForgetSession(); _client = null; _startNew = true;
                _step = 0; _remote = null; _finalSourceVerified = false; _newPreparation.IsVisible = false; _choices.Clear(); _annex.Clear(); _excluded.IsChecked = false;
                _address.IsEnabled = _account.IsEnabled = _password.IsEnabled = true;
                _body.Children.Clear(); _body.Children.Add(Text("Serveur HTTPS")); _body.Children.Add(_address);
                _body.Children.Add(Text("Compte")); _body.Children.Add(_account); _body.Children.Add(Text("Mot de passe")); _body.Children.Add(_password);
                _status.Text = "L’ancienne préparation reste conservée. Reconnectez-vous pour sauvegarder l’état actuel du PC dans une nouvelle reprise.";
                _next.Content = "Se connecter et vérifier le serveur";
            };
            Layout("Reprendre les données de ce PC", "Le PC reste intact. Chaque conflit demande un choix ; les fichiers uniquement serveur sont conservés. Les modules non portés restent consultables dans leur copie locale.", [_body, _status], _next, _cancel, destructive: true);
            _next.Classes.Remove("danger"); _next.Classes.Add("primary-action");
            Avalonia.Automation.AutomationProperties.SetLiveSetting(_status, Avalonia.Automation.AutomationLiveSetting.Polite);
        }
        protected override void OnDialogClosed()
        {
            _closed = true; _lifetime.Cancel(); _password.Text = ""; _client?.ForgetSession();
            if (!_busy) { _http.Dispose(); _migration?.Dispose(); }
            base.OnDialogClosed();
        }
        private void RequireCurrent() { _lifetime.Token.ThrowIfCancellationRequested(); if (_closed || !_current()) throw new InvalidOperationException("Cet atelier n’est plus actif. Rouvrez la reprise depuis le bon atelier."); }
        private async Task AdvanceAsync()
        {
            if (_busy) return;
            _newPreparation.IsVisible = false;
            _body.IsEnabled = false;
            _busy = true; _idle = new(TaskCreationOptions.RunContinuationsAsynchronously); _next.IsEnabled = false;
            try
            {
                RequireCurrent();
                if (_step == 0)
                {
                    if (!Uri.TryCreate(_address.Text, UriKind.Absolute, out var server)) throw new ArgumentException("Indiquez une adresse HTTPS valide.");
                    _client = new RemoteWorkspaceClient(_http, server);
                    var account = (_account.Text ?? "").Trim(); var password = _password.Text ?? ""; _password.Text = "";
                    await _client.LoginAsync(account, password, _lifetime.Token); await _client.GetMigrationCapabilitiesAsync(_lifetime.Token); RequireCurrent();
                    Directory.CreateDirectory(PrivateRoot);
                    WorkspacePathSafety.EnsureNoLinks(PrivateRoot);
                    var candidates = Directory.EnumerateDirectories(PrivateRoot)
                        .Where(d => Guid.TryParseExact(Path.GetFileName(d), "N", out _))
                        .Select(d => { WorkspacePathSafety.EnsureNoLinks(d); return Path.Combine(d, "journal.json"); })
                        .Where(File.Exists).OrderByDescending(File.GetLastWriteTimeUtc);
                    foreach (var path in _startNew ? Enumerable.Empty<string>() : candidates)
                    {
                        try { _migration = GuidedWorkspaceMigration.Load(path, server, account, _root); break; }
                        catch (GuidedWorkspaceMigrationIdentityException) { }
                    }
                    _migration ??= await Task.Run(() => GuidedWorkspaceMigration.Create(server, account, _root, PrivateRoot)); RequireCurrent();
                    _address.IsEnabled = _account.IsEnabled = _password.IsEnabled = false;
                    if (_migration.Journal.ApplyRequested) { _step = 5; _next.Content = "Vérifier uniquement l’état et le reçu"; _status.Text = "Activation déjà demandée. Aucune nouvelle activation ne sera envoyée."; return; }
                    if (_migration.Journal.ArchiveSha256 is not null) { _step = 2; _next.Content = "Reprendre le transfert et la comparaison"; _status.Text = "Sauvegarde portable retrouvée pour ce compte, serveur et atelier."; return; }
                    _body.Children.Clear();
                    var snapshot = await Task.Run(() => new WorkspaceReconciliationService().Capture(_root)); RequireCurrent();
                    var excluded = snapshot.Issues.Where(i => i.Code == "ExcludedEntry").ToArray();
                    _status.Text = "Serveur compatible. La sauvegarde originale sera vérifiée dans un dossier privé hors Nextcloud.\n" + _migration.Journal.Directory;
                    if (excluded.Length > 0)
                    {
                        _body.Children.Add(Text("Entrées hors périmètre conservées uniquement sur le PC :\n" + string.Join("\n", excluded.Select(i => i.Path))));
                        _body.Children.Add(_excluded);
                    }
                    else _excluded.IsChecked = true;
                    _step = 1; _next.Content = "Sauvegarder et préparer la copie portable";
                }
                else if (_step == 1)
                {
                    if (_excluded.IsChecked != true) throw new InvalidDataException("Confirmez le périmètre des entrées conservées uniquement sur ce PC.");
                    _status.Text = "Sauvegarde originale et vérification de la copie…";
                    var selected = _annex.Where(a => a.Choice.IsChecked == true).Select(a => a.Id).ToHashSet();
                    var report = await Task.Run(() => _migration!.Prepare(selected)); RequireCurrent();
                    _body.Children.Clear(); _annex.Clear();
                    foreach (var issue in report.Issues)
                    {
                        _body.Children.Add(Text(issue.Path + " · " + issue.Message));
                        if (issue.Code == "MissingCollectionFolder" && Guid.TryParse(Path.GetFileNameWithoutExtension(issue.Path), out var id))
                        { var check = new CheckBox { Content = "Conserver cette fiche en annexe privée (inactive sur le serveur)" }; _annex.Add((id, check)); _body.Children.Add(check); }
                    }
                    if (!report.Succeeded) { _status.Text = "Préparation bloquée. Aucun fichier transmis. Choisissez les annexes éventuelles ou corrigez les anomalies puis préparez une nouvelle reprise."; return; }
                    _body.Children.Add(Text($"Archive portable vérifiée · {BackupSize(_migration!.Journal.ArchiveBytes)}\nSHA256 : {_migration.Journal.ArchiveSha256}"));
                    _status.Text = "Copie portable prête. Le serveur actif restera inchangé pendant le transfert et la comparaison.";
                    _step = 2; _next.Content = "Transférer et comparer";
                }
                else if (_step == 2)
                {
                    var progress = new Progress<long>(bytes => { if (!_closed && _current() && _step == 2 && _busy) _status.Text = $"Transfert confirmé : {bytes:N0} / {_migration!.Journal.ArchiveBytes:N0} octets"; });
                    _remote = await _migration!.TransferAsync(_client!, progress, _lifetime.Token); RequireCurrent(); RenderReview();
                }
                else if (_step == 3)
                {
                    var selected = _choices.Select(c => new WorkspaceReconciliationChoice(c.Path,
                        c.Choice.SelectedIndex == 1 ? WorkspaceConflictChoice.Pc : WorkspaceConflictChoice.Server)).ToArray();
                    if (_choices.Any(c => c.Choice.SelectedIndex < 1)) throw new InvalidDataException("Choisissez PC ou serveur pour chaque conflit.");
                    _remote = await _client!.SetMigrationChoicesAsync(_migration!.Journal.OperationId!.Value, selected, _lifetime.Token); RequireCurrent();
                    if (_remote.Phase != WorkspaceMigrationPhase.Prepared || _remote.Issues.Any(i => i.Blocking) || string.IsNullOrWhiteSpace(_remote.ReviewHash)) { RenderReview(); return; }
                    _body.Children.Add(Text("Confirmez l’activation sur le serveur : ses données actuelles sont sauvegardées ; les conflits suivent vos choix. Le PC et sa sauvegarde originale sont conservés. Une réponse perdue imposera la consultation du reçu uniquement."));
                    _status.Text = "Choix enregistrés et candidate auditée. Vérifiez le bilan avant de cliquer explicitement sur Activer.";
                    foreach (var choice in _choices) choice.Choice.IsEnabled = false;
                    _step = 4; _next.Content = "Activer cette copie sur le serveur"; _cancel.Focus();
                }
                else if (_step == 4)
                {
                    RequireCurrent(); _status.Text = "Activation demandée ; conservation du journal local…";
                    _remote = await Task.Run(() => _migration!.ApplyAsync(_client!, _remote!, _lifetime.Token)); RequireCurrent(); await RenderResultAsync();
                }
                else if (_step == 5)
                { _remote = await _migration!.CheckAsync(_client!, _lifetime.Token); RequireCurrent(); await RenderResultAsync(); }
                else if (_step == 6)
                { await Task.Run(() => _migration!.RequireStableSource()); RequireCurrent(); if (!CanOpenServer) throw new InvalidDataException("Le PC a changé ou le reçu n’est pas validé. Le mode local est conservé."); SwitchSession = _client!.CaptureSession(_migration!.Journal.Account); Close(true); }
            }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or RemoteWorkspaceException or OperationCanceledException or System.Text.Json.JsonException)
            {
                if (!_closed) { _status.Text = e.Message; _newPreparation.IsVisible = CanStartNewPreparation; if (_newPreparation.IsVisible && !_body.Children.Contains(_newPreparation)) _body.Children.Add(_newPreparation); if (_migration?.Journal.ApplyRequested == true) { _step = 5; _next.Content = "Vérifier uniquement l’état et le reçu"; } }
            }
            finally { _busy = false; if (!_closed) { _next.IsEnabled = true; _body.IsEnabled = true; } else { _http.Dispose(); _migration?.Dispose(); } _idle.TrySetResult(); }
        }
        private void RenderReview()
        {
            _body.Children.Clear(); _choices.Clear();
            if (_remote?.Plan is not { } plan) { _status.Text = "Comparaison indisponible : " + string.Join("\n", _remote?.Issues.Select(i => i.Message) ?? []); return; }
            foreach (var issue in _remote.Issues) _body.Children.Add(Text(issue.Path + " · " + issue.Message));
            _body.Children.Add(Text($"{plan.Files.Count(f => f.Kind == WorkspaceDifferenceKind.Same):N0} inchangés · {plan.Files.Count(f => f.Kind == WorkspaceDifferenceKind.PcOnly):N0} ajouts PC · {plan.Files.Count(f => f.Kind == WorkspaceDifferenceKind.ServerOnly):N0} conservés du serveur · {plan.Files.Count(f => f.Kind == WorkspaceDifferenceKind.Conflict):N0} conflits.\nLes fichiers inchangés sont comptés ci-dessus ; les différences sont détaillées ci-dessous."));
            foreach (var file in plan.Files.Where(f => f.Kind != WorkspaceDifferenceKind.Same).OrderBy(f => f.Kind == WorkspaceDifferenceKind.Conflict ? 0 : 1))
            {
                var kind = file.Kind switch { WorkspaceDifferenceKind.Same => "Inchangé", WorkspaceDifferenceKind.PcOnly => "Ajout PC", WorkspaceDifferenceKind.ServerOnly => "Serveur conservé", _ => "Conflit · choix obligatoire" };
                _body.Children.Add(Text(file.DisplayName + " · " + (Path.GetExtension(file.Path).TrimStart('.') is { Length: > 0 } extension ? extension.ToUpperInvariant() : "Fichier") + " · " + kind + "\n" + file.Path + "\n" + Detail("PC", file.Pc) + "\n" + Detail("Serveur", file.Server)));
                if (file.Kind == WorkspaceDifferenceKind.Conflict)
                {
                    var choice = new ComboBox { ItemsSource = new[] { "Choisir la version…", "Conserver PC", "Conserver serveur" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
                    var saved = _remote.Choices.FirstOrDefault(c => c.Path == file.Path); if (saved is not null) choice.SelectedIndex = saved.Choice == WorkspaceConflictChoice.Pc ? 1 : 2;
                    _choices.Add((file.Path, choice)); _body.Children.Add(choice);
                }
            }
            _status.Text = "Comparaison par contenu. Les dates ne sélectionnent jamais une version automatiquement.";
            _step = 3; _next.Content = "Vérifier les choix et le bilan final";
        }
        private async Task RenderResultAsync()
        {
            if (_remote!.Phase == WorkspaceMigrationPhase.Applied && _migration!.Journal.Receipt is { } receipt)
            {
                _body.Children.Clear(); _body.Children.Add(Text($"Reçu vérifié : {receipt.OperationId}\nActivation : {receipt.AppliedAt.ToLocalTime():g}\nEmpreinte finale : {receipt.Fingerprint}\nSauvegarde serveur vérifiée.\nSauvegarde PC : {_migration.Journal.Directory}"));
                _newPreparation.IsVisible = true; _body.Children.Add(_newPreparation);
                _finalSourceVerified = await Task.Run(() => _migration.SourceStable()); RequireCurrent();
                if (!_finalSourceVerified) { _status.Text = "Serveur activé, mais le PC a changé depuis la sauvegarde. Le mode local est conservé ; vérifiez ces changements avant de poursuivre."; _step = 5; return; }
                _status.Text = "Activation réussie et PC stable. Vous pouvez ouvrir explicitement le mode serveur."; _step = 6; _next.Content = "Ouvrir le mode serveur";
            }
            else
            {
                _status.Text = $"État : {_remote.Phase}. Aucune nouvelle activation ne sera envoyée.\n" + string.Join("\n", _remote.Issues.Select(i => i.Message));
                _step = 5; _next.Content = "Vérifier uniquement l’état et le reçu";
                _newPreparation.IsVisible = CanStartNewPreparation;
                if (_newPreparation.IsVisible && !_body.Children.Contains(_newPreparation)) _body.Children.Add(_newPreparation);
            }
        }
        private static string Detail(string label, MigrationFile? file) => file is null ? label + " : absent" : $"{label} : {file.Bytes:N0} octets · {file.ModifiedAt.ToLocalTime():g}\nSHA256 {file.Sha256}";
        private static TextBlock Text(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 20 };
    }
}
