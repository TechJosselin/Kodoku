# Architecture multiplayer

**Statut : document architectural central, en partie vision, en partie faits moteur confirmés.** Les principes marqués « confirmé » proviennent de l'API s&box (vault Obsidian, `SBOX_NETWORKING_SUMMARY.md`, `verified_with_api: true`) ou d'un test réel documenté sur l'ancienne version du projet. Le reste est une intention d'architecture pour Kodoku, pas encore implémentée — voir [../status/CURRENT_STATE.md](../status/CURRENT_STATE.md).

Ce document est la référence pour appliquer [../../.claude/rules/multiplayer.md](../../.claude/rules/multiplayer.md) à une fonctionnalité concrète.

## Principes d'autorité (confirmé, modèle moteur)

s&box est **host-authoritative par convention, pas par imposition du moteur** : n'importe quel `GameObject` networké a un **owner** (qui pilote son état `[Sync]`), distinct du **host** (seul à exécuter `[Rpc.Host]` et, par défaut, seul à pouvoir `Refresh()` un objet networké). Owner ≠ host ≠ créateur ≠ « autorité de gameplay » — quatre notions séparées qu'il faut choisir explicitement pour chaque système, pas supposer équivalentes.

**Décision Kodoku (ADR-0002)** : pour tout état de gameplay qui doit résister à un client incohérent ou malveillant (santé, inventaire, progression), le **host** est la source de vérité, même si le moteur autoriserait un modèle owner-authoritative. La stratégie exacte pour le mouvement joueur reste ouverte — voir [../status/OPEN_QUESTIONS.md](../status/OPEN_QUESTIONS.md).

## Ownership (confirmé)

`OwnerTransfer` (`Takeover`/`Fixed`/`Request`) et `NetworkOrphaned` (`Destroy`/`Host`/`Random`/`ClearOwner`) sont des réglages au niveau de l'objet racine networké. `NetworkObject.HasControl(Connection)` est le primitive documenté pour « cette connexion a-t-elle l'autorité ici ». `IsProxy` est vrai pour tout objet qu'on ne contrôle pas — y compris, sur chaque client sauf le host, tout objet non possédé.

**Point de vigilance confirmé par test réel (ancien projet)** : `GameObject.IsProxy` peut retarder par rapport à la propagation réelle de l'ownership juste après un spawn (lu `False` avant de repasser `True`). Ne jamais utiliser `IsProxy` brut comme unique critère pour déclencher une prise de contrôle locale (caméra, input) au moment du spawn — préférer `OwnerConnection == Connection.Local` quand disponible, avec un repli explicite documenté si `OwnerConnection` n'est pas encore résolu. Détail complet dans [../development/LEGACY_REFERENCE_POLICY.md](../development/LEGACY_REFERENCE_POLICY.md).

## État synchronisé — `[Sync]` (confirmé)

Owner-authoritative par défaut ; `SyncFlags.FromHost`/`[HostSync]` bascule une propriété en host-authoritative. `SyncFlags.Interpolate` lisse les types numériques/transform entre ticks. `SyncFlags.Query` interroge un getter au lieu de dépendre de l'appel du setter. Un ancien attribut `[Net]` évoqué par une source communautaire n'existe pas dans l'API actuelle — ne pas s'y fier.

## RPC (confirmé + contrainte spécifique Kodoku)

`[Rpc.Broadcast]` / `[Rpc.Host]` / `[Rpc.Owner]` s'appellent comme des méthodes normales ; `Rpc.Caller` identifie l'appelant. `Rpc.FilterInclude`/`FilterExclude` filtrent les destinataires et ne s'imbriquent pas.

**Contrainte non documentée officiellement, confirmée empiriquement sur l'ancienne version du projet** : une seconde RPC sortante, émise depuis l'intérieur du handler de réponse d'une première RPC, n'arrive jamais côté hôte (trois variantes testées, échec silencieux dans les trois cas). Concevoir tout flux multi-étapes comme un aller-retour requête/réponse unique, jamais comme une chaîne de RPC déclenchées les unes dans les autres. Voir [../status/OPEN_QUESTIONS.md](../status/OPEN_QUESTIONS.md).

## Spawn et despawn (confirmé)

`NetworkSpawn()` capture la hiérarchie **au moment de l'appel** — les changements structurels ultérieurs (nouveaux composants/enfants) ne sont pas networkés sans appel explicite à `Network.Refresh()` (host-only par défaut). `GameObject.Destroy()` met la suppression en file jusqu'à la fin de frame ; `DetachFromNetwork()` transforme un objet networké en objet local pur sur chaque client, sans le détruire.

