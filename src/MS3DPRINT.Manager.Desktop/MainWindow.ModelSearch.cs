using MS3DPRINT.Manager.Desktop.ModelSearch;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>3D-model search page: a multi-tab browser created on first visit, so the web engine only starts when used.</summary>
public partial class MainWindow
{
    private void EnsureModelSearchPage()
    {
        if (ModelSearchWorkspacePanel.Child is not null) return;
        ModelSearchWorkspacePanel.Child = new ModelBrowserView();
    }
}
