# Sauvegardes de MS3DPRINT Manager

Dans **Paramètres → Espace et stockage → Sauvegarde et restauration** :

1. Enregistrer les formulaires en cours. Le brouillon de l’estimateur est aussi vidé vers sa sauvegarde automatique avant la copie.
2. Choisir si les documents doivent être inclus, puis **Créer une sauvegarde…**.
3. Choisir un fichier ZIP **hors de l’espace de travail**, de préférence sur un autre support. Une archive existante n’est jamais écrasée : choisir un autre nom.

La dernière sauvegarde réussie affiche sa date, le nombre de fichiers, le volume des données non compressées et son emplacement. Ce statut indique une création passée ; il ne surveille pas les déplacements ou suppressions ultérieurs de l’archive.

## Contenu

Toujours inclus : métadonnées clients/projets/collections, devis et brouillons enregistrés, filaments/prix, imprimantes, réglages atelier, modèles d’arborescence, historique métier et registre des codes clients (`data/client-codes.json` et `client-codes.previous*.json`). L’arborescence des dossiers gérés est conservée, même sans documents.

Avec documents : fichiers sous les onze dossiers métier de `FolderTemplates.Main` (clients, modèles, produits, communication, machines/matériaux, fournisseurs, administratif, comptabilité, commercial, ressources, archives), ainsi que la corbeille récupérable de l’application.

Sans documents : fichiers métier et corbeille exclus. Les dossiers de documents restaurés sont vides. Ce choix sert à récupérer les fiches et les devis ; il ne remplace pas une sauvegarde complète des fichiers.

Exclus : autres dossiers de la racine, notamment `98_DEVELOPPEMENT`, exécutable, préférences du poste (fenêtre, performances, thème et emplacement actif), fichiers hors de l’espace géré et liens/jonctions. Les réglages atelier sont bien inclus. Un journal de classement en attente bloque la sauvegarde : terminer sa récupération dans le logiciel avant de réessayer.

## Restaurer

**Restaurer une copie…** demande l’archive, puis le dossier parent. Le logiciel propose un **nouveau sous-dossier** et présente date, contenu, volume, intégrité vérifiée et chemin final. **Annuler** n’extrait rien. La copie n’est écrite qu’après **Restaurer la copie** ; aucun dossier existant ne peut être remplacé. L’archive est revérifiée, y compris son empreinte depuis l’aperçu.

L’espace de travail actif reste inchangé. Après contrôle de la copie, utiliser **Changer d’espace de travail…** pour ouvrir cette restauration si nécessaire. Un espace imposé par `MS3DPRINT_WORKSPACE` ne peut pas être changé depuis les paramètres.

## Garanties et limites

- ZIP versionné, manifeste et SHA256 de chaque fichier ; vérification avant restauration. Les chemins sortants, flux Windows, noms réservés, doublons, incohérences et fichiers manquants/altérés sont refusés.
- Écriture temporaire puis publication sans écrasement. Extraction dans un dossier temporaire voisin puis renommage vers le nouveau dossier ; nettoyage temporaire en cas d’erreur gérée.
- Traitement en arrière-plan, état d’opération visible et protection contre les doubles lancements. Jusqu’à 100 000 fichiers, 100 000 dossiers et 512 Gio non compressés par archive.
- L’archive n’est **pas chiffrée** et peut contenir des informations clients. SHA256 détecte les altérations ; il ne prouve pas l’identité de l’auteur d’une archive.
- Éviter les modifications et imports simultanés, notamment par un autre ordinateur synchronisé. Le service compare l’inventaire, les tailles et dates de modification ; il ne fournit pas un instantané système transactionnel. Un fichier verrouillé, indisponible ou changé pendant l’opération peut empêcher la sauvegarde.
- Les archives ZIP ordinaires sans manifeste MS3DPRINT ne sont pas prises en charge. Il n’y a ni planification automatique ni remplacement destructif d’un espace existant.

Les essais utilisent uniquement des espaces temporaires synthétiques. Aucun test de restauration ne porte sur les données réelles de l’utilisateur.
