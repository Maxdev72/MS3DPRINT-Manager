using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using MS3DPRINT.Manager.Core.ModelSearch;

namespace MS3DPRINT.Manager.Desktop.ModelSearch;

/// <summary>Prototype of the 3D-model search page: a search box that opens one browser tab per site, plus a small multi-tab browser.</summary>
public sealed class ModelBrowserView : UserControl
{
    private sealed class BrowserTab
    {
        public required NativeWebView View { get; init; }
        public required Button Header { get; init; }
        public required TextBlock Title { get; init; }
    }

    private readonly List<BrowserTab> _tabs = [];
    private readonly TextBox _query = new() { PlaceholderText = "Rechercher un modèle 3D (ex. spatule mastic)", MinWidth = 320 };
    private readonly TextBox _address = new() { PlaceholderText = "Adresse ou recherche", VerticalContentAlignment = VerticalAlignment.Center };
    private readonly StackPanel _tabStrip = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    private readonly Grid _viewHost = new();
    private readonly Button _back = new() { Content = Icons.Glyph("IconArrowLeft") };
    private readonly Button _forward = new() { Content = Icons.Glyph("IconArrowRight") };
    private readonly Button _reload = new() { Content = Icons.Glyph("IconRefresh") };
    private readonly Button _newTab = new() { Content = Icons.Glyph("IconPlus") };
    private readonly WrapPanel _siteChecks = new() { Orientation = Orientation.Horizontal };
    private readonly Dictionary<ModelSearchSite, CheckBox> _sites = [];
    private BrowserTab? _active;

    public static string WebDataFolder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MS3DPRINT Manager", "webview");

    public ModelBrowserView()
    {
        foreach (var site in ModelSearchSite.Defaults)
        {
            var check = new CheckBox { Content = site.Name, IsChecked = site.Name is "Yeggi" or "MakerWorld" or "Printables", Margin = new Thickness(0, 0, 14, 0) };
            _sites[site] = check;
            _siteChecks.Children.Add(check);
        }

        var search = new Button { Content = "Rechercher", Classes = { "primary-action" } };
        search.Click += (_, _) => SearchSelectedSites();
        _query.KeyDown += (_, e) => { if (e.Key == Key.Enter) { SearchSelectedSites(); e.Handled = true; } };
        _address.KeyDown += (_, e) => { if (e.Key == Key.Enter) { NavigateActive(_address.Text ?? string.Empty); e.Handled = true; } };
        _back.Click += (_, _) => _active?.View.GoBack();
        _forward.Click += (_, _) => _active?.View.GoForward();
        _reload.Click += (_, _) => _active?.View.Refresh();
        _newTab.Click += (_, _) => AddTab(new Uri("https://www.yeggi.com/"), "Nouvel onglet");

        var searchRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8, Children = { _query, search } };
        Grid.SetColumn(search, 1);
        var addressRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*"), ColumnSpacing = 6, Children = { _back, _forward, _reload, _address } };
        Grid.SetColumn(_forward, 1); Grid.SetColumn(_reload, 2); Grid.SetColumn(_address, 3);
        var tabRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6, Children = { new ScrollViewer { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, Content = _tabStrip }, _newTab } };
        Grid.SetColumn(_newTab, 1);

        var top = new StackPanel { Spacing = 8, Children = { searchRow, _siteChecks, tabRow, addressRow } };
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 10, Children = { top, _viewHost } };
        Grid.SetRow(_viewHost, 1);
        Content = root;
    }

    /// <summary>Opens one tab per ticked site on the results of <see cref="_query"/>.</summary>
    private void SearchSelectedSites()
    {
        var text = _query.Text ?? string.Empty;
        foreach (var (site, check) in _sites)
        {
            if (check.IsChecked != true || ModelSearchUrlBuilder.Build(site, text) is not { } uri) continue;
            AddTab(uri, site.Name);
        }
    }

    private void NavigateActive(string text)
    {
        if (_active is null || ModelSearchUrlBuilder.FromAddressBar(text) is not { } uri) return;
        _active.View.Navigate(uri);
    }

    private void AddTab(Uri uri, string title)
    {
        // created visible: the native engine is only started for a control that is shown when it is attached; Select hides the others afterwards
        var view = new NativeWebView();
        var label = new TextBlock { Text = title, MaxWidth = 140, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var close = new Button { Content = Icons.Glyph("IconClose"), Padding = new Thickness(4), MinWidth = 0, MinHeight = 0 };
        var header = new Button { Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { label, close } } };
        var tab = new BrowserTab { View = view, Header = header, Title = label };
        header.Click += (_, _) => Select(tab);
        close.Click += (_, e) => { e.Handled = true; CloseTab(tab); };

        view.NavigationCompleted += (_, e) =>
        {
            if (_active == tab) _address.Text = view.Source?.AbsoluteUri;
        };
        view.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            if (e.Request is { } request) AddTab(request, "Nouvel onglet");
        };

        // The native web engine only exists once the control is attached and shown: a Navigate before that is lost.
        // cookies and cache go with the user's other application data, not next to the executable (which is delivered in a synchronised folder)
        // (the Windows-specific event arguments are not visible to this platform-neutral project, hence the reflection)
        view.EnvironmentRequested += (_, e) => e.GetType().GetProperty("UserDataFolder")?.SetValue(e, WebDataFolder);
        view.AdapterCreated += (_, _) => view.Navigate(uri);

        _tabs.Add(tab);
        _tabStrip.Children.Add(header);
        _viewHost.Children.Add(view);
        Select(tab);
    }

    private void Select(BrowserTab tab)
    {
        _active = tab;
        foreach (var other in _tabs)
        {
            var current = other == tab;
            other.View.IsVisible = current;
            if (current) other.Header.Classes.Add("selected"); else other.Header.Classes.Remove("selected");
            other.Header.FontWeight = current ? FontWeight.SemiBold : FontWeight.Normal;
        }
        _address.Text = tab.View.Source?.AbsoluteUri;
    }

    private void CloseTab(BrowserTab tab)
    {
        var index = _tabs.IndexOf(tab);
        _tabs.Remove(tab);
        _tabStrip.Children.Remove(tab.Header);
        _viewHost.Children.Remove(tab.View);
        if (_active != tab) return;
        _active = null;
        if (_tabs.Count > 0) Select(_tabs[Math.Min(index, _tabs.Count - 1)]); else _address.Text = string.Empty;
    }
}
