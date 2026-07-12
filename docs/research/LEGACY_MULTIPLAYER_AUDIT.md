# Audit de l'ancien Kodoku (`kodoku_legacy`, branche `Multiplayer`)

**Nature de ce document : recherche/inventaire, pas une architecture visée.** Il documente ce qui existait dans l'ancien projet et compare avec l'état réel du nouveau projet à la date de rédaction. Aucune décision d'implémentation n'est prise ici — voir la section 14 pour les points qui restent à trancher par l'utilisateur.

- **Dépôt audité** : `https://github.com/TechJosselin/Kodoku_Legacy.git`, branche `Multiplayer`, commit `ff4df9c` ("Fix late-joiner player invisibility via local-only RemotePlayerVisualProxySystem").
- **Méthode** : clone isolé dans `../kodoku_legacy_audit` (hors du dépôt de travail, jamais modifié), lecture intégrale de `Code/`, `UnitTests/`, `Docs/`, `CLAUDE.md`/`IA.md` (identiques), et des scènes/prefabs JSON. Un graphe de connaissances (`graphify-out/`) préexistait dans ce dépôt (généré le 2026-07-04) et a servi de carte d'orientation initiale, mais toutes les affirmations de ce rapport sont vérifiées contre le code réel, pas contre le graphe seul.
- **Projet actuel comparé** : `TechJosselin/Kodoku`, branche `feature/project-foundation`, commit `468a58f`.
- **Aucun fichier de `kodoku_legacy` n'a été modifié. Aucune fusion, aucun commit, aucune implémentation n'a été effectuée pendant cette mission.**

---

## 1. Résumé exécutif

L'ancien Kodoku (branche `Multiplayer`) est un jeu de survie/inventaire à grille, bâti sur une base solo puis partiellement adapté au coop. Le socle réseau (identité du pawn local, ownership, caméra locale non-répliquée, visuel des joueurs distants) a été durci par plusieurs cycles de bugs réels documentés en détail dans `CLAUDE.md` — c'est la partie la plus mature et la plus directement réutilisable **comme référence de conception**, déjà largement reconstruite et validée dans le nouveau projet.

**Constat central du reste du projet : à l'exception du socle réseau (Player/Camera/Scene), aucun système de gameplay de l'ancien Kodoku n'est répliqué.** Inventaire, items, équipement, vitals, interaction, menu — tout est écrit comme si un seul joueur existait : pas de `[Sync]`, pas de `[Rpc]`, pas d'autorité host. Un `InventoryComponent` de proxy montrerait un état local vide/divergent, pas les données réelles du propriétaire. C'est cohérent avec l'avertissement du propre `CLAUDE.md` de l'ancien projet ("préparation coop, avant tout réseau réel") mais signifie que **rien dans `Code/Inventory/`, `Code/Items/`, `Code/Loadout/`, `Code/Interaction/`, `Code/GameMenu/`, `Code/UI/` (hors Player/Camera) ne peut être copié tel quel** : chaque système doit être re-conçu avec une autorité réseau explicite, pas juste porté.

Point positif déjà acquis dans le nouveau projet : `PlayerVitalsComponent` actuel est **déjà** host-authoritative (`[Sync(SyncFlags.FromHost)]`), alors que l'équivalent legacy ne l'était pas du tout — la leçon a déjà été appliquée une fois avant même cet audit.

15 systèmes fonctionnels distincts ont été identifiés (section 3). 3 sont déjà reconstruits et validés dans le nouveau projet (fondation réseau, identité joueur, vitals réseau + HUD minimal). 1 est explicitement reporté (stamina liée au sprint). Les 11 autres sont absents et nécessitent une reconception, pas un portage.

---

## 2. Structure de l'ancien projet

```text
kodoku_legacy (branche Multiplayer)
├── Code/                        — assembly unique "kodoku" (voir note d'historique ci-dessous)
│   ├── AssetPaths/              — Kodoku.Lib.AssetPaths
│   ├── Core/                    — Kodoku.Core (SceneLoaderComponent)
│   ├── GameMenu/                — Kodoku.Lib.GameMenu (état C#, pas l'UI)
│   ├── Gameplay/, Glue/         — Kodoku.Glue (bridges spécifiques au jeu)
│   ├── Interaction/             — Kodoku.Lib.Interaction
│   ├── Inventory/               — Kodoku.Lib.Inventory
│   ├── Items/                   — Kodoku.Lib.Items
│   ├── Loadout/                 — Kodoku.Lib.Loadout
│   ├── Player/                  — Kodoku.Lib.Player
│   ├── UI/                      — Kodoku.Lib.UI (Razor)
│   ├── Vitals/                  — Kodoku.Lib.Vitals
│   └── World/                   — Kodoku.World (WorldRootComponent)
├── UnitTests/                   — MSTest, référence Code/kodoku.csproj directement
├── Assets/
│   ├── scenes/                  — Kodoku.scene, Kodoku_CoopTest.scene, World/{Base,Exploration,Shop}.scene, Tests/TestScene.scene
│   ├── Prefabs/                 — Player/PlayerController.prefab, Items/*, Containers/Wardrobe/*
│   ├── Data/Items/               — ressources `.item` (ItemDefinition)
│   └── UI/, Materials/, Models/, Textures/, Fonts/
├── Docs/EditorComponentPlacement.md  — guide de câblage scène (en français)
├── CLAUDE.md / IA.md             — identiques ; CLAUDE.md fait référence
└── graphify-out/                 — graphe de connaissances préexistant (2026-07-04)
```

**Note d'historique interne au projet legacy** (documentée dans son propre `CLAUDE.md`) : jusqu'au 2026-07-05, `Code/` et une bibliothèque séparée `Libraries/KodokuLib/` coexistaient (frontière de dépendances stricte, liaison via `Code/Glue/`). La bibliothèque a été supprimée et fusionnée directement dans `Code/` — les namespaces `Kodoku.Lib.*` ont été conservés tels quels (pas renommés) pour limiter le churn. Cela n'affecte pas cet audit (le code et les responsabilités sont identiques), mais explique pourquoi les namespaces ne correspondent pas à la disposition physique des dossiers de premier niveau.

**Aucune fonctionnalité de sauvegarde/persistance, de mort/respawn, de combat, d'IA ou de gestion de zones/extraction n'existe dans ce code** — seulement des placeholders documentaires ou rien du tout. Voir section 3.

---

## 3. Inventaire complet des systèmes

Pour chaque système : objectif, fichiers (chemins relatifs à la racine du dépôt legacy sauf mention contraire), scènes/prefabs concernés, dépendances, état réseau, problèmes observés, valeur à conserver, éléments à abandonner.

### 3.1 Fondation réseau (Player / Camera / Scène)

**Objectif fonctionnel** : identifier le pawn local vs les proxies, garantir qu'une seule caméra de rendu existe par client et qu'elle n'est jamais volée/gelée par un autre client, faire apparaître visuellement les joueurs distants malgré un bug de réplication de renderer.

