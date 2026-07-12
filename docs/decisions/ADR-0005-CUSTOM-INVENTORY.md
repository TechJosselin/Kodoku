# ADR-0005 — Inventaire personnalisé plutôt que le système natif s&box

**Statut** : Acceptée

## Contexte

Le moteur s&box fournit un système natif d'inventaire et d'armes (`Sandbox.BaseInventoryComponent`/`BaseInventoryItem`/`BaseCombatWeapon`, plus la réserve de munitions abstraite `BaseAmmoResource`/`BaseAmmoPickup`), découvert le 12 juillet 2026 dans l'éditeur. Kodoku possédait déjà une fondation d'items propre et validée (`ItemDefinition`, `ItemInstance`, `WorldItemComponent`, `LootSpawnPointComponent` — voir [ITEM_ARCHITECTURE.md](../architecture/ITEM_ARCHITECTURE.md)), mais aucun inventaire joueur, pickup interactif, conteneur ni équipement n'existait encore. Cette découverte est intervenue avant la construction de l'inventaire V2 (étape 8 de la [ROADMAP.md](../status/ROADMAP.md)), rendant son évaluation peu coûteuse : aucun code Kodoku existant n'aurait été à défaire si le système natif s'était avéré pertinent.

### Options évaluées

Trois stratégies ont été documentées dans [SBOX_BUILTIN_INVENTORY_EVALUATION.md](../research/SBOX_BUILTIN_INVENTORY_EVALUATION.md) :

