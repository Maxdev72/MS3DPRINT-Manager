using System.Windows;
using MS3DPRINT.Manager.Core.Collections;

namespace MS3DPRINT.Manager.App.Views;

public partial class CollectionEditorWindow : Window
{
    private readonly CollectionProfileStore _store;
    private CollectionProfile _original;
    private readonly bool _isNew;

    public CollectionEditorWindow(string workspaceRoot, CollectionItemSummary item, string category)
    {
        ArgumentNullException.ThrowIfNull(item);
        InitializeComponent();
        _store = new CollectionProfileStore(workspaceRoot);
        var existing = _store.FindByPath(item.Path);
        if (existing is not null && existing.Category != category) throw new ArgumentException("La catégorie du dossier ne correspond pas à la fiche.", nameof(category));
        _isNew = existing is null;
        var now = DateTimeOffset.UtcNow;
        _original = existing ?? new CollectionProfile(Guid.NewGuid(), category,
            Path.GetRelativePath(Path.GetFullPath(workspaceRoot), Path.GetFullPath(item.Path)).Replace('\\', '/'),
            item.Name, null, null, null, null, null, null, null, now, now);
        NameBox.Text = _original.Name;
        DescriptionBox.Text = _original.Description ?? "";
        ContactBox.Text = _original.Contact ?? "";
        AddressBox.Text = _original.Address ?? "";
        PhoneBox.Text = _original.Phone ?? "";
        EmailBox.Text = _original.Email ?? "";
        WebsiteBox.Text = _original.Website ?? "";
        NotesBox.Text = _original.Notes ?? "";
        SupplierPanel.Visibility = category == "06_FOURNISSEURS" ? Visibility.Visible : Visibility.Collapsed;
        MaxHeight = SystemParameters.WorkArea.Height * 0.9;
        MaxWidth = SystemParameters.WorkArea.Width * 0.9;
    }

    public CollectionProfile? SavedProfile { get; private set; }

    public CollectionProfile SaveProfile()
    {
        var profile = _original with
        {
            Name = NameBox.Text.Trim(), Description = Optional(DescriptionBox.Text), Notes = Optional(NotesBox.Text),
            Contact = Optional(ContactBox.Text), Address = Optional(AddressBox.Text), Phone = Optional(PhoneBox.Text),
            Email = Optional(EmailBox.Text), Website = Optional(WebsiteBox.Text), UpdatedAt = DateTimeOffset.UtcNow
        };
        SavedProfile = _isNew && SavedProfile is null ? _store.Create(profile) : _store.Update(profile, _original.UpdatedAt);
        _original = SavedProfile;
        return SavedProfile;
    }

    private static string? Optional(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = "";
        try { SaveProfile(); DialogResult = true; }
        catch (Exception exception) { ErrorText.Text = UiErrorMessages.For(exception); }
    }
}
