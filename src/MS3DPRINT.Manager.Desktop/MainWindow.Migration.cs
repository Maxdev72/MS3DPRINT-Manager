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
        var action = new Button { Name = "StartGuidedWorkspaceMigration", Content = "Synchroniser cet atelier", IsEnabled = !IsServerWorkspace };
        action.Click += async (_, _) => await OpenGuidedMigrationAsync();
        return new Border { Classes = { "card-raised" }, Padding = new Avalonia.Thickness(22, 20), Child = new StackPanel { Spacing = 14, Children = {
            SettingsCardHeader("IconSave", "Connexion à mon atelier en ligne", "Sauvegarde automatique et premier envoi de cet atelier. Retrouvez ensuite les mêmes clients, projets et tâches sur le PC et le site."), action } } };
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
        private readonly Button _next = new() { Name = "MigrationNext", Content = "Se connecter et préparer la synchronisation" };
        private readonly Button _cancel = new() { Name = "MigrationCancel", Content = "Fermer" };
        private readonly TextBox _address = new() { Name = "MigrationServer", Text = "https://ms3dforge.fr", PlaceholderText = "Adresse HTTPS" };
        private readonly TextBox _account = new() { Name = "MigrationAccount", PlaceholderText = "Compte" };
        private readonly TextBox _password = new() { Name = "MigrationPassword", PlaceholderText = "Mot de passe", PasswordChar = '●' };
        private readonly List<(string Path, ComboBox Choice)> _choices = [];
        private readonly HashSet<Guid> _inactiveCollections = [];
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
                _step = 0; _remote = null; _finalSourceVerified = false; _newPreparation.IsVisible = false; _choices.Clear(); _inactiveCollections.Clear();
                _address.IsEnabled = _account.IsEnabled = _password.IsEnabled = true;
                _body.Children.Clear(); _body.Children.Add(Text("Serveur HTTPS")); _body.Children.Add(_address);
                _body.Children.Add(Text("Compte")); _body.Children.Add(_account); _body.Children.Add(Text("Mot de passe")); _body.Children.Add(_password);
                _status.Text = "L’ancienne préparation reste conservée. Reconnectez-vous pour sauvegarder l’état actuel du PC dans une nouvelle reprise.";
                _next.Content = "Se connecter et préparer la synchronisation";
            };
            Layout("Synchroniser mon atelier", "Manager prépare et sauvegarde votre atelier automatiquement. Vous confirmez le bilan avant de modifier le serveur. Votre PC reste intact.", [_body, _status], _next, _cancel, destructive: true);
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
                    if (_migration.Journal.ApplyRequested)
                    {
                        _step = 5; _remote = await _migration.CheckAsync(_client, _lifetime.Token); RequireCurrent();
                        await RenderResultAsync(); return;
                    }
                    if (_migration.Journal.ArchiveSha256 is not null) await TransferAndReviewAsync();
                    else await PrepareAndReviewAsync();
                }
                else if (_step == 1) await PrepareAndReviewAsync();
                else if (_step == 2) await TransferAndReviewAsync();
                else if (_step == 3)
                {
                    await FreezeReviewAsync();
                }
                else if (_step == 4)
                {
                    RequireCurrent(); _status.Text = "Activation demandée ; conservation du journal local…";
                    _remote = await Task.Run(() => _migration!.ApplyAsync(_client!, _remote!, _lifetime.Token)); RequireCurrent(); await RenderResultAsync();
                }
                else if (_step == 5)
                { _remote = await _migration!.CheckAsync(_client!, _lifetime.Token); RequireCurrent(); await RenderResultAsync(); }
            }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or RemoteWorkspaceException or OperationCanceledException or System.Text.Json.JsonException)
            {
                if (!_closed) { _status.Text = e.Message; _newPreparation.IsVisible = CanStartNewPreparation; if (_newPreparation.IsVisible && !_body.Children.Contains(_newPreparation)) _body.Children.Add(_newPreparation); if (_migration?.Journal.ApplyRequested == true) { _step = 5; _next.Content = "Vérifier uniquement l’état et le reçu"; } }
            }
            finally { _busy = false; if (!_closed) { _next.IsEnabled = true; _body.IsEnabled = true; } else { _http.Dispose(); _migration?.Dispose(); } _idle.TrySetResult(); }
        }
        private async Task PrepareAndReviewAsync()
        {
            _step = 1; _next.Content = "Réessayer la préparation";
            _status.Text = "Sauvegarde de votre PC et préparation de la copie…";
            var report = await Task.Run(() => _migration!.Prepare(_inactiveCollections)); RequireCurrent();
            // Missing folders are preserved in the private annex, disclosed before final activation.
            var missing = report.Issues.Where(i => i.Blocking).ToArray();
            if (missing.Length > 0 && missing.All(i => i.Code == "MissingCollectionFolder"
                && Guid.TryParse(Path.GetFileNameWithoutExtension(i.Path), out _)))
            {
                foreach (var issue in missing) _inactiveCollections.Add(Guid.Parse(Path.GetFileNameWithoutExtension(issue.Path)));
                _status.Text = "Conservation des anciennes fiches et vérification de la copie…";
                report = await Task.Run(() => _migration!.Prepare(_inactiveCollections)); RequireCurrent();
            }
            if (!report.Succeeded)
            {
                _body.Children.Clear();
                _body.Children.Add(Text("La préparation ne peut pas encore se terminer. Votre PC est intact et aucun fichier n’a été envoyé."));
                _body.Children.Add(Details("Voir le problème", report.Issues.Select(i => i.Path + " · " + i.Message)));
                _status.Text = "Vous pouvez réessayer. Si le problème persiste, conservez cette fenêtre pour le diagnostic.";
                return;
            }
            await TransferAndReviewAsync();
        }
        private async Task TransferAndReviewAsync()
        {
            _step = 2; _next.Content = "Réessayer la comparaison";
            _body.Children.Clear();
            _body.Children.Add(Text("Votre PC est sauvegardé. La copie est envoyée dans un espace privé pour comparaison ; l’atelier serveur actif reste inchangé jusqu’à votre confirmation."));
            var progress = new Progress<long>(bytes => { if (!_closed && _current() && _step == 2 && _busy) _status.Text = $"Envoi de la copie : {BackupSize(bytes)} / {BackupSize(_migration!.Journal.ArchiveBytes)}"; });
            _status.Text = "Envoi et comparaison avec votre atelier en ligne…";
            _remote = await _migration!.TransferAsync(_client!, progress, _lifetime.Token); RequireCurrent();
            RenderReview();
            if (_remote.Phase == WorkspaceMigrationPhase.Prepared && !_remote.Issues.Any(i => i.Blocking)
                && _remote.Plan is { } plan && !plan.Files.Any(f => f.Kind == WorkspaceDifferenceKind.Conflict))
                await FreezeReviewAsync();
        }
        private void RenderReview()
        {
            _body.Children.Clear(); _choices.Clear();
            if (_remote?.Plan is not { } plan) { _status.Text = "Comparaison indisponible : " + string.Join("\n", _remote?.Issues.Select(i => i.Message) ?? []); return; }
            foreach (var issue in _remote.Issues) _body.Children.Add(Text(issue.Message));
            _body.Children.Add(Text(Summary(plan)));
            var conflicts = plan.Files.Where(f => f.Kind == WorkspaceDifferenceKind.Conflict).ToArray();
            if (conflicts.Length > 0)
            {
                _body.Children.Add(Text("Certaines données existent dans deux versions. Quelle version souhaitez-vous garder ?"));
                var usePc = new Button { Name = "MigrationUsePcForAll", Content = "Garder la version du PC pour tous les conflits" };
                usePc.Click += async (_, _) =>
                {
                    if (_busy || _step != 3) return;
                    foreach (var item in _choices) item.Choice.SelectedIndex = 1;
                    await AdvanceAsync();
                };
                _body.Children.Add(usePc);
                var individual = new StackPanel { Spacing = 14 };
                foreach (var file in conflicts)
                {
                    individual.Children.Add(Text(file.DisplayName));
                    var choice = new ComboBox { ItemsSource = new[] { "Choisir la version…", "Conserver PC", "Conserver serveur" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
                    var saved = _remote.Choices.FirstOrDefault(c => c.Path == file.Path); if (saved is not null) choice.SelectedIndex = saved.Choice == WorkspaceConflictChoice.Pc ? 1 : 2;
                    _choices.Add((file.Path, choice)); individual.Children.Add(choice);
                }
                _body.Children.Add(new Expander { Header = "Choisir fichier par fichier", Content = individual, HorizontalAlignment = HorizontalAlignment.Stretch });
            }
            _body.Children.Add(Details("Voir les fichiers concernés", plan.Files.Where(f => f.Kind != WorkspaceDifferenceKind.Same)
                .Select(f => f.DisplayName + "\n" + f.Path + "\n" + Detail("PC", f.Pc) + "\n" + Detail("Serveur", f.Server))));
            _status.Text = "Les éléments présents uniquement sur le serveur sont conservés. Aucune version n’est choisie d’après sa date.";
            _step = 3; _next.Content = "Continuer avec ces choix";
        }
        private async Task FreezeReviewAsync()
        {
            if (_choices.Any(c => c.Choice.SelectedIndex < 1)) throw new InvalidDataException("Choisissez une version pour chaque conflit, ou utilisez le bouton pour garder toutes les versions du PC.");
            var selected = _choices.Select(c => new WorkspaceReconciliationChoice(c.Path,
                c.Choice.SelectedIndex == 1 ? WorkspaceConflictChoice.Pc : WorkspaceConflictChoice.Server)).ToArray();
            _remote = await _client!.SetMigrationChoicesAsync(_migration!.Journal.OperationId!.Value, selected, _lifetime.Token); RequireCurrent();
            if (_remote.Phase != WorkspaceMigrationPhase.Prepared || _remote.Plan is null || _remote.Issues.Any(i => i.Blocking) || string.IsNullOrWhiteSpace(_remote.ReviewHash)) { RenderReview(); return; }
            foreach (var choice in _choices) choice.Choice.IsEnabled = false;
            _body.Children.Clear();
            _body.Children.Add(Text(Summary(_remote.Plan!)));
            if (selected.Length > 0)
            {
                _body.Children.Add(Text($"Versions retenues : {selected.Count(c => c.Choice == WorkspaceConflictChoice.Pc)} du PC · {selected.Count(c => c.Choice == WorkspaceConflictChoice.Server)} du serveur."));
                var edit = new Button { Content = "Modifier ces choix" };
                edit.Click += (_, _) => { if (!_busy && _step == 4) RenderReview(); };
                _body.Children.Add(edit);
            }
            var annex = Path.Combine(_migration.Journal.Directory, _migration.Journal.PreparationName!, "inactive-collections");
            WorkspacePathSafety.EnsureNoLinks(annex);
            var inactive = Directory.Exists(annex) ? Directory.EnumerateFiles(annex, "*.json").Count(p => Guid.TryParse(Path.GetFileNameWithoutExtension(p), out _)) : 0;
            if (inactive > 0) _body.Children.Add(Text($"{inactive} ancienne(s) fiche(s) de modèles sans dossier conservée(s) à part dans la sauvegarde privée, sans activation sur le serveur."));
            if (_migration.Journal.ExcludedEntries.Count > 0)
                _body.Children.Add(Details("Ces éléments restent uniquement sur ce PC", _migration.Journal.ExcludedEntries));
            _body.Children.Add(Text("Votre PC reste intact. Le serveur est sauvegardé avant toute modification."));
            _body.Children.Add(Text("Après confirmation, Manager ouvrira les clients, projets et tâches partagés avec le site. Les autres modules restent dans l’atelier local."));
            _status.Text = "Tout est prêt. La synchronisation attend votre confirmation.";
            _step = 4; _next.Content = "Confirmer la synchronisation"; _cancel.Focus();
        }
        private static string Summary(WorkspaceReconciliationPlan plan) => $"Fichiers à ajouter : {plan.Files.Count(f => f.Kind == WorkspaceDifferenceKind.PcOnly):N0}\nConservés du serveur : {plan.Files.Count(f => f.Kind == WorkspaceDifferenceKind.ServerOnly):N0}\nVersions différentes : {plan.Files.Count(f => f.Kind == WorkspaceDifferenceKind.Conflict):N0}";
        private static Expander Details(string title, IEnumerable<string> lines) => new()
        {
            Header = title, HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = Text(string.Join("\n\n", lines))
        };
        private async Task RenderResultAsync()
        {
            if (_remote!.Phase == WorkspaceMigrationPhase.Applied && _migration!.Journal.Receipt is not null)
            {
                _body.Children.Clear(); _body.Children.Add(Text("Votre atelier est disponible en ligne. Les sauvegardes du PC et du serveur sont conservées."));
                _finalSourceVerified = await Task.Run(() => _migration.SourceStable()); RequireCurrent();
                if (!_finalSourceVerified)
                {
                    _status.Text = "Serveur activé, mais le PC a changé depuis la sauvegarde. Le mode local est conservé ; vérifiez ces changements avant de poursuivre.";
                    _step = 5; _next.Content = "Vérifier l’état du serveur";
                    _newPreparation.IsVisible = true; _body.Children.Add(_newPreparation); return;
                }
                _status.Text = "Synchronisation terminée. Ouverture de votre atelier en ligne…";
                SwitchSession = _client!.CaptureSession(_migration.Journal.Account); Close(true);
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
