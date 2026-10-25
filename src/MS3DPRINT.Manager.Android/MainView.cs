using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MS3DPRINT.Manager.Companion.Pages;
using MS3DPRINT.Manager.Companion.Services;
using MS3DPRINT.Manager.Companion.Ui;
using MS3DPRINT.Manager.Core.Mobile;

namespace MS3DPRINT.Manager.Companion;

/// <summary>Frame of the app: title bar, the five screens, bottom navigation, and a confirmation sheet for anything that deletes.</summary>
public sealed class MainView : UserControl
{
    private readonly AppServices _app = AppServices.Current;
    private readonly string[] _titles = ["Saisir", "Projets", "Activité", "Envois", "Réglages"];
    private readonly Control[] _pages;
    private readonly EntryPage _entry = new();
    private readonly ProjectsPage _projects;
    private readonly OutboxPage _outbox;
    private readonly List<Button> _tabs = [];
    private readonly TextBlock _title = new() { FontSize = 20, FontWeight = FontWeight.SemiBold, Foreground = Pal.Text, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _sub = new() { FontSize = 12, Foreground = Pal.Faint };
    private readonly Border _badge = new() { Background = Pal.Warning, CornerRadius = new CornerRadius(9), MinWidth = 18, Height = 18, Padding = new Thickness(5, 0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, IsVisible = false };
    private readonly TextBlock _badgeText = new() { FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Pal.Canvas, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Grid _overlay = new() { IsVisible = false, Background = Brush.Parse("#CC060A12") };
    private readonly Button _refresh;
    private int _current;

    public MainView()
    {
        Background = Pal.Canvas;
        _projects = new ProjectsPage(OpenEntryFor);
        _outbox = new OutboxPage(Confirm);
        _pages = [_entry, _projects, new ActivityPage(), _outbox, new SettingsPage()];

        _refresh = new Button { Content = Glyph.Icon(Glyph.Refresh, 22, Pal.Muted), Background = Brushes.Transparent, MinWidth = 48, MinHeight = 48, Padding = new Thickness(0) };
        _refresh.Click += async (_, _) => await RefreshAsync();
        var titleBlock = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { _title, _sub } };
        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(18, 10, 8, 8), Children = { titleBlock, _refresh } };
        Grid.SetColumn(_refresh, 1);

        var nav = new Grid { Background = Pal.Sidebar };
        string[] icons = [Glyph.Plus, Glyph.Projects, Glyph.Activity, Glyph.Upload, Glyph.Settings];
        for (var i = 0; i < _pages.Length; i++)
        {
            nav.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            var index = i;
            var content = new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center, Children = { Glyph.Icon(icons[i], 24, Pal.Faint), new TextBlock { Text = _titles[i], FontSize = 11, Foreground = Pal.Faint, HorizontalAlignment = HorizontalAlignment.Center } } };
            var tab = new Button { Content = content, Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, MinHeight = 62, Padding = new Thickness(0, 6), CornerRadius = new CornerRadius(0) };
            tab.Click += (_, _) => Go(index);
            var holder = new Grid { Children = { tab } };
            if (i == 3) { _badge.Child = _badgeText; _badge.Margin = new Thickness(0, 8, 14, 0); holder.Children.Add(_badge); }
            Grid.SetColumn(holder, i); _tabs.Add(tab); nav.Children.Add(holder);
        }

        var host = new Grid();
        foreach (var page in _pages) host.Children.Add(page);

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        Grid.SetRow(host, 1); Grid.SetRow(nav, 2);
        root.Children.Add(bar); root.Children.Add(host); root.Children.Add(nav);
        var shell = new Grid { Children = { root, _overlay } };
        Content = shell;
        _app.Changed += UpdateChrome;
        Go(0);
        Loaded += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this)?.StorageProvider is { } storage) await _app.InitializeAsync(storage);
            _entry.RestoreDraft();
            if (CameraCapture.Recovered is not null) PhotoRecovered();
            if (ShareInbox.Pending is not null || ShareInbox.Failure is not null) ReceiveShared();
            else if (!_app.Exchange.IsConfigured) Go(4);
        };
    }

    /// <summary>The Android back button: closes a sheet, then a project, then returns to the first screen; false lets the app go to the background.</summary>
    public bool HandleBack()
    {
        if (_overlay.IsVisible) { _overlay.IsVisible = false; return true; }
        if (_current == 1 && _projects.CanGoBack) { _projects.GoBack(); return true; }
        if (_current != 0) { Go(0); return true; }
        return false;
    }

    /// <summary>A file arrived through the Android Share menu: open the entry screen with it attached.</summary>
    public void ReceiveShared()
    {
        var failure = ShareInbox.TakeFailure();
        var shared = ShareInbox.Take();
        if (failure is null && shared is null) return;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            Go(0);
            if (failure is not null) _entry.ShowProblem(failure);
            else if (shared is { } file) _entry.Receive(file.Bytes, file.Name);
        });
    }

    public void PhotoRecovered()
    {
        if (CameraCapture.TakeRecovered() is not { } photo) return;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => { Go(0); _entry.AttachRecovered(photo); });
    }

    public void Resumed()
    {
        _ = _app.SendPendingAsync();
        _ = _app.RefreshSnapshotAsync();
    }

    private void Go(int index)
    {
        _current = index;
        for (var i = 0; i < _pages.Length; i++)
        {
            _pages[i].IsVisible = i == index;
            var on = i == index;
            var content = (StackPanel)_tabs[i].Content!;
            Glyph.Paint(content.Children[0], on ? Pal.Accent : Pal.Faint);
            ((TextBlock)content.Children[1]).Foreground = on ? Pal.Accent : Pal.Faint;
            ((TextBlock)content.Children[1]).FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
        }
        UpdateChrome();
    }

    private void OpenEntryFor(bool revenue, SnapshotProject project)
    {
        _entry.Prefill(revenue, project);
        Go(0);
    }

    private async Task RefreshAsync()
    {
        _refresh.IsEnabled = false;
        await _app.RefreshSnapshotAsync();
        await _app.SendPendingAsync();
        _refresh.IsEnabled = true;
    }

    private void UpdateChrome()
    {
        _title.Text = _titles[_current];
        var snapshot = _app.Snapshot;
        _sub.Text = !_app.Exchange.IsConfigured ? "Dossier d’échange non choisi"
            : snapshot is null ? "En attente de l’état du PC"
            : "État du PC : " + Ago(snapshot.GeneratedAt) + (_app.PendingCount > 0 ? $" · {_app.PendingCount} en attente d’envoi" : "");
        _sub.Foreground = !_app.Exchange.IsConfigured || _app.PendingCount > 0 ? Pal.Warning : Pal.Faint;
        _badge.IsVisible = _app.PendingCount > 0; _badgeText.Text = _app.PendingCount.ToString();
    }

    private static string Ago(DateTimeOffset when)
    {
        var span = DateTimeOffset.Now - when;
        if (span < TimeSpan.FromMinutes(1)) return "à l’instant";
        if (span < TimeSpan.FromHours(1)) return $"il y a {(int)span.TotalMinutes} min";
        if (span < TimeSpan.FromDays(1)) return $"il y a {(int)span.TotalHours} h";
        return $"il y a {(int)span.TotalDays} j";
    }

    /// <summary>A sheet asking before anything is deleted; Cancel is the first and default choice.</summary>
    private void Confirm(string title, string message, Action onYes)
    {
        _overlay.Children.Clear();
        var cancel = Ui.Ui.Secondary("Annuler"); cancel.Click += (_, _) => _overlay.IsVisible = false;
        var yes = Ui.Ui.Danger("Confirmer", Glyph.Trash); yes.Click += (_, _) => { _overlay.IsVisible = false; onYes(); };
        var sheet = new Border
        {
            Background = Pal.Surface, BorderBrush = Pal.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18, 18, 0, 0), Padding = new Thickness(20, 22, 20, 24), VerticalAlignment = VerticalAlignment.Bottom,
            Child = new StackPanel { Spacing = 14, Children = { Ui.Ui.Text(title, 20, Pal.Text, bold: true), Ui.Ui.Muted(message, 15), cancel, yes } }
        };
        _overlay.Children.Add(sheet);
        _overlay.IsVisible = true;
    }
}
