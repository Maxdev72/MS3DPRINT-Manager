# Pointage manuel — Implementation Plan

> **For agentic workers:** use executing-plans task by task in the current authorized workspace.

**Goal:** Pointer et dépointer les achats et recettes, individuellement ou par sélection, avec filtres et totaux persistants.

**Architecture:** Réutiliser les journaux annuels append-only et leur verrou de mutation. Un événement de pointage contient les identifiants concernés et une date (ou null pour dépointer). Une correction invalide le pointage; une annulation l'exclut. Un petit contrôle partagé fournit la sélection, les filtres et les totaux des deux pages.

- [x] Ajouter les tests de persistance, lot, dépointage, correction, annulation et conflits de modification; implémenter les événements dans PurchaseJournal, RevenueJournal et les modèles.
- [x] Ajouter le contrôle de pointage partagé, l'intégrer aux pages achats/recettes; confirmer le dépointage, afficher les échecs sans succès prématuré. Tester les deux parcours et les captures à 960 et 1440 px.
- [x] Revoir les changements, exécuter les tests concernés et publier (1 209 tests complets réussis, puis 13 ciblés après la dernière simplification).
- [ ] Remplacer l'unique exécutable autorisé après fermeture de l'application et vérification SHA256.

Publication prête : `outputs/MS3DPRINT-Manager/MS3DPRINT.Manager.Windows.App.exe`, SHA256 `D031EF107BAF9DD20963144253DC1EA263DED801900C9BAF840A1F561B2B293A`. Livraison en attente de fermeture du logiciel.

Les données, montants et exports existants restent inchangés. Aucun import de relevé bancaire ni rapprochement automatique.
