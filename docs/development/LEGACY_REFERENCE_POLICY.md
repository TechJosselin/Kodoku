# Politique de référence à Kodoku_Legacy

`Kodoku_Legacy` (https://github.com/TechJosselin/kodoku_legacy, chemin local dans `CLAUDE.local.md`) :

- est une **archive** ;
- est en **lecture seule** ;
- **ne constitue pas** l'architecture de référence de la reconstruction ;
- peut être **inspecté** pour comprendre une intention passée ;
- **ne doit pas être copié directement** (code, scène, prefab) ;
- **ne doit jamais recevoir de modification** depuis une session sur le nouveau projet ;
- **ne doit jamais devenir une dépendance** du nouveau Kodoku (pas de référence de projet, pas de package, pas de chemin d'asset pointant dessus).

## Procédure avant toute réutilisation d'intention

1. **Identifier l'intention** — quel besoin l'ancienne solution couvrait-elle réellement (ex. : « distinguer le joueur possédé par ce client des autres joueurs »).
2. **Identifier les erreurs de l'ancienne solution** — ce qui a mal fonctionné ou a nécessité des correctifs répétés (voir exemple ci-dessous).
3. **Redéfinir les besoins multiplayer** pour la nouvelle architecture, indépendamment de la façon dont l'ancien code les résolvait — voir [../architecture/MULTIPLAYER_ARCHITECTURE.md](../architecture/MULTIPLAYER_ARCHITECTURE.md).
4. **Reconstruire proprement**, avec le nouveau vocabulaire d'architecture (autorité, réplication, présentation locale).
5. **Tester indépendamment de l'ancien projet** — un comportement de l'ancien projet n'est jamais une preuve de correction pour le nouveau.

## Exemple d'erreur architecturale identifiée (à ne pas reproduire)

Sur l'ancienne version du projet, plusieurs bugs de multijoueur (vol de caméra entre clients, écran figé, écran noir chez un joueur qui rejoint) provenaient d'une confusion entre le pion réseau du joueur (pawn) et la caméra de rendu réelle du client — la caméra n'était pas suffisamment isolée comme concept purement local. Le principe correctif (caméra jamais répliquée comme propriété globale du pawn) est repris comme règle d'architecture dans [../architecture/PLAYER_ARCHITECTURE.md](../architecture/PLAYER_ARCHITECTURE.md) — **le mécanisme exact utilisé pour corriger ce bug dans l'ancien projet n'est pas repris**, seule l'intention (« isoler la caméra ») l'est.

Un autre point identifié : un enchaînement de RPC (une seconde RPC déclenchée depuis le traitement de la réponse d'une première) échouait silencieusement. La contrainte (« un flux multi-étapes est un aller-retour unique ») est reprise dans [../architecture/MULTIPLAYER_ARCHITECTURE.md](../architecture/MULTIPLAYER_ARCHITECTURE.md), sans reprendre le code qui la contournait.

## Ce qui n'est volontairement pas repris ici

Les structures de classes, noms de composants et organisation de fichiers de `Kodoku_Legacy` (ex. `KodokuPlayerComponent`, `InventoryContainer`, découpage en dossiers `Code/Player`, `Code/Items`, etc.) ne sont **pas** documentées comme modèle dans ce dépôt — les citer ici créerait une tentation de les recopier telles quelles, contrairement à la mission de reconstruction propre. Se référer à `Kodoku_Legacy` directement (chemin dans `CLAUDE.local.md`) si le nom ou le rôle d'un ancien composant doit être retrouvé ponctuellement.
