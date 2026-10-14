using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MS3DPRINT.Manager.Core.Files;
using MS3DPRINT.Manager.Core.ModelSearch;

namespace MS3DPRINT.Manager.Desktop.ModelSearch;

/// <summary>
/// 3D-model search page, laid out like a browser: tabs, then one bar (back, forward, reload, a single box for a search or an
/// address, the site choice) and the page itself. A keyword searches every ticked site, an address opens it. What the browser
/// downloads goes to a buffer folder of the application, and a finished 3D file is offered to the slicer.
/// </summary>
public sealed class ModelBrowserView : UserControl
{
    private sealed class BrowserTab
    {
        /// <summary>The live web view; null while the tab sleeps (its page is released and loaded again when the tab is shown).</summary>
        public NativeWebView? View { get; set; }
        public required Button Header { get; init; }
        /// <summary>The site this tab searches, so that a new search reuses it instead of opening a duplicate.</summary>
        public string? Site { get; init; }
        /// <summary>Where this tab is, or what it loads when it wakes up.</summary>
        public Uri? Address { get; set; }
        public bool Loading { get; set; }
        /// <summary>A page that costs a lot of memory: never loaded in the background.</summary>
        public bool Heavy { get; init; }
        /// <summary>True once the page of the current web view has finished loading at least once.</summary>
        public bool Navigated { get; set; }
        /// <summary>Small dot in the tab header while its page loads.</summary>
        public required Border LoadingDot { get; init; }
        public DateTime LastShown { get; set; } = DateTime.UtcNow;
    }

    // Memory budget: each loaded page costs one to three hundred MB, so only a few stay loaded and the others sleep.
    private const int MaxLivePages = 8;
    private static readonly TimeSpan TabSleepAfter = TimeSpan.FromMinutes(10);
    // the tabs behind the shown one load in the background, one at a time, so that a click shows a page that is already there
    private static readonly TimeSpan PreloadTimeout = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan PageSleepAfter = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan BufferKeeps = TimeSpan.FromDays(7);

    private const string LicenceReminder = "Vérifiez la licence de chaque modèle avant de l’imprimer pour un client : beaucoup sont réservés à l’usage personnel (CC BY-NC, « non commercial »).";

    private readonly List<BrowserTab> _tabs = [];
    private readonly HashSet<string> _selected = new(StringComparer.Ordinal);
    // the site choice is drawn twice (start page and "Sites" panel) from the same selection
    private readonly List<(string Name, Button Chip)> _chips = [];
    private readonly List<Button> _selectAlls = [];
    private readonly List<TextBlock> _siteHints = [];

