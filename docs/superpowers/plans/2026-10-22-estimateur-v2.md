# Estimateur V2 — plan d’implémentation

Objectif : appliquer le cahier des charges utilisateur du 22 octobre 2026 dans l’architecture .NET 10 / Avalonia existante.

## Décisions

- `EstimateInputs.EngineVersion` absent vaut 1 : moteur historique inchangé, résultats sauvegardés jamais recalculés au chargement.
- Nouveaux devis en V2 ; conversion d’un ancien uniquement par choix explicite dans le formulaire.
- `EstimateV2Options` conserve les hypothèses de production, postes, charges et prix. Les taux et puissances des machines sont des instantanés du devis.
- Pièce : quantité passages. Ensemble sans taille de plateau : un passage. Ensemble avec taille : plafond(quantité / capacité), dernier plateau facturé comme un plateau complet.
- Affectations multi-machines en heures totales par machine : somme égale à l’occupation nominale, chaque taux appliqué seulement à ces heures.
- Postes classables par commande, par pièce conforme ou par tentative (une tentative = un passage de production).
- Coûts nominaux non arrondis ; aléas uniquement sur les postes répétables. V2 arrondit les prix facturables au centime, à l’écart de zéro ; conseillé/plancher au centime supérieur pour préserver les seuils. Rentabilité fondée sur le prix final arrondi.
- Charges pondérées selon parts de CA vente / service BIC / service BNC, contributions et commissions activables. Aucun taux réglementaire par défaut, aucun calcul d’impôt personnel ni de TVA collectée.
- Ancien forfait devient supplément commercial par défaut ; option coût réel par pièce conforme.

## Étapes et vérifications

- [x] Tests historiques et V2 : unité, 40 pièces, ensemble, plateaux, machines, conception, aléas, forfaits, prix, validation, arrondis, JSON ancien/nouveau.
- [x] Moteur V2 et amortissement individuel / moyenne pondérée du parc ; conservation du calcul historique.
- [x] Formulaire imprimante : années, heures annuelles, tarif manuel et maintenance, conservation des anciennes durées en heures.
- [x] Assistant d’estimation : version, production et affectations, nature des postes, charges, prix ajustable, mise à jour automatique et sauvegarde intégrale.
- [x] Tableau de rentabilité et avertissement sous le plancher, sans libellé « bénéfice net ».
- [x] Tests de solution, compilation Release, publication portable et remplacement de l’unique `MS3DPRINT Manager.exe` avec SHA256 identiques.
- [x] Documentation des hypothèses, des formules et des limites de classification de l’activité.

Validation financière : 50 + 100 / 0,95 = 155,263157… ; 139,05 / (1 − 0,212 − 0,20) = 236,479591… ; ancien scénario 132,10 → 139,05 → 187,72 → 387,72.

## Validation obtenue

- Solution : 699 tests Manager Core + 73 tests Estimator, tous réussis.
- Six parcours headless vérifient navigation historique, rafraîchissement, électricité, sauvegarde et rendu desktop/compact.
- Publication PortableWinX64 réussie : 0 avertissement, 0 erreur.
- Livraison : unique `MS3DPRINT Manager.exe` dans le dossier parent de sources.
- SHA256 publié/livré : `6205DAF5CC805F1E7452BDF631E01A2543016EF9A9214328EC28A58DC07BD1E4`.
- Revue de code : deux défauts d’état UI corrigés, seconde passe sans nouvelle anomalie concrète.
