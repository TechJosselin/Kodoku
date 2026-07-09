# Architecture joueur

**Statut : architecture visée, non implémentée.** Aucun pawn, contrôleur ou composant joueur n'existe dans le code actuel de Kodoku — voir [../status/CURRENT_STATE.md](../status/CURRENT_STATE.md). Ce document décrit la séparation de responsabilités à respecter quand ce code sera écrit, et capitalise sur des erreurs identifiées sur l'ancienne version du projet (voir [../development/LEGACY_REFERENCE_POLICY.md](../development/LEGACY_REFERENCE_POLICY.md)) — sans en reprendre le code.

## Séparation de responsabilités envisagée

Un « joueur » n'est pas un objet monolithique. Il se décompose en responsabilités distinctes, qui ne doivent pas être mélangées dans un seul composant :

- **Pawn réseau** — le `GameObject` networké lui-même, son ownership, son cycle de spawn/despawn.
- **Identité** — quel `Connection` possède ce pawn, distinct de « quel client regarde à travers ce pawn » (voir la note ci-dessous sur `Local`).
- **Mouvement** — logique de déplacement, soumise à une stratégie d'autorité pas encore tranchée (voir [../status/OPEN_QUESTIONS.md](../status/OPEN_QUESTIONS.md)).
- **État de gameplay** — santé, inventaire, statut : voir [MULTIPLAYER_ARCHITECTURE.md](MULTIPLAYER_ARCHITECTURE.md) pour l'autorité de ces données.
- **Apparence** — modèle, tenue équipée : donnée répliquée mais dont le rendu est décidé localement par chaque client.
- **Animations** — pilotées par l'état répliqué, jouées localement.
- **Points d'ancrage** — attaches (main, dos, etc.) pour objets tenus/équipés.
- **Caméra locale** — voir principe ci-dessous, jamais une propriété globale répliquée du pawn.
- **HUD local** — voir [UI_ARCHITECTURE.md](UI_ARCHITECTURE.md).
- **Inputs locaux** — jamais lus depuis un pawn distant (proxy).
- **Mort et respawn** — transition d'état soumise à l'autorité du host (cohérent avec ADR-0002), séquencement non encore décidé.

## Principe central : la caméra n'est pas une propriété répliquée du pawn

**La caméra principale ne doit jamais être pilotée comme une propriété globale répliquée du pawn.** Chaque client a exactement une caméra de rendu réelle, strictement locale (`NetworkMode.Never` ou équivalent), jamais spawnée en réseau, donc sans owner et non affectable par un autre client. Le pawn réseau (local ou distant) ne doit jamais lui-même devenir la caméra de rendu.

Ce principe n'est pas une préférence de style : sur l'ancienne version du projet, plusieurs bugs sérieux (vol de caméra entre clients, écran figé, écran noir chez un joueur qui rejoint) ont été causés par la confusion entre « le pawn a une caméra » et « la caméra de rendu est une propriété locale distincte du pawn ». Ne pas reproduire cette confusion dans la reconstruction. Les mécanismes précis (ordre des composants, gestion de `IsMainCamera`, désactivation des caméras du pawn) ne sont **pas repris ici** — ce sont des détails d'implémentation de l'ancien code, à reconcevoir proprement, pas à copier. Voir [../development/LEGACY_REFERENCE_POLICY.md](../development/LEGACY_REFERENCE_POLICY.md) pour la procédure de réutilisation d'intention.

## `Local` vs `IsProxy`

Comme détaillé dans [MULTIPLAYER_ARCHITECTURE.md](MULTIPLAYER_ARCHITECTURE.md#ownership-confirmé), déterminer « est-ce mon propre pawn » ne doit pas reposer uniquement sur `IsProxy` au moment du spawn (retard confirmé par test réel). La logique exacte de résolution du pawn local pour Kodoku reste à concevoir — ne pas supposer qu'une solution existe déjà.

## Éléments encore ouverts

- Stratégie d'autorité du mouvement (host-authoritative strict vs. owner-authoritative avec réconciliation) — voir [../status/OPEN_QUESTIONS.md](../status/OPEN_QUESTIONS.md).
- Séquencement exact mort/respawn.
- Représentation des joueurs distants (proxy) — niveau de détail visuel, LOD réseau.
