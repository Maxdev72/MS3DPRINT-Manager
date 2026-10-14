using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MS3DPRINT.Manager.Core.ModelSearch;

namespace MS3DPRINT.Manager.Desktop.ModelSearch;

/// <summary>
/// 3D-model search page: one search box opens a browser tab per ticked site. The chrome stays thin (toolbar, tabs, address bar)
/// so the web page gets the room; the site choice folds away once a search is made.
/// </summary>
public sealed class ModelBrowserView : UserControl
{
    private sealed class BrowserTab
    {
        public required NativeWebView View { get; init; }
        public required Button Header { get; init; }
        /// <summary>The site this tab searches, so that a new search reuses it instead of opening a duplicate.</summary>
        public string? Site { get; init; }
        public bool Loading { get; set; }
    }

    private const double ToolbarHeight = 40;

    private readonly List<BrowserTab> _tabs = [];
    private readonly Dictionary<ModelSearchSite, Button> _chips = [];
    private readonly HashSet<string> _selected = new(StringComparer.Ordinal);

    private readonly TextBox _query = new() { PlaceholderText = "Rechercher un modèle 3D (ex. spatule mastic)", VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox _address = new() { PlaceholderText = "Adresse ou recherche", VerticalContentAlignment = VerticalAlignment.Center, MinHeight = 36 };
    private readonly Button _sitesButton = new() { MinHeight = ToolbarHeight };
    private readonly Button _selectAll = new() { Classes = { "ghost" } };
    private readonly TextBlock _siteHint = new() { Classes = { "caption" }, IsVisible = false, Foreground = (IBrush?)Application.Current?.FindResource("WarningBrush") };
    private readonly Border _sitePanel = new() { Classes = { "card-raised" }, IsVisible = true };
    private readonly StackPanel _tabStrip = new() { Orientation = Orientation.Horizontal, Spacing = 2 };
    private readonly Grid _tabRow = new() { IsVisible = false };
    private readonly Grid _viewHost = new();
    private readonly Control _emptyState;
    private readonly ProgressBar _progress = new() { IsIndeterminate = true, Height = 2, MinHeight = 2, IsVisible = false };
    private readonly Button _back = new() { Classes = { "tool" }, IsEnabled = false };
    private readonly Button _forward = new() { Classes = { "tool" }, IsEnabled = false };
    private readonly Button _reload = new() { Classes = { "tool" } };
    private readonly Button _newTab = new() { Classes = { "tool" } };
    private readonly Button _external = new() { Classes = { "tool" }, IsEnabled = false };
    private readonly DispatcherTimer _follow = new() { Interval = TimeSpan.FromMilliseconds(1200) };
    private BrowserTab? _active;
    private bool _polling;

    public static string WebDataFolder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MS3DPRINT Manager", "webview");

    public ModelBrowserView()
    {
        foreach (var site in ModelSearchSite.Defaults)
            if (site.Name is "Nozzlehub" or "Printables" or "MakerWorld") _selected.Add(site.Name);

        var search = new Button { Content = "Rechercher", Classes = { "primary-action" }, MinHeight = ToolbarHeight };
        search.Click += (_, _) => SearchSelectedSites();
        _query.KeyDown += (_, e) => { if (e.Key == Key.Enter) { SearchSelectedSites(); e.Handled = true; } };
        AutomationProperties.SetName(_query, "Rechercher un modèle 3D");

        var searchBox = new Border
        {
            Classes = { "search-box" }, Height = ToolbarHeight,
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8,
                Children = { Icons.Glyph("IconSearch", 18), _query }
            }
        };
        Grid.SetColumn(_query, 1);
        _query.Classes.Add("search-input");

        _sitesButton.Click += (_, _) => SetSitesOpen(!_sitePanel.IsVisible);
        ToolTip.SetTip(_sitesButton, "Choisir les sites interrogés");

        var toolbar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 8, Children = { searchBox, _sitesButton, search } };
        Grid.SetColumn(_sitesButton, 1);
        Grid.SetColumn(search, 2);

        BuildSitePanel();

