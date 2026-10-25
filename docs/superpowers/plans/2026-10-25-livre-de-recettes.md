# Livre de recettes — plan d'implémentation (version 1)

**Goal :** registre des encaissements avec annulation traçable, totaux et exports PDF/CSV/déclaration.

**Architecture :** journal JSONL en ajout seul par année dans `.ms3dprint-manager/revenues/AAAA.jsonl` (inclus dans les sauvegardes ZIP existantes), services purs dans `Core/Revenues`, page « Recettes » dans `Desktop` en fichiers partiels `MainWindow.Revenues*.cs`.

**Tech :** .NET 10, Avalonia 12.1.4, xUnit ; aucun nouveau paquet (PDF texte écrit à la main).

**Spec :** `docs/superpowers/specs/2026-10-25-livre-de-recettes-design.md`

## Contraintes globales
- `TreatWarningsAsErrors` actif ; tout en français côté interface.
- Aucune suppression ni réécriture d'une ligne du journal ; confirmation avant toute action destructive (ici : l'annulation demande un motif).
- Écritures par ajout avec `ProfileMutationLock` ; une ligne illisible est ignorée et signalée.

## Tâches
1. **Modèle et journal** (`Revenues/RevenueModels.cs`, `RevenueJournal.cs`, tests `Revenues/RevenueJournalTests.cs`) : `RevenueEntry`, `RevenueCancellation`, `RevenueLine`, `RevenueJournal.Append/Cancel/Load(year)`.
2. **Totaux** (`RevenueTotals.cs`, tests) : par mois, nature, année, hors annulées ; doublon de facture.
3. **Exports** (`RevenueExporter.cs`, `SimplePdfWriter.cs`, tests) : CSV (séparateur `;`, UTF-8 BOM), PDF texte paginé, totaux de déclaration en texte.
4. **Réglages d'en-tête** (`Revenues/RevenueHeaderStore.cs`) : nom et SIRET de l'atelier.
5. **Page Recettes** (`MainWindow.Revenues.cs`, `RevenueEntryWindow.cs`, `RevenueCancelWindow.cs`, XAML, navigation) + tests d'interface headless.
6. **Finition** : `CHANGELOG.md`, build Release, suite de tests complète, commit.
