# Roadmap

Étapes de reconstruction, sans dates artificielles. L'ordre reflète les dépendances techniques, pas un engagement de calendrier. Voir [CURRENT_STATE.md](CURRENT_STATE.md) pour ce qui est fait à date.

## Ordre de priorité actuel (révisé le 2026-07-16)

1. Robustesse/non-régression du drop — **validée** (retest loot A à G, cinq scénarios de robustesse du drop — voir [../architecture/ITEM_ARCHITECTURE.md](../architecture/ITEM_ARCHITECTURE.md)).
2. Équipement corporel minimal (étape 8).
3. Consommation d'items reliée aux vitals (étape 8).
4. Stacks, split/merge et quantités (étape 8).
5. Poids maximal (étape 8).
6. Progression vers l'UI finale (étape 8).
7. Conteneurs du monde et regroupement des drops (étape 9).
8. Scènes, zones et extraction (étape 11).
9. Persistance et reconnexion (étape 12).

**Le transfert direct d'item entre deux joueurs n'est pas prévu** — décision de gameplay explicite du 2026-07-16 : l'échange entre joueurs passe par le système déjà validé (joueur A dépose l'item, joueur B le ramasse), pas par un transfert direct inventaire-à-inventaire. Cette décision remplace l'ordre de priorité précédent (2026-07-15), qui plaçait un tel transfert entre la robustesse du drop et les conteneurs du monde — voir [OPEN_QUESTIONS.md](OPEN_QUESTIONS.md) et étape 8 ci-dessous.

## 1. Fondation du projet

- **Objectif** : projet s&box vide, compilable, dépôt configuré.
- **Dépendances** : aucune.
- **Critères de validation** : `kodoku.sbproj` valide, le projet compile, dépôt GitHub configuré.
- **Hors périmètre** : tout code de gameplay.
- **Statut** : fait — voir [CURRENT_STATE.md](CURRENT_STATE.md).

## 2. Documentation et règles

- **Objectif** : fondation documentaire (`CLAUDE.md`, règles, architecture visée, ADR) avant d'écrire du code de gameplay.
- **Dépendances** : étape 1.
- **Critères de validation** : documents créés, cohérents, sans information inventée.
- **Hors périmètre** : configuration de sécurité technique (permissions, hooks, protections de branche — voir [CURRENT_STATE.md](CURRENT_STATE.md)).
- **Statut** : fait (cette mission).

## 3. Laboratoire réseau minimal

- **Objectif** : une session réseau minimale fonctionnelle (host + un client rejoint), sans gameplay, pour valider le modèle réseau s&box dans ce projet.
- **Dépendances** : étape 1.
- **Critères de validation** : deux instances se connectent, se voient dans les logs/`get_network_status`, sans code de gameplay.
- **Hors périmètre** : pawn, mouvement, UI.
- **Statut** : `En cours`. Présent (commit `97b27d1`) : `Assets/scenes/Tests/GameplayTest.scene` avec un `NetworkGameManager` (`Sandbox.NetworkHelper`, `StartServer: true`), prefab joueur assigné, quatre points de spawn — une première exécution avec plusieurs joueurs est possible. Reste à valider (voir [CURRENT_STATE.md](CURRENT_STATE.md)) : test à deux instances réelles, visibilité croisée host/client, ownership, late join, déconnexion, nettoyage réseau, absence d'erreurs dans les logs. Non déclaré terminé.

## 4. Pawn joueur minimal

- **Objectif** : un pawn networké spawné par joueur, ownership correcte.
- **Dépendances** : étape 3.
- **Critères de validation** : testé à deux instances (voir [../development/TESTING_MULTIPLAYER.md](../development/TESTING_MULTIPLAYER.md)) — ownership correcte, pas de doublon.
- **Hors périmètre** : mouvement, caméra, HUD.
- **Statut** : `En cours`. Présent : `Assets/Prefabs/Players/kodoku_player.prefab` (commit `97b27d1`), un pawn stock s&box (`Sandbox.PlayerController`, `Rigidbody`, mouvement standard, modèle citizen visible), un composant Kodoku d'identité, `KodokuPlayerComponent` (`Code/Players/KodokuPlayerComponent.cs`) — résolution du pawn local, distincte de `IsProxy` brut — voir [../architecture/PLAYER_ARCHITECTURE.md](../architecture/PLAYER_ARCHITECTURE.md), et un premier état de gameplay host-authoritative, `PlayerVitalsComponent` (`Code/Players/Vitals/PlayerVitalsComponent.cs`). **Résolution du pawn local et vitals réseau minimaux validés pour leur périmètre** par test réel à plusieurs instances (host + clients successifs, late join, déconnexion, isolation `NetFlags.OwnerOnly`) le 2026-07-11, voir [CURRENT_STATE.md](CURRENT_STATE.md) — jalon « Networked Player Vitals + Local HUD ». Un premier HUD local minimal (`Code/UI/Hud/GameHud.razor`, hors périmètre strict de cette étape mais construit pour visualiser/tester les vitals) est également validé pour sa version minimale — le HUD de gameplay complet reste à faire. Reste à faire pour cette étape : ownership au sens autorité de gameplay pour l'inventaire et la progression (au-delà des vitals), séparation claire entre état réseau et présentation locale, règles de mort et respawn futures. L'étape « Pawn joueur minimal » dans son ensemble n'est pas déclarée terminée.