    private readonly TextBox _omnibox = new() { PlaceholderText = "Rechercher un modèle 3D ou saisir une adresse", VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _sitesButton = new() { MinHeight = 36 };
    private readonly Border _sitePanel = new() { Classes = { "card-raised" }, IsVisible = false };
    private readonly Border _toolbar = new() { Classes = { "browser-toolbar" } };
    private readonly StackPanel _tabStrip = new() { Orientation = Orientation.Horizontal, Spacing = 2 };
    private readonly Grid _tabRow = new() { IsVisible = false };
    private readonly Grid _viewHost = new();
    private readonly Control _startPage;
    // the start page has its own big search box; an example is typed into it, like someone searching
    private readonly TextBox _startInput = new() { VerticalAlignment = VerticalAlignment.Center, FontSize = 16 };
    private readonly DispatcherTimer _typing = new() { Interval = TimeSpan.FromMilliseconds(65) };
    // all in French, and things one really prints: tools, storage, spare parts, decoration
    private static readonly string[] ExampleQueries =
    [
        "spatule mastic", "support de bobine de filament", "boîte de rangement vis", "organiseur de câbles", "pièce de rechange électroménager",
        "poignée de tiroir", "figurine articulée", "vase spirale", "porte-clés personnalisé", "support de casque audio"
    ];
    private int _exampleIndex, _typedLength, _pauseTicks, _blink;
    private bool _erasing;
    /// <summary>Set by a click in the box: the user is about to type, the animation steps aside until the box is left.</summary>
    private bool _animationPaused;
    private const string StaticHint = "Rechercher un modèle 3D ou saisir une adresse";
    private readonly ProgressBar _progress = new() { IsIndeterminate = true, Height = 2, MinHeight = 2, IsVisible = false };
    private readonly Button _back = new() { Classes = { "tool" }, IsEnabled = false };
    private readonly Button _forward = new() { Classes = { "tool" }, IsEnabled = false };
    private readonly Button _reload = new() { Classes = { "tool" } };
    private readonly Button _newTab = new() { Classes = { "tool" } };
    private readonly Button _external = new() { Classes = { "tool" }, IsEnabled = false };
    private readonly DispatcherTimer _follow = new() { Interval = TimeSpan.FromMilliseconds(1200) };

    // downloads: the buffer folder, and the one window that offers finished 3D files to the slicer
    private readonly DownloadBuffer _buffer = new(DownloadBuffer.DefaultFolder);
    private DownloadDialog? _downloadDialog;

    private BrowserTab? _active;
    private readonly Queue<BrowserTab> _preload = new();
    private BrowserTab? _preloading;
    private DateTime _preloadStarted;
    private bool _polling;
    private DateTime? _hiddenSince;

    public static string WebDataFolder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MS3DPRINT Manager", "webview");

    public ModelBrowserView()
    {
        foreach (var site in ModelSearchSite.Defaults)
            if (site.Name is "Thangs" or "Printables" or "MakerWorld") _selected.Add(site.Name);

        PrepareDownloads();

        // ---- the bar: back, forward, reload, one box for a keyword or an address, the sites, open outside ----
        _back.Content = Icons.Glyph("IconArrowLeft", 18);
        _forward.Content = Icons.Glyph("IconArrowRight", 18);
        _reload.Content = Icons.Glyph("IconRefresh", 18);
        _newTab.Content = Icons.Glyph("IconPlus", 18);
        _external.Content = Icons.Glyph("IconOpen", 18);
        Describe(_back, "Page précédente");
        Describe(_forward, "Page suivante");
        Describe(_reload, "Actualiser (F5)");
        Describe(_newTab, "Nouvel onglet (Ctrl+T)");
        Describe(_external, "Ouvrir cette page dans le navigateur de Windows (utile pour une connexion ou une vérification « humain »)");
        _back.Click += (_, _) => Engine(() => _active?.View?.GoBack());
        _forward.Click += (_, _) => Engine(() => _active?.View?.GoForward());
        _reload.Click += (_, _) => Engine(() => _active?.View?.Refresh());
        _newTab.Click += (_, _) => OpenBlankTab();
        _external.Click += async (_, _) => await OpenExternalAsync();

        _omnibox.Classes.Add("search-input");
        _omnibox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Submit(_omnibox.Text); e.Handled = true; } };
        _startInput.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Submit(_startInput.Text); e.Handled = true; } };
        _startInput.TextChanged += (_, _) => UpdateStartAnimation();
        _startInput.LostFocus += (_, _) => { _animationPaused = false; UpdateStartAnimation(); };
        _typing.Tick += (_, _) => TypeStep();
        _omnibox.GotFocus += (_, _) => Dispatcher.UIThread.Post(() => _omnibox.SelectAll());
        AutomationProperties.SetName(_omnibox, "Rechercher un modèle 3D ou saisir une adresse");
        var omniBorder = new Border
        {
            Classes = { "search-box" }, Height = 36,
            Child = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8, Children = { Icons.Glyph("IconSearch", 18), _omnibox } }
        };
        Grid.SetColumn(_omnibox, 1);

        _sitesButton.Click += (_, _) => SetSitesOpen(!_sitePanel.IsVisible);
        ToolTip.SetTip(_sitesButton, "Choisir les sites interrogés par une recherche");
        AutomationProperties.SetName(_sitesButton, "Choisir les sites");

        var bar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*,Auto,Auto"), ColumnSpacing = 4,
            Children = { _back, _forward, _reload, omniBorder, _sitesButton, _external }
        };
        Grid.SetColumn(_forward, 1); Grid.SetColumn(_reload, 2); Grid.SetColumn(omniBorder, 3); Grid.SetColumn(_sitesButton, 4); Grid.SetColumn(_external, 5);
        omniBorder.Margin = new Thickness(4, 0);
        _toolbar.Child = bar;

        // ---- tabs ----
        _tabStrip.Children.Add(_newTab);
        _tabRow.Children.Add(new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _tabStrip
        });

        // ---- site panel (once a search is made) and start page (before) ----
        _sitePanel.Child = BuildChooser();
        _startPage = BuildStartPage();
        _viewHost.Children.Add(_startPage);

        var chrome = new StackPanel { Spacing = 0, Children = { _tabRow, _toolbar, _progress, _sitePanel } };
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 6, Children = { chrome, _viewHost } };
        Grid.SetRow(_viewHost, 1);
        Content = root;

        UpdateSelectionUi();
        UpdateToolbarShape();
        _follow.Tick += async (_, _) => { ManageMemory(); PreloadTick(); await FollowActivePageAsync(); };
        AttachedToVisualTree += (_, _) => { _follow.Start(); FocusInput(); UpdateStartAnimation(); };
        DetachedFromVisualTree += (_, _) => { _follow.Stop(); _typing.Stop(); };
        KeyDown += View_KeyDown;
