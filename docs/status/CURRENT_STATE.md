# État actuel du projet

**Ce document ne liste que ce qui est réellement vérifié dans le projet à la date indiquée.** Rien ici n'est déduit d'une intention ou d'une roadmap — voir [ROADMAP.md](ROADMAP.md) pour ce qui est prévu mais pas fait.

Date de rédaction initiale : 2026-07-09. Vérifié par inspection directe du dépôt (`git log`, arborescence de fichiers) et de Claude Bridge en lecture seule (outil de développement utilisé à l'époque, voir mise à jour du 2026-07-11 ci-dessous).

**Mise à jour du 2026-07-09** (commit `97b27d1`, « Ajout de la scène réseau et du prefab joueur minimal ») : Claude Bridge n'était pas connecté au moment de cette vérification (éditeur s&box fermé) — inspection faite par lecture directe de `Assets/scenes/Tests/GameplayTest.scene` et `Assets/Prefabs/Players/kodoku_player.prefab`, plus `git log`/`git show --stat` pour confirmer le contenu du commit.

**Mise à jour du 2026-07-11** : Claude Bridge (`Libraries/sboxskinsgg.claudebridge/`) a été désinstallé — voir [CLAUDE.md](../../CLAUDE.md#compilation-et-tests). Aucun outil de vérification de compilation ou de lecture de logs externe n'est plus disponible depuis une session Claude Code ; toute confirmation de compilation ou de test passe désormais uniquement par l'éditeur s&box. Un premier composant Kodoku a été ajouté, `Code/Players/KodokuPlayerComponent.cs`, attaché à la racine de `Assets/Prefabs/Players/kodoku_player.prefab`, compilé et **validé par un test réel à deux instances** (host + client, logs applicatifs à l'appui, voir [TESTING_MULTIPLAYER.md](../development/TESTING_MULTIPLAYER.md)) : identification correcte du pawn local et des pawns proxy sur chaque instance. Le test a révélé et corrigé un cas non prévu — le pawn du host est possédé par lui dès sa création, sans transition détectable par `IGameObjectNetworkEvents.StartControl` seul (qui ne réagit qu'à un *changement* de contrôle) ; une vérification complémentaire dans `OnStart` a été nécessaire pour couvrir ce cas. Résultat exact du test, sur chaque instance :

- **Host** : pawn du host → joueur local ; pawn du client → proxy distant ; `KodokuPlayerComponent.Local` → pawn du host.
- **Client** : pawn du client → joueur local ; pawn du host → proxy distant ; `KodokuPlayerComponent.Local` → pawn du client.

Committé (`7dbe487`, « feat(player): add multiplayer local player resolution & remove claude bridge definitely ») et poussé sur `feature/project-foundation`.

**Mise à jour du 2026-07-11 (suite)** : `PlayerVitalsComponent` (`Code/Players/Vitals/PlayerVitalsComponent.cs`, non commité) ajouté à la racine de `kodoku_player.prefab` — état vital réseau (`Health`/`Stamina`/`Hunger`/`Thirst`/`Radiation`, `[Sync(SyncFlags.FromHost)]`), méthodes de mutation **autoritaires sans attribut RPC** (pas un point d'entrée réseau — voir commentaire de classe). Un outil de test temporaire, `Code/Debug/PlayerVitalsDebugComponent.cs` (à retirer après validation), expose les seules commandes réseau actuelles (`[Rpc.Host(NetFlags.OwnerOnly)]`, montants fixes, déclenchables par touches U/I/O/P ou boutons inspecteur).

**Validé par test réel à trois instances** (host Jo, clients successifs Smithers/Moe/Nelson/Wiggum), logs applicatifs à l'appui :

- host modifie son propre pawn → répliqué correctement à tous les clients ;
- **client modifie son propre pawn** (`U`/`I`/`O`/`P` déclenchés depuis la fenêtre du client) → RPC `OwnerOnly` exécutée sur le host → répliqué correctement à toutes les instances ;
- tentative du host de déclencher une commande sur le pawn d'un client → rejetée silencieusement (aucun log, `NetFlags.OwnerOnly` bloque l'appel avant exécution) ;
- **late join** : un nouveau client (Nelson) rejoignant après que Jo a modifié `Health` a reçu la valeur courante (`90`) immédiatement, pas la valeur par défaut — les propriétés `[Sync(SyncFlags.FromHost)]` de ce composant étaient donc disponibles très tôt dans le cycle de vie côté late joiner (avant `OnStart`, qui log l'enregistrement proxy) ; ceci est une donnée empirique pour ce composant précis, pas une résolution générale de la question moteur ouverte dans [OPEN_QUESTIONS.md](OPEN_QUESTIONS.md) ;
- **déconnexion** : deux déconnexions client observées (Wiggum, Nelson), une seule exception présente à chaque fois (`TcpChannel`/`SocketException 10054`, bas niveau moteur, déjà présente avant toute vitals modifiée — non liée à ce code), aucune exception `PlayerVitalsComponent`/`KodokuPlayerComponent`/`VitalsChanged`, vitals du host inchangées après le départ du client.

Non testé : reconnexion, valeurs extrêmes/négatives fournies en amont d'un futur système gameplay (le clamp lui-même est couvert par le code, pas par un test réseau dédié).

**Mise à jour du 2026-07-11 (HUD)** : `Code/UI/Hud/GameHud.razor` + `GameHud.razor.scss` (non commités) — HUD local minimal affichant `Health`/`Stamina`/`Hunger`/`Thirst`/`Radiation` du pawn local (`KodokuPlayerComponent.Local?.PlayerVitals`, jamais mis en cache, jamais de recherche arbitraire du « premier joueur »). Ajouté dans `Assets/scenes/Tests/GameplayTest.scene` sous `_Local/UI/GameHud` (`Sandbox.UI.ScreenPanel` + `Kodoku.UI.GameHud`, strictement local — pas de `[Sync]`, aucune autorité de gameplay). **Validé par test réel** (host + client, pawn contrôlable, valeurs modifiées via `PlayerVitalsDebugComponent`) : chaque instance n'affiche que ses propres vitals, mise à jour en temps réel.

Deux bugs réels trouvés et corrigés pendant ce test :
- `KodokuPlayerComponent.PlayerVitals` était résolu en `OnAwake`, un point du cycle de vie qui peut s'exécuter avant que les composants suivants du même GameObject soient pleinement prêts — déplacé vers `OnStart` (exécuté après l'`OnAwake` de tous les composants du GameObject). Ce n'était en fait pas la cause du bug observé (log de diagnostic à l'appui : `PlayerVitals` résolvait déjà correctement), mais c'est une correction de fond légitime, conservée.
- **Cause réelle** : un panneau Razor s&box ne reconstruit son contenu que si `BuildHash()` change de valeur — sans le surcharger, le premier rendu (fait avant qu'un pawn local existe) restait figé pour toujours. Corrigé en surchargeant `BuildHash()` avec les valeurs de vitals courantes et l'identité de `KodokuPlayerComponent.Local` (pour couvrir aussi un changement de pawn local dont les vitals coïncideraient par hasard avec les précédentes).

**Mise à jour du 2026-07-11 (nettoyage du jalon)** : le jalon « Networked Player Vitals + Local HUD » est considéré **terminé pour son périmètre** :

- `KodokuPlayerComponent` → terminé et validé host/client (identité, résolution du pawn local/proxy).
- `PlayerVitalsComponent` → terminé et validé host/client — valeurs synchronisées depuis le host (`[Sync(SyncFlags.FromHost)]`), mutations de production (`TakeDamage`/`Heal`/`ConsumeStamina`/`RestoreStamina`/`SetHunger`/`SetThirst`/`SetRadiation`/`ResetVitals`) **non exposées librement par RPC** — ce sont des appliqueurs autoritaires, pas un point d'entrée réseau. `ResetVitals` conservée (primitive utile à un futur respawn), documentée comme telle, toujours sans RPC.
- `GameHud` → terminé pour sa version minimale, strictement local (pas de `[Sync]`, aucune autorité de gameplay), relié uniquement à `KodokuPlayerComponent.Local?.PlayerVitals`, mise à jour en temps réel validée par test réel.
- `PlayerVitalsDebugComponent` (`Code/Debug/PlayerVitalsDebugComponent.cs`) → **retiré** du dépôt et de la racine de `kodoku_player.prefab` après validation de la réplication qu'il servait à tester. Les touches U/I/O/P et les boutons inspecteur associés n'existent plus.

## Fonctionnel ou présent

- Nouveau projet s&box **Kodoku** créé (`kodoku.sbproj` : type `game`, org `local`, `MaxPlayers: 4`, `TickRate: 50`, `GameNetworkType: Multiplayer`).
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
- **Un prefab joueur minimal**, `Assets/Prefabs/Players/kodoku_player.prefab` (commit `97b27d1`), construit à partir de composants **stock s&box** : `Sandbox.PlayerController`, `Rigidbody`, modes de mouvement standards (`MoveModeWalk`/`Swim`/`Ladder`), `Dresser`, modèle `citizen.vmdl`. Deux composants Kodoku à sa racine : `Kodoku.Player.KodokuPlayerComponent` (identification du pawn local vs. proxy, référence au `PlayerController` et à `PlayerVitalsComponent`, sans état de gameplay — committé, `7dbe487`) et `Kodoku.Player.Vitals.PlayerVitalsComponent` (état vital réseau host-authoritative). Les deux terminés pour leur périmètre et validés par test réel à plusieurs instances (voir mises à jour ci-dessus). Le composant de debug temporaire qui a servi à ces tests (`PlayerVitalsDebugComponent`) a été retiré du prefab.
- **Un HUD local minimal**, `Code/UI/Hud/GameHud.razor`/`.scss`, affichant les vitals du pawn local — voir mise à jour du 2026-07-11 (HUD) ci-dessus. Premier code UI (Razor/SCSS) du projet. Terminé pour sa version minimale.

## En cours / non validé

Ce qui suit **n'a pas encore** été vérifié par un test réel — au-delà de l'identification pawn local/proxy et de la réplication/autorité des vitals (host+client+late join+déconnexion), validées le 2026-07-11 (voir ci-dessus) — et ne doit pas être considéré comme terminé — voir [ROADMAP.md](ROADMAP.md), étapes 3 à 5 :

- ownership du pawn au sens autorité de gameplay pour tout ce qui n'est pas encore les vitals (inventaire, progression — voir [MULTIPLAYER_ARCHITECTURE.md](../architecture/MULTIPLAYER_ARCHITECTURE.md)) ;
- late join et déconnexion pour tout composant réseau futur autre que `KodokuPlayerComponent`/`PlayerVitalsComponent` — le test du 2026-07-11 est une donnée empirique pour ces deux composants précis, pas une résolution générale de la question moteur ouverte dans [OPEN_QUESTIONS.md](OPEN_QUESTIONS.md) ;
- reconnexion (aucun composant Kodoku ne l'a testée à ce jour) ;
- caméra locale définitive (voir section « Caméra » ci-dessous) ;
- séparation présentation/état réseau du pawn (voir [PLAYER_ARCHITECTURE.md](../architecture/PLAYER_ARCHITECTURE.md)) — l'identité (`KodokuPlayerComponent`) et un premier état de gameplay (`PlayerVitalsComponent`) sont faits et validés, le reste (apparence, animations, mort/respawn) non ;
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

## Items et points de loot

**Mise à jour du 2026-07-12** : une première fondation d'items existe et est validée par tests réels à deux instances — voir [ITEM_ARCHITECTURE.md](../architecture/ITEM_ARCHITECTURE.md) pour le détail complet (V1/V2, tests A à G, éléments encore ouverts) :

- `Code/Items/Definitions/ItemDefinition.cs` (+ `ItemCategory.cs`/`ItemTags.cs`) — ressource `.item` (`GameResource`), données statiques.
- `Code/Items/Instances/ItemInstance.cs` — état runtime minimal (`InstanceId`/`Definition`/`Quantity`), classe C# pure.
- `Code/Items/World/WorldItemComponent.cs` — représentation monde, réplication réseau host-authoritative minimale, validée (Tests A à E).
- `Code/Items/Loot/LootSpawnPointComponent.cs` — génération de loot host-authoritative, V1 mono-item, validée (Tests A à G).
- `Assets/Data/Items/Consumables/Drinks/water_bottle.item` et `Assets/Prefabs/Items/Consumables/Drinks/water_bottle.prefab` — première ressource/prefab concrets.
- Trois `LootSpawnPointComponent` installés manuellement dans `Assets/scenes/Tests/GameplayTest.scene`.

**Reste non implémenté** : inventaire joueur, pickup interactif, conteneurs, équipement, consommation, armes, persistance.

**Découverte du 2026-07-12** : un système natif d'inventaire/armes existe dans le moteur s&box (`Sandbox.BaseInventoryComponent`/`BaseInventoryItem`/`BaseCombatWeapon`), non utilisé par Kodoku à ce jour, étudié dans [docs/research/SBOX_BUILTIN_INVENTORY_EVALUATION.md](../research/SBOX_BUILTIN_INVENTORY_EVALUATION.md). Aucune décision d'adoption n'est prise.

## Non implémenté

Aucune trace, même en placeholder, dans le projet actuel :

- Inventaire joueur, pickup interactif, conteneurs, équipement, consommation d'objets
- Interaction (logique — le GameObject `Interactables` existe mais est un placeholder vide)
- Ennemis (logique — le GameObject `Enemies` existe mais est un placeholder vide)
- Combat, armes
- Chargement de zones / extraction (logique — `ExtractionPoints` existe mais est un placeholder vide)

`Editor/` ne contient toujours que `Assembly.cs` (déclarations `global using` uniquement, aucune classe). `Code/` contient `Assembly.cs`, `Code/Players/KodokuPlayerComponent.cs`, `Code/Players/Vitals/PlayerVitalsComponent.cs`, `Code/UI/Hud/GameHud.razor`/`.scss` et, depuis le 2026-07-12, `Code/Items/` (`Definitions/`, `Instances/`, `World/`, `Loot/` — voir « Items et points de loot » ci-dessus). `Code/Debug/` n'existe plus (composants de debug temporaires retirés après validation). `Assets/scenes/` n'est plus vide (`Tests/GameplayTest.scene`), mais la scène de démarrage déclarée dans `kodoku.sbproj` (`scenes/minimal.scene`) n'existe toujours pas sur disque. `Assets/Prefabs/` contient `Players/kodoku_player.prefab` et `Items/Consumables/Drinks/water_bottle.prefab`. `Assets/Data/` contient `Items/Consumables/Drinks/water_bottle.item` — n'est plus vide. `Assets/Materials/`, `Assets/Models/`, `Assets/Textures/` restent vides.

## Sur la sécurité technique

**La fondation documentaire créée par cette mission ne constitue pas, à elle seule, un système de sécurité technique.** Elle définit des règles et conventions à destination de Claude Code, mais ne les fait pas respecter mécaniquement.

Devront être configurés dans une **mission séparée**, non couverte par la présente mission documentaire :

- les permissions Claude Code ;
- les hooks `PreToolUse` ;
- les protections GitHub de la branche `main`.

Aucun de ces mécanismes n'a été créé pendant cette mission.

## Vault Obsidian

Recherche s&box existante dans le vault (`D:\Obsidian\Kodoku\01 Docs\`, chemin détaillé dans `CLAUDE.local.md`) : notes officielles sur le réseau/scène/composants s&box (vérifiées contre l'API installée), synthèses (`SBOX_NETWORKING_SUMMARY.md`, `SBOX_LIFECYCLE_SUMMARY.md`, `SBOX_OPEN_QUESTIONS.md`). Les sous-dossiers `01 Docs/architecture/` du vault (`NETWORKING.md`, `PLAYER_LIFECYCLE.md`) existent mais sont vides à ce jour — non utilisés comme source pour ce document.