## 5. Caméra et présentation locale

- **Objectif** : caméra strictement locale par client, jamais répliquée — voir [../architecture/PLAYER_ARCHITECTURE.md](../architecture/PLAYER_ARCHITECTURE.md) et ADR-0003.
- **Dépendances** : étape 4.
- **Critères de validation** : testé à deux instances — chaque client garde sa propre vue, pas de vol/gel de caméra.
- **Hors périmètre** : HUD de gameplay, inputs de gameplay au-delà du regard/déplacement de base.
- **Statut** : `Stock, testé à deux instances pour le scénario de base — aucune architecture locale dédiée`. La caméra de `GameplayTest.scene` (`_Local/Main Camera`) est pilotée par le `Sandbox.PlayerController` stock du prefab joueur (`UseCameraControls: true`) via le mécanisme natif `IsMainCamera` du moteur — aucune architecture locale Kodoku dédiée (composant caméra local explicite) n'est implémentée. **Testé réellement à deux instances le 2026-07-13** (connexion, déplacement/rotation simultanés, déconnexion, reconnexion) : aucun vol ni gel de caméra reproduit — voir la section caméra de [CURRENT_STATE.md](CURRENT_STATE.md) pour le détail et la réserve méthodologique (scénarios à plus de deux joueurs ou avec changement de pawn non couverts). Décision : la caméra stock est conservée telle quelle, pas de composant supplémentaire créé à ce stade.

## 6. Première interaction complète

- **Objectif** : un cycle complet interaction monde → requête host → réponse, comme cas d'école du modèle d'autorité — voir [../architecture/MULTIPLAYER_ARCHITECTURE.md](../architecture/MULTIPLAYER_ARCHITECTURE.md).
- **Dépendances** : étape 4.
- **Critères de validation** : testé à deux instances, y compris interaction simultanée par deux joueurs.
- **Hors périmètre** : items réels (peut utiliser un placeholder).
- **Statut** : `Fait pour son périmètre`. Le cycle interaction → RPC host → validation → mutation est implémenté directement avec un item réel (`WorldItemPickupComponent`, voir étape 9) — le scanner de détection est le système `Component.IPressable` stock du `Sandbox.PlayerController` déjà présent sur `kodoku_player.prefab`, pas un composant Kodoku dédié (décision documentée dans [ITEM_ARCHITECTURE.md](../architecture/ITEM_ARCHITECTURE.md)). **Validé par test réel à plusieurs instances le 2026-07-13**, y compris interaction par plusieurs joueurs successifs — voir [../architecture/ITEM_ARCHITECTURE.md](../architecture/ITEM_ARCHITECTURE.md). Un test de concurrence déterministe (deux transactions strictement simultanées) reste à construire si jugé nécessaire au-delà des scénarios déjà validés.

## 7. Nouveau système d'items

- **Objectif** : `ItemDefinition`/`ItemInstance` — voir [../architecture/ITEM_ARCHITECTURE.md](../architecture/ITEM_ARCHITECTURE.md).
- **Dépendances** : étape 2 (architecture définie).
- **Critères de validation** : au moins une définition d'item chargée et instanciée, testée en réseau.
- **Hors périmètre** : inventaire, équipement.
- **Statut** : `En cours`. `ItemDefinition`, `ItemInstance` et `WorldItemComponent` (réplication réseau host-authoritative) validés par test réel à deux instances (Tests A à E, 2026-07-12 — voir [../architecture/ITEM_ARCHITECTURE.md](../architecture/ITEM_ARCHITECTURE.md)). `LootSpawnPointComponent` (génération de loot host-authoritative, V1 mono-item) **validé par test réel à deux instances le 2026-07-12, Tests A à G, retest complet rejoué avec succès le 2026-07-16** après le refactor de `PublishAuthoritativeNetworkState()` (`WorldItemComponent.TryInitializeAuthoritativeNew()` le réutilisant désormais) — aucune régression confirmée par test réel, voir [../architecture/ITEM_ARCHITECTURE.md](../architecture/ITEM_ARCHITECTURE.md). `LootTableDefinition`, pickup/inventaire restent hors périmètre de cette étape.