- **A. Système natif uniquement** — networking, pickup/drop/switch/transfer et prédiction de tir déjà fournis, mais système à slots (Hotbar/Buckets) sans grille 2D documentée, et chaque `BaseInventoryItem` est un GameObject vivant en permanence dans la hiérarchie de l'inventaire (y compris rangé), à l'opposé du choix déjà fait pour `ItemInstance` (classe C# pure).
- **B. Architecture hybride** — `ItemInstance` Kodoku comme source de vérité pour l'identité/état, adossée à `BaseInventoryComponent` pour le transport réseau (pickup/drop/transfer). Un spike a suivi cette piste (`spike/native-inventory-adapter`, voir [NATIVE_INVENTORY_SPIKE_RESULTS.md](../research/NATIVE_INVENTORY_SPIKE_RESULTS.md)) : pickup, drop, ré-acquisition et transfert (Tests A, B, D, E, F, G) se sont montrés solides et reproductibles sur cinq passages réels à deux/trois instances, `InstanceId` stable, aucune duplication. Une limite réelle a été isolée précisément (Test H) : seul l'`ActiveItem` d'un inventaire se réplique de façon fiable à un late joiner ; un item rangé dans un autre slot ne se réplique pas tant qu'il ne le devient pas. La piste de contournement testée (`Network.Refresh()` host-only) n'a pas donné de résultat positif dans la fenêtre observée, sans être totalement exclue. Le spike a été arrêté par décision explicite avant qu'une resynchronisation applicative de secours n'ait pu être conçue et testée.
- **C. Inventaire entièrement personnalisé** — contrôle total de la grille spatiale (largeur/hauteur/rotation, déjà modélisée côté `ItemDefinition`), état indépendant des GameObjects, cohérent avec `ItemInstance`, persistance entièrement maîtrisée. Coût : pickup, transfert, ownership, réplication à concevoir et valider intégralement à deux instances.

## Décision

Kodoku retient l'**option C : un inventaire personnalisé**, construit autour de `ItemInstance` (source de vérité logique) et `InventoryContainer`/`InventoryPlacement` (placements spatiaux). Les `GameObject` ne représentent que les objets réellement présents dans le monde (`WorldItemComponent`) — jamais un exemplaire simplement rangé dans un conteneur. `BaseInventoryComponent`/`BaseInventoryItem` ne sont **pas** utilisés par l'architecture de production. Le noyau `InventoryContainer` (classe C# pure, sans réseau, sans `Component`, sans `GameObject`) est déjà implémenté et validé pour son périmètre strictement local (Tests A à O, 15/15, voir [ITEM_ARCHITECTURE.md](../architecture/ITEM_ARCHITECTURE.md)).

## Raisons

- **Grille spatiale non native** — aucune notion de largeur/hauteur/rotation dans `BaseInventoryComponent` (système à slots Hotbar/Buckets), alors que `ItemDefinition.GridWidth`/`GridHeight`/`CanRotate` existent déjà et sont au cœur du gameplay extraction-shooter visé.
- **Divergence structurelle de fond, confirmée par le spike** — un `BaseInventoryItem` natif est un GameObject/composant vivant en permanence dans la hiérarchie de l'inventaire (y compris rangé), à l'opposé du choix déjà fait pour `ItemInstance` (classe C# pure, indépendante du moteur). Le spike a montré que cette divergence n'empêche pas un transport fiable (pickup/drop/transfert), mais ne l'a pas fait disparaître.
- **Stacking et poids non natifs** — aucune propriété équivalente à `MaxStack`/`Quantity`/`Weight` trouvée sur `BaseInventoryItem`/`BaseInventoryComponent` dans les sources consultées.
- **Munitions physiques non natives** — le système natif ne fournit qu'une réserve abstraite par type (`BaseAmmoResource`), pas d'objet ramassable/empilable dans une grille, alors qu'un système de munitions « objet d'inventaire » reste envisagé pour Kodoku.
- **Limite non résolue du late join pour les items inactifs (Test H)** — point potentiellement bloquant pour une architecture hybride, dont la résolution (resynchronisation applicative explicite) restait à concevoir et n'a pas été testée avant l'arrêt du spike.
- **Aucun mécanisme de sauvegarde natif documenté** — la persistance devra être conçue côté Kodoku dans tous les cas, natif ou non.
- Le système natif reste malgré tout une **référence utile** de principes déjà éprouvés (autorité host, validation avant mutation, opérations atomiques via `Transfer`) — dont `InventoryContainer` s'inspire déjà dans sa propre conception (`CanPlace` avant mutation, `TryMove` atomique).

## Conséquences positives

- Contrôle total du modèle spatial (grille, rotation, poids, stacking) sans contourner un système de slots pensé pour un autre usage.
- `ItemInstance` reste l'unique source de vérité pour l'identité, la quantité et l'état persistant — pas de risque de double source de vérité entre une donnée Kodoku et un GameObject natif.
- Le noyau local (`InventoryContainer`) est déjà écrit, testé et validé (Tests A à O), donc cette décision ne remet rien en cause de ce qui existe déjà en production.
- Aucune dépendance à un comportement moteur non entièrement documenté (late join des items inactifs, sémantique exacte de `Transfer(..., slot)`) pour la suite du développement de l'inventaire.

## Compromis et limites

- Pickup, drop, transfert, ownership et réplication de l'inventaire (au-delà du noyau local déjà validé) doivent être conçus, écrits et validés à deux instances intégralement — rien n'est gratuit, contrairement à l'option hybride qui aurait réutilisé le transport réseau déjà écrit et testé par Facepunch.
- Risque réel de recréer, avec plus de bugs potentiels au démarrage, des fonctions déjà fournies et partiellement éprouvées par le moteur (transfert atomique, autorité host, prédiction de tir pour de futures armes).
- Si Kodoku ajoute des armes plus tard, `BaseCombatWeapon` (chargeurs, prédiction de tir, `ShotClaim`) ne sera pas réutilisé directement — un système de tir/armes personnalisé sera à concevoir séparément, hors périmètre de cette décision.

## Éléments encore ouverts

- Réplication du futur `PlayerInventoryComponent` — autorité, snapshot, late join : non conçue, non implémentée.
- Pickup interactif (portée, ligne de vue) — non conçu, non implémenté ; le système natif documentait déjà l'absence de contrôle par défaut sur ce point (`CanPickupWorldItem`), un point de vigilance qui reste valable même pour une implémentation personnalisée.
- Drop réseau, transfert entre conteneurs/joueurs, stack merge/split, inventaires imbriqués, équipement par emplacement corporel, persistance/sauvegarde — tous non implémentés, hors périmètre de cette décision et du noyau `InventoryContainer` actuel.
- Si un futur système d'armes est envisagé, réévaluer séparément `BaseCombatWeapon` pour ce seul périmètre — cette décision porte sur l'inventaire, pas sur les armes.

## Références

- [SBOX_BUILTIN_INVENTORY_EVALUATION.md](../research/SBOX_BUILTIN_INVENTORY_EVALUATION.md) — étude documentaire du système natif.
- [NATIVE_INVENTORY_SPIKE_RESULTS.md](../research/NATIVE_INVENTORY_SPIKE_RESULTS.md) — résultats du spike expérimental hybride.
- [ITEM_ARCHITECTURE.md](../architecture/ITEM_ARCHITECTURE.md) — section « Inventory Core », noyau `InventoryContainer` retenu et validé.
