# Questions ouvertes

Décisions non encore tranchées. Ne pas les résoudre arbitrairement dans le code ou la documentation — les lever explicitement quand l'information nécessaire existe (test moteur, décision de conception).

## Décisions résolues

- **Inventaire natif, hybride ou personnalisé ?** — **Résolue le 12 juillet 2026 : inventaire personnalisé Kodoku retenu** (`InventoryContainer`/`InventoryPlacement`, noyau local déjà validé Tests A à O). Le système natif s&box (`BaseInventoryComponent`/`BaseInventoryItem`) est conservé comme référence (principes d'autorité host, validation avant mutation, opérations atomiques) mais n'est pas adopté en production, y compris sous forme hybride malgré des résultats de spike globalement positifs (pickup/drop/transfert fiables, mais late join incomplet pour un item inactif). Voir [ADR-0005](../decisions/ADR-0005-CUSTOM-INVENTORY.md) pour la décision complète et [SBOX_BUILTIN_INVENTORY_EVALUATION.md](../research/SBOX_BUILTIN_INVENTORY_EVALUATION.md)/[NATIVE_INVENTORY_SPIKE_RESULTS.md](../research/NATIVE_INVENTORY_SPIKE_RESULTS.md) pour l'étude et le spike qui l'ont informée. Restent ouvertes, en aval de cette décision : réplication du `PlayerInventoryComponent`, snapshots et late join, pickup concurrent, drop réseau, transfert entre conteneurs, stack merge/split, inventaires imbriqués, équipement, persistance — voir « Architecture / gameplay » ci-dessous.

## Architecture / gameplay

- **Stratégie exacte d'autorité du mouvement joueur** — host-authoritative strict, owner-authoritative avec réconciliation côté host, ou hybride. Laissée ouverte par [ADR-0002](../decisions/ADR-0002-HOST-AUTHORITY.md). À trancher après un premier prototype de contrôleur testé à plusieurs instances.
- **Structure finale des scènes** — une scène unique vs. plusieurs zones chargées/déchargées, mécanisme de transition. Voir [../architecture/SCENE_ARCHITECTURE.md](../architecture/SCENE_ARCHITECTURE.md).
- **Extensions futures d'`ItemDefinition`** — une V1 existe déjà et est validée pour son périmètre (`ItemId`/`DisplayName`/`Description`/`Category`/`Tags`/`Icon`/`GridWidth`/`GridHeight`/`CanRotate`/`Weight`/`MaxStack`/`WorldModel`/`WorldPrefabOverride`, voir [../architecture/ITEM_ARCHITECTURE.md](../architecture/ITEM_ARCHITECTURE.md)). Reste ouvert : quels champs supplémentaires un futur système d'inventaire/équipement/durabilité nécessitera (ex. emplacement d'équipement, durabilité, effets de consommation) — non décidés, à ajouter au moment où le besoin réel apparaît.
- **Réplication du futur `PlayerInventoryComponent`** — **résolue pour son périmètre le 2026-07-13** : snapshot complet en lecture seule vers le propriétaire implémenté et validé par huit scénarios de test réel (host+client, pickup, inventaire plein, révisions successives, resync, prise de contrôle, déconnexion) — voir [ITEM_ARCHITECTURE.md](../architecture/ITEM_ARCHITECTURE.md), section « Snapshot d'inventaire propriétaire — V1 ». Restent ouverts : late join avec un pawn déjà chargé (pas testable tant qu'aucune persistance n'existe), système de delta (volontairement écarté pour cette V1), toute réplication liée au drop/transfert/équipement.
- **Pickup interactif** (portée, ligne de vue, concurrence entre deux joueurs sur le même item) — non conçu, non implémenté.
- **Drop réseau, transfert entre conteneurs/joueurs, stack merge/split, inventaires imbriqués, équipement par emplacement corporel, persistance/sauvegarde de l'inventaire** — tous non implémentés, hors périmètre du noyau `InventoryContainer` actuel.
- **Système de sauvegarde** — mécanisme et portée (état joueur seul, ou état monde complet), fréquence, format.
- **Stratégie de transition entre zones** — dépend de la structure de scènes ci-dessus.
- **Gestion de la reconnexion** — récupération d'état après déconnexion, ré-association à un pawn existant vs. nouveau spawn.
- **Choix de Libraries externes éventuelles** — aucune Library de gameplay n'est décidée à ce jour.

## Comportement moteur s&box non confirmé (source : vault Obsidian, `SBOX_OPEN_QUESTIONS.md`)

Ces points sont des faits moteur non documentés officiellement, pas des décisions Kodoku — mais ils contraignent la conception tant qu'ils ne sont pas levés par un test moteur dédié.

- **Late join** : ordre exact entre la reconstruction réseau d'un objet et le cycle de vie de ses composants (`OnAwake`/`OnStart`/`OnNetworkSpawn`) ; disponibilité garantie ou non des valeurs `[Sync]` avant `OnStart`. Lacune identifiée comme la plus impactante pour la fiabilité du multiplayer Kodoku.
- **Moment exact où l'ownership devient fiable** (`OwnerConnection`, `IsProxy`) pour un objet reçu par un client distant lors d'un spawn réseau.
- **Résolution de `ComponentReference`/`GameObjectReference` chez un late joiner** — cohérence des `GameObject.Id` entre clients pour un même objet logique.
- **Comportement d'une référence locale (`[Property] GameObject`/`Component`) portée par un composant réseau** — répliquée ou purement locale par client : non documenté.
- **RPC chaînées** : pourquoi une seconde RPC émise depuis le handler de réponse d'une première n'arrive jamais — confirmé empiriquement sur l'ancien projet, mécanisme non expliqué par une source officielle. Voir [../architecture/MULTIPLAYER_ARCHITECTURE.md](../architecture/MULTIPLAYER_ARCHITECTURE.md#rpc-confirmé--contrainte-spécifique-kodoku).

Pour le détail complet et le classement par niveau de confiance (`CONFIRMED` / `PARTIALLY DOCUMENTED` / `NOT DOCUMENTED` / `REQUIRES ENGINE TEST`), voir `docs/references/SBOX_OPEN_QUESTIONS.md` dans le vault Obsidian (chemin dans `CLAUDE.local.md`).
