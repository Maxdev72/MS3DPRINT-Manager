using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MS3DPRINT.Manager.Core.Mobile;
using MS3DPRINT.Manager.Core.Revenues;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>Link with the Android companion: publication of the workshop picture and import of what the phone sent.</summary>
public partial class MainWindow
{
    private static readonly TimeSpan MobilePublishInterval = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan MobilePublishMinimumGap = TimeSpan.FromSeconds(20);
    private DispatcherTimer? _mobilePublishTimer;
    private DateTimeOffset _mobilePublishedAt = DateTimeOffset.MinValue;
    private int _mobilePublishing;
    private string? _mobilePublishError;

    private void StartMobilePublication()
    {
        _mobilePublishTimer = new DispatcherTimer { Interval = MobilePublishInterval };
        _mobilePublishTimer.Tick += (_, _) => RequestMobileSnapshot(force: true);
        _mobilePublishTimer.Start();
        DispatcherTimer.RunOnce(() => RequestMobileSnapshot(force: true), TimeSpan.FromSeconds(6));
    }

    /// <summary>Publishes the picture in the background. Quick successive requests (every list reload asks) are merged.</summary>
    private void RequestMobileSnapshot(bool force = false)
    {
        var root = WorkspaceRoot;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return;
        if (!force && DateTimeOffset.Now - _mobilePublishedAt < MobilePublishMinimumGap) return;
        if (Interlocked.Exchange(ref _mobilePublishing, 1) == 1) return;
        _ = Task.Run(() =>
        {
            try
            {
                if (!new MobileSettingsStore(root).Load().PublishEnabled) return;
                MobileSnapshotPublisher.Publish(root);
                _mobilePublishedAt = DateTimeOffset.Now; _mobilePublishError = null;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException)
            { _mobilePublishError = exception.Message; }
            finally { Interlocked.Exchange(ref _mobilePublishing, 0); }
        });
    }

    private Border BuildMobileSettingsCard(string root)
    {
        MobileSettings settings;
        try { settings = new MobileSettingsStore(root).Load(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { settings = new(); }
        var publish = new CheckBox { Content = "Publier l’état de l’atelier pour le téléphone (projets, clients, activité)", IsChecked = settings.PublishEnabled };
        publish.IsCheckedChanged += (_, _) =>
        {
            try { new MobileSettingsStore(root).Save(new MobileSettings(publish.IsChecked == true)); if (publish.IsChecked == true) RequestMobileSnapshot(force: true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { Notify(e.Message, error: true); }
        };
        var folder = MobileInbox.FolderFor(root);
        var pending = 0;
        try { var scan = MobileInbox.Scan(root); pending = scan.Items.Count + scan.Revenues.Count; } catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { }
        var last = _mobilePublishedAt == DateTimeOffset.MinValue ? "pas encore publié depuis l’ouverture du logiciel" : $"publié à {_mobilePublishedAt:HH:mm}";
        var open = new Button { Content = Icons.Label("IconOpen", "Ouvrir le dossier d’échange", 16) };
        open.Click += (_, _) =>
        {
            try { Directory.CreateDirectory(folder); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true }); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { Notify(e.Message, error: true); }
        };
        var now = new Button { Content = Icons.Label("IconRefresh", "Publier maintenant", 16) };
        now.Click += (_, _) => { RequestMobileSnapshot(force: true); Notify("État de l’atelier envoyé dans le dossier d’échange."); };
        var details = new StackPanel { Spacing = 0, Children =
        {
            SettingsValueRow("Dossier d’échange", MobileExchange.FolderName),
            SettingsValueRow("État de l’atelier", last + (_mobilePublishError is null ? "" : " · erreur : " + _mobilePublishError)),
            SettingsValueRow("Saisies du téléphone à importer", pending.ToString())
        } };
        var header = SettingsCardHeader("IconUpload", "Application mobile", "Le téléphone lit l’état de l’atelier et dépose ses saisies (achats, recettes, photos) dans le dossier « " + MobileExchange.FolderName + " » ; vous les importez ici, après vérification.");
        return new Border { Classes = { "card-raised" }, Child = new StackPanel { Spacing = 16, Children = { header, publish, details, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { open, now } } } } };
    }

    /// <summary>Imports the collections noted on the phone, after confirmation. A line already recorded (same id) is only filed away.</summary>
    private async void ImportMobileRevenues_Click(object? sender, RoutedEventArgs e)
    {
        var root = WorkspaceRoot;
        MobileInboxScan scan;
        try { scan = await Task.Run(() => MobileInbox.Scan(root)); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        { Notify("Lecture du dossier d’échange impossible : " + exception.Message, error: true); return; }
        if (scan.Revenues.Count == 0)
        {
            Notify(scan.Errors.Count > 0 ? "Aucune recette importable : " + scan.Errors[0] : $"Aucune recette du téléphone en attente. Dossier d’échange : « {MobileExchange.FolderName} » dans l’espace de travail.");
            return;
        }
        var dialog = new MobileRevenueInboxWindow(scan.Revenues, scan.Errors);
        if (!await dialog.ShowDialog<bool>(this)) return;
        var chosen = dialog.Selected;
        var imported = 0; var already = 0; var failures = new List<string>();
        await Task.Run(() =>
        {
            var journal = new RevenueJournal(root);
            var known = journal.Years().SelectMany(y => journal.Load(y).Lines).Select(l => l.Entry.Id).ToHashSet();
            foreach (var item in chosen)
            {
                try
                {
                    if (known.Contains(item.Revenue.Id)) { already++; MobileInbox.MarkProcessed(root, item); continue; }
                    var stored = item.PhotoPath is null ? null : RevenueInvoiceArchive.Store(root, item.PhotoPath, null, item.Revenue.ReceivedOn.Year);
                    journal.Append(MobileRevenueExchange.ToEntry(item.Revenue, stored));
                    MobileInbox.MarkProcessed(root, item);
                    imported++;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException)
                { failures.Add($"{item.Revenue.Label} : {exception.Message}"); }
            }
        });
        if (chosen.Count > 0) _revenueYear = chosen.Max(i => i.Revenue.ReceivedOn.Year);
        if (RevenuesWorkspacePanel.IsVisible) await LoadRevenuesAsync();
        RequestMobileSnapshot(force: true);
        Notify($"{imported} recette{(imported > 1 ? "s" : "")} importée{(imported > 1 ? "s" : "")}" + (already > 0 ? $", {already} déjà présente{(already > 1 ? "s" : "")}" : "") + (failures.Count > 0 ? $", {failures.Count} en échec : {failures[0]}" : "."), error: failures.Count > 0);
    }
}
