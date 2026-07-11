# État actuel du projet

**Ce document ne liste que ce qui est réellement vérifié dans le projet à la date indiquée.** Rien ici n'est déduit d'une intention ou d'une roadmap — voir [ROADMAP.md](ROADMAP.md) pour ce qui est prévu mais pas fait.

Date de rédaction initiale : 2026-07-09. Vérifié par inspection directe du dépôt (`git log`, arborescence de fichiers) et de Claude Bridge en lecture seule (outil de développement utilisé à l'époque, voir mise à jour du 2026-07-11 ci-dessous).

**Mise à jour du 2026-07-09** (commit `97b27d1`, « Ajout de la scène réseau et du prefab joueur minimal ») : Claude Bridge n'était pas connecté au moment de cette vérification (éditeur s&box fermé) — inspection faite par lecture directe de `Assets/scenes/Tests/GameplayTest.scene` et `Assets/Prefabs/Players/kodoku_player.prefab`, plus `git log`/`git show --stat` pour confirmer le contenu du commit.

**Mise à jour du 2026-07-11** : Claude Bridge (`Libraries/sboxskinsgg.claudebridge/`) a été désinstallé — voir [CLAUDE.md](../../CLAUDE.md#compilation-et-tests). Aucun outil de vérification de compilation ou de lecture de logs externe n'est plus disponible depuis une session Claude Code ; toute confirmation de compilation ou de test passe désormais uniquement par l'éditeur s&box. Un premier composant Kodoku a été ajouté, `Code/Players/KodokuPlayerComponent.cs` (non commité), attaché à la racine de `Assets/Prefabs/Players/kodoku_player.prefab`, compilé et **validé par un test réel à deux instances** (host + client, logs applicatifs à l'appui, voir [TESTING_MULTIPLAYER.md](../development/TESTING_MULTIPLAYER.md)) : identification correcte du pawn local et des pawns proxy sur chaque instance. Le test a révélé et corrigé un cas non prévu — le pawn du host est possédé par lui dès sa création, sans transition détectable par `IGameObjectNetworkEvents.StartControl` seul (qui ne réagit qu'à un *changement* de contrôle) ; une vérification complémentaire dans `OnStart` a été nécessaire pour couvrir ce cas.

## Fonctionnel ou présent

- Nouveau projet s&box **Kodoku** créé (`kodoku.sbproj` : type `game`, org `local`, `MaxPlayers: 64`, `TickRate: 50`, `GameNetworkType: Multiplayer`).
- Nouveau dépôt GitHub configuré (`https://github.com/TechJosselin/Kodoku`, remote `origin` confirmé localement).
- Ancien projet conservé séparément comme `Kodoku_Legacy` (dépôt distinct, en lecture seule) — voir [../development/LEGACY_REFERENCE_POLICY.md](../development/LEGACY_REFERENCE_POLICY.md). Note : le remote Git local de `Kodoku_Legacy` pointe encore vers `github.com/TechJosselin/kodoku.git`, pas `kodoku_legacy` — écart non résolu, voir `CLAUDE.local.md`.
- Certains PNG de l'ancienne UI conservés sous `Assets/ui/` (commit `abd02d8`, « Ajout des assets UI conservés ») : icônes d'items (équipement, consommables), icônes de vitals (santé/faim/radiation/stamina/soif), fonds de panneaux d'inventaire, icônes de slots d'équipement. Aucun code (Razor/SCSS) ne les consomme actuellement.
- Fondation documentaire créée par une mission précédente (`CLAUDE.md`, `CLAUDE.local.md`, `.claude/rules/`, `docs/`).
- Le projet compilait sans erreur à la date de rédaction initiale (confirmé par inspection à l'époque, voir historique Git) — non re-vérifié depuis.
- **Une première scène de test multiplayer**, `Assets/scenes/Tests/GameplayTest.scene` (commit `97b27d1`), avec :
  - une hiérarchie structurée `_Systems` / `_Local` / `_World` / `_Debug`, cohérente avec [SCENE_ARCHITECTURE.md](../architecture/SCENE_ARCHITECTURE.md) ;
  - dans `_Systems` : un `NetworkGameManager` portant un `Sandbox.NetworkHelper` configuré avec `PlayerPrefab` → `kodoku_player.prefab` et `StartServer: true` ;
  - quatre points de spawn (`SpawnPoint1`–`4`, sous `_World/SpawnPoints`), référencés par le `NetworkHelper` ;
  - dans `_World` : environnement de base (sol, skybox), éclairage directionnel ;
  - plusieurs **placeholders vides**, préparés pour de futurs systèmes mais sans composant ni logique : `GameSession`, `PlayerSpawner`, `SaveManager`, `SceneRules` (dans `_Systems`), `LocalPlayerBootstrap` (dans `_Local`), `ExtractionPoints`, `Interactables`, `Enemies`, `Navigation` (dans `_World`).
- **Un prefab joueur minimal**, `Assets/Prefabs/Players/kodoku_player.prefab` (commit `97b27d1`), construit à partir de composants **stock s&box** : `Sandbox.PlayerController`, `Rigidbody`, modes de mouvement standards (`MoveModeWalk`/`Swim`/`Ladder`), `Dresser`, modèle `citizen.vmdl`. Un premier composant Kodoku, `Kodoku.Player.KodokuPlayerComponent` (identification du pawn local vs. proxy via `IsProxy`/`IGameObjectNetworkEvents`, référence au `PlayerController`, sans état de gameplay), a été ajouté à sa racine le 2026-07-11 — **non commité**, mais compilé et validé par un test réel à deux instances (voir mise à jour du 2026-07-11 ci-dessus).

## En cours / non validé

Ce qui suit **n'a pas encore** été vérifié par un test réel à deux instances — au-delà de l'identification pawn local/proxy validée le 2026-07-11 (voir ci-dessus) — et ne doit pas être considéré comme terminé — voir [ROADMAP.md](ROADMAP.md), étapes 3 à 5 :

- ownership du pawn au sens autorité de gameplay (au-delà de l'identification local/proxy — voir [MULTIPLAYER_ARCHITECTURE.md](../architecture/MULTIPLAYER_ARCHITECTURE.md)) ;
- late join au sens du timing engine non confirmé (ordre `OnAwake`/`OnStart`/`OnNetworkSpawn` vs. disponibilité des `[Sync]`) — `KodokuPlayerComponent` n'a aucune propriété `[Sync]` et ne renseigne donc pas cette question, voir [OPEN_QUESTIONS.md](OPEN_QUESTIONS.md) ;
- déconnexion et nettoyage des objets réseau ;
- reconnexion ;
- caméra locale définitive (voir section « Caméra » ci-dessous) ;
- séparation présentation/état réseau du pawn (voir [PLAYER_ARCHITECTURE.md](../architecture/PLAYER_ARCHITECTURE.md)) — l'identité (`KodokuPlayerComponent`) est faite et validée, le reste non ;
- logique de spawn personnalisée (`PlayerSpawner` est un placeholder vide) ;
- logique de session (`GameSession` est un placeholder vide) ;
- sauvegarde (`SaveManager` est un placeholder vide) ;
- règles de scène (`SceneRules` est un placeholder vide).

## Caméra — point de vigilance

La caméra est actuellement pilotée par le `Sandbox.PlayerController` **stock**, avec `UseCameraControls: true` : c'est ce composant, non une architecture Kodoku dédiée, qui prend la caméra principale de la scène (`_Local/Main Camera`, `IsMainCamera: true`) via le mécanisme natif du moteur. Cette solution est acceptable comme **prototype minimal uniquement**.

Avant de considérer l'étape « Caméra et présentation locale » comme avancée, elle devra être testée explicitement avec deux instances pour vérifier :

- qu'un client ne prend jamais la caméra de l'autre joueur ;
- que chaque client contrôle uniquement sa propre présentation ;
- que le timing d'ownership et `IsProxy` (risque documenté dans [OPEN_QUESTIONS.md](OPEN_QUESTIONS.md)) ne provoque pas de mauvaise sélection de caméra juste après le spawn.

La conception définitive de la caméra reste l'objectif de l'étape « Caméra et présentation locale » de la [ROADMAP.md](ROADMAP.md) et doit respecter [ADR-0003-LOCAL-PRESENTATION.md](../decisions/ADR-0003-LOCAL-PRESENTATION.md).

## Non implémenté

Aucune trace, même en placeholder, dans le projet actuel :

- Système d'items
- Inventaire
- Interaction (logique — le GameObject `Interactables` existe mais est un placeholder vide)
- Ennemis (logique — le GameObject `Enemies` existe mais est un placeholder vide)
- Combat
- Chargement de zones / extraction (logique — `ExtractionPoints` existe mais est un placeholder vide)

`Editor/` ne contient toujours que `Assembly.cs` (déclarations `global using` uniquement, aucune classe). `Code/` contient `Assembly.cs` et, depuis le 2026-07-11, `Code/Players/KodokuPlayerComponent.cs` (identification de pawn, sans état de gameplay — voir « Fonctionnel ou présent » ci-dessus). `Assets/scenes/` n'est plus vide (`Tests/GameplayTest.scene`), mais la scène de démarrage déclarée dans `kodoku.sbproj` (`scenes/minimal.scene`) n'existe toujours pas sur disque. `Assets/Prefabs/` n'est plus vide (`Players/kodoku_player.prefab`). `Assets/Data/`, `Assets/Materials/`, `Assets/Models/`, `Assets/Textures/` restent vides.

## Sur la sécurité technique

**La fondation documentaire créée par cette mission ne constitue pas, à elle seule, un système de sécurité technique.** Elle définit des règles et conventions à destination de Claude Code, mais ne les fait pas respecter mécaniquement.

Devront être configurés dans une **mission séparée**, non couverte par la présente mission documentaire :

- les permissions Claude Code ;
- les hooks `PreToolUse` ;
- les protections GitHub de la branche `main`.

Aucun de ces mécanismes n'a été créé pendant cette mission.

## Vault Obsidian

Recherche s&box existante dans le vault (`D:\Obsidian\Kodoku\01 Docs\`, chemin détaillé dans `CLAUDE.local.md`) : notes officielles sur le réseau/scène/composants s&box (vérifiées contre l'API installée), synthèses (`SBOX_NETWORKING_SUMMARY.md`, `SBOX_LIFECYCLE_SUMMARY.md`, `SBOX_OPEN_QUESTIONS.md`). Les sous-dossiers `01 Docs/architecture/` du vault (`NETWORKING.md`, `PLAYER_LIFECYCLE.md`) existent mais sont vides à ce jour — non utilisés comme source pour ce document.
