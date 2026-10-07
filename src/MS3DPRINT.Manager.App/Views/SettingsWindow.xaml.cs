using System.Windows;
using System.Windows.Controls;
using System.Collections.ObjectModel;
using MS3DPRINT.Manager.App.Hardware;
using MS3DPRINT.Manager.App.ViewModels;
using MS3DPRINT.Manager.Core.Storage;

namespace MS3DPRINT.Manager.App.Views;

public partial class SettingsWindow : Window
{
    private readonly ThemeSettingsStore _settings;
    private readonly IHardwareInfoProvider _hardwareInfoProvider;

    public SettingsWindow(ThemeSettingsStore settings, string storageRoot)
        : this(settings, storageRoot, new WindowsHardwareInfoProvider())
    {
    }

    public SettingsWindow(ThemeSettingsStore settings, string storageRoot, IHardwareInfoProvider hardwareInfoProvider)
    {
        InitializeComponent();
        _settings = settings;
        _hardwareInfoProvider = hardwareInfoProvider ?? throw new ArgumentNullException(nameof(hardwareInfoProvider));
        StorageProviderCards = new ObservableCollection<StorageProviderCard>(StorageProviderCatalog.Create(storageRoot).Select(definition => new StorageProviderCard(definition)));
        DataContext = this;
        AccentBox.ItemsSource = new AccentOption[]
        {
            new(AccentPreference.Blue, "Bleu", "#1769AA"),
            new(AccentPreference.Green, "Vert", "#237B4B"),
            new(AccentPreference.Violet, "Violet", "#6F42B5"),
            new(AccentPreference.Red, "Rouge", "#B42332"),
            new(AccentPreference.Orange, "Orange", "#EF8A32"),
            new(AccentPreference.Yellow, "Jaune", "#F6CD35")
        };
        var appearance = _settings.LoadAppearance();
        ThemeBox.SelectedItem = ThemeBox.Items.Cast<ComboBoxItem>().Single(item => item.Tag.ToString() == appearance.Theme.ToString());
        AccentBox.SelectedValue = appearance.Accent;
        MaxHeight = SystemParameters.WorkArea.Height * 0.9;
        MaxWidth = SystemParameters.WorkArea.Width * 0.9;
        Loaded += SettingsWindow_Loaded;
    }

    public ObservableCollection<StorageProviderCard> StorageProviderCards { get; }

    private async void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= SettingsWindow_Loaded;
        HardwareProfile profile;
        try
        {
            profile = await Task.Run(_hardwareInfoProvider.GetHardwareProfile);
        }
        catch
        {
            profile = HardwareProfileFactory.Fallback(Environment.ProcessorCount);
        }

        ProcessorValue.Text = profile.ProcessorName;
        ThreadsValue.Text = profile.LogicalProcessorCount.ToString();
        MemoryValue.Text = profile.TotalMemory;
        GraphicsAdapters.ItemsSource = profile.GraphicsAdapters;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var preference = Enum.Parse<ThemePreference>(((ComboBoxItem)ThemeBox.SelectedItem).Tag.ToString()!);
        var accent = (AccentPreference)AccentBox.SelectedValue;
        var appearance = new ThemeAppearance(preference, accent);
        _settings.Save(appearance);
        ThemeManager.Apply(appearance);
        DialogResult = true;
    }
}
