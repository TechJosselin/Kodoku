# ADR-0003 — Local presentation

**Statut** : Acceptée

## Contexte

Sur l'ancienne version du projet, la confusion entre le pion réseau d'un joueur (pawn) et les éléments de présentation qui devraient être strictement locaux à un client (en particulier la caméra) a produit des bugs sérieux de multijoueur (vol de caméra, écran figé, écran noir) — voir [../development/LEGACY_REFERENCE_POLICY.md](../development/LEGACY_REFERENCE_POLICY.md). Sans règle explicite, rien n'empêche un futur système de répéter cette confusion pour la caméra, le HUD, ou d'autres éléments de présentation.

## Décision

**Caméra, HUD, menus, inputs et audio listener ne sont jamais répliqués comme état global.** Ce sont des concepts strictement locaux à chaque client, qui lisent l'état des systèmes de gameplay (répliqué ou non) mais ne sont eux-mêmes jamais portés par une propriété réseau partagée. Voir [../architecture/PLAYER_ARCHITECTURE.md](../architecture/PLAYER_ARCHITECTURE.md) et [../architecture/UI_ARCHITECTURE.md](../architecture/UI_ARCHITECTURE.md).

## Conséquences positives

- Élimine par construction toute une classe de bugs de multijoueur déjà rencontrée sur l'ancienne version du projet.
- Clarifie, pour tout nouveau système, ce qu'il ne doit jamais tenter de répliquer.

## Compromis et limites

- Nécessite de concevoir explicitement, pour chaque nouvel élément de présentation, comment il obtient les données dont il a besoin depuis les systèmes autoritaires (voir [../architecture/MULTIPLAYER_ARCHITECTURE.md](../architecture/MULTIPLAYER_ARCHITECTURE.md)) plutôt que de le brancher directement sur un flux réseau.

## Éléments à réévaluer

Aucun à ce jour — ce principe est directement corrélé à des bugs réels déjà observés ; le réévaluer demanderait une raison de gameplay forte et explicite (ex. caméra spectateur partagée, qui n'est pas envisagée actuellement).
