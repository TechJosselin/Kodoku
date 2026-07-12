# ADR-0002 — Host authority

**Statut** : Acceptée

## Contexte

s&box permet un modèle owner-authoritative pour l'état `[Sync]` d'un objet networké (voir [../architecture/MULTIPLAYER_ARCHITECTURE.md](../architecture/MULTIPLAYER_ARCHITECTURE.md)) — le moteur n'impose pas d'autorité host par défaut. Sans décision explicite, chaque système de gameplay risque d'adopter un modèle d'autorité différent au gré de l'implémentation, rendant le comportement réseau global imprévisible.

## Décision

Le **host est la source de vérité pour les états de gameplay importants** (ceux qui doivent résister à un client incohérent ou malveillant : santé, inventaire, progression, tout ce qui accorde ou retire une ressource de jeu). La stratégie exacte pour le **mouvement joueur** reste volontairement ouverte à ce stade — voir [../status/OPEN_QUESTIONS.md](../status/OPEN_QUESTIONS.md) — car un modèle strictement host-authoritative pour le mouvement a des implications de réactivité perçue qui n'ont pas encore été évaluées pour Kodoku.

## Conséquences positives

- Un modèle d'autorité cohérent et prévisible pour l'essentiel du gameplay.
- Réduit la surface pour la triche côté client sur les états qui comptent.

## Compromis et limites

- Le host devient un point de charge et de responsabilité plus important ; les implications de performance à `MaxPlayers: 64` (voir `kodoku.sbproj`) ne sont pas encore évaluées.
- Laisser le mouvement ouvert signifie qu'un système qui en dépend (ex. détection de collision côté gameplay) ne peut pas encore supposer un modèle d'autorité définitif.

## Éléments à réévaluer

- Stratégie d'autorité du mouvement, une fois un premier prototype de contrôleur joueur testé à plusieurs instances.
- Cette décision une fois qu'un nombre significatif de systèmes de gameplay existeront et pourront révéler un besoin de nuance par système plutôt qu'une règle unique.
