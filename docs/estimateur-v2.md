# Utiliser l’estimateur V2

Les nouveaux devis utilisent directement la V2, sans sélecteur de moteur. Un devis existant sans numéro de moteur est historique (version 1) : son chargement, sa consultation et son enregistrement sans modification conservent le résultat enregistré. Pour le convertir, utiliser explicitement « Convertir ce devis en V2 » dans les informations du devis. Les données clients, les tarifs et le parc restent dans leurs fichiers actuels. Les propriétés JSON ajoutées sont facultatives : aucune réécriture des anciennes fiches n’est nécessaire.

## Paramètres de l’atelier et formulaire simplifié

Configurer une fois les cotisations, contributions, commissions, le prix du kWh, la marge, les aléas, le forfait et les natures de coûts dans **Paramètres → Atelier**. Le barème France 2026 peut préremplir les cotisations ; les taux et l’activité CFP restent modifiables. Le catalogue annuel est distinct du moteur et aucun profil personnel n’est intégré à l’exécutable partagé.

Le devis demande les données de la commande et affiche les règles qu’il a retenues. Ses réglages sauvegardés restent inchangés après modification de l’atelier. Le bouton d’information ouvre [l’explication de chaque champ](guide-champs-estimateur.md).

## Production

- **Une pièce** : poids et durée par pièce, multipliés par la quantité commandée.
- **Ensemble, pièces par plateau = 0** : poids et durée de toute la commande ; un seul passage.
- **Ensemble, capacité renseignée** : poids et durée d’un plateau complet ; passages = plafond(quantité / capacité). Exemple : 40 pièces, 10 par plateau, 2 h et 100 g par plateau donnent 4 passages, 8 h et 400 g. Un dernier plateau incomplet garde les données du plateau complet : adapter la saisie si sa durée réelle diffère.
- Le poids inclut les supports et purges par défaut. Décocher pour ajouter le poids supplémentaire par pièce ou plateau.
- Sans imprimante précise, le taux est la moyenne des amortissements des machines actives, pondérée par leurs heures productives annuelles. Une ancienne fiche sans heures annuelles a un poids de 1 ; si toutes sont anciennes, la moyenne est arithmétique. Renseigner les capacités annuelles du parc pour une pondération représentative.
- L’amortissement est distinct de la maintenance. Le taux manuel remplace l’amortissement calculé ; il ne s’ajoute pas à lui. Les anciennes fiches conservent leur durée de vie en heures tant que le couple années / heures annuelles reste à zéro.
- Les affectations multi-machines remplacent le taux de référence : renseigner les heures **totales** de chaque machine avant échecs. Leur somme doit égaler l’occupation nominale de la commande. Le moteur calcule la somme des heures × (amortissement + maintenance), et les kWh propres à chaque machine.
- Puissance non renseignée : l’électricité se saisit manuellement par pièce / plateau. Pour une répartition multi-machines, ce montant couvre seulement les machines sans puissance ; les autres sont calculées automatiquement. La puissance moyenne du parc n’est utilisée que si toutes les machines actives ont une puissance connue.

Les taux, puissances, prix du filament et affectations sont sauvegardés avec le devis. Modifier ensuite les fiches du parc ou du filament ne modifie pas rétroactivement ses hypothèses. Un choix explicite d’une autre machine ou d’un autre filament reprend ses paramètres actuels.

## Coûts et échecs

### Fabrication confiée à un fournisseur

Cocher **Fabrication confiée à un fournisseur** dans la saisie. Indiquer la quantité commandée, le coût d’achat pour la commande complète, le transport facturé par le fournisseur et son nom facultatif. Si le transport est déjà compris dans l’achat, laisser le transport à zéro. Les champs matière, machine et durée internes sont masqués et exclus du calcul.

Ajouter votre préparation, vos finitions, votre emballage et la livraison au client dans les frais complémentaires. Les charges et la marge de l’atelier s’appliquent au prix de vente final, comme pour une fabrication interne : le prix d’achat n’est pas le prix de revente. Le prix plancher couvre les coûts et charges renseignés ; le prix conseillé ajoute la marge cible. Un ajustement ou une remise sous le plancher est signalé.

Ce mode représente une fabrication achetée conforme pour l’ensemble de la commande. La provision d’échecs de vos imprimantes ne lui est pas appliquée ; un coût de reprise externe restant à votre charge peut être saisi dans les autres frais. Une même fabrication ne peut pas cumuler le devis fournisseur et les coûts de vos imprimantes. Le fournisseur, ses montants et les réglages du devis sont sauvegardés. Les anciens devis restent en fabrication interne sans conversion.

### Fabrication interne

Les coûts physiques d’impression sont répétables : matière, occupation machine et énergie. Tous les frais complémentaires se saisissent en **montants totaux pour la commande**, avant provision pour échecs. Par exemple, 2 € de finitions pour chacune des 40 pièces donnent un montant à saisir de 80 €. Le logiciel ne multiplie pas ces montants une seconde fois par la quantité ou les plateaux.

Dans **Paramètres → Atelier → Frais à prévoir en cas d’échec**, cocher uniquement les frais à engager de nouveau après une impression ratée. Par défaut, la main-d’œuvre d’impression et les consommables sont concernés ; la conception, les finitions, la livraison et les autres frais ne sont pas répétés. Les postes supplémentaires attendent aussi un montant total, avec une case « À refaire si l’impression échoue ». Les frais généraux restent une allocation fixe à la commande.