#if DEBUG
        // development hook (Debug builds only): opens an address when the page appears, to test downloads without clicking
        if (Environment.GetEnvironmentVariable("MS3DPRINT_TEST_URL") is { Length: > 0 } testUrl && Uri.TryCreate(testUrl, UriKind.Absolute, out var testUri))
            AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() => { if (_tabs.Count == 0) Select(AddTab(testUri, "Test")); });
        if (Environment.GetEnvironmentVariable("MS3DPRINT_TEST_QUERY") is { Length: > 0 } testQuery)
            AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() => { if (_tabs.Count == 0) SearchSelectedSites(testQuery); });
#endif
    }

    /// <summary>Runs a call to the web engine; when it is not ready for it (invalid state) the call is simply skipped.</summary>
    private static void Engine(Action action) => Engine(() => { action(); return true; });

    private static bool Engine(Func<bool> call)
    {
        try { return call(); }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or InvalidOperationException or ObjectDisposedException or NotSupportedException)
        {
            return false;
        }
    }

    private static void Describe(Button button, string text)
    {
        ToolTip.SetTip(button, text);
        AutomationProperties.SetName(button, text);
    }

    // ---- downloads -----------------------------------------------------------------------------------------------

    /// <summary>Points the browser profile at the buffer folder (before the browser starts) and watches it.</summary>
    private void PrepareDownloads()
    {
        try
        {
            _buffer.PrepareProfile(WebDataFolder);
            _buffer.FileReady += file => Dispatcher.UIThread.Post(() => ShowDownload([file], null));
            _buffer.BatchReady += batch => Dispatcher.UIThread.Post(() => ShowDownload(batch.Files, batch.Problem));
            _buffer.StartWatching();
            _ = Task.Run(() => _buffer.PurgeOlderThan(BufferKeeps));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // without the buffer the browser downloads to the Downloads folder as usual; the page works the same
        }
    }

    /// <summary>
    /// Shows the download window, or adds to it when it is already open: several files in a row never make several windows.
    /// </summary>
    private void ShowDownload(IReadOnlyList<BufferedFile> files, string? problem)
    {
        if (_downloadDialog is null)
        {
            var dialog = new DownloadDialog();
            dialog.Closed += (_, _) => { if (ReferenceEquals(_downloadDialog, dialog)) _downloadDialog = null; };
            _downloadDialog = dialog;
            if (TopLevel.GetTopLevel(this) is Window owner) dialog.Show(owner); else dialog.Show();
        }
        _downloadDialog.Add(files);
        if (problem is not null) _downloadDialog.Say(problem);
        _downloadDialog.Activate();
    }

    // ---- site choice -----------------------------------------------------------------------------------------------

    /// <summary>The site choice: chips by category, with select all. One instance on the start page, one in the "Sites" panel.</summary>
    private Control BuildChooser()
    {
        var selectAll = new Button { Classes = { "ghost" } };
        selectAll.Click += (_, _) => SetAllSites(!AllSitesSelected);
        _selectAlls.Add(selectAll);
        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children = { new TextBlock { Text = "Sites interrogés par une recherche", Classes = { "card-title" }, VerticalAlignment = VerticalAlignment.Center }, selectAll }
        };
        Grid.SetColumn(selectAll, 1);

        var groups = new StackPanel { Spacing = 6 };
        foreach (var group in ModelSearchSite.Defaults.GroupBy(site => site.Category))
        {
            var chips = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var site in group)
            {
                var name = site.Name;
                var chip = new Button { Content = name, Classes = { "filter-chip" }, Margin = new Thickness(0, 0, 6, 6) };
                if (site.Note.Length > 0) ToolTip.SetTip(chip, site.Note);
                chip.Click += (_, _) => Toggle(name);
                _chips.Add((name, chip));
                chips.Children.Add(chip);
            }
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("170,*") };
            var label = new TextBlock { Text = group.Key, Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 8, 0, 0) };
            Grid.SetColumn(chips, 1);
            row.Children.Add(label);
            row.Children.Add(chips);
            groups.Children.Add(row);
        }

        var hint = new TextBlock { Classes = { "caption" }, IsVisible = false, Foreground = (IBrush?)Application.Current?.FindResource("WarningBrush") };
        _siteHints.Add(hint);
        return new StackPanel
        {
            Spacing = 10,
            Children = { header, groups, hint, new TextBlock { Text = LicenceReminder, Classes = { "caption" }, TextWrapping = TextWrapping.Wrap } }
        };
    }

    private Control BuildStartPage()
    {
        var title = new TextBlock { Text = "Cherchez un modèle sur plusieurs sites à la fois", Classes = { "page-title" }, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };

        _startInput.Classes.Add("search-input");
        AutomationProperties.SetName(_startInput, "Rechercher un modèle 3D ou saisir une adresse");
        var go = new Button { Content = Icons.Glyph("IconArrowRight", 18), Classes = { "tool" } };
        Describe(go, "Lancer la recherche (Entrée)");
        go.Click += (_, _) => Submit(_startInput.Text);
        var inputBox = new Border
        {
            Classes = { "search-box" }, Height = 52, MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 10,
                Children = { Icons.Glyph("IconSearch", 20), _startInput, go }
            }
        };
        Grid.SetColumn(_startInput, 1);
        Grid.SetColumn(go, 2);
        // a click anywhere in the box stops the animation (the text box handles its own clicks, hence handledEventsToo)
        inputBox.AddHandler(InputElement.PointerPressedEvent, (_, _) => { _animationPaused = true; UpdateStartAnimation(); }, handledEventsToo: true);

        var steps = new TextBlock
        {
            Text = "Entrée : un onglet s’ouvre pour chaque site coché, le premier de la liste s’affiche. Une adresse s’ouvre directement.",
            Classes = { "caption" }, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap
        };
        var card = new Border { Classes = { "card-raised" }, Child = BuildChooser(), Margin = new Thickness(0, 14, 0, 0) };
        var shortcuts = new TextBlock { Text = "Ctrl+L barre · Ctrl+T nouvel onglet · Ctrl+W fermer l’onglet · F5 actualiser", Classes = { "caption" }, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center };

        // centred in the page, a little above the middle (the eye reads the middle of a page slightly higher than its true centre)
        var column = new StackPanel
        {
            Spacing = 14, MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 48), Children = { title, inputBox, steps, card, shortcuts }
        };
        var holder = new Grid { Margin = new Thickness(0, 16), Children = { column } };
        var scroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = holder };
        // the column fills the viewport so that it can be centred in it; a window too short for it scrolls
        scroll.SizeChanged += (_, e) => holder.MinHeight = Math.Max(0, e.NewSize.Height - 32);
        return scroll;
    }

    // ---- start page animation: examples typed into the search box ----------------------------------------------------

    /// <summary>The animation runs only while the start page is on show with an empty box; with reduced motion the example stays still.</summary>
    private void UpdateStartAnimation()
    {
        var empty = string.IsNullOrEmpty(_startInput.Text);
        var wanted = _startPage.IsVisible && empty && !_animationPaused;
        if (!wanted) { _typing.Stop(); if (empty) _startInput.PlaceholderText = StaticHint; return; }
        if (DesktopApplication.ReduceMotion) { _typing.Stop(); _startInput.PlaceholderText = ExampleQueries[0]; return; }
        if (!_typing.IsEnabled) _typing.Start();
    }

    /// <summary>One step of the typing: letter by letter, a pause on the finished example, quicker deletion, then the next example.</summary>
    private void TypeStep()
    {
        if (!IsEffectivelyVisible || !_startPage.IsVisible || _animationPaused || !string.IsNullOrEmpty(_startInput.Text)) return;
        _blink++;
        var text = ExampleQueries[_exampleIndex];
        if (_pauseTicks > 0) _pauseTicks--;
        else if (!_erasing)
        {
            _typedLength++;
            if (_typedLength >= text.Length) { _typedLength = text.Length; _erasing = true; _pauseTicks = 26; }
        }
        else
        {
            _typedLength = Math.Max(0, _typedLength - 2);
            if (_typedLength == 0) { _erasing = false; _exampleIndex = (_exampleIndex + 1) % ExampleQueries.Length; _pauseTicks = 8; }
        }
        // a thin caret that blinks, as in a field being typed in
        _startInput.PlaceholderText = text[.._typedLength] + (_blink / 6 % 2 == 0 ? "|" : " ");
    }

    /// <summary>The start page's own box while it is on show, the bar's box otherwise.</summary>
    private void FocusInput(bool selectAll = false)
    {
        var input = _startPage.IsVisible ? _startInput : _omnibox;
        input.Focus();
        if (selectAll) input.SelectAll();
    }

    private bool AllSitesSelected => _chips.Select(chip => chip.Name).Distinct().All(_selected.Contains);

    private void Toggle(string name)
    {
        if (!_selected.Remove(name)) _selected.Add(name);
        UpdateSelectionUi();
    }

    private void SetAllSites(bool selected)
    {
        foreach (var site in ModelSearchSite.Defaults)
            if (selected) _selected.Add(site.Name); else _selected.Remove(site.Name);
        UpdateSelectionUi();
    }

    private void UpdateSelectionUi()
    {
        foreach (var (name, chip) in _chips)
        {
            var on = _selected.Contains(name);
            if (on) chip.Classes.Add("selected"); else chip.Classes.Remove("selected");
            AutomationProperties.SetName(chip, name + (on ? ", sélectionné" : ", non sélectionné"));
        }
        foreach (var selectAll in _selectAlls) selectAll.Content = AllSitesSelected ? "Tout désélectionner" : "Tout sélectionner";
        foreach (var hint in _siteHints) hint.IsVisible = false;
        UpdateSitesButton();
    }

    private void UpdateSitesButton()
    {
        _sitesButton.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            Children = { new TextBlock { Text = $"Sites · {_selected.Count}/{ModelSearchSite.Defaults.Count}", VerticalAlignment = VerticalAlignment.Center }, Icons.Glyph(_sitePanel.IsVisible ? "IconChevronUp" : "IconChevronDown", 14) }
        };
    }

    /// <summary>The panel folds over the page area; before any tab exists the start page already shows the choice.</summary>
    private void SetSitesOpen(bool open)
    {
        _sitePanel.IsVisible = open && _tabs.Count > 0;
        UpdateSitesButton();
    }

    // ---- the bar's box: a keyword searches the sites, an address opens ------------------------------------------------

    private void Submit(string? typed)
    {
        var text = (typed ?? string.Empty).Trim();
        if (text.Length == 0) return;
        if (ModelSearchUrlBuilder.TryParseAddress(text) is { } address)
        {
            if (_active is null) Select(AddTab(address, "Nouvel onglet"));
            else { Load(_active, address); Select(_active); }
            return;
        }
        SearchSelectedSites(text);
    }

    /// <summary>Opens (or reuses) one tab per ticked site and shows the first of the list.</summary>
    private void SearchSelectedSites(string text)
    {
        var chosen = ModelSearchSite.Defaults.Where(site => _selected.Contains(site.Name)).ToList();
        if (chosen.Count == 0)
        {
            foreach (var hint in _siteHints) { hint.Text = "Cochez au moins un site pour lancer la recherche."; hint.IsVisible = true; }
            SetSitesOpen(true);
            return;
        }

        BrowserTab? first = null;
        var behind = new List<BrowserTab>();
        foreach (var site in chosen)
        {
            if (ModelSearchUrlBuilder.Build(site, text) is not { } uri) continue;
            var tab = _tabs.FirstOrDefault(existing => existing.Site == site.Name) ?? AddTab(uri, site.Name, site.Name);
            Load(tab, uri);
            if (first is null) first = tab; else behind.Add(tab);
        }
        if (first is not null)
        {
            Select(first);
            StartPreload(behind);
        }
        SetSitesOpen(false);
    }

    // ---- background loading of the other tabs ------------------------------------------------------------------------------

    private void StartPreload(IEnumerable<BrowserTab> tabs)
    {
        _preload.Clear();
        _preloading = null;
        foreach (var tab in tabs) _preload.Enqueue(tab);
    }

    /// <summary>Loads the next queued tab, hidden behind the shown one, while there is room in the page budget.</summary>
    private void PreloadNext()
    {
        _preloading = null;
        while (_preload.Count > 0)
        {
            if (_tabs.Count(t => t.View is not null) >= MaxLivePages) { _preload.Clear(); return; }
            var next = _preload.Dequeue();
            if (!_tabs.Contains(next) || next.View is not null || next.Address is null || next == _active || next.Heavy) continue;
            Wake(next);
            if (next.View is { } view) view.IsVisible = false;
            _preloading = next;
            _preloadStarted = DateTime.UtcNow;
            RefreshChrome();
            return;
        }
    }

    /// <summary>A page finished loading: if it was the shown one or the one being preloaded, the next queued tab goes.</summary>
    private void OnPageLoaded(BrowserTab tab)
    {
        if (_preload.Count == 0 && _preloading is null) return;
        if (tab == _preloading || (_preloading is null && tab == _active)) PreloadNext();
    }

    private void PreloadTick()
    {
        if (!IsEffectivelyVisible || (_preload.Count == 0 && _preloading is null)) return;
        // a page that never says it is done, or a shown page that already finished: do not stall the queue
        if (_preloading is { } current && (DateTime.UtcNow - _preloadStarted > PreloadTimeout || !_tabs.Contains(current) || current.View is null)) PreloadNext();
        else if (_preloading is null && _active is { Navigated: true, Loading: false }) PreloadNext();
    }

    /// <summary>Points a tab at an address: a loaded tab navigates now, a sleeping one loads it when it is shown.</summary>
    private void Load(BrowserTab tab, Uri uri)
    {
        tab.Address = uri;
        if (tab.View is { } view)
        {
            if (tab == _active) { tab.Navigated = false; Engine(() => view.Navigate(uri)); }
            else Sleep(tab);
        }
        else if (tab == _active) Wake(tab);
    }

    // ---- tabs ------------------------------------------------------------------------------------------------------------

    private void OpenBlankTab()
    {
        var tab = AddTab(null, "Nouvel onglet");
        Select(tab);
        _omnibox.Focus();
    }

    /// <summary>Adds the tab header only: the web view is created when the tab is first shown, which keeps a 16-site search light.</summary>
    private BrowserTab AddTab(Uri? uri, string title, string? site = null)
    {
        var label = new TextBlock { Text = title, MaxWidth = 150, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var close = new Button { Content = Icons.Glyph("IconClose", 12), Classes = { "tab-close" } };
        Describe(close, "Fermer l’onglet (Ctrl+W)");
        var dot = new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center, IsVisible = false, Background = (IBrush?)Application.Current?.FindResource("AccentBrush") };
        var header = new Button { Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { label, dot, close } }, Classes = { "browser-tab", "sleeping" } };
        AutomationProperties.SetName(header, "Onglet " + title);
        var tab = new BrowserTab { Header = header, Site = site, Address = uri, LoadingDot = dot, Heavy = ModelSearchSite.Defaults.Any(known => known.Name == site && known.Heavy) };
        header.Click += (_, _) => Select(tab);
        header.PointerReleased += (_, e) => { if (e.InitialPressMouseButton == MouseButton.Middle) CloseTab(tab); };
        close.Click += (_, e) => { e.Handled = true; CloseTab(tab); };

        _tabs.Add(tab);
        _tabStrip.Children.Insert(_tabStrip.Children.Count - 1, header);
        _tabRow.IsVisible = true;
        _startPage.IsVisible = false;
        UpdateToolbarShape();
        UpdateStartAnimation();
        return tab;
    }

    /// <summary>The bar joins the selected tab: square at the corner under the tabs, rounded all round when there are none.</summary>
    private void UpdateToolbarShape()
    {
        _toolbar.CornerRadius = _tabRow.IsVisible ? new CornerRadius(0, 8, 8, 8) : new CornerRadius(8);
        // before the first search the start page has its own search box: the bar would only repeat it
        _toolbar.IsVisible = _tabRow.IsVisible;
    }

    /// <summary>Creates the web view of a tab (the page loads from <see cref="BrowserTab.Address"/>).</summary>
    private void Wake(BrowserTab tab)
    {
        if (tab.View is not null) return;
        // created visible: the native engine is only started for a control that is shown when it is attached; Select hides the others
        var view = new NativeWebView();
        tab.View = view;
        tab.Navigated = false;
        // cookies and cache go with the user's other application data, not next to the executable (which is delivered in a synchronised folder)
        // (the Windows-specific event arguments are not visible to this platform-neutral project, hence the reflection)
        view.EnvironmentRequested += (_, e) => e.GetType().GetProperty("UserDataFolder")?.SetValue(e, WebDataFolder);
        var address = tab.Address;
        if (address is not null) view.AdapterCreated += (_, _) => Engine(() => view.Navigate(address));
        view.NavigationStarted += (_, _) => { tab.Loading = true; RefreshChrome(); };
        view.NavigationCompleted += async (_, _) => { tab.Loading = false; tab.Navigated = true; RefreshChrome(); await ReadPageAsync(tab); OnPageLoaded(tab); };
        view.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            if (e.Request is { } request) Select(AddTab(request, "Nouvel onglet"));
        };
        _viewHost.Children.Add(view);
        tab.Header.Classes.Remove("sleeping");
    }

    /// <summary>Releases the web page (and its browser processes) but keeps the tab and its address.</summary>
    private void Sleep(BrowserTab tab)
    {
        if (tab.View is not { } view) return;
        if (view.Source is { Scheme: "http" or "https" } current && tab.Address is null) tab.Address = current;
        _viewHost.Children.Remove(view);
        tab.View = null;
        tab.Loading = false;
        tab.Header.Classes.Add("sleeping");
        if (tab == _active) RefreshChrome();
    }

    private void Select(BrowserTab tab)
    {
        _active = tab;
        tab.LastShown = DateTime.UtcNow;
        Wake(tab);
        foreach (var other in _tabs)
        {
            var current = other == tab;
            if (other.View is { } view) view.IsVisible = current;
            if (current) other.Header.Classes.Add("selected"); else other.Header.Classes.Remove("selected");
        }
        // the page budget: the most recently shown pages stay loaded, the others sleep
        foreach (var extra in _tabs.Where(t => t.View is not null).OrderByDescending(t => t.LastShown).Skip(MaxLivePages).ToList()) Sleep(extra);
        _omnibox.Text = tab.Address is { } address && address.AbsoluteUri != "about:blank" ? address.AbsoluteUri : string.Empty;
        RefreshChrome();
    }

    private void CloseTab(BrowserTab tab)
    {
        var index = _tabs.IndexOf(tab);
        Sleep(tab);
        _tabs.Remove(tab);
        _tabStrip.Children.Remove(tab.Header);
        if (_active == tab)
        {
            _active = null;
            if (_tabs.Count > 0) Select(_tabs[Math.Min(index, _tabs.Count - 1)]);
        }
        if (_tabs.Count == 0)
        {
            _tabRow.IsVisible = false;
            _startPage.IsVisible = true;
            _sitePanel.IsVisible = false;
            _omnibox.Text = string.Empty;
            UpdateToolbarShape();
            UpdateSitesButton();
            UpdateStartAnimation();
            FocusInput();
        }
        RefreshChrome();
    }

    /// <summary>Back/forward availability, loading bar and the "open outside" button follow the active tab.</summary>
    private void RefreshChrome()
    {
        foreach (var tab in _tabs) tab.LoadingDot.IsVisible = tab.Loading && tab.View is not null;
        var active = _active;
        _back.IsEnabled = Engine(() => active?.View?.CanGoBack == true);
        _forward.IsEnabled = Engine(() => active?.View?.CanGoForward == true);
        _progress.IsVisible = active?.Loading == true;
        _external.IsEnabled = active?.Address is { Scheme: "http" or "https" };
    }

    /// <summary>Reads the address and title the page shows now: single-page sites change both without a navigation event.</summary>
    private async Task ReadPageAsync(BrowserTab tab)
    {
        if (tab.View is not { } view) return;
        try
        {
            var raw = await view.InvokeScript("JSON.stringify([location.href, document.title])");
            if (string.IsNullOrWhiteSpace(raw)) return;
            // some engines hand the JSON string back as a JSON string of itself
            var json = raw.StartsWith('"') ? JsonSerializer.Deserialize<string>(raw) ?? raw : raw;
            var pair = JsonSerializer.Deserialize<string[]>(json);
            if (pair is not { Length: 2 }) return;
            if (!string.IsNullOrWhiteSpace(pair[1])) ToolTip.SetTip(tab.Header, pair[1]);
            if (Uri.TryCreate(pair[0], UriKind.Absolute, out var page) && page.Scheme is "http" or "https")
            {
                tab.Address = page;
                if (tab == _active && !_omnibox.IsFocused) _omnibox.Text = page.AbsoluteUri;
            }
            if (tab == _active) RefreshChrome();
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or NotSupportedException or ArgumentException
            or System.Runtime.InteropServices.COMException or ObjectDisposedException)
        {
            // a page that refuses scripts, or an engine not ready (0x8007139F, "invalid state") while a page starts or closes,
            // keeps the address it was opened with: this is only a refresh of the address bar, never worth an error message
        }
    }

    /// <summary>
    /// Keeps the memory in check: a page not looked at for a few minutes sleeps, and when the user works on another page of the
    /// application, every page sleeps after a while. They are loaded again, where they were, when shown.
    /// </summary>
    private void ManageMemory()
    {
        var now = DateTime.UtcNow;
        if (!IsEffectivelyVisible)
        {
            _hiddenSince ??= now;
            if (now - _hiddenSince >= PageSleepAfter) foreach (var tab in _tabs.ToList()) Sleep(tab);
            return;
        }
        _hiddenSince = null;
        if (_active is { View: null } sleeping)
        {
            Wake(sleeping);
            foreach (var other in _tabs.Where(t => t != sleeping && t.View is not null)) other.View!.IsVisible = false;
        }
        foreach (var tab in _tabs.Where(t => t != _active && t.View is not null && now - t.LastShown >= TabSleepAfter).ToList()) Sleep(tab);
    }

    private async Task FollowActivePageAsync()
    {
        if (_polling || _active is not { View: not null, Navigated: true, Loading: false } tab || !IsEffectivelyVisible) return;
        _polling = true;
        try { await ReadPageAsync(tab); }
        finally { _polling = false; }
    }

    private async Task OpenExternalAsync()
    {
        if (_active?.Address is not { Scheme: "http" or "https" } page) return;
        if (TopLevel.GetTopLevel(this)?.Launcher is { } launcher) await launcher.LaunchUriAsync(page);
    }

    private void View_KeyDown(object? sender, KeyEventArgs e)
    {
        var control = e.KeyModifiers == KeyModifiers.Control;
        if (control && e.Key == Key.L) { FocusInput(selectAll: true); e.Handled = true; }
        else if (control && e.Key == Key.T) { OpenBlankTab(); e.Handled = true; }
        else if (control && e.Key == Key.W && _active is { } tab) { CloseTab(tab); e.Handled = true; }
        else if ((e.Key == Key.F5 || (control && e.Key == Key.R)) && _active is not null) { Engine(() => _active.View?.Refresh()); e.Handled = true; }
    }
}
