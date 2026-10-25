using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MS3DPRINT.Manager.Companion.Services;
using MS3DPRINT.Manager.Companion.Ui;

namespace MS3DPRINT.Manager.Companion.Pages;

/// <summary>Exchange folder, state of the PC, and a few words on how the phone and the computer work together.</summary>
public sealed class SettingsPage : UserControl
{
    public const string Version = "1.1";
    private readonly AppServices _app = AppServices.Current;
    private readonly StackPanel _stack = new() { Spacing = 14, Margin = new Thickness(16, 12, 16, 28) };
    private Control? _notice;
    private bool _busy;

    public SettingsPage()
    {
        Content = new ScrollViewer { Content = _stack, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        _app.Changed += () => { if (!_busy) Rebuild(); };
        Rebuild();
    }

    private void Notice(string message, BannerKind kind) { _notice = Ui.Ui.Banner(message, kind); Rebuild(); }

    private void Rebuild()
    {
        _stack.Children.Clear();
        if (_notice is not null) _stack.Children.Add(_notice);

        // exchange folder
        var folder = new StackPanel { Spacing = 10 };
        folder.Children.Add(Ui.Ui.Text("Dossier d’échange", 18, Pal.Text, bold: true));
        folder.Children.Add(Ui.Ui.Muted("C’est le dossier « Echanges mobile » de votre espace de travail Nextcloud. Le téléphone y dépose ses saisies et y lit l’état de l’atelier."));
        folder.Children.Add(_app.Exchange.IsConfigured
            ? Ui.Ui.Banner("Dossier choisi : " + (_app.Exchange.Name ?? "Echanges mobile"), BannerKind.Success)
            : Ui.Ui.Banner(string.IsNullOrEmpty(_app.Data.FolderBookmark) ? "Aucun dossier choisi : rien ne peut être envoyé au PC." : "Le dossier choisi n’est plus accessible : choisissez-le à nouveau.", BannerKind.Warning));
        var pick = _app.Exchange.IsConfigured ? Ui.Ui.Secondary("Changer de dossier", Glyph.Folder) : Ui.Ui.Primary("Choisir le dossier d’échange", Glyph.Folder);
        pick.Click += async (_, _) => await PickAsync();
        folder.Children.Add(pick);
        if (_app.Exchange.IsConfigured)
        {
            var test = Ui.Ui.Secondary("Vérifier l’accès au dossier", Glyph.Check);
            test.Click += async (_, _) => await TestAsync();
            folder.Children.Add(test);
        }
        _stack.Children.Add(Ui.Ui.Card(folder));

        // snapshot
        var state = new StackPanel { Spacing = 10 };
        state.Children.Add(Ui.Ui.Text("État de l’atelier", 18, Pal.Text, bold: true));
        var snapshot = _app.Snapshot;
        state.Children.Add(Ui.Ui.Muted(snapshot is null ? "Aucun état reçu du PC pour l’instant."
            : $"Publié par le PC le {snapshot.GeneratedAt:dd/MM/yyyy à HH:mm} : {snapshot.Projects.Count} projets, {snapshot.Clients.Count} clients."));
        if (_app.SnapshotProblem is { } problem) state.Children.Add(Ui.Ui.Banner(problem, BannerKind.Warning));
        var refresh = Ui.Ui.Secondary("Actualiser maintenant", Glyph.Refresh);
        refresh.IsEnabled = _app.Exchange.IsConfigured;
        refresh.Click += async (_, _) => { _busy = true; var message = await _app.RefreshSnapshotAsync(); _busy = false; Notice(message, _app.SnapshotProblem is null ? BannerKind.Success : BannerKind.Warning); };
        state.Children.Add(refresh);
        _stack.Children.Add(Ui.Ui.Card(state));

        // how it works
        var about = new StackPanel { Spacing = 8 };
        about.Children.Add(Ui.Ui.Text("Comment ça marche", 18, Pal.Text, bold: true));
        about.Children.Add(Ui.Ui.Muted("1. Vous notez un achat ou une recette ici, avec la photo du ticket."));
        about.Children.Add(Ui.Ui.Muted("2. Le téléphone la dépose dans le dossier d’échange ; Nextcloud la synchronise."));
        about.Children.Add(Ui.Ui.Muted("3. Sur le PC, MS3DPRINT Manager la propose à l’import (boutons « Téléphone » des pages Achats et Recettes) : rien n’entre dans vos registres sans votre confirmation."));
        about.Children.Add(Ui.Ui.Muted("Rien n’est envoyé ailleurs que dans ce dossier. Sans réseau, vos saisies restent sur le téléphone et partent dès que le dossier est de nouveau accessible."));
        _stack.Children.Add(Ui.Ui.Card(about));
        _stack.Children.Add(Ui.Ui.Text($"MS3DPRINT Compagnon · version {Version}", 12, Pal.Faint));
    }

    private async Task PickAsync()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        _busy = true;
        try
        {
            if (!await _app.Exchange.PickAsync(storage, _app.Data)) { _busy = false; return; }
            _app.Store.Save(_app.Data);
            _busy = false;
            _app.RaiseChanged();
            Notice("Dossier d’échange enregistré.", BannerKind.Success);
            await _app.RefreshSnapshotAsync();
            await _app.SendPendingAsync();
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException or Java.Lang.Exception)
        { _busy = false; Notice("Dossier inaccessible : " + exception.Message, BannerKind.Error); }
        finally { _busy = false; }
    }

    private async Task TestAsync()
    {
        _busy = true;
        try
        {
            var names = await _app.Exchange.ListNamesAsync();
            await _app.Exchange.TestWriteAsync();
            _busy = false;
            Notice($"Accès au dossier : lecture et écriture possibles ({names.Count} fichier{(names.Count > 1 ? "s" : "")} présent{(names.Count > 1 ? "s" : "")}).", BannerKind.Success);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException or Java.Lang.Exception)
        { _busy = false; Notice("Le dossier n’est pas utilisable : " + exception.Message, BannerKind.Error); }
        finally { _busy = false; }
    }
}
