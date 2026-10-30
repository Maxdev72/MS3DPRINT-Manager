using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace MS3DPRINT.Manager.Core.Tests.Estimates;

public sealed partial class EstimateUiTests
{
    [Fact]
    public Task Collection_creation_previews_normalized_folder_and_keeps_invalid_form_open() => Run(() =>
    {
        var dialog = new MS3DPRINT.Manager.Desktop.CreateCollectionWindow();
        dialog.Show();
        try
        {
            var name = dialog.GetVisualDescendants().OfType<TextBox>().Single(input => input.PlaceholderText == "ex. Pots décoratifs");
            name.Text = "Pots décoratifs";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Pots décoratifs", dialog.NameValue);
            Assert.Contains(dialog.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Dossier : POTS_DECORATIFS");
            CaptureDraftUi(dialog, "collection-creation-folder-preview");
            name.Text = "...";
            Dispatcher.UIThread.RunJobs();
            dialog.GetVisualDescendants().OfType<Button>().Single(button => button.Content as string == "Créer")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(dialog.IsVisible);
            Assert.Contains("form-invalid", name.Classes);
        }
        finally { dialog.Close(); }
        return Task.CompletedTask;
    });
}
