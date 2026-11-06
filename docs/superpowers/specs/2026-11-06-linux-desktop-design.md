# Ubuntu / Linux Mint — conception

Cible initiale : Ubuntu et Linux Mint x86-64, application native Avalonia 12 et .NET 10 autonome. Même interface et moteur métier, nouveau point d’entrée Linux sans référence Windows. Windows conserve son exécutable et ses chemins historiques.

Sous Linux, préférences dans LocalApplicationData/MS3DPRINT Manager/data, atelier proposé dans ~/MS3DPRINT (choix explicite au premier lancement local). Ouverture des fichiers par associations du bureau, dossier par xdg-open et choix d’application par gio. Session uniquement dans le coffre Secret Service via secret-tool, jamais en clair ; absence du coffre expliquée sans stockage de secours.

Archive tar.gz distincte sous outputs/linux-x64, lanceur natif MS3DPRINT Manager, notice dépendances. PDF texte, gestion et visualiseur partagés ; OCR Windows ne sera pas chargé. Les limites Linux des fonctions dépendantes de Windows seront documentées sans prétendre à une parité non testée. Aucune migration des données réelles.

Validation : tests portables des chemins et du coffre, compilation Linux, démarrage graphique isolé sous Ubuntu avec atelier fictif. Linux Mint doit encore être validé sur un poste réel.
