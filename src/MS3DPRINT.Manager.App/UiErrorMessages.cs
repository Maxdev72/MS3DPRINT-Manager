using System.ComponentModel;
using System.Text.Json;
using MS3DPRINT.Manager.Core.Storage;
using MS3DPRINT.Manager.Core.Files;

namespace MS3DPRINT.Manager.App;

internal static class UiErrorMessages
{
    public static string For(Exception exception) => exception switch
    {
        ProjectFileTransferException => exception.Message,
        ExplorerOpenException => "Impossible d’ouvrir le dossier dans l’Explorateur Windows. Vérifiez que le chemin existe et que l’Explorateur est disponible.",
        FolderConflictException => "Un dossier ou un code existe déjà. " + exception.Message,
        UnauthorizedAccessException => "Accès refusé. Vérifiez vos droits sur le dossier de stockage.",
        DirectoryNotFoundException => "Un dossier requis est introuvable. " + exception.Message,
        IOException => "Impossible d’écrire ou de lire le dossier. Vérifiez le disque et la synchronisation Nextcloud. " + exception.Message,
        JsonException => "Les données de l’application sont illisibles. " + exception.Message,
        ArgumentOutOfRangeException outOfRange when outOfRange.ParamName == "existingNames"
            => "Aucun numéro de projet n’est disponible pour ce client et cette année (limite : 999).",
        Win32Exception => "Impossible de créer le dossier. Vérifiez vos droits et l’état du disque.",
        InvalidOperationException => exception.Message,
        ArgumentException => "Vérifiez les champs du formulaire. " + exception.Message,
        _ => "Une erreur inattendue est survenue. " + exception.Message
    };
}
