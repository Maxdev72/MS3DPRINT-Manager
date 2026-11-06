# Validation de la reprise guidée — 6 novembre 2026

- 1 299 tests Core/Windows réussis : outputs/guided-workspace-reconciliation/core-final.log.
- 124 tests serveur réussis sous Windows et Ubuntu (WSL), dont cinq parcours natif/serveur
  de transfert, perte de réponse, reprise après fermeture et refus terminal avant activation.
- 8 tests Linux réussis, y compris distinction des ateliers PC/pc et permissions privées.
- Captures natives du bilan en largeur 1 400 et 960 : outputs/guided-workspace-migration/.
- Publication autonome Windows et Linux réussie ; publication serveur Linux préparée dans
  outputs/guided-workspace-reconciliation/server-candidate/.

Les scénarios vérifient les empreintes, les annexes choisies, les versions métier et techniques,
la conservation des sources et fichiers serveur seuls, les limites et protections d’accès,
les commandes Pending, les coupures réseau et les frontières de redémarrage de la bascule.
Les erreurs de persistance bloquent l’accès métier jusqu’à récupération vérifiée. Une reprise
refusée avant activation est terminale ; une nouvelle préparation garde les anciens journaux.

Les revues ont conduit à corriger la persistance du reçu, la récupération après restauration,
la publication mobile pendant la reprise, les callbacks de contrôleurs fermés et le parcours
après refus. Ces régressions ont été reproduites avant correction et vérifiées après.

Les premières exécutions complètes Core ont signalé trois attentes d’anciens tests de réglages
à adapter à la nouvelle carte. Après correction, la suite complète ci-dessus passe sans échec.
L’avertissement Avalonia AVA3001 des tests utilisant ses API internes est préexistant.

Aucun atelier réel n’a été transféré ou activé. Aucun service Coolify n’a été modifié. Le serveur
public conserve sa version précédente tant qu’une mise à jour explicite n’a pas été réalisée.
Les sites commerciaux restent hors périmètre. L’APK Android n’est pas remplacé.

La publication compilée ne vaut pas installation : le remplacement de l’exécutable utilisateur
attend la fermeture normale du logiciel. Le reçu local de livraison, quand le remplacement
est effectué, est conservé sous outputs/guided-workspace-reconciliation/delivery.json.
