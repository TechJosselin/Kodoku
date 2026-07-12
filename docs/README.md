# Documentation Kodoku — index

Point d'entrée de la documentation du projet. Voir [../CLAUDE.md](../CLAUDE.md) pour le résumé des règles essentielles à destination de Claude Code.

## Architecture (`architecture/`)

Décrit l'architecture **visée** pour la reconstruction — pas ce qui est déjà implémenté, sauf mention contraire dans [status/CURRENT_STATE.md](status/CURRENT_STATE.md).

| Document | Rôle |
|---|---|
| [PROJECT_ARCHITECTURE.md](architecture/PROJECT_ARCHITECTURE.md) | Vue d'ensemble : domaines fonctionnels, dépendances entre eux, séparation données/logique/présentation |
| [MULTIPLAYER_ARCHITECTURE.md](architecture/MULTIPLAYER_ARCHITECTURE.md) | Document architectural central : autorité, ownership, réplication, RPC, late join, déconnexion |
| [PLAYER_ARCHITECTURE.md](architecture/PLAYER_ARCHITECTURE.md) | Séparation pawn réseau / identité / mouvement / présentation locale (caméra, HUD) |
| [ITEM_ARCHITECTURE.md](architecture/ITEM_ARCHITECTURE.md) | `ItemDefinition` vs `ItemInstance`, inventaire, équipement, conteneurs |
| [SCENE_ARCHITECTURE.md](architecture/SCENE_ARCHITECTURE.md) | Organisation des scènes, objets statiques/réseau/locaux, spawn, chargement de zones |
| [UI_ARCHITECTURE.md](architecture/UI_ARCHITECTURE.md) | HUD/menus locaux, absence d'autorité gameplay dans l'UI |

## Développement (`development/`)

| Document | Rôle |
|---|---|
| [DEVELOPMENT_WORKFLOW.md](development/DEVELOPMENT_WORKFLOW.md) | Cycle de travail recommandé, de la tâche à la mise à jour de la documentation |
| [TESTING_MULTIPLAYER.md](development/TESTING_MULTIPLAYER.md) | Checklist de tests coop (host/client, late join, déconnexion, etc.) |
| [ASSET_MIGRATION.md](development/ASSET_MIGRATION.md) | Politique de transfert d'assets depuis `Kodoku_Legacy` |
| [LEGACY_REFERENCE_POLICY.md](development/LEGACY_REFERENCE_POLICY.md) | Statut de `Kodoku_Legacy` et procédure avant toute réutilisation d'intention |

## Décisions (`decisions/`)

Architecture Decision Records — voir [decisions/README.md](decisions/README.md) pour le fonctionnement. Décisions actuelles : [ADR-0001](decisions/ADR-0001-MULTIPLAYER-FIRST.md) (multiplayer-first), [ADR-0002](decisions/ADR-0002-HOST-AUTHORITY.md) (host authority), [ADR-0003](decisions/ADR-0003-LOCAL-PRESENTATION.md) (présentation locale), [ADR-0004](decisions/ADR-0004-CLEAN-REBUILD.md) (reconstruction propre).

## État du projet (`status/`)

| Document | Rôle |
|---|---|
| [CURRENT_STATE.md](status/CURRENT_STATE.md) | Ce qui est réellement vérifié dans le projet, à la date indiquée |
| [ROADMAP.md](status/ROADMAP.md) | Étapes de reconstruction, sans dates artificielles |
| [OPEN_QUESTIONS.md](status/OPEN_QUESTIONS.md) | Décisions non encore tranchées |
| [CHANGELOG_DOCUMENTATION.md](status/CHANGELOG_DOCUMENTATION.md) | Historique des changements de documentation |

## Recherche (`research/`)

Études ponctuelles — pas des architectures adoptées, voir chaque document pour son statut exact.

| Document | Rôle |
|---|---|
| [LEGACY_MULTIPLAYER_AUDIT.md](research/LEGACY_MULTIPLAYER_AUDIT.md) | Audit du socle multiplayer de `Kodoku_Legacy` |
| [SBOX_BUILTIN_INVENTORY_EVALUATION.md](research/SBOX_BUILTIN_INVENTORY_EVALUATION.md) | Évaluation du système Inventory/Weapons natif s&box, aucune décision d'adoption prise |

## Sources externes (non versionnées ici)

Le vault Obsidian (recherche s&box) et `Kodoku_Legacy` (archive) sont des ressources externes à ce dépôt — leurs chemins locaux sont dans `CLAUDE.local.md`, pas ici.