        _newTab.Content = Icons.Glyph("IconPlus", 18);
        _back.Content = Icons.Glyph("IconArrowLeft", 18);
        _forward.Content = Icons.Glyph("IconArrowRight", 18);
        _reload.Content = Icons.Glyph("IconRefresh", 18);
        _external.Content = Icons.Glyph("IconOpen", 18);
        Describe(_newTab, "Nouvel onglet (Ctrl+T)");
        Describe(_back, "Page précédente");
        Describe(_forward, "Page suivante");
        Describe(_reload, "Actualiser (F5)");
        Describe(_external, "Ouvrir cette page dans le navigateur de Windows (utile pour une connexion ou une vérification « humain »)");
        _back.Click += (_, _) => _active?.View.GoBack();
        _forward.Click += (_, _) => _active?.View.GoForward();
        _reload.Click += (_, _) => _active?.View.Refresh();
        _newTab.Click += (_, _) => OpenBlankTab();
        _external.Click += async (_, _) => await OpenExternalAsync();
        _address.KeyDown += (_, e) => { if (e.Key == Key.Enter) { NavigateActive(_address.Text ?? string.Empty); e.Handled = true; } };
        AutomationProperties.SetName(_address, "Adresse de la page");

        _tabStrip.Children.Add(_newTab);
        _tabRow.Children.Add(new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _tabStrip
        });

        var navigation = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*,Auto"), ColumnSpacing = 4,
            Children = { _back, _forward, _reload, _address, _external }
        };
        Grid.SetColumn(_forward, 1); Grid.SetColumn(_reload, 2); Grid.SetColumn(_address, 3); Grid.SetColumn(_external, 4);

        _emptyState = BuildEmptyState();
        _viewHost.Children.Add(_emptyState);

        var chrome = new StackPanel { Spacing = 8, Children = { toolbar, _sitePanel, _tabRow, navigation, _progress } };
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 6, Children = { chrome, _viewHost } };
        Grid.SetRow(_viewHost, 1);
        Content = root;

        UpdateSelectionUi();
        _follow.Tick += async (_, _) => await FollowActivePageAsync();
        AttachedToVisualTree += (_, _) => { _follow.Start(); _query.Focus(); };
        DetachedFromVisualTree += (_, _) => _follow.Stop();
        KeyDown += View_KeyDown;
    }

    private static void Describe(Button button, string text)
    {
        ToolTip.SetTip(button, text);
        AutomationProperties.SetName(button, text);
    }

    /// <summary>The site choice: tick-like chips by category, kept in one panel that folds away after a search.</summary>
    private void BuildSitePanel()
    {
        _selectAll.Click += (_, _) => SetAllSites(!AllSitesSelected);
        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children = { new TextBlock { Text = "Sites interrogés", Classes = { "card-title" }, VerticalAlignment = VerticalAlignment.Center }, _selectAll }
        };
        Grid.SetColumn(_selectAll, 1);

        var groups = new StackPanel { Spacing = 8 };
        foreach (var group in ModelSearchSite.Defaults.GroupBy(site => site.Category))
        {
            var chips = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var site in group)
            {
                var chip = new Button { Content = site.Name, Classes = { "filter-chip" }, Margin = new Thickness(0, 0, 6, 6) };
                if (site.Note.Length > 0) ToolTip.SetTip(chip, site.Note);
                chip.Click += (_, _) => { Toggle(site.Name); };
                _chips[site] = chip;
                chips.Children.Add(chip);
            }
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("170,*") };
            var label = new TextBlock { Text = group.Key, Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 8, 0, 0) };
            Grid.SetColumn(chips, 1);
            row.Children.Add(label);
            row.Children.Add(chips);
            groups.Children.Add(row);
        }

        _sitePanel.Child = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                header, groups, _siteHint,
                new TextBlock { Text = LicenceReminder, Classes = { "caption" }, TextWrapping = TextWrapping.Wrap }
            }
        };
    }

    private const string LicenceReminder = "Vérifiez la licence de chaque modèle avant de l’imprimer pour un client : beaucoup sont réservés à l’usage personnel (CC BY-NC, « non commercial »).";

    private Control BuildEmptyState()
    {
        var icon = Icons.Glyph("IconSearch", 40);
        icon.Opacity = 0.6;
        var title = new TextBlock { Text = "Cherchez un modèle sur plusieurs sites à la fois", Classes = { "section-title" }, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
        var steps = new TextBlock
        {
            Text = "Saisissez un mot-clé puis Entrée : un onglet s’ouvre pour chaque site coché, le premier de la liste s’affiche. Nozzlehub interroge à lui seul une dizaine de plateformes.",
            Classes = { "muted" }, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 520
        };
        var shortcuts = new TextBlock { Text = "Ctrl+L adresse · Ctrl+T nouvel onglet · Ctrl+W fermer l’onglet · F5 actualiser", Classes = { "caption" }, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 520 };
        return new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Children = { icon, title, steps, shortcuts } };
    }

    // ---- site selection ------------------------------------------------------------------------------------------

    private bool AllSitesSelected => _chips.Keys.All(site => _selected.Contains(site.Name));

    private void Toggle(string name)
    {
        if (!_selected.Remove(name)) _selected.Add(name);
        UpdateSelectionUi();
    }

    private void SetAllSites(bool selected)
    {
        foreach (var site in _chips.Keys)
            if (selected) _selected.Add(site.Name); else _selected.Remove(site.Name);
        UpdateSelectionUi();
    }

    private void UpdateSelectionUi()
    {
        foreach (var (site, chip) in _chips)
        {
            var on = _selected.Contains(site.Name);
            if (on) chip.Classes.Add("selected"); else chip.Classes.Remove("selected");
            AutomationProperties.SetName(chip, site.Name + (on ? ", sélectionné" : ", non sélectionné"));
        }
        _selectAll.Content = AllSitesSelected ? "Tout désélectionner" : "Tout sélectionner";
        _siteHint.IsVisible = false;
        UpdateSitesButton();
    }

    private void UpdateSitesButton()
    {
        _sitesButton.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            Children = { new TextBlock { Text = $"Sites · {_selected.Count}/{_chips.Count}", VerticalAlignment = VerticalAlignment.Center }, Icons.Glyph(_sitePanel.IsVisible ? "IconChevronUp" : "IconChevronDown", 14) }
        };
    }

    private void SetSitesOpen(bool open)
    {
        _sitePanel.IsVisible = open;
        UpdateSitesButton();
    }

    // ---- search and tabs -----------------------------------------------------------------------------------------

    /// <summary>Opens (or reuses) one tab per ticked site and shows the first of the list.</summary>
    private void SearchSelectedSites()
    {
        var text = (_query.Text ?? string.Empty).Trim();
        if (text.Length == 0) { _query.Focus(); return; }
        var chosen = _chips.Keys.Where(site => _selected.Contains(site.Name)).ToList();
        if (chosen.Count == 0)
        {
            _siteHint.Text = "Cochez au moins un site pour lancer la recherche.";
            _siteHint.IsVisible = true;
            SetSitesOpen(true);
            return;
        }

        BrowserTab? first = null;
        foreach (var site in chosen)
        {
            if (ModelSearchUrlBuilder.Build(site, text) is not { } uri) continue;
            BrowserTab tab;
            if (_tabs.FirstOrDefault(existing => existing.Site == site.Name) is { } existing)
            {
                existing.View.Navigate(uri);
                tab = existing;
            }
            else tab = AddTab(uri, site.Name, site.Name);
            first ??= tab;
        }
        if (first is not null) Select(first);
        SetSitesOpen(false);
    }

    private void NavigateActive(string text)
    {
        if (ModelSearchUrlBuilder.FromAddressBar(text) is not { } uri) return;
        if (_active is null) { Select(AddTab(uri, "Nouvel onglet")); return; }
        _active.View.Navigate(uri);
    }

    private void OpenBlankTab()
    {
        var tab = AddTab(null, "Nouvel onglet");
        Select(tab);
        _address.Focus();
    }

    private BrowserTab AddTab(Uri? uri, string title, string? site = null)
    {
        // created visible: the native engine is only started for a control that is shown when it is attached; Select hides the others afterwards
        var view = new NativeWebView();
        var label = new TextBlock { Text = title, MaxWidth = 150, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var close = new Button { Content = Icons.Glyph("IconClose", 12), Classes = { "tab-close" } };
        Describe(close, "Fermer l’onglet (Ctrl+W)");
        var header = new Button { Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { label, close } }, Classes = { "browser-tab" } };
        AutomationProperties.SetName(header, "Onglet " + title);
        var tab = new BrowserTab { View = view, Header = header, Site = site };
        header.Click += (_, _) => Select(tab);
        header.PointerReleased += (_, e) => { if (e.InitialPressMouseButton == MouseButton.Middle) CloseTab(tab); };
        close.Click += (_, e) => { e.Handled = true; CloseTab(tab); };

        // cookies and cache go with the user's other application data, not next to the executable (which is delivered in a synchronised folder)
        // (the Windows-specific event arguments are not visible to this platform-neutral project, hence the reflection)
        view.EnvironmentRequested += (_, e) => e.GetType().GetProperty("UserDataFolder")?.SetValue(e, WebDataFolder);
        if (uri is not null) view.AdapterCreated += (_, _) => view.Navigate(uri);
        view.NavigationStarted += (_, _) => { tab.Loading = true; RefreshChrome(); };
        view.NavigationCompleted += async (_, _) => { tab.Loading = false; RefreshChrome(); await ReadPageAsync(tab); };
        view.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            if (e.Request is { } request) Select(AddTab(request, "Nouvel onglet"));
        };

        _tabs.Add(tab);
        _tabStrip.Children.Insert(_tabStrip.Children.Count - 1, header);
        _viewHost.Children.Add(view);
        _tabRow.IsVisible = true;
        _emptyState.IsVisible = false;
        return tab;
    }

    private void Select(BrowserTab tab)
    {
        _active = tab;
        foreach (var other in _tabs)
        {
            var current = other == tab;
            other.View.IsVisible = current;
            if (current) other.Header.Classes.Add("selected"); else other.Header.Classes.Remove("selected");
        }
        _address.Text = tab.View.Source is { } source && source.AbsoluteUri != "about:blank" ? source.AbsoluteUri : string.Empty;
        RefreshChrome();
    }

    private void CloseTab(BrowserTab tab)
    {
        var index = _tabs.IndexOf(tab);
        _tabs.Remove(tab);
        _tabStrip.Children.Remove(tab.Header);
        _viewHost.Children.Remove(tab.View);
        if (_active == tab)
        {
            _active = null;
            if (_tabs.Count > 0) Select(_tabs[Math.Min(index, _tabs.Count - 1)]);
        }
        if (_tabs.Count == 0)
        {
            _tabRow.IsVisible = false;
            _emptyState.IsVisible = true;
            _address.Text = string.Empty;
            SetSitesOpen(true);
            _query.Focus();
        }
        RefreshChrome();
    }

    /// <summary>Back/forward availability, loading bar and the "open outside" button follow the active tab.</summary>
    private void RefreshChrome()
    {
        var active = _active;
        _back.IsEnabled = active?.View.CanGoBack == true;
        _forward.IsEnabled = active?.View.CanGoForward == true;
        _progress.IsVisible = active?.Loading == true;
        _external.IsEnabled = active?.View.Source is { Scheme: "http" or "https" };
    }

    /// <summary>Reads the address and title the page shows now: single-page sites change both without a navigation event.</summary>
    private async Task ReadPageAsync(BrowserTab tab)
    {
        try
        {
            var raw = await tab.View.InvokeScript("JSON.stringify([location.href, document.title])");
            if (string.IsNullOrWhiteSpace(raw)) return;
            // some engines hand the JSON string back as a JSON string of itself
            var json = raw.StartsWith('"') ? JsonSerializer.Deserialize<string>(raw) ?? raw : raw;
            var pair = JsonSerializer.Deserialize<string[]>(json);
            if (pair is not { Length: 2 }) return;
            if (!string.IsNullOrWhiteSpace(pair[1])) ToolTip.SetTip(tab.Header, pair[1]);
            if (tab == _active && !_address.IsFocused && Uri.TryCreate(pair[0], UriKind.Absolute, out var page) && page.Scheme is "http" or "https")
                _address.Text = page.AbsoluteUri;
            if (tab == _active) RefreshChrome();
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or NotSupportedException or ArgumentException)
        {
            // a page that refuses scripts keeps the address it was opened with
        }
    }

    private async Task FollowActivePageAsync()
    {
        if (_polling || _active is not { } tab || !IsEffectivelyVisible) return;
        _polling = true;
        try { await ReadPageAsync(tab); }
        finally { _polling = false; }
    }

    private async Task OpenExternalAsync()
    {
        if (_active?.View.Source is not { Scheme: "http" or "https" } page) return;
        if (TopLevel.GetTopLevel(this)?.Launcher is { } launcher) await launcher.LaunchUriAsync(page);
    }

    private void View_KeyDown(object? sender, KeyEventArgs e)
    {
        var control = e.KeyModifiers == KeyModifiers.Control;
        if (control && e.Key == Key.L) { _address.Focus(); _address.SelectAll(); e.Handled = true; }
        else if (control && e.Key == Key.T) { OpenBlankTab(); e.Handled = true; }
        else if (control && e.Key == Key.W && _active is { } tab) { CloseTab(tab); e.Handled = true; }
        else if ((e.Key == Key.F5 || (control && e.Key == Key.R)) && _active is not null) { _active.View.Refresh(); e.Handled = true; }
    }
}
