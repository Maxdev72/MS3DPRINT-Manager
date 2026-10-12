using System.Text;

namespace MS3DPRINT.Manager.Core.Naming;

/// <summary>
/// Gives back the accents that folder names lose (folders are upper case, ASCII, with underscores): "EPEE" shown as "Épée".
/// Only words that have no other reading are listed, so "cote" (côte, côté, cote) or "ou" (ou, où) are left alone.
/// This is for display; stored names are never changed, and a name typed with its accents is never touched.
/// </summary>
public static class FrenchAccents
{
    private static readonly Dictionary<string, string> Words = Build(
        // arts, objects and shops
        "epee épée", "epees épées", "replique réplique", "repliques répliques", "echec échec", "echecs échecs", "samurai samouraï",
        "creation création", "creations créations", "createur créateur", "creatif créatif", "creative créative", "gachette gâchette",
        "tronconneuse tronçonneuse", "crantee crantée", "crane crâne", "chassis châssis", "ecusson écusson",
        "embleme emblème", "trophee trophée", "medaille médaille", "decor décor", "decors décors", "decoration décoration",
        "decorations décorations", "deco déco", "visiere visière", "etagere étagère", "etui étui", "etoile étoile", "ecole école",
        "eclairage éclairage", "ecran écran", "ecrans écrans", "evier évier", "poele poêle", "theiere théière", "cafe café",
        "creme crème", "meche mèche", "chaine chaîne", "chaines chaînes", "tete tête", "tetes têtes", "pere père", "mere mère",
        "frere frère", "soeur sœur", "coeur cœur", "oeuf œuf", "boeuf bœuf", "noel noël", "fete fête", "ete été",
        // workshop vocabulary
        "piece pièce", "pieces pièces", "detachee détachée", "detachees détachées", "modele modèle", "modeles modèles",
        "modelisation modélisation", "reparation réparation", "reparations réparations", "boitier boîtier", "boitiers boîtiers",
        "boite boîte", "boites boîtes", "equerre équerre", "equerres équerres", "ecrou écrou", "ecrous écrous", "poignee poignée",
        "poignees poignées", "cle clé", "cles clés", "etiquette étiquette", "etiquettes étiquettes", "etrier étrier",
        "reglage réglage", "reglages réglages", "resine résine", "resines résines", "resistance résistance", "resistant résistant",
        "materiau matériau", "materiaux matériaux", "materiel matériel", "systeme système", "metal métal", "metallique métallique",
        "numero numéro", "numeros numéros", "entree entrée", "electrique électrique", "electronique électronique", "energie énergie",
        "equipe équipe", "equipement équipement", "equipements équipements", "personnalise personnalisé", "personnalisee personnalisée",
        "personnalises personnalisés", "assemble assemblé", "assemblee assemblée", "imprime imprimé", "imprimee imprimée",
        "societe société", "telecommande télécommande", "telephone téléphone", "camera caméra", "ecouteurs écouteurs",
        // ordinals and common words
        "premiere première", "derniere dernière", "deuxieme deuxième", "troisieme troisième", "quatrieme quatrième",
        "reference référence", "references références", "elements éléments", "element élément",
        "elevation élévation", "etape étape", "etapes étapes", "ecart écart", "echelle échelle", "epaisseur épaisseur",
        "precision précision", "securite sécurité", "deplacement déplacement",
        "demonstration démonstration", "presentoir présentoir", "presentation présentation", "revetement revêtement",
        "melange mélange", "degage dégagé", "decoupe découpe", "decoupes découpes",
        "decale décalé", "reserve réserve", "recuperation récupération", "recupere récupéré");

    private static Dictionary<string, string> Build(params string[] pairs)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in pairs)
        {
            var parts = pair.Split(' ');
            if (parts[0] != parts[1]) map[parts[0]] = parts[1];
        }
        return map;
    }

    /// <summary>The accented form of a lower-case unaccented word, or null when it is not in the list.</summary>
    public static string? TryRestore(string lowerWord) => Words.GetValueOrDefault(lowerWord);

    /// <summary>
    /// Restores the accents of every known word of a text, keeping each word's case (upper, capitalised or lower).
    /// A word that already carries an accent or that is unknown is left as it is.
    /// </summary>
    public static string Restore(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var result = new StringBuilder(text.Length);
        var index = 0;
        while (index < text.Length)
        {
            if (!char.IsLetter(text[index])) { result.Append(text[index++]); continue; }
            var start = index;
            while (index < text.Length && char.IsLetter(text[index])) index++;
            var word = text[start..index];
            var restored = TryRestore(word.ToLowerInvariant());
            result.Append(restored is null ? word : ApplyCase(restored, word));
        }
        return result.ToString();
    }

    private static string ApplyCase(string accented, string original)
    {
        if (original.Length > 1 && original.All(char.IsUpper)) return accented.ToUpperInvariant();
        if (char.IsUpper(original[0])) return char.ToUpperInvariant(accented[0]) + accented[1..];
        return accented;
    }
}
