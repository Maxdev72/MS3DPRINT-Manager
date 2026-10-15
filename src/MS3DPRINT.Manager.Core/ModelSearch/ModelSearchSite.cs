namespace MS3DPRINT.Manager.Core.ModelSearch;

/// <summary>A 3D-model website with the address template of its search page; <c>{q}</c> stands for the query.</summary>
/// <remarks><paramref name="Heavy"/> marks a page that costs a lot of memory (dozens of processes for its advertising): it is never loaded in the background.</remarks>
public sealed record ModelSearchSite(string Name, string SearchUrlTemplate, bool Enabled = true, string Category = "", string Note = "", bool Heavy = false)
{
    public const string QueryPlaceholder = "{q}";
    public const string SearchEngines = "Moteurs de recherche";
    public const string Communities = "Communautés";
    public const string Manufacturers = "Fabricants d’imprimantes";
    public const string SpecificNeeds = "Besoins précis";

    public static IReadOnlyList<ModelSearchSite> Defaults { get; } =
    [
        new("Thangs", "https://thangs.com/search/{q}?scope=all", Category: SearchEngines, Note: "Recherche par texte ou par forme ; héberge aussi des modèles."),
        new("Yeggi", "https://www.yeggi.com/q/{q}/", Category: SearchEngines, Note: "Cherche dans de nombreux dépôts à la fois. Page très chargée en publicités : environ 2 Go de mémoire, donc jamais chargée en arrière-plan (seulement à l’ouverture de son onglet).", Heavy: true),
        new("Printables", "https://www.printables.com/search/models?q={q}", Category: Communities, Note: "Prusa : la communauté la mieux modérée."),
        new("Thingiverse", "https://www.thingiverse.com/search?q={q}&type=things", Category: Communities, Note: "Le plus grand choix gratuit ; licences très variables."),
        new("Cults3D", "https://cults3d.com/fr/recherche?q={q}", Category: Communities, Note: "Place de marché française : designs gratuits et payants, licence commerciale souvent proposée."),
        new("MyMiniFactory", "https://www.myminifactory.com/search/?query={q}", Category: Communities, Note: "Figurines, décors et accessoires, imprimabilité testée."),
        new("MakerWorld", "https://makerworld.com/fr/search/models?keyword={q}", Category: Manufacturers, Note: "Bambu Lab : profils d’impression prêts pour vos imprimantes Bambu."),
        new("Creality Cloud", "https://www.crealitycloud.com/fr/search/models?q={q}", Category: Manufacturers, Note: "Creality : plus d’un million de modèles, qualité variable."),
        new("Nexprint", "https://www.nexprint.com/en/search/works?keyword={q}", Category: Manufacturers, Note: "Elegoo : plateforme ouverte, intégrée à Elegoo Slicer et OrcaSlicer."),
        new("Snapmaker Space", "https://space.snapmaker.com/en/search/models?keyword={q}", Category: Manufacturers, Note: "Snapmaker : modèles multicolores et multimatières (U1), encore en bêta."),
        new("Sketchfab", "https://sketchfab.com/search?q={q}&features=downloadable&type=models", Category: SpecificNeeds, Note: "Formes organiques, accessoires, décors ; seulement les modèles téléchargeables. Vérifiez la licence."),
        new("GrabCAD", "https://grabcad.com/library?query={q}", Category: SpecificNeeds, Note: "Pièces mécaniques et pièces de rechange d’ingénieurs (compte gratuit pour télécharger)."),
        new("TraceParts", "https://www.traceparts.com/en/search?Keywords={q}", Category: SpecificNeeds, Note: "Cotes exactes de pièces standard (visserie, roulements…) ; référence plus que modèle à imprimer."),
        new("Smithsonian 3D", "https://3d.si.edu/explore?edan_q={q}", Category: SpecificNeeds, Note: "Scans d’objets de musée, licence CC0 (usage commercial permis). Le site peut demander de cocher une vérification « humain » au premier accès."),
        new("CGTrader", "https://www.cgtrader.com/search?keywords={q}", Category: SpecificNeeds, Note: "Modèles professionnels souvent payants : accessoires, répliques. Tous ne sont pas imprimables.")
    ];
}
