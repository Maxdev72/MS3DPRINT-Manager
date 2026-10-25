# Livre de recettes — conception

Date : 25 octobre 2026. Statut : validée en chat par l’utilisateur, en attente de relecture de ce document.

## Objectif
Tenir dans MS3DPRINT Manager le registre chronologique des encaissements d’un micro-entrepreneur, avec pièce justificative (facture émise sous Indy), consultation, exports et totaux de déclaration. Le périmètre devis et facturation ne change pas : le logiciel n’émet aucune facture.

## Décisions
- Registre légal : colonnes réglementaires (date d’encaissement, client, libellé, montant, nature, mode de règlement, référence de pièce).
- Saisie libre dans un premier temps ; saisie depuis la fiche projet plus tard.
- Pièce justificative : numéro Indy saisi à la main en version 1 ; analyse locale du PDF Indy en version 2 (sans IA ni envoi externe).
- Correction par annulation traçable : aucune ligne n’est supprimée ni modifiée.
- Exports : PDF imprimable, CSV/Excel, totaux de déclaration (par période et par nature).

## Stockage
Journal en ajout seul, un fichier par année civile : `Recettes/AAAA.jsonl` dans l’espace de travail. Chaque ligne JSON est une entrée de type `recette` ou `annulation`. Les fichiers sont synchronisés avec l’espace de travail et inclus dans les sauvegardes ZIP existantes. Une ligne illisible est ignorée et signalée, sans empêcher la lecture des autres. L’écriture ajoute une ligne puis vide les tampons ; jamais de réécriture du fichier.

## Modèle (Core, `Revenues/`)
- `RevenueEntry` : `Id`, `ReceivedOn` (date d’encaissement, non future), `Label`, `Amount` (> 0, 2 décimales), `Nature` (`Goods`, `Services`, `Mixed` avec `GoodsAmount` et `ServicesAmount`), `PaymentMethod`, `ClientId?`, `ProjectRef?`, `InvoiceNumber`, `InvoicePdfPath?`, `RecordedAt`.
- `RevenueCancellation` : `Id`, `EntryId`, `Reason` (obligatoire), `CancelledAt`.
- `RevenueJournal` : lecture/ajout par année, ligne annulée marquée mais conservée.
- `RevenueTotals` : totaux par mois, année et nature, hors recettes annulées.
- Règles : montant positif ; doublon de numéro de facture signalé sans bloquer ; une recette annulée ne se modifie plus ni ne s’annule deux fois.

## Interface (Desktop)
- Entrée « Recettes » dans la navigation : liste dense et triable, sélecteur d’année, totaux (année, mois, nature), lignes annulées barrées avec leur motif.
- Menu « ⋯ » par ligne : Annuler, Ouvrir la facture, Ouvrir le projet.
- Bouton « Enregistrer un encaissement » : formulaire de saisie libre.
- Panneau Exports : PDF, CSV, totaux de déclaration, pour l’année choisie.
- En-tête du PDF : nom et SIRET de l’atelier, ajoutés aux réglages de l’atelier.

## Phasage
1. Version 1 : modèle, journal, saisie libre, annulation, liste, totaux, exports PDF et CSV, réglages d’en-tête, tests.
2. Version 2 : glisser-déposer du PDF Indy, lecture locale du texte, ligne pré-remplie à confirmer, copie du PDF dans le dossier du client ou du projet. Nécessite un exemple de facture Indy.
3. Plus tard : saisie depuis la fiche projet avec montant suggéré du devis.

## Tests
- Journal : ajout, annulation, totaux, changement d’année, ligne corrompue, fichier absent.
- Exports : contenu et totaux du CSV ; structure du PDF.
- Interface Avalonia headless : formulaire, annulation avec motif, navigation, comme le reste de l’application.

## Hors périmètre
Émission de factures, numérotation automatique, import bancaire, envoi à l’administration.
