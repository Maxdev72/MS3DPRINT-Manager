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
        ThemeBox.SelectedIndex = (int)_settings.Load();
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
        _settings.Save(preference);
        ThemeManager.Apply(preference);
        DialogResult = true;
    }
}
