# Validation Linux — 6 novembre 2026

- Publication autonome linux-x64 réussie, runtime inclus .NET 10.0.14.
- Compilation native du code dans Ubuntu 26.04 WSL (SDK 10.0.403).
- 7 tests Linux réussis : chemins, réglages hors binaire, coffre absent, coffre verrouillé, session via entrée standard, rendu fenêtre atelier fictif.
- 89 tests Windows ciblés réussis : WorkspaceLocation, préférences, registre clients, coffre Windows.
- Lancement du binaire autonome sous Xvfb : fenêtre principale MS3DPRINT Manager 1400x760 détectée après le splash, aucune erreur dans la sortie du processus.
- Capture contrôlée sous Linux : outputs/linux-compatibility/linux-workshop.png.
- Archive : outputs/MS3DPRINT-Manager-linux-x64.tar.gz ; SHA256 dans le fichier .sha256 voisin.
- Empreintes des binaires publiés et empaquetés : outputs/linux-compatibility/delivery.json.
- Exécutable Windows livré inchangé : 29CC14B2CA89B6DA430B958BCB9060E504F34B603B27CDD1A51121986998761B.

Limites : pas de test sur Linux Mint ni sur un poste Linux réel. Pas de test de connexion au serveur réel, du trousseau de bureau réel ou du navigateur intégré. Le contrat du coffre est testé avec un exécutable de test isolé. OCR, détection de slicer et suppression sûre de source externe restent limités comme indiqué dans docs/linux.md. Synchronisation PC/serveur non achevée par cette livraison. Données réelles inchangées.

Les ébauches non suivies de réconciliation présentes avant ce travail restent séparées de la modification Linux.
