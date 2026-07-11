# Questions ouvertes

Décisions non encore tranchées. Ne pas les résoudre arbitrairement dans le code ou la documentation — les lever explicitement quand l'information nécessaire existe (test moteur, décision de conception).

## Architecture / gameplay

- **Stratégie exacte d'autorité du mouvement joueur** — host-authoritative strict, owner-authoritative avec réconciliation côté host, ou hybride. Laissée ouverte par [ADR-0002](../decisions/ADR-0002-HOST-AUTHORITY.md). À trancher après un premier prototype de contrôleur testé à plusieurs instances.
- **Structure finale des scènes** — une scène unique vs. plusieurs zones chargées/déchargées, mécanisme de transition. Voir [../architecture/SCENE_ARCHITECTURE.md](../architecture/SCENE_ARCHITECTURE.md).
- **Format complet des futures `ItemDefinition`** — champs exacts, conventions ; volontairement non recopié de l'ancien projet. Voir [../architecture/ITEM_ARCHITECTURE.md](../architecture/ITEM_ARCHITECTURE.md).
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
