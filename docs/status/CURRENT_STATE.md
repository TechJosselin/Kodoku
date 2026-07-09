# État actuel du projet

**Ce document ne liste que ce qui est réellement vérifié dans le projet à la date indiquée.** Rien ici n'est déduit d'une intention ou d'une roadmap — voir [ROADMAP.md](ROADMAP.md) pour ce qui est prévu mais pas fait.

Date de rédaction initiale : 2026-07-09. Vérifié par inspection directe du dépôt (`git log`, arborescence de fichiers) et de Claude Bridge en lecture seule (`get_project_info`, `get_scene_hierarchy`, `get_compile_errors`).

**Mise à jour du 2026-07-09** (commit `97b27d1`, « Ajout de la scène réseau et du prefab joueur minimal ») : Claude Bridge n'était pas connecté au moment de cette vérification (éditeur s&box fermé) — inspection faite par lecture directe de `Assets/scenes/Tests/GameplayTest.scene` et `Assets/Prefabs/Players/kodoku_player.prefab`, plus `git log`/`git show --stat` pour confirmer le contenu du commit.

## Fonctionnel ou présent

- Nouveau projet s&box **Kodoku** créé (`kodoku.sbproj` : type `game`, org `local`, `MaxPlayers: 64`, `TickRate: 50`, `GameNetworkType: Multiplayer`).
- Nouveau dépôt GitHub configuré (`https://github.com/TechJosselin/Kodoku`, remote `origin` confirmé localement).
- Ancien projet conservé séparément comme `Kodoku_Legacy` (dépôt distinct, en lecture seule) — voir [../development/LEGACY_REFERENCE_POLICY.md](../development/LEGACY_REFERENCE_POLICY.md). Note : le remote Git local de `Kodoku_Legacy` pointe encore vers `github.com/TechJosselin/kodoku.git`, pas `kodoku_legacy` — écart non résolu, voir `CLAUDE.local.md`.
- Certains PNG de l'ancienne UI conservés sous `Assets/ui/` (commit `abd02d8`, « Ajout des assets UI conservés ») : icônes d'items (équipement, consommables), icônes de vitals (santé/faim/radiation/stamina/soif), fonds de panneaux d'inventaire, icônes de slots d'équipement. Aucun code (Razor/SCSS) ne les consomme actuellement.
- Claude Bridge (`Libraries/sboxskinsgg.claudebridge/`) installé comme Library de développement, visible dans `kodoku.slnx` et connecté (confirmé via `get_bridge_status`) — outil de développement uniquement, voir [../../.claude/rules/sbox-bridge.md](../../.claude/rules/sbox-bridge.md).
- Fondation documentaire créée par une mission précédente (`CLAUDE.md`, `CLAUDE.local.md`, `.claude/rules/`, `docs/`).
- Le projet compilait sans erreur à la date de rédaction initiale (confirmé via `get_compile_errors`) — non re-vérifié pour cette mise à jour (Claude Bridge déconnecté, voir ci-dessus).
- **Une première scène de test multiplayer**, `Assets/scenes/Tests/GameplayTest.scene` (commit `97b27d1`), avec :
  - une hiérarchie structurée `_Systems` / `_Local` / `_World` / `_Debug`, cohérente avec [SCENE_ARCHITECTURE.md](../architecture/SCENE_ARCHITECTURE.md) ;
  - dans `_Systems` : un `NetworkGameManager` portant un `Sandbox.NetworkHelper` configuré avec `PlayerPrefab` → `kodoku_player.prefab` et `StartServer: true` ;
  - quatre points de spawn (`SpawnPoint1`–`4`, sous `_World/SpawnPoints`), référencés par le `NetworkHelper` ;
  - dans `_World` : environnement de base (sol, skybox), éclairage directionnel ;
  - plusieurs **placeholders vides**, préparés pour de futurs systèmes mais sans composant ni logique : `GameSession`, `PlayerSpawner`, `SaveManager`, `SceneRules` (dans `_Systems`), `LocalPlayerBootstrap` (dans `_Local`), `ExtractionPoints`, `Interactables`, `Enemies`, `Navigation` (dans `_World`).
- **Un prefab joueur minimal**, `Assets/Prefabs/Players/kodoku_player.prefab` (commit `97b27d1`), construit uniquement à partir de composants **stock s&box** : `Sandbox.PlayerController`, `Rigidbody`, modes de mouvement standards (`MoveModeWalk`/`Swim`/`Ladder`), `Dresser`, modèle `citizen.vmdl`. **Aucun composant Kodoku personnalisé n'existe encore** sur ce prefab.

## En cours / non validé

Ce qui suit a un premier passage présent dans la scène/le prefab ci-dessus, mais **n'a pas été vérifié par un test réel** (Claude Bridge déconnecté au moment de cette mise à jour) et ne doit pas être considéré comme terminé — voir [ROADMAP.md](ROADMAP.md), étapes 3 à 5 :

- validation complète avec deux instances (host + client) ;
- ownership du pawn ;
- visibilité réciproque host/client ;
- late join ;
- déconnexion et nettoyage des objets réseau ;
- reconnexion ;
- caméra locale définitive (voir section « Caméra » ci-dessous) ;
- composants joueur spécifiques à Kodoku (identité, séparation présentation/état réseau — voir [PLAYER_ARCHITECTURE.md](../architecture/PLAYER_ARCHITECTURE.md)) ;
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

`Code/` et `Editor/` ne contiennent chacun toujours que `Assembly.cs` (déclarations `global using` uniquement, aucune classe) — confirmé, aucun nouveau fichier `.cs` n'a été ajouté par le commit `97b27d1`. `Assets/scenes/` n'est plus vide (`Tests/GameplayTest.scene`), mais la scène de démarrage déclarée dans `kodoku.sbproj` (`scenes/minimal.scene`) n'existe toujours pas sur disque. `Assets/Prefabs/` n'est plus vide (`Players/kodoku_player.prefab`). `Assets/Data/`, `Assets/Materials/`, `Assets/Models/`, `Assets/Textures/` restent vides.

## Sur la sécurité technique

**La fondation documentaire créée par cette mission ne constitue pas, à elle seule, un système de sécurité technique.** Elle définit des règles et conventions à destination de Claude Code, mais ne les fait pas respecter mécaniquement.

Devront être configurés dans une **mission séparée**, non couverte par la présente mission documentaire :

- les permissions Claude Code ;
- les hooks `PreToolUse` ;
- les protections GitHub de la branche `main`.

Aucun de ces mécanismes n'a été créé pendant cette mission.

## Vault Obsidian

Recherche s&box existante dans le vault (`D:\Obsidian\Kodoku\01 Docs\`, chemin détaillé dans `CLAUDE.local.md`) : notes officielles sur le réseau/scène/composants s&box (vérifiées contre l'API installée), synthèses (`SBOX_NETWORKING_SUMMARY.md`, `SBOX_LIFECYCLE_SUMMARY.md`, `SBOX_OPEN_QUESTIONS.md`). Les sous-dossiers `01 Docs/architecture/` du vault (`NETWORKING.md`, `PLAYER_LIFECYCLE.md`) existent mais sont vides à ce jour — non utilisés comme source pour ce document.