## 8. Inventaire et équipement

- **Objectif** : conteneurs, emplacements d'équipement, consommation d'objets.
- **Dépendances** : étapes 6, 7.
- **Critères de validation** : transfert atomique d'une `ItemInstance` entre l'inventaire et un emplacement d'équipement, testé à deux instances sans duplication ni désynchronisation.
- **Hors périmètre** : UI définitive (peut utiliser un placeholder minimal) ; **transfert direct d'item entre deux inventaires appartenant à deux joueurs différents — ne fait pas partie du gameplay prévu de Kodoku** (décision du 2026-07-16, voir « Nouvelle prochaine priorité » ci-dessous et [OPEN_QUESTIONS.md](OPEN_QUESTIONS.md)) : l'échange entre joueurs passe par le système déjà validé, `PlayerItemDropComponent` → `WorldItemPickupComponent` (joueur A dépose, joueur B ramasse).
- **Statut** : `En cours`. Noyau local de conteneur à grille (`InventoryContainer`, branche `feature/inventory-core`) implémenté et **validé pour son périmètre strictement local** (Tests A à O, 15/15, exécution réelle en éditeur le 2026-07-12) — voir [../architecture/ITEM_ARCHITECTURE.md](../architecture/ITEM_ARCHITECTURE.md), section « Inventory Core », pour la portée exacte de cette validation. Depuis le 2026-07-13, un composant joueur (`PlayerInventoryComponent`) et un pickup host-authoritative (`WorldItemPickupComponent`) existent et sont **validés par test réel à plusieurs instances** (pickup, bonne attribution d'inventaire, inventaire plein) — voir « Interaction et pickup — V1 » dans [../architecture/ITEM_ARCHITECTURE.md](../architecture/ITEM_ARCHITECTURE.md). Toujours le 2026-07-13, la réplication réseau du contenu vers le client propriétaire (snapshot complet, panneau debug) est **implémentée et validée par test réel, huit scénarios** — voir « Snapshot d'inventaire propriétaire — V1 » dans le même document. Depuis le 2026-07-15, le drop d'une pile complète (`PlayerItemDropComponent`) est **validé par test réel pour son périmètre core**, et depuis le 2026-07-16 **pour ses cinq scénarios de robustesse** (double requête, chaîne vide, chaîne non-GUID, GUID absent, ownership rejeté, tous testés host et client) — voir « Drop d'item — V1 » dans le même document ; seuls le rollback (aucun moyen d'injection propre identifié) et la quantité supérieure à 1 (non testable avec le contenu actuel) restent ouverts. Restent non commencés : équipement, consommation, stacks, UI finale — le critère de validation formel de cette étape (transfert atomique inventaire ↔ équipement) reste donc non couvert.
- **Robustesse/non-régression du drop (mission du 2026-07-15/16) : terminée.** Retest des Tests A à G du loot et les cinq scénarios de robustesse du drop (double requête, identifiants invalides, rejet d'ownership) sont désormais validés par test réel — voir [../architecture/ITEM_ARCHITECTURE.md](../architecture/ITEM_ARCHITECTURE.md).
- **Nouvelle prochaine priorité (décidée le 2026-07-16) : équipement corporel minimal.** Le transfert direct d'item entre deux joueurs ne fait pas partie du gameplay prévu de Kodoku et n'est **pas** implémenté — décision explicite qui remplace l'ordre de priorité précédent (2026-07-15). Première version visée pour l'équipement : déplacement d'une pile complète depuis l'inventaire vers un emplacement d'équipement et déséquipement vers l'inventaire ; un nombre très limité de slots pour commencer ; compatibilité item/slot validée côté host ; ownership validé côté host ; conservation stricte de l'`InstanceId` ; refus propre si l'inventaire n'a pas de place au déséquipement ; transaction atomique avec rollback ; snapshot propriétaire cohérent ; test réel host + client sans duplication ni désynchronisation ; panneau debug minimal acceptable, sans UI finale. Voir [OPEN_QUESTIONS.md](OPEN_QUESTIONS.md) pour le suivi de cette décision.
- **Évaluation du système natif terminée.** Décision actuelle : inventaire personnalisé Kodoku (`InventoryContainer`/`InventoryPlacement`), pas le système natif `BaseInventoryComponent`/`BaseInventoryItem` — voir [ADR-0005](../decisions/ADR-0005-CUSTOM-INVENTORY.md). La recherche native (étude + spike expérimental) reste archivée dans `docs/research/`.

## 9. Objets du monde

- **Objectif** : items ramassables, conteneurs de butin dans la scène — voir [../architecture/SCENE_ARCHITECTURE.md](../architecture/SCENE_ARCHITECTURE.md).
- **Dépendances** : étape 8.
- **Critères de validation** : ramassage/dépôt testé à deux instances, pas de duplication d'objet.
- **Hors périmètre** : génération procédurale de contenu.
- **Statut** : `Fait pour son périmètre` (ramassage/dépôt individuels uniquement — voir ci-dessous pour les conteneurs). Ramassage implémenté et **validé par test réel à plusieurs instances le 2026-07-13** (`WorldItemPickupComponent`, transaction atomique host-authoritative : réservation → validation → ajout en inventaire → destruction réseau de l'objet-monde seulement après confirmation ; pas de duplication observée) — voir [../architecture/ITEM_ARCHITECTURE.md](../architecture/ITEM_ARCHITECTURE.md), section « Interaction et pickup — V1 ». Dépôt (drop, `PlayerItemDropComponent`) **validé par test réel le 2026-07-15 pour son périmètre core et le 2026-07-16 pour ses cinq scénarios de robustesse** (drop host/client, continuité d'`InstanceId`, late join, obstacle devant le joueur, double requête, identifiants invalides, ownership rejeté) — voir section « Drop d'item — V1 » du même document. Le critère de validation de cette étape (« ramassage/dépôt ») est donc couvert pour son périmètre core ; seuls le rollback et la quantité supérieure à 1 restent ouverts (voir même document).
- **Conteneurs du monde (regroupement de plusieurs items déposés dans une seule boîte, ex. `Cardboard_box`) : non commencés, explicitement reportés** (décision du 2026-07-15) — voir [../architecture/ITEM_ARCHITECTURE.md](../architecture/ITEM_ARCHITECTURE.md) et [OPEN_QUESTIONS.md](OPEN_QUESTIONS.md) pour la condition d'entrée (les transactions atomiques d'équipement/déséquipement et la synchronisation de l'inventaire propriétaire doivent être validées avant d'introduire un conteneur public partagé, étape 8) et les raisons du report. Cette étape 9 n'est donc **pas** terminée dans son ensemble malgré son statut « fait pour son périmètre » : le ramassage/dépôt individuel est validé pour son périmètre core, les conteneurs du monde restent un sous-objectif non commencé de l'« Objectif » ci-dessus.

## 10. IA et combat

- **Objectif** : entités non-joueur, logique de combat de base.
- **Dépendances** : étape 8 (pour les drops éventuels).
- **Critères de validation** : comportement IA cohérent pour tous les clients, testé à deux instances.
- **Hors périmètre** : IA avancée (pathfinding complexe, comportements sociaux).

## 11. Scènes, zones et extraction

- **Objectif** : plusieurs zones, transition entre elles — voir [../architecture/SCENE_ARCHITECTURE.md](../architecture/SCENE_ARCHITECTURE.md) et [../status/OPEN_QUESTIONS.md](OPEN_QUESTIONS.md).
- **Dépendances** : étape 9.
- **Critères de validation** : transition de zone testée à deux instances, sans perte d'état inattendue.
- **Hors périmètre** : contenu final des zones.

## 12. Persistance et reconnexion

- **Objectif** : sauvegarde/chargement d'état, reconnexion après déconnexion.
- **Dépendances** : étapes 7, 8, 11.
- **Critères de validation** : reconnexion testée à deux instances sans duplication ni perte d'état.
- **Hors périmètre** : optimisation de performance de sauvegarde.

## 13. Production de contenu

- **Objectif** : contenu de jeu réel (items, zones, IA) au-delà des cas de test des étapes précédentes.
- **Dépendances** : toutes les étapes précédentes.
- **Critères de validation** : à définir au moment venu.
- **Hors périmètre** : rien — c'est la phase de contenu.