## Late join (non documenté officiellement — lacune connue)

Confirmé conceptuellement : un nouveau joueur reçoit des données additionnelles attachées au snapshot lors de sa connexion. **Non confirmé** : l'ordre exact entre cette reconstruction et le cycle de vie des composants (`OnAwake`/`OnStart`/`OnNetworkSpawn`), et si les valeurs `[Sync]` sont garanties présentes avant `OnStart`. C'est la lacune la plus importante identifiée pour la fiabilité du multiplayer Kodoku — voir [../status/OPEN_QUESTIONS.md](../status/OPEN_QUESTIONS.md). Toute fonctionnalité qui dépend d'un ordre précis à ce niveau doit être testée avec un late joiner réel, pas supposée correcte.

## Déconnexion (confirmé, mécanisme ; stratégie Kodoku ouverte)

`INetworkListener.OnDisconnected(Connection)` (host-only) et la politique `NetworkOrphaned` par objet déterminent ce qui arrive aux objets d'une connexion qui part. La stratégie de reconnexion (récupération d'état, ré-association à un pawn existant) n'est pas encore décidée — voir [../status/OPEN_QUESTIONS.md](../status/OPEN_QUESTIONS.md).

## Objets du monde et interactions

Un objet interactif networké suit les mêmes règles d'autorité que tout autre état de gameplay : le host valide et accorde (pattern requête/réponse en un aller-retour, cf. RPC ci-dessus). Détail dans [SCENE_ARCHITECTURE.md](SCENE_ARCHITECTURE.md).

## Persistance

La persistance (sauvegarde/chargement) est un domaine séparé de la réplication réseau — un état peut être répliqué sans être persistant (ex. un événement sonore ponctuel) ou persistant sans être répliqué en continu (ex. état reconstruit à la connexion). Ne pas confondre les deux lors de la conception d'un système. Stratégie non encore décidée — voir [../status/OPEN_QUESTIONS.md](../status/OPEN_QUESTIONS.md).

## Présentation locale (confirmé par ADR-0003 et par un cas documenté sur l'ancienne version)

Caméra, HUD, menus, inputs et audio listener sont strictement locaux, jamais portés par un état réseau global. Détail complet (y compris les règles de fonctionnement caméra tirées de tests réels) dans [PLAYER_ARCHITECTURE.md](PLAYER_ARCHITECTURE.md).

## Stratégie de tests

Toute fonctionnalité de gameplay networké doit être validée avec **au moins deux instances** (host + client) avant d'être considérée terminée — le mode solo ne teste pas la logique réseau. Checklist complète : [../development/TESTING_MULTIPLAYER.md](../development/TESTING_MULTIPLAYER.md).

## Matrice d'autorité (générique)

Gabarit à remplir à mesure que chaque système est conçu. Les lignes ci-dessous sont des **exemples illustratifs de la façon de raisonner**, pas des systèmes déjà implémentés dans Kodoku.

| Système | Autorité | Répliqué | Local | Persistant |
|---|---|---|---|---|
| État de santé (exemple) | Host (`[HostSync]` si résistance à la triche requise) ou owner (`[Sync]`) selon décision à prendre | Oui (`[Sync]`, `SyncFlags.Interpolate` pour un affichage lissé) | Non | À décider (voir [OPEN_QUESTIONS.md](../status/OPEN_QUESTIONS.md)) |
| Événement sonore ponctuel (exemple) | Qui déclenche l'action | `[Rpc.Broadcast]` (événement, pas un état) | — | Non (un late joiner ne reçoit jamais les RPC passées) |
| Activation de la caméra locale (exemple) | N/A — décision purement locale | Non — jamais networké (`NetworkMode.Never`) | Oui, entièrement | N/A |
| Ouverture d'un menu local (exemple) | N/A — état UI local | Non | Oui | Non |

## Sources

- `docs/references/SBOX_NETWORKING_SUMMARY.md`, `docs/references/SBOX_OPEN_QUESTIONS.md`, `docs/references/SBOX_LIFECYCLE_SUMMARY.md` dans le vault Obsidian (chemin dans `CLAUDE.local.md`) — synthèses vérifiées contre l'API s&box installée.
- Constats empiriques (tests à 2 clients) documentés dans `CLAUDE.md` de `Kodoku_Legacy`, utilisés ici uniquement comme connaissance d'erreurs à éviter, pas comme modèle de code à copier — voir [../development/LEGACY_REFERENCE_POLICY.md](../development/LEGACY_REFERENCE_POLICY.md).
