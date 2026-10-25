using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;

namespace MS3DPRINT.Manager.Desktop;

public partial class MainWindow
{
    private bool MotionReduced => DesktopApplication.ReduceMotion || Classes.Contains("reduce-motion");
    private bool _brandNavigationQueued;
    private double _brandNavigationY = double.NaN;

    private void InitializeBrandPresentation()
    {
        NavigationItems.LayoutUpdated += (_, _) => QueueBrandNavigation();
        SizeChanged += (_, _) => QueueBrandNavigation();
        QueueBrandNavigation();
    }

    private void QueueBrandNavigation()
    {
        if (_brandNavigationQueued) return;
        _brandNavigationQueued = true;
        Dispatcher.UIThread.Post(() => { _brandNavigationQueued = false; UpdateBrandNavigation(); }, DispatcherPriority.Render);
    }

    private void UpdateBrandNavigation()
    {
        var selected = NavigationItems.Children.OfType<Button>().FirstOrDefault(b => b.Classes.Contains("selected"));
        if (selected is null || selected.Bounds.Height <= 0) { NavigationIndicator.Opacity = 0; return; }
        if (selected.TranslatePoint(default, NavigationItems) is not { } position) return;
        NavigationIndicator.Opacity = 1;
        NavigationIndicator.Height = selected.Bounds.Height;
        if (!double.IsNaN(_brandNavigationY) && Math.Abs(position.Y - _brandNavigationY) < .01) return;
        NavigationIndicator.Transitions = MotionReduced || double.IsNaN(_brandNavigationY) ? null : BrandMotion.Movement(220);
        NavigationIndicator.RenderTransform = TransformOperations.Parse(FormattableString.Invariant($"translateY({position.Y}px)"));
        _brandNavigationY = position.Y;
    }

    private void SetPageBrand(string destination)
    {
        var icon = destination switch
        {
            "Projets" => "IconProjects", "Clients" => "IconClients", "Estimations" => "IconEstimates",
            "Imprimantes" => "IconPrinters", "Recettes" => "IconBook", "Filaments" => "IconFilaments", "Collections" => "IconCollections",
            "Paramètres" => "IconSettings", "Corbeille" => "IconTrash", "Modèles 3D" => "IconSearch", "Visualiseur 3D" => "IconCube", _ => "IconHome"
        };
        PageIcon.Data = (Geometry?)this.FindResource(icon);
        QueueBrandNavigation();
    }

    private async void DashboardStage_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string status }) await OpenFilteredAsync("Projets", status);
    }
}