**Fichiers** :
- `Code/Player/KodokuPlayerComponent.cs` — ancre centrale : résolution `Local` (ownership-based via `OwnerConnection`/`Connection.Local`, avec repli sur `GameObject.Network.IsOwner` pendant la fenêtre où `OwnerConnection` n'est pas encore arrivé), événement statique `LocalChanged`, méthodes `FindOwning(GameObject)` (résolution par ancêtre) et `IsOwnedByLocalPlayer(GameObject)` (garde anti-double-exécution pour les composants qui existent une fois par pawn, local ET par proxy), enforcement idempotent par frame de l'état caméra/contrôleur (`EnforcePawnCameraNeverMain`, `EnforceLocalPlayerControllerFlags`).
- `Code/Player/LocalPlayerCameraComponent.cs` — la seule caméra de rendu réelle par client, créée via `Scene.CreateObject()` + `NetworkMode.Never` (jamais network-spawned, donc jamais répliquée). `Sandbox.PlayerController` n'a **aucun champ caméra** (confirmé par réflexion/décompilation) : il pilote simplement la caméra courante `IsMainCamera=true`.
- `Code/Player/RemotePlayerVisualProxySystem.cs` (dernier commit de la branche) — contourne un bug confirmé : le `SkinnedModelRenderer` networké d'un pawn distant n'est jamais rendu chez un client qui le reçoit par snapshot/rattrapage (bien que toutes les propriétés inspectées soient correctes). Solution : chaque client construit son propre "double visuel" local (non networké) par joueur distant, suit sa position/rotation chaque frame via `PlayerController.UpdateAnimation(renderer)`.
- `Code/Core/SceneLoaderComponent.cs` — charge le monde localement uniquement côté host (`Networking.IsActive && !Networking.IsHost` → skip) ; le monde arrive par réplication chez les clients.
- `Code/World/WorldRootComponent.cs` — marqueur `WorldId` simple, sans logique.

**Scènes/prefabs** : `Assets/Prefabs/Player/PlayerController.prefab` (ordre des composants **load-bearing** : `KodokuPlayerComponent` doit précéder `Sandbox.PlayerController` dans le tableau `Components` — l'exécution `OnStart`/`OnUpdate` suit l'ordre du tableau sérialisé, pas le type ni l'ordre déclaratif) ; `Assets/scenes/Kodoku.scene` (solo, `NetworkHelper.StartServer=false`) ; `Assets/scenes/Kodoku_CoopTest.scene` (copie dédiée coop, `StartServer=true`, `PlayerPrefab` câblé, le "Player Controller" statique désactivé).

**Dépendances entrantes** : tous les autres systèmes (Inventory, Vitals, Interaction, UI) résolvent leur propriétaire via `KodokuPlayerComponent.FindOwning`/`.Local`.
**Dépendances sortantes** : aucune (c'est la fondation).

**État réseau** : le seul système réellement pensé réseau de tout le projet. `KodokuPlayerComponent` implémente `Component.INetworkSpawn`. Testé à 2 clients réels (host+joiner) selon `CLAUDE.md` et `Docs/EditorComponentPlacement.md`.

**Problèmes architecturaux observés** (7 règles "load-bearing" documentées après plusieurs régressions réelles, voir `CLAUDE.md` § Camera Ownership) :
1. Ordre des composants dans le prefab non protégé par le compilateur — une réorganisation manuelle dans l'éditeur peut silencieusement réintroduire un vol de caméra.
2. `CameraComponent.Priority` empile le rendu indépendamment de `IsMainCamera` — une caméra de proxy simplement activée peut couvrir tout l'écran même si elle n'est pas "principale".
3. Écrire `Enabled` sur une caméra networkée réplique cet état vers son propriétaire réel (bug vécu : désactiver la caméra "vue comme proxy" a noirci l'écran du vrai propriétaire).
4. `GameObject.IsProxy` **retarde** par rapport à la propagation réelle de l'ownership juste après le spawn — ne jamais s'y fier comme critère primaire.
5. Un `[Property]` simple (pas `[Sync]`) sur un composant networké se réplique quand même (bug vécu : `IsMainCamera=true` posé côté joiner sur son propre pawn s'est répliqué vers l'hôte).
6. `ScreenPanel.TargetCamera` ne doit jamais être câblé par GUID vers une caméra de scène spécifique.
7. Risque résiduel non vérifié : `HideBodyInFirstPerson` pourrait dépendre de la caméra effectivement active.

**Valeur à conserver** : ces 7 règles sont directement transposables — elles décrivent un comportement du moteur s&box (pas du code métier), déjà partiellement pertinent pour le nouveau projet (voir section 9, risque R1). Le pattern `FindOwning`/`Local`/`LocalChanged`/`IsOwnedByLocalPlayer` est une bonne référence conceptuelle pour toute future extension de `KodokuPlayerComponent`.
**Éléments à abandonner** : les repères legacy (`LegacyFindFirst*InScene`, `CanUseLegacySceneFallback`) sont explicitement documentés comme dette temporaire dans l'ancien projet lui-même — ne pas les reproduire, le nouveau projet n'a jamais eu cette dette.

---

### 3.2 Vitals (santé/endurance/faim/soif/folie)

**Objectif** : 5 statistiques de jauge (`Health`, `Stamina`, `Hunger`, `Thirst`, `Madness`).

**Fichiers** : `Code/Vitals/PlayerVitalsComponent.cs`, `Code/Vitals/VitalStat.cs` (classe pure, `Current`/`Max`/`Normalized`, `Set`/`Add`/`Remove` avec clamp), `Code/Vitals/VitalStatKind.cs` (enum).

**Dépendances entrantes** : `InventoryComponent.TryUseItem()` appelle `Vitals.ApplyItemUseEffects(...)` avec les deltas de l'`ItemDefinition` consommée.
**Dépendances sortantes** : aucune.

**État réseau : AUCUN.** Pas de `[Sync]`. `PlayerVitalsComponent.OverrideWithDebugValues` (désactivé par défaut) réapplique des sliders de debug chaque frame si activé — mécanisme de test, pas de gameplay. Chaque client aurait donc ses propres valeurs locales non partagées.

**Problèmes observés** : aucune autorité définie ; `ApplyDebugValues()` s'exécute une fois dans `OnStart` (donc chaque client réinitialise ses propres vitals localement à la connexion).

**Valeur à conserver** : la séparation `VitalStat` (classe de calcul pure, testée unitairement dans `UnitTests/Vitals/VitalStatTests.cs`) vs `PlayerVitalsComponent` (composant qui l'expose) est un bon pattern de conception.
**Comparaison avec le nouveau projet** : déjà surclassé. `Code/Players/Vitals/PlayerVitalsComponent.cs` actuel a 5 stats (Health/Stamina/Hunger/Thirst/**Radiation**, pas Madness), est `[Sync(SyncFlags.FromHost)]`, avec des méthodes de mutation autoritaires (`TakeDamage`, `Heal`, `ConsumeStamina`, etc.) déjà pensées pour l'autorité host — voir section 6. **Ne pas régresser vers le modèle non-réseauté de l'ancien projet.**

---

### 3.3 Items (définitions et instances)

**Objectif** : séparer la donnée statique (`ItemDefinition`, ressource `.item`/`GameResource`) de l'instance runtime (`ItemInstance`, avec `InstanceId` stable via `Guid.NewGuid()`).

**Fichiers** : `Code/Items/ItemDefinition.cs`, `Code/Items/ItemInstance.cs`, `Code/Items/ItemEnums.cs` (`InventoryItemKind` : Simple/Backpack/Headwear/GasMask/BodyArmor/TacticalRig/Weapon/Special/Pants/Footwear ; `InventoryEquipmentSlot` : 9 emplacements).

`ItemDefinition` porte : identité (`ItemId`, `DisplayName`), présentation (`IconPath`/`ModelPath`/`PrefabPath`), dimensions grille (`Width`/`Height`/`CanRotate`), empilage (`IsStackable`/`MaxStack`), poids, stockage interne (`StorageWidth`/`StorageHeight`, capé à 6, `CreatesContainer` calculé), et effets d'usage (`IsUsable`/`ConsumeOnUse`/`UseQuantity` + 5 deltas de vitals).

**Scènes/prefabs concernés** : `Assets/Data/Items/**/*.item`, `Assets/Prefabs/Items/**` (ex. `Consumables/Drinks/WaterBottle/water_bottle.prefab`, `Consumables/Medical/Bandage.prefab`, `Equipment/Backpacks/raider_backpack.prefab`, `Equipment/Weapons/Ranged/shotgun.prefab`) — tous composés uniquement de `WorldItemComponent` + collider + `ModelRenderer`(+`Rigidbody` sauf le sac à dos).

**Dépendances entrantes** : Inventory, Loadout, Interaction, GameMenu (UI), AssetPaths.
**Dépendances sortantes** : aucune (couche la plus basse du domaine gameplay).

**État réseau : aucun.** `ItemInstance` est une classe C# pure, jamais synchronisée. Une création/destruction d'`ItemInstance` côté client resterait purement locale.

**Problèmes observés** : `ItemDefinition.CanEquipTo` encode un mapping figé `ItemKind → Slot` en dur (switch), pas extensible sans recompilation ; aucune identité stable pour l'`ItemDefinition` elle-même au-delà du chemin de ressource (`GetStableId()` retombe sur `DisplayName` si `ItemId` est vide — fragile).

**Valeur à conserver** : la séparation Definition/Instance et le calcul de stack/rotation (`GetWidth`/`GetHeight` selon rotation) sont un socle correct pour l'architecture déjà actée dans `docs/architecture/ITEM_ARCHITECTURE.md` du nouveau projet, qui prévoit explicitement la même séparation.
**À abandonner** : le contenu exact des ressources `.item` ne doit pas être recopié — `ITEM_ARCHITECTURE.md` le dit déjà explicitement ("recréées, pas réutilisées").

---

### 3.4 Inventaire (grille, conteneurs, transferts)

**Objectif** : stockage à grille avec empilement, découpage de stack, et trois types de conteneurs (Pockets/Backpack/Loot) traités par le même mécanisme.

**Fichiers** :
- `Code/Inventory/InventoryContainer.cs` — classe pure (pas un `Component`) : `CanAddItemAt`/`TryAddItem`/`TryAddItemAt`/`TryMoveItem`/`TryRemoveItem`/`TrySplitItem`, détection de chevauchement de rectangles, plan d'empilement (`BuildStackPlan`) avant placement.
- `Code/Inventory/InventoryComponent.cs` — **god node du projet (50 arêtes dans le graphe préexistant)** : possède le conteneur `pockets`, délègue à `LoadoutComponent`/`HotbarComponent`, résout `Vitals` via `KodokuPlayerComponent.FindOwning` avec repli legacy scanné-scène si non-networké. Orchestre équipement automatique, drop, pickup, split, usage d'item.
- `Code/Inventory/InventoryItemPlacement.cs`, `InventoryEnums.cs` (`InventoryContainerKind`), `InventoryActionResult.cs` (struct `Success`+`Reason`, pattern utilisé partout dans le domaine).
- `Code/Inventory/HotbarComponent.cs` — 8 slots fixes, touches numériques, gate `KodokuPlayerComponent.IsOwnedByLocalPlayer` pour éviter qu'un proxy ne lise les touches locales.
- `Code/Inventory/WorldItemComponent.cs` — **god node (51 arêtes)** : item ramassable, fitting automatique du `BoxCollider` sur le bounds du modèle (retry jusqu'à 10 frames), spawn depuis prefab ou fallback sphère de debug.
- `Code/Inventory/LootContainerComponent.cs` — **god node (26 arêtes)** : conteneur monde ouvrable, `StableContainerId` sérialisé (généré une fois, jamais régénéré — identité stable prévue pour une future persistance/réseau, contrairement à `GetHashCode()`).
- `Code/Inventory/InventoryPlayerInteractionComponent.cs` — **god node absolu (60 arêtes)** : façade `Request*` (pickup/drop/split/equip/move/use) appelée par le bridge d'interaction, gère les popups de stockage imbriqué (conteneur ouvert dans un conteneur), rollback manuel en cas d'échec de transfert.
- `Code/Inventory/InventoryBootstrapper.cs` (classe de base), `IInventoryDebugActions.cs` (interface), `InventoryDebugItemOption.cs`.

**Scènes/prefabs** : `Assets/Prefabs/Player/PlayerController.prefab/Inventory` (GameObject nommé littéralement `"Inventory"` — convention exploitée par `GameMenuUI` pour auto-résolution) porte `InventoryComponent` + `LoadoutComponent` + `InventoryPlayerInteractionComponent`. `Assets/Prefabs/Containers/Wardrobe/loot_wardrobe.prefab` porte `LootContainerComponent`.

**Dépendances entrantes** : GameMenuUI/InventoryPage (UI), Glue (WorldInventoryInteractionBridge, DebugInventoryBootstrapper).
**Dépendances sortantes** : Items, Loadout, Vitals (pour `TryUseItem`), Player (résolution du propriétaire).

**État réseau : aucun.** `InventoryComponent` est un `Component` ordinaire sans `[Sync]`. Avec plusieurs pawns networkés, chaque client verrait un `InventoryComponent` par pawn, mais son contenu ne serait synchronisé pour personne — un proxy afficherait un inventaire vide/divergent, pas celui du vrai propriétaire.

**Problèmes architecturaux observés** :
- **God nodes multiples** : `InventoryPlayerInteractionComponent` (60), `WorldItemComponent` (51), `InventoryComponent` (50), `LootContainerComponent` (26) concentrent énormément de responsabilités croisées — cohésion mesurée faible dans le graphe préexistant (communities "Player Inventory Interaction" à 0.08, "World-Inventory Bridge" à 0.10).
- Logique de rollback manuel dupliquée dans `InventoryPlayerInteractionComponent` (7 méthodes `Try*From*` presque identiques avec sauvegarde de position avant tentative, restauration si échec) — signal de refactoring, pas seulement de réseau.
- `InventoryComponent.TryDropItemToWorld` référence directement `WorldItemComponent` (couplage assumé et documenté comme volontaire dans le commentaire de code, mais reste un couplage fort entre deux systèmes qu'on pourrait vouloir découpler pour tester l'un sans l'autre).

**Valeur à conserver** : le pattern `InventoryActionResult` (résultat explicite Success/Reason, jamais d'exception pour un échec attendu) est propre et déjà cohérent avec le style du nouveau projet (`PlayerVitalsComponent` actuel utilise un style similaire de validation avant mutation). La séparation grille pure (`InventoryContainer`) / composant scène (`InventoryComponent`) est un bon point de départ conceptuel, à condition d'ajouter l'autorité réseau dès la conception (pas après coup, comme ici).
**Éléments à abandonner** : ne pas reproduire `InventoryPlayerInteractionComponent` comme façade unique de 60 arêtes — c'est explicitement l'anti-pattern que `.claude/rules/csharp.md` du nouveau projet met en garde ("responsabilités limitées par composant... s'il commence à orchestrer plusieurs domaines, c'est un signal pour le découper").

---

### 3.5 Loadout (équipement)

**Objectif** : 9 emplacements d'équipement (Headwear, GasMask, BodyArmor, TacticalRig, Backpack, Pants, Footwear, OnSling, OnBack), rendu du paperdoll piloté par un registre statique.

**Fichiers** : `Code/Loadout/LoadoutComponent.cs` (`TryEquip`/`TryUnequip`/`TrySwap`), `Code/Loadout/LoadoutSlotConfig.cs` (config immuable par slot : kinds acceptés, icône vide par défaut, variante de taille d'icône), `Code/Loadout/LoadoutSlotRegistry.cs` (dictionnaire statique `_all`, groupes de rendu `HeadSlots`/`BodySlots`/`WeaponSlots`/etc.).

**Dépendances entrantes** : InventoryComponent (délégation), GameMenuUI/InventoryPage (rendu paperdoll).
**Dépendances sortantes** : Items (`InventoryEquipmentSlot`, `ItemDefinition.CanEquipTo`), AssetPaths (icônes par défaut).

**État réseau : aucun.**

**Problèmes observés** : aucun majeur — c'est l'un des systèmes les plus propres du projet (registre immuable, pas d'état mutable partagé, dictionnaire `_equipped` privé).

**Valeur à conserver** : le pattern registre statique immuable (`LoadoutSlotRegistry`) pour la config de slots est réutilisable tel quel comme *idée* (pas comme code) — évite de disperser la logique "quel type d'item va dans quel slot" dans l'UI.

---

### 3.6 Interaction (raycast monde)

**Objectif** : détecter l'objet visé par le joueur et exposer une liste d'actions contextuelles.

**Fichiers** : `Code/Interaction/WorldInteractionComponent.cs` ("Generic World Interaction Scanner" — **redondant avec la logique de raycast déjà dupliquée dans `WorldInventoryInteractionBridge`**, voir ci-dessous), `Code/Interaction/WorldInteractionPromptAction.cs` (enum `Pickup`/`Equip`/`OpenLoot` + label), `Code/Interaction/WorldInteractionQuery.cs` (helper statique de raycast/recherche de composant dans la hiérarchie, réellement partagé par les deux).

**Problème architectural documenté explicitement dans l'ancien projet lui-même** (`Docs/EditorComponentPlacement.md`, tableau "Composants à ne pas confondre") : `WorldInteractionComponent` est un scanner bas niveau générique, **jamais utilisé en pratique** pour la boucle joueur réelle — c'est `Kodoku.Glue.WorldInventoryInteractionBridge` (section 3.7) qui réimplémente son propre raycast avec `WorldInteractionQuery` directement, sans passer par `WorldInteractionComponent`. Les deux composants font presque la même chose ; seul le bridge est réellement câblé sur `PlayerController.prefab/InteractionOrigin`.

**État réseau : aucun** (logique 100% locale par nature — c'est correct, l'interaction candidate n'a pas besoin d'être répliquée, seul le résultat de l'action déclenchée en a besoin).

**Valeur à conserver** : `WorldInteractionQuery` (raycast + recherche de composant en remontant/descendant la hiérarchie, jamais les frères) est un utilitaire propre, sans état, facilement portable.
**À abandonner** : `WorldInteractionComponent` lui-même — doublon non utilisé, à ne pas reconstruire en l'état. Si un scanner générique est nécessaire dans le nouveau projet, il doit être *le seul* point d'entrée, pas coexister avec une réimplémentation dans un bridge.

---

### 3.7 Glue (bridges spécifiques au jeu)

**Objectif** : coller les systèmes réutilisables (`Kodoku.Lib.*`) à la scène concrète.

**Fichiers** :
- `Code/Glue/WorldInventoryInteractionBridge.cs` — **composant central de la boucle interaction→inventaire** : raycast, construit la liste d'actions contextuelles (Pickup/Equip/Open), gère la sélection à la molette, exécute l'action au clic, pilote le `WorldInteractionHud`. Gate `KodokuPlayerComponent.IsOwnedByLocalPlayer` en toutes circonstances (existe une fois par pawn, local et par proxy).
- `Code/Glue/DebugInventoryBootstrapper.cs` — implémente `IInventoryDebugActions`, ajoute des items de test au démarrage, expose Give/Spawn pour `DebugMenuUI`. **Explicitement retiré du prefab réseau** (`PlayerController.prefab`) car un scan scène-large de `IInventoryDebugActions` par plusieurs joueurs pourrait cibler l'inventaire d'un autre joueur — encore présent uniquement, et volontairement, sur la scène solo statique.

**État réseau : aucun** (mais c'est le composant le plus soigné vis-à-vis du coop — c'est celui qui applique `IsOwnedByLocalPlayer` le plus rigoureusement, avec commentaires explicites sur le "pourquoi" à chaque garde).

**Valeur à conserver** : le pattern de garde `IsOwnedByLocalPlayer` appliqué systématiquement à tout composant d'input/UI qui existe une fois par pawn (local + proxies) est directement applicable au nouveau projet dès qu'un composant d'interaction apparaîtra.
**À abandonner** : `DebugInventoryBootstrapper` a déjà son équivalent de leçon apprise dans le nouveau projet (`PlayerVitalsDebugComponent`, retiré après validation — voir mémoire du jalon vitals) ; ne pas le laisser traîner dans un prefab réseau si un futur bootstrapper de debug est recréé.

---

### 3.8 GameMenu (état, hors UI)

**Objectif** : état pur (onglet actif, ouvert/fermé) séparé du rendu Razor.

**Fichiers** : `Code/GameMenu/GameMenuState.cs` (façade), `NavigationMenuState.cs` (onglet actif + ouverture, 5 onglets : Inventory/Stats/Quests/Map/Options), `InventoryMenuState.cs` (survol/sélection pour la grille), `GameMenuTab.cs` (enum).

**`Code/GameMenu/GameMenuComponent.cs` est explicitement documenté comme mort dans le `CLAUDE.md` de l'ancien projet lui-même** : "never instantiated or referenced anywhere; do not treat it as the source of truth." `GameMenuUI.razor` porte son propre `GameMenuState` directement, sans passer par ce composant.

**État réseau : aucun** (légitimement local — l'état d'ouverture d'un menu n'a pas de raison d'être répliqué).

**Valeur à conserver** : la séparation état (`GameMenuState`/`NavigationMenuState`) vs présentation (`.razor`) est un bon pattern, indépendant du réseau.
**À abandonner explicitement** : `GameMenuComponent.cs` — code mort confirmé par l'ancien projet lui-même, ne pas le reconstruire.

---

### 3.9 UI (Razor)

**Objectif** : présentation.

**Fichiers** (`Code/UI/`) :
- `GameHud/GameHudUI.razor` (+ `.scss`) — compose `HotbarHud` + `VitalsHud`, se cache si le menu est ouvert, résout `InventoryComponent` par nom de GameObject `"Inventory"` puis `PlayerVitalsComponent` sur le même GameObject, avec repli scène-large.
- `GameHud/Components/{Hotbar,Interaction,Vitals}/*.razor` — sous-composants.
- `GameMenu/GameMenuUI.razor` — **contrôleur central réel du menu** (pas `GameMenuComponent`) : raccourcis clavier (`TAB`/`M`/`I`), gestion d'Escape avec détection d'overlay s&box (`Game.Overlay`), auto-résolution scène-large d'`InventoryComponent`.
- `GameMenu/Components/{GameMenuHeader,GameMenuSidebar}.razor` — **code mort, jamais référencés** (même statut que `GameMenuComponent.cs`, confirmé par le même passage du `CLAUDE.md`).
- `GameMenu/Pages/{InventoryPage,MapPage,OptionsPage,QuestsPage,StatsPage}.razor` — une page par onglet ; seule `InventoryPage` et `OptionsPage` ont une logique réelle documentée (Map/Quests/Stats sont vraisemblablement des coquilles).
- `Debug/DebugMenuUI.razor` (+`.scss`) — panneau de debug indépendant (touche virgule), résout `IInventoryDebugActions` scène-large.
- `_Imports.razor`, `Styles/kodoku_inventory_textures.scss`.

**État réseau : aucun** — mais c'est cohérent : l'UI doit rester strictement locale par nature (voir ADR-0003 du nouveau projet). Le vrai problème n'est pas que l'UI soit locale, c'est qu'elle lit des données (`InventoryComponent`, `Vitals`) qui elles-mêmes ne sont pas répliquées.

**Problème documenté et déjà résolu dans l'ancien projet, à ne pas réapprendre à la dure** : `KodokuPlayerComponent.LocalChanged` + appel explicite à `StateHasChanged()` est **nécessaire** — résoudre `Inventory`/`Vitals` en C# ne déclenche pas seul un nouveau rendu Razor. Un test réel à 2 clients a montré la résolution correcte en C# (logs) mais un HUD resté vide jusqu'à l'ajout de ce fix. **Le nouveau projet a déjà découvert et corrigé le même problème pour `GameHud.razor` actuel** (bug de `BuildHash()` documenté dans le contexte de reprise) — leçon convergente, indépendamment redécouverte.

**Valeur à conserver** : la distinction stricte "où vit l'état" (GameMenuState) vs "qui l'affiche" (.razor) ; la détection d'overlay s&box pour Escape (`IsSandboxOverlayOpen`) contourne une bizarrerie documentée de l'API (`Game.Overlay.IsOpen` est une propriété d'instance alors que les méthodes `Show*` sont statiques).
**À abandonner** : `GameMenuHeader.razor`, `GameMenuSidebar.razor` — code mort confirmé, ne pas migrer.

---

### 3.10 AssetPaths

**Objectif** : centraliser les chemins de ressources en constantes plutôt qu'en chaînes dispersées.

**Fichiers** : `Code/AssetPaths/KodokuItemAssetPaths.cs` (une constante par définition d'item), `KodokuUiAssetPaths.cs` (icônes de slots/vitals).

**État réseau** : sans objet (constantes statiques).

**Valeur à conserver** : le pattern lui-même (constantes centralisées, jamais de chemin en dur ailleurs) est explicitement déjà une règle actée dans le nouveau projet pour la documentation (`.claude/rules/documentation.md` : "aucun chemin absolu local"), transposable aux assets de jeu quand ils existeront.

---

### 3.11 Tests (UnitTests/)

**Fichiers** : `UnitTests/{GameMenu,Inventory,Items,Loadout,Player,Vitals}/*Tests.cs`, `TestInit.cs` (MSTest, `[TestClass]`/`[TestMethod]`). Testent uniquement la logique pure (pas de dépendance à l'éditeur s&box) : `GameMenuStateTests`, `HotbarComponentTests`, `InventoryActionResultTests`, `InventoryContainerTests`, `LootContainerComponentTests`, `WorldItemDropTests`, `ItemDefinitionTests`, `LoadoutComponentTests`, `LoadoutSlotRegistryTests`, `KodokuPlayerComponentTests`, `VitalStatTests`.

**Valeur à conserver** : c'est la seule preuve dans tout le repo legacy d'une suite de tests exécutable en CLI (`dotnet test UnitTests/`). Le nouveau projet n'a actuellement **aucune** suite de tests automatisés (confirmé — `CLAUDE.md` du nouveau projet : "Aucune suite de tests automatisés n'existe pour le code du jeu"). Le fait que l'ancien projet ait pu tester `InventoryContainer`, `VitalStat`, `LoadoutSlotRegistry` etc. en pur C# sans dépendre de l'éditeur s&box est un signal de conception à retenir : **isoler la logique pure (non-Component) dès la conception pour la rendre testable en CLI**, comme cela a déjà commencé côté nouveau projet avec `PlayerVitalsComponent`'s `SetVital`/`ClampVital` (bien que ce ne soit pas encore extrait en classe séparée testable).

---

### 3.12 Placeholders / systèmes inexistants dans l'ancien projet

Ni le code, ni les scènes, ni les tests de l'ancien projet ne contiennent la moindre trace de : **mort/respawn, combat, armes actives (le `shotgun.prefab` n'est qu'un `WorldItemComponent` ramassable, aucune logique de tir), IA/ennemis, sauvegarde/persistance, reconnexion, extraction.** `Assets/scenes/World/{Base,Exploration,Shop}.scene` ne contiennent qu'un `WorldRootComponent` + décor statique (lumière, skybox, quelques colliders) — aucune logique de zone, aucune transition scriptée au-delà de ce que `SceneLoaderComponent` fait déjà (chargement additif simple par `WorldId`).

---

## 4. Inventaire des composants C# (référence rapide)

| Fichier (legacy) | Type | Networké ? | Rôle en une ligne |
|---|---|---|---|
| `Code/Player/KodokuPlayerComponent.cs` | Component, `INetworkSpawn` | **Oui** | Ancre d'identité/ownership du pawn |
| `Code/Player/LocalPlayerCameraComponent.cs` | Component | Non (délibérément `NetworkMode.Never`) | Caméra de rendu locale unique |
| `Code/Player/RemotePlayerVisualProxySystem.cs` | Component | Non (délibérément) | Double visuel local des joueurs distants |
| `Code/Core/SceneLoaderComponent.cs` | Component | Partiel (gate host-only) | Chargement additif du monde |
| `Code/World/WorldRootComponent.cs` | Component | Non | Marqueur d'identité de zone |
| `Code/Vitals/PlayerVitalsComponent.cs` | Component | Non | 5 jauges, sliders debug |
| `Code/Vitals/VitalStat.cs` | Classe pure | — | Calcul clamp/normalisation |
| `Code/Items/ItemDefinition.cs` | `GameResource` | Non | Donnée statique d'item |
| `Code/Items/ItemInstance.cs` | Classe pure | Non | Occurrence runtime d'un item |
| `Code/Inventory/InventoryContainer.cs` | Classe pure | Non | Grille + empilement |
| `Code/Inventory/InventoryComponent.cs` | Component | Non | Orchestrateur inventaire (god node) |
| `Code/Inventory/HotbarComponent.cs` | Component | Non | 8 slots numériques |
| `Code/Inventory/WorldItemComponent.cs` | Component, `ExecuteInEditor` | Non | Item ramassable monde (god node) |
| `Code/Inventory/LootContainerComponent.cs` | Component, `ExecuteInEditor` | Non | Conteneur monde (god node) |
| `Code/Inventory/InventoryPlayerInteractionComponent.cs` | Component | Non | Façade Request* (god node absolu) |
| `Code/Loadout/LoadoutComponent.cs` | Component | Non | Équipement par slot |
| `Code/Loadout/LoadoutSlotRegistry.cs` | Statique | — | Config immuable des slots |
| `Code/Interaction/WorldInteractionComponent.cs` | Component | Non | Scanner générique **non utilisé en pratique** |
| `Code/Interaction/WorldInteractionQuery.cs` | Statique | — | Helper raycast/hiérarchie partagé |
| `Code/Glue/WorldInventoryInteractionBridge.cs` | Component | Non (gate `IsOwnedByLocalPlayer`) | Boucle interaction→inventaire réelle |
| `Code/Glue/DebugInventoryBootstrapper.cs` | Component | Non | Give/Spawn debug |
| `Code/GameMenu/GameMenuComponent.cs` | Component | — | **Mort, non utilisé** |
| `Code/UI/GameMenu/GameMenuUI.razor` | PanelComponent | Non | Contrôleur réel du menu |
| `Code/UI/GameHud/GameHudUI.razor` | PanelComponent | Non | HUD composite |
| `Code/UI/GameMenu/Components/GameMenuHeader.razor` / `GameMenuSidebar.razor` | PanelComponent | — | **Mort, non utilisé** |

---

## 5. Inventaire des scènes et prefabs

### Scènes

| Scène | Composants clés (types distincts trouvés) |
|---|---|
| `Assets/scenes/Kodoku.scene` | `SceneLoaderComponent`, `DebugInventoryBootstrapper`, `WorldInventoryInteractionBridge`, `InventoryComponent`, `InventoryPlayerInteractionComponent`, `LoadoutComponent`, `KodokuPlayerComponent`, `DebugMenuUI`, `GameHudUI`, `GameMenuUI`, `WorldInteractionHud`, `PlayerVitalsComponent`, `NetworkHelper` (`StartServer=false`), `PlayerController` (statique, actif), colliders/rigidbody/dresser stock |
| `Assets/scenes/Kodoku_CoopTest.scene` | Copie de `Kodoku.scene` : `SceneLoaderComponent`, `DebugMenuUI`, `GameHudUI`, `GameMenuUI`, `WorldInteractionHud`, `NetworkHelper` (`StartServer=true`, `PlayerPrefab` câblé vers `PlayerController.prefab`) ; le "Player Controller" statique de la scène est **désactivé** (pas supprimé) |
| `Assets/scenes/World/Base.scene` | `WorldRootComponent` + décor (`DirectionalLight`, `SkyBox2D`, `ModelRenderer`, `BoxCollider`) |
| `Assets/scenes/World/Exploration.scene`, `Shop.scene` | `WorldRootComponent` seul (coquilles) |
| `Assets/scenes/Tests/TestScene.scene` | `WorldItemComponent`, `WorldRootComponent`, `SpawnPoint`, décor |

### Prefabs

| Prefab | Composants |
|---|---|
| `Assets/Prefabs/Player/PlayerController.prefab` | Racine : `KodokuPlayerComponent` **puis** `PlayerController` (ordre load-bearing), `Rigidbody`, `MoveModeWalk`/`MoveModeSwim`/`MoveModeLadder`, `Dresser`. Enfants : `Body` (SkinnedModelRenderer, désactivé), `Colliders` (Capsule+Box), `CameraRoot/PlayerCamera` (CameraComponent, **désactivé**, `IsMainCamera=false`), `InteractionOrigin` (`WorldInventoryInteractionBridge`), `Inventory` (`InventoryComponent`+`LoadoutComponent`+`InventoryPlayerInteractionComponent`), `Audio` (vide), `PlayerVitals` (`PlayerVitalsComponent`) |
| `Assets/Prefabs/Containers/Wardrobe/loot_wardrobe.prefab` | `LootContainerComponent`, `BoxCollider`, `ModelRenderer` |
| `Assets/Prefabs/Items/Consumables/Drinks/WaterBottle/water_bottle.prefab` | `WorldItemComponent`, `CapsuleCollider`, `ModelRenderer`, `Rigidbody` |
| `Assets/Prefabs/Items/Consumables/Medical/Bandage.prefab` | `WorldItemComponent`, `BoxCollider`, `CapsuleCollider`, `ModelRenderer`, `Rigidbody` |
| `Assets/Prefabs/Items/Equipment/Backpacks/raider_backpack.prefab` | `WorldItemComponent`, `ModelRenderer` (pas de collider propre — utilise probablement le fitting auto) |
| `Assets/Prefabs/Items/Equipment/Weapons/Ranged/shotgun.prefab` | `WorldItemComponent`, `BoxCollider`, `ModelRenderer`, `Rigidbody` |

**Détail utile pour une future stamina/sprint (fonctionnalité actuellement reportée dans le nouveau projet)** : `PlayerController.prefab` de l'ancien projet expose déjà les propriétés stock `RunSpeed=320`, `WalkSpeed=110`, `DuckedSpeed=70`, `AltMoveButton="run"`, `RunByDefault=false` — confirmation que `Sandbox.PlayerController` a une notion de course native exploitable sans logique custom de détection d'input, pour quand cette brique sera reprise.

---

## 6. Comparaison ancien/nouveau

| Système | Statut |
|---|---|
| Fondation player/network (identité, ownership, résolution du pawn local) | **RECONSTRUIT ET VALIDÉ** — `Code/Players/KodokuPlayerComponent.cs` actuel, testé host+client, late join, déconnexion |
| Vitals réseau | **RECONSTRUIT ET VALIDÉ** — `Code/Players/Vitals/PlayerVitalsComponent.cs` actuel, `[Sync(SyncFlags.FromHost)]`, **supérieur** à l'ancien (l'ancien n'était pas networké du tout) |
| HUD local des vitals | **RECONSTRUIT ET VALIDÉ (minimal)** — `Code/UI/Hud/GameHud.razor`, lit `KodokuPlayerComponent.Local` uniquement (jamais un proxy), `BuildHash()` correct |
| Stamina liée au sprint/déplacement | **ABSENT, reporté explicitement** (décision utilisateur de cette session) |
| Caméra locale dédiée (`LocalPlayerCameraComponent` équivalent) | **ABSENT** — le nouveau projet utilise encore la caméra stock pilotée directement par `PlayerController.UseCameraControls`, documenté comme "prototype stock uniquement" dans `docs/status/CURRENT_STATE.md` et `docs/status/ROADMAP.md` (étape 5, non commencée) |
| Double visuel proxy (`RemotePlayerVisualProxySystem` équivalent) | **ABSENT** — pas encore testé à 2 clients avec rendu visuel du proxy dans le nouveau projet (seul le pawn stock + son `SkinnedModelRenderer` par défaut existent) |
| Chargement de scène/monde additif (`SceneLoaderComponent` équivalent) | **ABSENT** — aucune logique de chargement de monde dans le nouveau projet |
| Items (Definition/Instance) | **ABSENT** — architecture *visée* documentée (`docs/architecture/ITEM_ARCHITECTURE.md`), zéro code, zéro ressource `.item` |
| Inventaire (grille, conteneurs) | **ABSENT** — aucune trace |
| Équipement/Loadout | **ABSENT** — aucune trace |
| Interaction monde (raycast, prompts) | **ABSENT** — `Interactables` existe comme GameObject placeholder vide dans la scène de test actuelle |
| Menu de jeu (GameMenu, onglets) | **ABSENT** |
| Debug tools (bootstrapper d'items) | **ABSENT** (et c'est cohérent : le nouveau projet a explicitement retiré son propre outil de debug temporaire, `PlayerVitalsDebugComponent`, après validation — même discipline que ce que l'ancien projet aurait dû appliquer à `DebugInventoryBootstrapper` dans un prefab réseau) |
| Mort/respawn | **ABSENT dans les deux projets** — `PlayerVitalsComponent.ResetVitals()` actuel existe déjà comme primitive utile pour un futur respawn (commentaire explicite dans le code), mais aucune logique de mort n'existe ni dans l'un ni dans l'autre |
| Combat, armes actives, IA, ennemis | **ABSENT dans les deux projets** — l'ancien projet n'avait que des items-armes ramassables sans logique de tir |
| Sauvegarde/persistance, reconnexion, zones/extraction | **ABSENT dans les deux projets** — placeholders vides (`GameSession`, `SaveManager`, `SceneRules`, `ExtractionPoints` dans le nouveau projet) |
| Tests automatisés | **ABSENT dans le nouveau projet** — l'ancien avait une suite MSTest exécutable en CLI (`UnitTests/`) que le nouveau projet n'a pas encore recréée pour aucun système |

---

## 7. Matrice de migration

| Priorité | Système | Ancien composant | Nouveau statut | Action recommandée | Dépendances | Test nécessaire |
|---|---|---|---|---|---|---|
| 1 | Caméra locale | `LocalPlayerCameraComponent`, règles 1-7 de Camera Ownership | Absent (stock) | Réécrire entièrement, en s'inspirant des 7 règles comme check-list de non-régression | Fondation player (faite) | test host/client, test late join |
| 2 | Items | `ItemDefinition`/`ItemInstance` | Absent (archi visée) | Réécrire entièrement (pas de portage — `ITEM_ARCHITECTURE.md` l'exige déjà) | Aucune | test local, test host/client (autorité de création) |
| 3 | Interaction monde | `WorldInteractionQuery` (utilitaire), `WorldInteractionComponent` (à ne pas reprendre) | Absent | Réutiliser partiellement (le seul l'utilitaire de raycast/hiérarchie), réécrire le composant scanner en un point d'entrée unique | Fondation player | test local, test host/client (interaction simultanée à deux) |
| 4 | Inventaire (grille) | `InventoryContainer` | Absent | Réutiliser partiellement l'algorithme de placement/stacking (classe pure, testable), réécrire l'autorité (host-authoritative dès la conception) | Items | test host/client, test late join, test déconnexion |
| 5 | Inventaire (composant scène) | `InventoryComponent`, `InventoryPlayerInteractionComponent` | Absent | Découper en plusieurs composants (ne pas reproduire la façade à 60 arêtes) | Inventaire (grille), Items, Vitals | test host/client, test late join |
| 6 | Équipement/Loadout | `LoadoutComponent`/`LoadoutSlotRegistry` | Absent | Réutiliser partiellement le pattern registre statique immuable | Items, Inventaire | test host/client |
| 7 | Objets du monde | `WorldItemComponent`, `LootContainerComponent` | Absent | Réécrire, avec `StableContainerId` conservé comme *idée* (identité stable dès la conception, pas ajoutée après coup comme dans l'ancien projet) | Inventaire, Items | test host/client, test late join (item déjà au sol), test déconnexion |
| 8 | Debug bootstrapper | `DebugInventoryBootstrapper` | Absent | Reporter — créer un outil temporaire seulement quand un test précis l'exige, le retirer avant commit (déjà la règle du nouveau projet) | Inventaire | test local uniquement |
| 9 | Menu de jeu | `GameMenuState`/`NavigationMenuState`, `GameMenuUI.razor` | Absent | Réutiliser partiellement le pattern état/présentation ; abandonner `GameMenuComponent.cs` (code mort confirmé) | HUD (fait), Inventaire | test local, test host/client (chaque client son propre menu) |
| 10 | Chargement de zones | `SceneLoaderComponent`/`WorldRootComponent` | Absent | Réutiliser partiellement le pattern host-only-load + réplication | Fondation réseau (faite) | test host/client, test changement de scène |
| 11 | Vitals avancés (stamina liée au sprint) | `PlayerVitalsComponent` (non networké, à ne pas imiter) | Reporté | Conserver le comportement déjà validé actuel (host-authoritative), ajouter uniquement la consommation/régénération | Vitals (fait), PlayerController stock | test host/client |
| 12 | Mort/respawn | Aucun équivalent legacy | Absent | Reporter — `ResetVitals()` déjà présent comme primitive | Vitals (fait) | test host/client, test reconnexion |
| 13 | Persistance/reconnexion | Aucun équivalent legacy | Absent | Reporter | Items, Inventaire, Zones | test reconnexion, test persistance |
| 14 | Combat/IA | Aucun équivalent legacy | Absent | Reporter | Items, Vitals, Zones | test host/client |
| 15 | Tests automatisés | `UnitTests/` (MSTest) | Absent | Introduire dès le premier système à logique pure non-Component (ex. futur `InventoryContainer`) | Aucune | `dotnet test` en CLI |

---

## 8. Carte des dépendances (systèmes absents, ordre de construction)

```text
Fondation player/network (FAIT)
  └─→ Caméra locale (non commencé)
        └─→ Interaction monde
              └─→ Items
                    └─→ Inventaire (grille pure)
                          └─→ Inventaire (composant scène)
                                ├─→ Équipement/Loadout
                                ├─→ Objets du monde (WorldItem/LootContainer)
                                └─→ Menu de jeu (Inventory page)
                                      └─→ Debug bootstrapper (optionnel, reportable)

Vitals réseau (FAIT)
  └─→ Vitals avancés / stamina (reporté par décision utilisateur)
        └─→ Mort/respawn

Fondation réseau (FAIT)
  └─→ Chargement de zones
        └─→ Persistance/reconnexion
              └─→ Extraction

(Combat/IA dépend de : Items + Vitals + Zones — tous en amont)
```

Constat de dépendance important : **Items est un prérequis dur pour Inventaire, Équipement ET Objets du monde** — dans l'ancien projet, ces trois systèmes référencent tous directement `ItemDefinition`/`ItemInstance`. Aucun des trois ne peut être testé de façon crédible sans au moins une `ItemDefinition` réelle chargée. C'est cohérent avec `docs/status/ROADMAP.md` du nouveau projet (étape 7 "Nouveau système d'items" avant étape 8 "Inventaire et équipement").

---

## 9. Risques techniques

| # | Risque | Fichiers concernés | Conséquence probable | Stratégie de reconstruction recommandée |
|---|---|---|---|---|
| R1 | Caméra stock pilotée par `PlayerController.UseCameraControls` sans architecture locale dédiée | Nouveau projet : `Assets/scenes/tests/GameplayTest.scene` (`_Local/Main Camera`) ; ancien projet : les 7 règles de `CLAUDE.md` § Camera Ownership | Vol/gel de caméra entre clients dès qu'un 2e joueur se connecte — **déjà identifié comme point de vigilance non résolu** dans `docs/status/CURRENT_STATE.md` du nouveau projet | Utiliser les 7 règles legacy comme check-list de conception avant de déclarer l'étape 5 de la roadmap avancée ; tester explicitement `IsMainCamera`/`Priority`/écriture de `[Property]` répliqué |
| R2 | Aucun système de gameplay legacy (hors Player/Camera) n'est networké | Tout `Code/Inventory/`, `Code/Items/`, `Code/Loadout/`, `Code/Interaction/`, `Code/GameMenu/` legacy | Un portage naïf de n'importe lequel de ces fichiers donnerait un système qui compile mais ne se synchronise jamais entre joueurs — bug silencieux, pas une erreur de compilation | Concevoir l'autorité réseau (host/owner/local) **avant** d'écrire le premier composant de chaque système, comme l'exige déjà `.claude/rules/multiplayer.md` |
| R3 | God nodes (`InventoryPlayerInteractionComponent` 60 arêtes, `WorldItemComponent` 51, `InventoryComponent` 50) | `Code/Inventory/InventoryPlayerInteractionComponent.cs`, `WorldItemComponent.cs`, `InventoryComponent.cs` (legacy) | Un composant qui orchestre raycast+inventaire+UI+monde devient impossible à tester isolément et à faire évoluer sans casser autre chose | Découper dès la conception : mouvement de données (grille), autorité réseau, présentation UI, entrée input = 4 responsabilités distinctes, pas 1 |
| R4 | Accès "premier trouvé dans la scène" (scan scène-large) comme repli par défaut | `KodokuPlayerComponent.LegacyFindFirst*InScene`, `InventoryComponent.EnsureInitialized` (repli), `WorldInventoryInteractionBridge.FindPreferredInventory` (repli), `GameHudUI`/`GameMenuUI` (repli documenté) | Avec plusieurs joueurs, un scan scène-large peut retourner les données d'un autre joueur — bug de confidentialité/désynchronisation, pas juste un bug visuel | Ne jamais introduire de repli scène-large dans le nouveau projet ; toujours résoudre via `KodokuPlayerComponent.FindOwning`/`.Local` dès le premier composant qui en a besoin, sans période de transition |
| R5 | Composant qui existe une fois par pawn (local ET proxy) sans garde d'exécution | `HotbarComponent.OnUpdate`, `InventoryPlayerInteractionComponent.OnUpdate`, `WorldInventoryInteractionBridge.OnUpdate` (tous gérés via `IsOwnedByLocalPlayer` dans l'ancien projet — la garde existe mais est répétée manuellement partout) | Sans garde, un proxy lit les touches locales et écrit dans le HUD/menu partagé, une fois par proxy présent, chaque frame | Reproduire le principe (`IsOwnedByLocalPlayer`-like), mais envisager de le centraliser plutôt que de le copier-coller composant par composant comme dans l'ancien projet |
| R6 | `[Property]` non-`[Sync]` qui se réplique quand même sur un composant networké | `CameraComponent.IsMainCamera`/`.Enabled`, `PlayerController.UseCameraControls` (cf. règle 5-6 de Camera Ownership) | Une écriture locale-only supposée peut se propager silencieusement à d'autres clients | Tester explicitement toute écriture de propriété sur un composant networké avec au moins 2 clients avant de la considérer sûre — ne jamais assumer qu'un `[Property]` simple reste local |
| R7 | API s&box potentiellement dépréciée/changée depuis la capture legacy | Toute référence à `Sandbox.PlayerController`/`Sandbox.NetworkHelper`/`Sandbox.Dresser` dans le code legacy (daté, verified contre le build "26.06.24" selon son propre `CLAUDE.md`) | Une supposition sur le comportement de l'API (ex. `PlayerController` sans champ caméra) pourrait ne plus être vraie sur la version actuelle de s&box installée | Revérifier par réflexion/doc officielle avant de s'appuyer sur une affirmation "confirmée par décompilation" datée de l'ancien projet — voir `docs/status/OPEN_QUESTIONS.md` du nouveau projet |

---

## 10. Composants à abandonner (ne pas reconstruire)

- `Code/GameMenu/GameMenuComponent.cs` — code mort confirmé par l'ancien projet lui-même.
- `Code/UI/GameMenu/Components/GameMenuHeader.razor` et `GameMenuSidebar.razor` — jamais référencés.
- `Code/Interaction/WorldInteractionComponent.cs` en tant que composant scène — doublon non utilisé de la logique déjà dans `WorldInventoryInteractionBridge` (seul `WorldInteractionQuery`, l'utilitaire statique sous-jacent, a de la valeur).
- Les replis "legacy scan scène-large" (`LegacyFindFirstInventoryInScene`, `LegacyFindFirstVitalsInScene`, `CanUseLegacySceneFallback`) — dette assumée par l'ancien projet pendant sa propre transition, sans objet dans un projet qui démarre coop-first.
- `DebugInventoryBootstrapper` tel quel — à remplacer par un futur outil de debug jetable créé/retiré par jalon, comme déjà pratiqué dans le nouveau projet.

## 11. Composants à remplacer par des composants stock s&box

- Caméra : aucun composant Kodoku custom n'est nécessaire pour la mécanique de base — `Sandbox.PlayerController` + `IsMainCamera` suffisent, à condition d'ajouter la couche de protection locale (`LocalPlayerCameraComponent`-like) **autour**, pas à la place.
- Mouvement/collision de base : `Sandbox.PlayerController`, `Sandbox.Rigidbody`, `Sandbox.Movement.MoveModeWalk/Swim/Ladder` sont déjà utilisés tels quels par les deux projets — rien à réécrire ici.
- Habillage visuel des joueurs : `Sandbox.Dresser` est déjà utilisé tel quel dans l'ancien projet ; le nouveau projet peut le réutiliser directement le moment venu (actuellement le prefab `kodoku_player.prefab` a déjà `Sandbox.Dresser`).
- Chargement de niveau simple : si le besoin reste "une scène additive par zone", `Scene.Load`/`SceneLoadOptions` stock (déjà ce que `SceneLoaderComponent` legacy enveloppe) est probablement suffisant sans composant Kodoku supplémentaire — seule la logique de retry/host-gating a de la valeur ajoutée.

## 12. Ordre recommandé de reconstruction

Cet ordre est **cohérent avec `docs/status/ROADMAP.md` déjà validé dans le nouveau projet** — cet audit ne le remplace pas, il en précise le contenu système par système à partir de ce que l'ancien projet a réellement dû résoudre.

1. **Caméra et présentation locale** (roadmap étape 5, non commencée) — *Pourquoi maintenant* : c'est un prérequis multijoueur direct déjà signalé comme point de vigilance dans `CURRENT_STATE.md` ; tant que non résolu, tout test à 2 clients au-delà du pawn reste fragile. *Débloque* : tout gameplay visible en coop sans risque de vol de caméra. *Dépend de* : fondation player (faite). *Critères de validation* : les 3 points déjà listés dans `CURRENT_STATE.md` § Caméra. *Risques multiplayer* : R1, R6.
2. **Interaction monde minimale** (roadmap étape 6) — *Pourquoi* : nécessaire pour tester n'importe quel système suivant avec un vrai geste joueur, peut utiliser un placeholder pour l'objet ciblé. *Débloque* : Items/Inventaire testables via une vraie boucle de jeu, pas juste du code isolé. *Dépend de* : caméra locale (étape 1). *Critères de validation* : cycle complet raycast → requête host → réponse, testé à 2 clients avec interaction simultanée. *Risques* : R2, R5.
3. **Items** (roadmap étape 7) — *Pourquoi* : prérequis dur pour Inventaire ET Équipement ET Objets du monde (section 8). *Débloque* : les trois systèmes suivants en parallèle si besoin. *Dépend de* : rien de gameplay, seulement l'architecture déjà actée dans `ITEM_ARCHITECTURE.md`. *Critères* : une définition chargée et instanciée, testée en réseau (création/destruction autoritaire). *Risques* : R2 (autorité de création).
4. **Inventaire et équipement** (roadmap étape 8) — *Pourquoi* : cœur du gameplay de survie, mais seulement après Items. *Débloque* : objets du monde ramassables, menu de jeu. *Dépend de* : interaction (étape 2), items (étape 3). *Critères* : transfert d'item entre deux joueurs testé sans désync. *Risques* : R2, R3, R4, R5 — c'est l'étape la plus à risque de reproduire les god nodes legacy si la conception n'impose pas de découpage dès le départ.
5. **Objets du monde** (roadmap étape 9) — *Pourquoi* : dépend d'un inventaire fonctionnel pour avoir une destination. *Débloque* : boucle de gameplay complète pickup/drop/loot. *Dépend de* : inventaire (étape 4). *Critères* : ramassage/dépôt testé à 2 instances, pas de duplication d'objet (risque explicite déjà nommé dans la roadmap). *Risques* : R2, R4.
6. **Vitals avancés / stamina liée au sprint** (reporté par décision utilisateur de cette session, mais logiquement indépendant du bloc Items/Inventaire — peut être repris à tout moment après la caméra) — *Pourquoi ici plutôt que plus tôt* : sans dépendance dure sur Inventaire, mais nécessite malgré tout un vrai geste joueur (sprint) donc bénéficie d'une caméra locale stable pour être testé confortablement. *Débloque* : mort/respawn (Health à 0). *Dépend de* : vitals réseau (fait), `Sandbox.PlayerController` déjà expose `RunSpeed`/`AltMoveButton` nativement (voir section 5). *Risques* : aucun nouveau — même modèle host-authoritative déjà validé pour les vitals actuelles.
7. **Menus et HUD restants** (roadmap, en parallèle de 4-5) — *Pourquoi* : dépend du contenu à afficher (inventaire), pas l'inverse. *Débloque* : jouabilité complète pour un testeur humain. *Dépend de* : inventaire (étape 4). *Risques* : R2 (UI qui lit un état non répliqué donnerait une fausse impression de fonctionnement en solo).
8. **Gestion des scènes et zones** (roadmap étape 11) — *Pourquoi* : indépendante du bloc gameplay ci-dessus, peut être menée en parallèle par un autre travail si nécessaire. *Débloque* : monde plus grand qu'une seule scène de test. *Dépend de* : fondation réseau (faite). *Risques* : aucun risque spécifique nouveau identifié dans l'ancien projet au-delà du host-only-load déjà résolu.
9. **Persistance et reconnexion** (roadmap étape 12) — *Pourquoi* : nécessite un état de jeu (items, inventaire, zones) déjà stable à sérialiser. *Dépend de* : étapes 3, 4, 8. *Risques* : aucun antécédent legacy (jamais implémenté dans l'ancien projet non plus) — terrain neuf, pas de leçon à réutiliser.
10. **Extraction** (roadmap, dans "Scènes, zones et extraction") — *Dépend de* : zones (étape 8) + persistance (étape 9).
11. **Combat, IA** (roadmap étape 10) — *Dépend de* : items (armes), vitals (dégâts), zones.
12. **Mort et respawn** — *Dépend de* : vitals avancés (étape 6). `ResetVitals()` existe déjà comme primitive côté nouveau projet.

## 13. Première prochaine brique recommandée

**Caméra et présentation locale** (section 12, étape 1), pour deux raisons concrètes indépendantes de cet audit :
1. C'est déjà l'étape suivante non commencée de `docs/status/ROADMAP.md`, juste après le jalon vitals/HUD qui vient d'être validé.
2. L'ancien projet démontre, avec 7 règles durement acquises sur plusieurs cycles de bugs réels (vol de caméra, écran noir, gel de vue), que c'est un point précis et bien délimité où une conception naïve casse silencieusement dès qu'un 2e client se connecte — exactement le type de risque que `docs/status/CURRENT_STATE.md` signale déjà sans y avoir encore répondu.

Ne pas commencer Items/Inventaire avant, même si la stamina reste reportée : sans caméra locale fiable, tout futur test à 2 instances (y compris pour la stamina, quand elle sera reprise) reste fragile pour une raison indépendante de la fonctionnalité testée.

## 14. Questions nécessitant une décision utilisateur

1. **Faut-il reproduire l'architecture "god node" `InventoryPlayerInteractionComponent` sous une forme découpée, ou repartir d'une conception entièrement différente pour la boucle interaction→inventaire ?** Cet audit recommande le découpage (section 3.4, 10) mais ne tranche pas la forme exacte.
2. **Le modèle de vitals cible reste-t-il 5 stats (Health/Stamina/Hunger/Thirst/Radiation, déjà actuel) ou faut-il réintroduire "Madness" (nom legacy) sous une forme quelconque ?** Le nouveau projet a déjà substitué Radiation à Madness sans que cet audit sache si c'est un choix définitif ou un simple renommage de test.
3. **`WorldInteractionComponent` (scanner générique inutilisé dans l'ancien projet) doit-il être recréé sous une forme quelconque, ou l'intégration directe dans le bridge d'interaction (comme l'ancien projet le fait *en pratique*, malgré son propre composant scanner jamais câblé) est-elle le modèle à suivre ?**
4. **Quelle stratégie d'autorité réseau pour la création/destruction d'`ItemInstance` ?** Host-authoritative par cohérence avec ADR-0002 est déjà proposé comme hypothèse dans `ITEM_ARCHITECTURE.md`, mais pas confirmé.
5. **Faut-il réintroduire une suite de tests automatisés (MSTest ou autre) dès le premier système à logique pure (probablement la future `InventoryContainer`), ou continuer sans tests automatisés comme actuellement ?** L'ancien projet montre que c'est possible sans dépendre de l'éditeur s&box pour la logique pure.
6. **Le graphe de connaissances préexistant dans `kodoku_legacy` (`graphify-out/`, créé le 2026-07-04) doit-il être maintenu à jour dans ce dépôt legacy pour de futures consultations, ou son usage s'arrête-t-il à cet audit ?** Cette mission l'a mis à jour de manière incrémentale dans le clone isolé `../kodoku_legacy_audit` (jamais dans le vrai `kodoku_legacy` local) — aucune action n'a été prise sur le dépôt legacy réel.

---

*Fin du rapport. Aucune implémentation n'a été commencée. Aucun fichier de `kodoku_legacy` n'a été modifié. Le clone d'audit `../kodoku_legacy_audit` est un dossier de travail temporaire, séparé du checkout `kodoku_legacy` habituel (qui contient des modifications locales non commitées, non touchées par cette mission).*
