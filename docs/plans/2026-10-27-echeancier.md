# Échéancier des achats

Conception approuvée : fiche synthétique organisme, équipement/libellé, mensualité habituelle, prochaine et dernière échéances, dernière mensualité, nombre restant et total restant. Déplier les échéances pour consulter les montants bancaires et confirmer un paiement. Charges récurrentes à fréquence mensuelle/trimestrielle/annuelle, avec fin facultative. Aucun taux de crédit ni recalcul d’amortissement.

## Étapes
- [x] Modèles et projection : échéances finies, récurrences sans fin avec horizon visible explicite, paiements confirmés séparés. Tester l’exemple FLOA : 22 × 35,08 + 34,99 = 806,75 euros.
- [x] Saisie/import : CSV ou collage Excel tabulé, dates et montants obligatoires ; capital, intérêts, assurance et solde facultatifs. Refuser les lignes invalides au lieu de les omettre. Prévisualiser avant sauvegarde.
- [x] Stockage JSON individuel dans .ms3dprint-manager/expense-plans, lecture tolérante avec erreurs visibles, écritures atomiques et contrôle de version. Préserver les échéances déjà payées lors d’une correction ; archivage avec confirmation.
- [x] Interface Achats : Registre / Échéancier, création financement ou charge, synthèse et échéances, confirmation à la date réelle, annulation de confirmation explicite. Aucune écriture automatique dans PurchaseJournal.
- [x] Vérifications métier et parcours de bout en bout, revue et captures 960/1440, publication du seul MS3DPRINT Manager.exe et vérification SHA256. Inclure le correctif des fenêtres de téléchargement déjà prêt.

La lecture automatique de tableaux PDF ou de captures n’est pas annoncée : aucun document bancaire exploitable n’a été fourni. La capture constitue une référence de présentation. Les données bancaires peuvent être saisies/collées ou importées en CSV ; elles restent la référence.


Vérification : 68 tests ciblés réussis (achats, échéancier, téléchargement, structure), puis 3 parcours UI réussis après finition de l’aperçu et ajout de l’archivage/restauration. Captures contrôlées à 960 et 1440 px. Publication autonome réussie. Remplacement de la livraison en attente : l’exécutable est encore utilisé par le logiciel ouvert, aucune fermeture forcée.


Livraison effectuée après fermeture normale : unique MS3DPRINT Manager.exe remplacé, SHA256 identique à la publication : 0FF748E3318999CC4C3CFC49BC148BD1B7C3302D5883836F9C8908B55E53C413.
