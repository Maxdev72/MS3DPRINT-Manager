# Instructions persistantes — MS3DPRINT Manager

## Nom et remplacement de l’exécutable

Préférence explicite de l’utilisateur, enregistrée le 7 octobre 2026 :

- Le nom de livraison est toujours `MS3DPRINT Manager.exe`.
- À chaque compilation destinée à l’utilisateur, remplacer la version précédente
  dans le dossier de livraison, au lieu d’ajouter une nouvelle variante.
- Le dossier de livraison ne doit contenir qu’un seul exécutable de l’application.
  Ne pas livrer de noms comportant `Material`, `v2`, `v3`, une date ou un numéro.
- La publication .NET produit en interne `MS3DPRINT.Manager.App.exe` dans
  `outputs/MS3DPRINT-Manager/`. Copier cette publication sous le nom de livraison
  `MS3DPRINT Manager.exe` dans le dossier parent de ce dépôt (`sources/`).
- Vérifier que les empreintes SHA256 du binaire publié et du binaire livré sont
  identiques avant d’annoncer la livraison.
- Si une sauvegarde de l’ancienne version est utile, la conserver uniquement
  dans `outputs/backups/`, jamais comme second exécutable dans le dossier livré.
- Conserver les dossiers `data`, les métadonnées `.ms3dprint-manager` et les
  documents de l’utilisateur pendant le remplacement.
- Ne pas lancer une instance supplémentaire à chaque livraison sans demande.
  Si un fichier est verrouillé, demander la fermeture normale du logiciel ;
  ne pas forcer l’arrêt au risque de perdre des modifications.
