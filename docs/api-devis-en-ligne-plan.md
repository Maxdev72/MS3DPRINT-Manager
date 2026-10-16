# API de devis d'impression 3D en ligne : plan

Date : 16 octobre 2026. Statut : plan à valider, rien n'est encore construit au-delà du moteur (`src/MS3DPRINT.Estimator`).

## 1. Objectif

Une API de devis instantané pour un atelier d'impression 3D, intégrable sur son propre site : le client dépose un fichier, choisit
matière, couleur, hauteur de couche, remplissage, parois et supports, et obtient un prix, un délai et un avis de faisabilité.
Le même service sert MS3DPRINT Manager et tout site qui veut le brancher (widget, WordPress/WooCommerce, autres).

Ce n'est pas une place de marché : pas de mise en concurrence de fabricants, pas de paiement géré par nous (Treatstock et Craftcloud
font cela). Le client final paie l'atelier, sur le site de l'atelier.

Références étudiées : [Treatstock](https://github.com/Treatstock/apiv2) (clé publique d'envoi et clé privée, iframe),
[Craftcloud](https://www.kiln3d.com/blog/craftcloud-live) (envoi, analyse différée, prix qui arrivent au fil de l'eau),
[WP3DPrinting](https://www.wp3dprinting.com/) (prix selon poids, volume, supports ou temps, paliers dégressifs, quota d'appels gratuits).

## 2. Ce qui existe déjà

| Brique | État |
|---|---|
| Estimation poids, durée, filament, supports | fait, 3 % sur le poids, durée ±25 % avec la moyenne, ±6,5 % avec la vitesse de la machine |
| Faisabilité : étanchéité, parois fines, surplombs, base, volume d'impression, unités | fait |
| Lecture STL, OBJ, 3MF | faite, mais dans le projet Avalonia (`MeshLoader`) : à **extraire** dans une bibliothèque sans interface |
| Matières : densité, débit maximal | fait pour 8 familles (PLA, PETG, ABS, ASA, TPU, PA, PC, PVA) |
| Prix, délai, couleurs, comptes, licences, API, widget | à faire |

## 3. Architecture

```
 Site du vendeur ── widget (web component) ──┐
 WooCommerce / autres ── plugin ─────────────┼──► API REST (ASP.NET Core)
 MS3DPRINT Manager ──────────────────────────┘        │
                                                      ├─► stockage des fichiers (chiffré, purge automatique)
                                                      ├─► file d'attente ──► workers d'analyse (isolés)
                                                      └─► base de données : comptes, clés, matières, règles, devis, quotas
 Console du vendeur (web) ── configuration, clés, statistiques, factures
```

Choix techniques proposés :
- **ASP.NET Core** : le moteur est en C#, il est réutilisé tel quel, sans réécriture ni risque de divergence.
- **Workers séparés de l'API** : une analyse lourde (200 000 triangles ≈ 0,5 s) ne bloque jamais les requêtes.
- **PostgreSQL** pour les données, **stockage objet** (compatible S3) pour les fichiers.
- Hébergement en **Union européenne** (RGPD, argument commercial pour un atelier français).

## 4. Parcours et points d'accès

Parcours en deux temps, comme Craftcloud : l'envoi rend la main tout de suite, l'analyse se fait en arrière-plan.

| Appel | Rôle | Clé |
|---|---|---|
| `POST /v1/models` | envoie un fichier ; répond `202` avec l'identifiant du modèle | publique |
| `GET /v1/models/{id}` | état (`en_analyse`, `analysé`, `refusé`) ; dimensions, volume, faisabilité (constats, gravité, points à surligner) | publique |
| `GET /v1/catalog` | matières, couleurs, hauteurs de couche, plages de remplissage et de parois autorisées par le vendeur | publique |
| `POST /v1/quotes` | demande un devis : modèle, matière, couleur, couche, remplissage, parois, supports, quantité, pays | publique |
| `GET /v1/quotes/{id}` | prix, détail, délai, validité, signature | publique |
| `POST /v1/quotes/{id}/accept` | fige le devis et le lie à une commande du site | privée |
| `POST /v1/webhooks` | le vendeur s'abonne aux événements (modèle analysé, devis accepté) | privée |
| `GET /v1/usage` | consommation du quota | privée |

Le widget n'utilise que la **clé publique**. Tout ce qui engage de l'argent (accepter, annuler, changer les règles) passe par la clé privée,
qui reste côté serveur du vendeur.

## 5. Configuration du vendeur

- **Matières** : nom, famille, densité, débit maximal (mm³/s), prix au kg, couleurs disponibles (avec stock ou surcoût), actives ou non.
- **Qualités proposées** : hauteurs de couche, plage de remplissage, nombre de parois, supports permis ou non.
- **Machine** : classe de vitesse (moyenne, rapide, lente), taux horaire, volume d'impression (pour refuser les pièces trop grandes).
- **Règles de prix** (voir 6), **règles de délai** (voir 7), **devise et TVA**.
- **Politique de faisabilité** : une erreur bloque-t-elle la commande, ou passe-t-elle en validation manuelle ?

## 6. Calcul du prix

Les bases de calcul de WP3DPrinting (poids, volume, supports, temps) sont reprises, avec les sorties du moteur :

```
prix = matière (poids × prix au g)
     + temps machine (durée × taux horaire)
     + supports (matière de supports × prix au g)
     + frais fixes par commande (préparation, finition) + options
     puis marge, puis dégressivité par quantité, puis minimum de commande, puis arrondi
```

Choix à faire : seuils de dégressivité sous la forme `quantité:coefficient` (`1:1;10:0.9;50:0.8`), minimum de commande, arrondi.

**Précision et responsabilité.** Le moteur est à ±3 % sur le poids mais à ±25 % sur la durée avec la moyenne seule. Trois réponses possibles,
à décider avec vous :
1. afficher un **prix indicatif** avec une fourchette, validé par l'atelier ;
2. régler la **classe de vitesse** de la machine du vendeur (ramène à ±6,5 %) et afficher un prix ferme ;
3. proposer les deux, au choix du vendeur.

Je recommande 2, avec un **plafond de marge** réglable par le vendeur.

## 7. Délai de livraison

```
délai = temps d'impression de la commande
      ÷ capacité d'impression par jour (heures de machine du vendeur)
      + charge déjà en file d'attente (jours)
      + délai de finition et d'expédition (selon matière et option)
      + transport (selon pays, règles du vendeur)
```
La charge en file se renseigne à la main au début, puis se synchronise avec MS3DPRINT Manager (projets en cours).

## 8. Sécurité

« Impossible à pirater » n'existe pas. Les mesures visent à empêcher qu'un client change son prix, qu'un tiers vole le service, ou qu'un fichier piégé fasse tomber un serveur :

- **Prix calculé uniquement côté serveur**, puis **signé** (HMAC) avec date de validité : un devis ne peut pas être modifié par le navigateur ; l'acceptation vérifie la signature.
- **Clé publique** limitée à un domaine, à l'envoi, à la lecture du catalogue et à la demande de devis, avec limitation de débit ; **clé privée** jamais dans le navigateur.
- **Workers isolés** : sans accès au réseau, avec limites de temps, de mémoire et de processeur, dans un conteneur séparé ; fichiers contrôlés avant lecture (taille, format réel, archives piégées).
- **Fichiers chiffrés au repos**, adresses temporaires signées, **purge automatique** (délai réglable, 30 jours par défaut), RGPD.
- Webhooks signés, journal d'audit, secrets hors du code, dépendances figées, mises à jour de sécurité suivies.
- **Audit externe** avant l'ouverture au public.

## 9. Licences et quotas

Modèle inspiré de WP3DPrinting (achat unique, 30 appels gratuits par mois, abonnement au-delà), à adapter :

| | Découverte | Atelier | Entreprise |
|---|---|---|---|
| Devis par mois | 30 | à fixer | à fixer |
| Sites | 1 | 1 | plusieurs |
| Faisabilité | oui | oui | oui |
| API privée et webhooks | non | oui | oui |
| Marque du widget | visible | masquable | masquable |

Les prix sont à définir avec vous. Une **clé = un domaine** ; le comptage se fait par devis créé ; au-delà du quota, le widget affiche « demander un devis » au lieu de bloquer le vendeur ; paiement des abonnements par un prestataire (Stripe, par exemple), plus tard.

## 10. Widget et intégrations

- **Widget** : composant web `<ms3d-quote key="…">` (ou iframe), aux couleurs du site, accessible, multilingue (français d'abord) : dépôt du fichier, aperçu 3D, constats de faisabilité qui surlignent les zones, choix des options, prix et délai en direct.
- **WooCommerce** (premier marché) : un produit « Impression 3D » ; le devis signé est enregistré dans la commande ; à la validation, le plugin **revérifie la signature côté serveur**.
- Ensuite : Shopify, PrestaShop, API seule pour les sites sur mesure.

## 11. Formats et orientation

- **MVP** : STL, 3MF, OBJ (déjà lus), plus ZIP.
- **Phase suivante** : STEP et IGES (conversion en maillage avec OpenCascade, sous licence LGPL, compatible avec une offre payante), images vers STL.
- **Orientation automatique** : faite dans le moteur (`AutoOrient`, objectifs moins de supports, plus rapide, plus stable) ; à exposer dans l'API après le MVP, car elle change le poids de supports donc le prix.

## 12. Phases proposées

| Phase | Contenu | Durée indicative |
|---|---|---|
| 0 | Décisions du paragraphe 14, extraction de la lecture des fichiers dans une bibliothèque sans interface | 1 semaine |
| 1 | API : envoi, analyse, faisabilité, devis (matières simples), prix et délai, signature des devis, tests de charge | 4 à 6 semaines |
| 2 | Console du vendeur : matières, couleurs, règles de prix et de délai, clés | 3 à 4 semaines |
| 3 | Widget et plugin WooCommerce | 4 semaines |
| 4 | Licences, quotas, facturation | 2 à 3 semaines |
| 5 | STEP/IGES, orientation automatique | 3 à 4 semaines |
| 6 | Audit de sécurité externe, correction, ouverture au public | 2 à 3 semaines |

Une première version utilisable par un atelier (phases 0 à 3) : environ **3 à 4 mois** à un rythme de développement régulier.

## 13. Risques

- **Précision de la durée** (±25 % avec la moyenne) : traitée par le réglage de vitesse et la marge du vendeur ; mesurée seulement sur des machines Bambu Lab.
- **Responsabilité du prix affiché** : prévoir les conditions d'utilisation (devis indicatif ou ferme selon le réglage du vendeur).
- **Fichiers piégés ou énormes** : limites de taille et isolation des workers (paragraphe 8).
- **Charge** : une analyse coûte de quelques millisecondes à une demi-seconde ; la file d'attente absorbe les pointes.
- **RGPD** : fichiers de clients, durée de conservation, hébergement dans l'Union européenne.

## 14. Décisions à prendre

1. **Modèle économique** : abonnement par site, quota de devis, ou les deux ? Prix des plans ?
2. **Prix indicatif ou ferme** (paragraphe 6) ?
3. **Hébergement** : un serveur dédié ou virtuel en France au départ (simple, peu cher), ou un cloud à montée en charge automatique ?
4. **Où vit le code** : dans ce dépôt, ou dans un dépôt séparé qui référence le moteur ? Je recommande un dépôt séparé pour l'API et un paquet partagé pour le moteur.
5. **Premier client réel** : votre atelier ? Cela fixe les matières, les couleurs et les délais du MVP.
