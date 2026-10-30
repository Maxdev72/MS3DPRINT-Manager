using Avalonia.Controls;
using MS3DPRINT.Manager.Core.Revenues;

namespace MS3DPRINT.Manager.Desktop;

internal static class InvoicePreviewUi
{
    public static void Open(InvoiceCandidate invoice, Button button, TextBlock error)
    {
        if (!File.Exists(invoice.FilePath))
        {
            FormKit.ShowError(error, button, "La facture est introuvable. Vérifiez son emplacement ou la synchronisation Nextcloud.");
            return;
        }
        try
        {
            if (!DesktopPreviewLauncher.TryOpenDocument(invoice.FilePath))
            {
                FormKit.ShowError(error, button, "Impossible d’afficher la facture : le lecteur PDF n’est pas disponible.");
                return;
            }
            FormKit.ClearError(error);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException
            or NotSupportedException or System.ComponentModel.Win32Exception)
        { FormKit.ShowError(error, button, "Impossible d’afficher la facture : " + e.Message); }
    }
}
