using System.Windows;
using System.Windows.Controls;
namespace MS3DPRINT.Manager.App.Controls;
public partial class EntityActions : UserControl
{
    public EntityActions() => InitializeComponent();
    public bool CanEdit { get => EditButton.Visibility == Visibility.Visible; set => EditButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed; }
    public event EventHandler? EditRequested;
    public event EventHandler? RenameRequested;
    public event EventHandler? MoveRequested;
    public event EventHandler? TrashRequested;
    private void Edit_Click(object sender, RoutedEventArgs e) => EditRequested?.Invoke(this, EventArgs.Empty);
    private void Rename_Click(object sender, RoutedEventArgs e) => RenameRequested?.Invoke(this, EventArgs.Empty);
    private void Move_Click(object sender, RoutedEventArgs e) => MoveRequested?.Invoke(this, EventArgs.Empty);
    private void Trash_Click(object sender, RoutedEventArgs e) => TrashRequested?.Invoke(this, EventArgs.Empty);
}