Les anciens devis V2 gardent leur résultat enregistré et leurs paramètres. À l’ouverture, leurs montants unitaires sont présentés sous forme de totaux équivalents. Après modification, le devis enregistre explicitement `ComplementaryCostsAreTotals = true`. L’absence de cette propriété conserve l’ancien fonctionnement du moteur ; aucun ancien fichier n’est réécrit silencieusement.

`Coût corrigé = coûts non répétés + coûts répétables / (1 − taux d’échec)`.

Ainsi, 100 € répétables et 50 € fixes avec 5 % d’échec donnent 155,263157… €, affichés 155,26 €. Le modèle suppose des tentatives indépendantes et un taux constant ; un échec répète le passage complet et ses coûts classés répétables. L’occupation attendue intègre les reprises ; les totaux matière et heures nominaux sont identifiés avant échecs.

Le forfait **coût réel** est un coût par pièce conforme, intégré avant le calcul du prix. Le forfait **supplément commercial** s’ajoute au prix conseillé ; les charges finales portent aussi sur ce supplément. La remise s’applique au prix conseillé, supplément compris, ou au prix commercial manuel.

## Charges et prix

Aucun taux réglementaire n’est codé en dur. Saisir les taux correspondant aux activités déclarées et aux dispositifs applicables. Les parts de CA vente et services BIC sont explicites ; le solde est classé services BNC. La moyenne pondérée des taux sociaux, augmentée des contributions et commissions activées, s’applique au prix commercial final. Cette répartition est une hypothèse commerciale du devis, pas une qualification automatique de chaque ligne facturée.

Les sources officielles distinguent l’activité mixte et les catégories de recettes ; l’impression 3D seule ne permet pas de déterminer la qualification de toutes les prestations (fabrication/vente d’objets, travail artisanal, conception). Confronter le paramétrage aux activités déclarées et, en cas de doute, demander une qualification à l’Urssaf :

- [Service Public : cumul de plusieurs activités indépendantes](https://entreprendre.service-public.fr/vosdroits/F33339)
- [INPI : types d’activités en micro-entreprise](https://www.inpi.fr/ressources/formalites-dentreprises/types-dactivites-possibles-en-tant-que-micro-entrepreneur)
- [Impôts : déclaration des revenus de micro-entrepreneur](https://www.impots.gouv.fr/particulier/questions/comment-declarer-les-revenus-provenant-de-mon-activite-dauto-entrepreneur)

Pour la franchise en base de TVA, saisir les coûts d’achat réellement supportés, TVA non récupérée comprise. Le moteur ne calcule ni TVA collectée ni impôt personnel, et n’annonce pas de bénéfice net fiscal. Le résultat reflète uniquement les coûts et charges sélectionnés.

- **Plancher** : coût corrigé / (1 − charges).
- **Conseillé, marge cible** : coût corrigé / (1 − charges − marge cible), puis supplément commercial éventuel.
- **Conseillé, majoration** : coût corrigé × (1 + majoration), puis supplément commercial éventuel. Cette méthode ne garantit pas la marge cible après charges ; le résultat final reste calculé.
- **Commercial final** : (prix manuel si saisi, sinon conseillé) × (1 − remise).
- **Résultat estimé** : prix final − coût corrigé − charges proportionnelles sur le prix final.
- **Marge réelle** : résultat / prix final. Si le prix final est nul, la division est évitée et le taux affiché vaut 0 ; le montant de la perte et l’avertissement restent affichés.

Exemple de prix cible : 139,05 € de coût corrigé, 21,2 % de charges **illustratives** et 20 % de marge donnent 236,48 € sans supplément. Les taux retenus et la marge cible doivent totaliser strictement moins de 100 %.

## Arrondis et compatibilité

Les calculs V2 conservent la précision `decimal` jusqu’au prix. Les prix plancher et conseillé sont portés au centime supérieur pour ne pas dégrader le seuil et la cible par un arrondi. Le prix commercial après remise est arrondi au centime à l’écart de zéro (`MidpointRounding.AwayFromZero`). La rentabilité utilise ce prix réellement facturable ; les coûts et charges restent précis dans le JSON et sont affichés au centime avec la même règle. Les taux et montants du formulaire sont rechargés avec leur précision sauvegardée.

Le moteur historique garde ses arrondis intermédiaires, son aléa global, sa majoration sur coût et son forfait après majoration. Son scénario de non-régression donne exactement : 132,10 € brut → 139,05 € après aléas → 187,72 € après majoration de 35 % → 387,72 € avec 40 forfaits de 5 €. La quantité de cet ancien moteur reste descriptive, ses coûts d’entrée ayant déjà été totalisés par le formulaire.

Les tests `EstimateV2Tests`, `PrinterCostCalculatorTests`, `EstimateStoreTests` et `EstimateUiTests` couvrent calcul, JSON ancien/nouveau, navigation et sauvegarde, précision des paramètres, électricité, rafraîchissement et rendu de l’assistant en deux tailles. Les captures et journaux de compilation sont conservés dans `outputs/` et ne sont pas livrés avec l’exécutable.
