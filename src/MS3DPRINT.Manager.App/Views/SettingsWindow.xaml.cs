using System.Windows;
using System.Windows.Controls;
using System.Collections.ObjectModel;
using MS3DPRINT.Manager.App.ViewModels;
using MS3DPRINT.Manager.Core.Storage;

namespace MS3DPRINT.Manager.App.Views;

public partial class SettingsWindow : Window
{
    private readonly ThemeSettingsStore _settings;

    public SettingsWindow(ThemeSettingsStore settings, string storageRoot)
    {
        InitializeComponent();
        _settings = settings;
        StorageProviderCards = new ObservableCollection<StorageProviderCard>(StorageProviderCatalog.Create(storageRoot).Select(definition => new StorageProviderCard(definition)));
        DataContext = this;
        ThemeBox.SelectedIndex = (int)_settings.Load();
        MaxHeight = SystemParameters.WorkArea.Height * 0.9;
        MaxWidth = SystemParameters.WorkArea.Width * 0.9;
    }

    public ObservableCollection<StorageProviderCard> StorageProviderCards { get; }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var preference = Enum.Parse<ThemePreference>(((ComboBoxItem)ThemeBox.SelectedItem).Tag.ToString()!);
        _settings.Save(preference);
        ThemeManager.Apply(preference);
        DialogResult = true;
    }
}
