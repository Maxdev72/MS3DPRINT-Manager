namespace MS3DPRINT.Manager.App.Views;

public interface IUnsavedChangesPage
{
    bool HasUnsavedChanges { get; }
    bool TrySaveChanges();
}
