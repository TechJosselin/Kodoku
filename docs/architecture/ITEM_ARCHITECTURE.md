# Architecture des items

**Statut : `ItemDefinition` (fondation statique), `ItemInstance` (état runtime minimal), `WorldItemComponent` (réplication réseau minimale host-authoritative, Tests A à E) et `LootSpawnPointComponent` (génération de loot host-authoritative, V1 mono-item, Tests A à G) implémentés et validés par des tests réels à deux instances (12 juillet 2026 — voir sections dédiées ci-dessous) — voir [../status/CURRENT_STATE.md](../status/CURRENT_STATE.md).** L'inventaire (conteneurs, transferts, équipement) reste une architecture visée, non implémentée. Seuls des PNG d'icônes de l'ancienne UI ont été conservés au-delà du code (voir [../development/ASSET_MIGRATION.md](../development/ASSET_MIGRATION.md)).

## Séparation conceptuelle envisagée

- **`ItemDefinition`** (ressource `.item`, données statiques) — décrit ce qu'est un type d'objet, indépendamment de toute instance : identifiant, apparence, dimensions, propriétés d'usage. Une donnée, pas un état de partie.
- **`ItemInstance`** (runtime) — une occurrence concrète d'un `ItemDefinition`, avec un identifiant stable propre à l'instance (nécessaire pour survivre à une reconstruction réseau ou une sauvegarde — voir [../../.claude/rules/csharp.md](../../.claude/rules/csharp.md)) et un état propre (ex. quantité empilée, durabilité si applicable).
- **Représentation dans un inventaire** — un `ItemInstance` placé dans un conteneur, avec position/rotation si l'inventaire est spatial.
- **Représentation dans le monde** — un `ItemInstance` matérialisé comme objet ramassable, potentiellement networké.
- **Équipement** — un `ItemInstance` assigné à un emplacement d'équipement plutôt qu'à une position d'inventaire.
- **Conteneurs** — inventaire du joueur, conteneurs du monde, et éventuellement sous-conteneurs portés par un item (ex. sac à dos) : à concevoir comme le même mécanisme de base, pas trois systèmes séparés.

## Identifiants stables et état

- Un `ItemDefinition` a besoin d'un identifiant stable indépendant du chemin de fichier (pour survivre à un déplacement/renommage d'asset).
- Un `ItemInstance` a besoin d'un identifiant stable indépendant de toute mécanique moteur (GUID de scène, `GetHashCode()`) pour rester valide à travers une sauvegarde ou une reconstruction réseau.
- L'état persistant (ce qui doit survivre à une sauvegarde) et l'état synchronisé (ce qui doit être répliqué en temps réel entre clients) sont deux préoccupations distinctes à traiter séparément — voir [MULTIPLAYER_ARCHITECTURE.md](MULTIPLAYER_ARCHITECTURE.md#persistance).

## Sur l'ancienne version du projet

Les anciennes ressources `.item` seront **recréées**, pas réutilisées — leur ancienne structure exacte (champs, conventions de nommage) ne doit pas être reprise automatiquement. L'ancien projet peut être consulté pour comprendre quelles catégories d'items existaient et quel besoin elles couvraient (ex. consommables avec effets sur des statistiques de vitals, équipement par emplacement), mais chaque champ doit être redécidé pour la nouvelle architecture, en particulier vis-à-vis de l'autorité réseau (qui a le droit de créer/détruire/modifier un `ItemInstance`). Voir [../development/LEGACY_REFERENCE_POLICY.md](../development/LEGACY_REFERENCE_POLICY.md).

## `ItemDefinition` — V1 (décision validée pour son périmètre)

`ItemDefinition` (`Code/Items/Definitions/ItemDefinition.cs`, namespace `Kodoku.Items`) est un `GameResource` (`[AssetType(Name = "Item Definition", Extension = "item", Category = "Kodoku")]`), un fichier `.item` par type d'objet. Aucun état runtime — voir séparation conceptuelle ci-dessus. L'inspecteur est organisé en quatre groupes (`[Group("...")]`) :

- **Identity** : `ItemId` (string, stable, unique, jamais dérivé de `DisplayName` — `[Title("Item ID")]`/`[Description(...)]` pour clarifier l'intention dans l'inspecteur ; `[KeyProperty]` a été écarté après revue, cet attribut désigne la propriété d'affichage d'un élément dans une liste/collection, pas une clé primaire), `DisplayName` (string), `Description` (string, `[TextArea]`), `Category` (`ItemCategory`, enum simple — `Miscellaneous`/`Consumable`/`Medical`/`Food`/`Equipment`/`Weapon`/`Ammunition`/`Tool`/`Resource`/`Key`/`Quest`, ne détermine aucun comportement automatiquement), `Tags` (`ItemTags`, `[Flags]` — `None`/`Drink`/`Water` pour l'instant, volontairement minimal).
- **Presentation** : `Icon` (référence typée `Texture`).
- **Inventory** : `GridWidth`/`GridHeight` (int, clampés à un minimum de 1), `CanRotate` (bool, défaut `true` — la rotation *effective* d'un exemplaire n'appartient pas à cette définition), `Weight` (float, kilogrammes, clampé à un minimum de 0), `MaxStack` (int, clampé à un minimum de 1 ; `MaxStack = 1` signifie non empilable, pas de propriété `IsStackable` séparée).
- **World** : `WorldModel` (référence typée `Model`), `WorldPrefabOverride` (référence typée `PrefabFile` — confirmée comme un type d'asset réel du moteur, `PrefabFile : GameResource`, mais non consommée par aucun système actuellement).

Les types d'attributs (`[AssetType]`, `[Group]`, `[Title]`, `[Description]`, `[TextArea]`, ainsi que `Texture`/`Model`/`PrefabFile` comme références typées) ont été vérifiés directement contre les assemblies moteur installées localement (`Sandbox.Engine.dll`/`Sandbox.System.dll`) avant utilisation, pas supposés depuis une documentation externe.

**Validation** : les minimums numériques (`GridWidth`/`GridHeight`/`MaxStack` ≥ 1, `Weight` ≥ 0) sont appliqués par clamp dans le setter de chaque propriété (même pattern que `PlayerVitalsComponent`). `ItemId`/`DisplayName` vides et l'unicité globale de `ItemId` entre toutes les ressources `.item` **ne sont pas encore validés** — aucune API moteur simple et propre identifiée pour ce cas sans construire un mécanisme dédié (ex. registre global) ; reporté à un futur système (voir Éléments encore ouverts).

**Première ressource concrète** : `Assets/Data/Items/Consumables/Drinks/water_bottle.item` (`ItemId = kodoku.consumable.water_bottle`, catégorie `Consumable`, tags `Drink | Water`, grille 1×2, `MaxStack = 1` — volontairement non empilable, une future `ItemInstance` portera un état interne, ex. quantité d'eau restante, qui rendrait l'empilement ambigu). Utilise l'icône déjà migrée `Assets/ui/game/icons/items/consumables/drinks/icon_water.png` ; aucun modèle 3D n'existe encore dans le projet, `WorldModel` est laissé vide plutôt que de fabriquer un asset temporaire.

## `ItemInstance` — V1 (décision validée pour son périmètre)

`ItemInstance` (`Code/Items/Instances/ItemInstance.cs`, namespace `Kodoku.Items`) est une classe C# pure (pas un `Component`, pas un `GameResource`) : l'état runtime d'un exemplaire précis d'un `ItemDefinition`. Elle ne contient que trois données :

- **`InstanceId`** (`Guid`) — identifiant stable de l'exemplaire, indépendant de `Definition.ItemId`. Généré à la création (`CreateNew`), jamais régénéré à la restauration (`Restore`).
- **`Definition`** (`ItemDefinition`) — référence obligatoire vers les données statiques partagées. `DisplayName`/`Icon`/`Weight`/`GridWidth`/`GridHeight`/`MaxStack` ne sont **pas** recopiés sur l'instance : ils se lisent via `Definition`.
- **`Quantity`** (`int`) — invariant `1 <= Quantity <= Definition.MaxStack`, appliqué strictement (aucun clamp silencieux à la création/restauration, qui masquerait une sauvegarde ou une réplication incorrecte — une quantité hors limites lève une exception).

**Deux chemins de construction distincts**, tous deux passant par un constructeur privé qui valide (`definition` non nul, `Definition.ItemId` non vide, `InstanceId` non `Guid.Empty`, `Quantity` dans les bornes — échec = exception, pas de valeur par défaut silencieuse) :
- `ItemInstance.CreateNew(definition, quantity = 1)` — génère un nouvel `InstanceId` (`Guid.NewGuid()`). Chemin destiné à une future autorité host (création non encore contrôlée par un système réseau — pas implémenté ici).
- `ItemInstance.Restore(instanceId, definition, quantity)` — conserve l'`InstanceId` fourni. Préparé pour une future sauvegarde/réplication/late join ; **aucun de ces systèmes n'est implémenté**, seul le point d'entrée existe.

**Mutations** (retournent `bool`, jamais d'exception pour un échec attendu — cohérent avec le style déjà utilisé par `PlayerVitalsComponent`) : `TrySetQuantity`/`TryAddQuantity`/`TryRemoveQuantity` refusent toute valeur qui violerait l'invariant de `Quantity` ; `Quantity = 0` n'est jamais autorisé dans cette version — la suppression d'une instance appartiendra au futur système qui la possède (inventaire, monde). `CanStackWith` répond à « ces deux instances pourraient-elles être empilées ? » sans jamais fusionner de quantités — le transfert réel appartient à un futur système d'inventaire.

**Données explicitement exclues de `ItemInstance`** (appartiennent à de futurs systèmes, pas à ce jalon) : propriétaire/`PlayerId`, `ContainerId`, emplacement équipé, durabilité, quantité d'eau restante ou toute autre propriété de consommable, réplication réseau/RPC, sauvegarde, registre global.

**Autorité réseau prévue, non implémentée** : `ItemInstance` est actuellement une classe C# pure sans aucune notion réseau. La décision de qui a le droit de créer/détruire/modifier une instance (probable host-authoritative par cohérence avec ADR-0002) reste à concevoir — voir Éléments encore ouverts.

## `WorldItemComponent` — V2 (réplication réseau minimale, host-authoritative)

`WorldItemComponent` (`Code/Items/World/WorldItemComponent.cs`, namespace `Kodoku.Items`) relie `ItemDefinition` → `ItemInstance` → une représentation dans une scène. Ce n'est **pas** un système de ramassage/interaction/inventaire — voir « Hors périmètre » ci-dessous.

- **Configuration** (inspecteur) : `Definition` (`ItemDefinition`, obligatoire), `InitialQuantity` (int, défaut 1, `[Range(1, 999)]` — doit rester `≤ Definition.MaxStack`, jamais clampée silencieusement).
- **Runtime local** (lecture seule, `[ReadOnly]`) : `IsInitialized`, `RuntimeInstanceId`, `RuntimeItemId`, `RuntimeQuantity` — dérivées de `Instance`, jamais une copie éditable.
- **`Instance`** (`ItemInstance`, lecture seule depuis le code) : seule source de vérité locale après initialisation.
- **État réseau autoritaire** : `NetworkInstanceId`/`NetworkItemId` (`string`, `[Sync(SyncFlags.FromHost)]`) et `NetworkQuantity` (`int`, `[Sync(SyncFlags.FromHost)]`), renseignés uniquement par le host. `Guid` n'a aucun précédent confirmé comme type supporté nativement par `[Sync]` dans ce projet ni dans le code moteur inspecté (`Sandbox.Engine.dll`/`Sandbox.System.dll`) — `NetworkInstanceId` utilise donc une représentation en chaîne canonique (`Guid.ToString()`), reconvertie avec `Guid.TryParse` + validation côté réception plutôt que de supposer un support natif non vérifié. **Ne sont pas synchronisés** : `DisplayName`, `Description`, `Icon`, `Weight`, `GridWidth`, `GridHeight`, `WorldPrefabOverride`, ou toute autre donnée de `ItemDefinition` — données statiques déjà présentes dans les assets, résolues localement via `Definition`.

**Autorité : `Networking.IsHost`, pas `IsProxy`.** Le jalon précédent utilisait `GameObject.IsProxy` pour décider qui a le droit de créer une instance ; corrigé dans cette version. `MULTIPLAYER_ARCHITECTURE.md` documente explicitement que `GameObject.IsProxy` peut retarder par rapport à la propagation réelle de l'ownership juste après un spawn — un risque vécu sur l'ancienne version du projet. `Sandbox.Networking.IsHost` est un indicateur de session, stable dès l'appel, sans cette fenêtre d'incertitude par-objet ; c'est le signal utilisé pour toute décision d'autorité de création.

**Un seul chemin de création, idempotent** : `TryInitializeAuthoritativeNew()`.
- Refuse (log + `false`, jamais d'exception) si appelé par un client non-host sur un GameObject networké.
- Appelle en interne `TryInitializeNew()` (mécanique : valide `Definition`/`InitialQuantity`, crée l'`ItemInstance`) — une seconde initialisation y est donc refusée automatiquement.
- Si le GameObject est networké, renseigne ensuite `NetworkInstanceId`/`NetworkItemId`/`NetworkQuantity` (aucun log de succès — voir note ci-dessous).
- `OnStart()` vérifie `IsInitialized` en premier et ne fait rien si déjà vrai — évite une double tentative si un appelant externe a déjà initialisé le composant de façon synchrone avant que `OnStart()` (différé "avant le premier Update") ne s'exécute.

**Côté non-host (`GameObject.Network.Active && !Networking.IsHost`)** : `OnStart()` ne crée jamais d'instance. Il appelle directement `TryRestoreFromNetworkState()`, sans logger — l'attente de l'état synchronisé est normale sur un proxy et ne doit pas produire un warning par item. `TryRestoreFromNetworkState()` est une tentative idempotente et bornée (jamais de boucle par frame) qui :
1. ne fait rien si déjà initialisé ou si l'état réseau (`NetworkInstanceId`/`NetworkItemId`/`NetworkQuantity`) n'est pas encore complet (pas un warning — l'état peut légitimement arriver après `OnStart`) ;
2. rejette (`Log.Error`, une fois) un `NetworkInstanceId` qui ne parse pas en `Guid` valide ;
3. rejette (`Log.Error`, une fois) si `Definition` locale est absente ou si `Definition.ItemId != NetworkItemId` — jamais de substitution silencieuse d'une autre définition ;
4. sinon appelle `ItemInstance.Restore(...)` puis `TryInitializeFromInstance(...)` — aucun proxy ne génère jamais son propre `InstanceId`.

**Logs** : `[WorldItem][Host][Initialized]`/`[WorldItem][Host][Spawned]`/`[WorldItem][Joiner][Restored]` étaient des logs de succès temporaires (un message par item et par client), utilisés uniquement pour la validation manuelle des Tests A à E (voir ci-dessous) — retirés du code une fois la validation obtenue. Seules les erreurs de configuration réelle restent loguées (`Log.Error`/`Log.Warning`) : `Guid` réseau invalide, `ItemId` incompatible avec `Definition`, `Definition` absente, `Quantity` invalide, seconde initialisation incohérente.

Cette même méthode est aussi appelée par trois callbacks `[Change(nameof(...))]` sur chacune des trois propriétés réseau (`OnNetworkInstanceIdChanged`/`OnNetworkItemIdChanged`/`OnNetworkQuantityChanged`) — le mécanisme officiel pour réagir à l'arrivée tardive de l'état synchronisé (late join), plutôt qu'une boucle de sondage. Sa nature idempotente absorbe sans risque le fait que les trois champs puissent arriver en plusieurs callbacks séparés.

**Prefab de la bouteille** : `WorldItemComponent` déjà présent sur `Assets/Prefabs/Items/Consumables/Drinks/water_bottle.prefab` (`Definition` → `water_bottle.item`, `InitialQuantity = 1`, inchangé dans ce jalon) ; aucun champ réseau n'y est configurable (les propriétés `[Sync]` ne portent pas `[Property]`, comme `PlayerVitalsComponent`).

**Hors périmètre explicite de ce jalon** : raycast, interaction, pickup, drop, inventaire, placement de grille, loadout, propriétaire/`PlayerId`, conteneur parent, durabilité, eau restante, consommation, sauvegarde, persistance inter-session, RPC de spawn demandé par un joiner, `ItemRegistry` global, loot tables, respawn automatique.

## Spawner debug réseau (retiré après validation)

Un composant temporaire (`WorldItemNetworkDebugSpawnerComponent`, `Code/Debug/`) et une action d'input dédiée (`debug_spawn_water_bottle`, touche `B`) ont servi à valider manuellement la réplication ci-dessus à deux instances réelles (host + joiner). Host-only, il spawnait une bouteille networkée devant `KodokuPlayerComponent.Local` et appelait `TryInitializeAuthoritativeNew()`. **Ce composant, son entrée dans `ProjectSettings/Input.config` et son instance dans `GameplayTest.scene` ont été retirés après la validation des Tests A à E ci-dessous** — ce n'était pas un système de gameplay et il ne fait pas partie de l'architecture permanente. Politique cohérente avec le retrait déjà appliqué à `PlayerVitalsDebugComponent` après validation des vitals réseau (voir [../status/CURRENT_STATE.md](../status/CURRENT_STATE.md)).

**Tests manuels — validés par l'utilisateur à deux instances réelles (host + joiners « Lisa » puis « Krusty »), logs fournis le 12 juillet 2026, avant le retrait du spawner debug** (`sbox-dev.log` = host, `sbox.log` = joiner ; les tags `[Host][Initialized]`/`[Host][Spawned]`/`[Joiner][Restored]` cités ci-dessous étaient les logs de succès temporaires actifs à ce moment-là, retirés du code depuis — voir note « Logs » ci-dessus) :

- **Test A — host + joiner déjà connectés** : ✅ validé, à plusieurs reprises. Chaque pression host produit exactement un `[WorldItem][Host][Initialized]`/`[Host][Spawned]`, et le joiner restaure le même `InstanceId`/`ItemId`/`Quantity` immédiatement après (ex. `8e3d46eb-9108-41b9-a7f4-1dfd5fa841f2` identique des deux côtés, run Krusty).
- **Test B — deux bouteilles** : ✅ validé. Ex. `8e3d46eb…` et `dd28068c…` (run Krusty) ont chacune un `InstanceId` distinct, identique host/joiner par paire.
- **Test C — late join, scénario strict (deux bouteilles pré-existantes)** : ✅ validé (run Krusty, 15:04:xx). Le host crée deux bouteilles (`77f4a4ef…` à 15:04:12.07, `1b130c42…` à 15:04:13.79) **avant** que Krusty ne se connecte (15:04:32.44) ; les deux sont restaurées par le joiner avec les mêmes `InstanceId` dès la connexion (deux logs `[WorldItem][Joiner][Restored]` distincts, même timestamp de connexion), aucun nouvel ID généré.
- **Test D — touche côté joiner** : ✅ validé, sur les deux runs (Lisa puis Krusty). Pressions distinctes de `B` côté joiner → un log `Spawn ignored: local instance is not host.` par pression, zéro bouteille créée par le joiner.
- **Test E — maintien de la touche côté host** : ✅ validé (confirmé manuellement par l'utilisateur) — maintenir `B` sur le host ne produit qu'un seul spawn, pas de spam continu.

Tous les tests A à E sont désormais validés sans réserve. Aucune erreur (`Log.Error`), aucune divergence d'ID, et le warning systématique retiré (voir ci-dessus) n'est réapparu dans aucun des logs fournis.

## `LootSpawnPointComponent` — V1 (mono-item, validé)

`LootSpawnPointComponent` (`Code/Items/Loot/LootSpawnPointComponent.cs`, namespace `Kodoku.Items`) est un marqueur de conception de niveau : placé dans une scène (maison, nature, bâtiment, zone d'exploration), il décide **uniquement côté host** si un `WorldItem` doit apparaître à son emplacement. Ce n'est **pas** un `WorldItem` lui-même — il ne porte aucune `ItemInstance`, aucun état `[Sync]`, et n'est pas networké : seule l'autorité de session (`Sandbox.Networking.IsHost`) détermine qui a le droit de l'évaluer, exactement comme pour la création autoritaire de `WorldItemComponent`. Le flux cible :

```
Zone chargée → seul le host évalue chaque LootSpawnPoint → tirage de SpawnChance (host)
→ clone du prefab résolu depuis Item.WorldPrefabOverride → NetworkSpawn()
→ WorldItemComponent.TryInitializeAuthoritativeNew() crée l'ItemInstance autoritaire
→ joiners et late joiners restaurent la même instance (mécanisme déjà validé ci-dessus)
```

**V1 volontairement minimale** : chaque point référence directement une seule `ItemDefinition` (`Item`), pas une table de loot. `LootTableDefinition`, les poids/rareté, les biomes/catégories de bâtiment, une seed de zone et le respawn automatique sont explicitement **reportés** — ils n'ont de sens qu'une fois plusieurs types d'items disponibles.

- **Configuration** (inspecteur, `[Group("Configuration")]`) : `Item` (`ItemDefinition`, obligatoire), `SpawnChance` (`float`, `[Range(0f, 1f)]`, défaut 1), `Quantity` (`int`, `[Range(1, 999)]`, défaut 1 — doit rester `≤ Item.MaxStack`).
- **Runtime** (lecture seule, `[Group("Runtime")]`) : `HasEvaluated`/`HasSpawned` (`bool`) — un point évalué une seule fois par le host reste marqué même si aucun item n'a été créé (chance ratée ou configuration invalide), pour empêcher toute réévaluation involontaire.
- **Autorité** : `Networking.IsHost`, pas `GameObject.IsProxy` (même choix que `WorldItemComponent`, voir ci-dessus). `LootSpawnPointComponent` n'étant pas lui-même un objet networké, `Networking.IsHost` est utilisé directement, sans le garde `GameObject.Network.Active` qu'utilise `WorldItemComponent` pour ses propres décisions de propriété — confirmé par la documentation officielle (`IsHost` vaut `true` soit si la session n'est pas active, soit si l'instance locale est le host ; voir `docs/official/sbox/networking/00-networking-overview.md`). Un joiner ne fait donc **aucun** tirage ni clone ; `OnStart()` sort immédiatement sans logger (non-comportement normal, pas une erreur).
- **`TrySpawn()`** — évaluation unique et idempotente, appelée depuis `OnStart()` côté host uniquement. Refuse (log + `false`, jamais d'exception) une seconde évaluation, un appel depuis un client non-host, `Item` nul, `Item.ItemId` vide, `Item.WorldPrefabOverride` absent, `Quantity` hors bornes (`< 1` ou `> Item.MaxStack`), ou `SpawnChance` hors `[0, 1]`. `HasEvaluated` passe à `true` dès la première validation d'autorité franchie, avant même l'issue du tirage — une seule évaluation, quel que soit le résultat.
- **Tirage** : `Game.Random.NextSingle() >= SpawnChance` → aucun spawn, aucun log (configuration valide, juste malchance — le log temporaire `[LootSpawnPoint][Host][Skipped]` utilisé pendant la validation a été retiré après les Tests A à G, voir ci-dessous). `Game.Random` confirmé par inspection directe des assemblies moteur (`Sandbox.Engine.dll`) comme une propriété statique de type `System.Random` — pas d'API inventée. `SpawnChance = 0` → jamais de spawn ; `SpawnChance = 1` → toujours un spawn (`NextSingle()` retourne `[0, 1)`, donc `>= 1` n'est jamais vrai).
- **Spawn** : réutilise exactement le flux déjà validé par l'ancien spawner debug — `Sandbox.GameObject.Clone(Item.WorldPrefabOverride, GameObject.WorldTransform)` (position/rotation du point lui-même) puis `NetworkSpawn()`. Si le clone n'a pas de `WorldItemComponent`, il est détruit et le spawn refusé (configuration de prefab invalide). `LootSpawnPointComponent` ne renseigne jamais lui-même une propriété `[Sync]` de `WorldItemComponent` ni ne crée de seconde `ItemInstance` — cette responsabilité reste entièrement à `WorldItemComponent.TryInitializeAuthoritativeNew()`.
- **Ordre d'initialisation de `Quantity`** : `WorldItemComponent.InitialQuantity` est lu une seule fois, au moment de `TryInitializeNew()` (appelée par `TryInitializeAuthoritativeNew()`), pas à `OnStart()` du prefab cloné. `LootSpawnPointComponent` assigne donc `worldItem.InitialQuantity = Quantity` **après** le clone mais **avant** d'appeler explicitement `TryInitializeAuthoritativeNew()` — cet appel explicite, identique au pattern déjà validé par l'ancien spawner debug, s'exécute de façon synchrone avant que `OnStart()` du `WorldItemComponent` cloné n'ait la moindre chance de s'exécuter (déféré à « avant le premier Update »), donc avant qu'il ne lise la valeur par défaut du prefab. Aucune seconde `ItemInstance` n'est créée : le garde `IsInitialized` de `WorldItemComponent.OnStart()` absorbe la tentative devenue no-op. **Validé avec une quantité réellement différente de celle du prefab** (Test G, `water_bottle.item` temporairement à `MaxStack = 2`, point à `Quantity = 2`) : une seule `WorldItem` créée, `Quantity = 2` côté host et joiner, même `InstanceId`, aucune initialisation intermédiaire à `Quantity = 1` observée.
- **Représentation éditeur** : `[Icon("casino")]` au niveau de la classe (icône Material Symbols affichée dans la liste de composants, confirmée par inspection des assemblies — pattern déjà utilisé par des dizaines de composants moteur, ex. `[Icon("mic")]` sur `Voice`). `DrawGizmos()` (méthode virtuelle de `Component`, confirmée par inspection) dessine une sphère filaire (`Gizmo.Draw.LineSphere`) et le nom du GameObject (`Gizmo.Draw.WorldText`) dans la vue scène — repère de conception uniquement, aucun modèle visible en jeu.

**Hors périmètre explicite de ce jalon** : `LootTableDefinition`, entrées pondérées, plusieurs catégories d'items par point, rareté, seed de zone, respawn automatique, persistance, sauvegarde, conteneurs/coffres, pickup, inventaire, interaction, IA.

**Installation manuelle dans `GameplayTest.scene`** (non modifiée automatiquement par une session Claude Code) : trois `LootSpawnPointComponent` (`LootSpawnPoint_WaterBottle_A`/`_B`/`_C`), `Item = water_bottle.item`. `A` et `B` à `SpawnChance = 1`/`Quantity = 1` ; `C` a servi aux Tests D et E (`SpawnChance = 0` puis `Quantity = 2` invalide) et a été remis à une configuration valide après les tests (`SpawnChance = 0.5`, `Quantity = 1`).

**Tests manuels A à G — validés par l'utilisateur à deux instances réelles (host + joiners successifs), 12 juillet 2026.** Les logs de succès temporaires (`[LootSpawnPoint][Host][Spawned]`/`[Host][Skipped]`) ayant été retirés du code après validation (voir « Tirage » ci-dessus), les comparaisons d'`InstanceId` (Tests A, B, C, G) ont été confirmées **par inspection directe de `WorldItemComponent.RuntimeInstanceId` dans l'éditeur** (propriété `[ReadOnly]` déjà exposée), pas par les logs :

- **Test A — host seul** : ✅ validé. Deux `LootSpawnPoint` à `SpawnChance = 1` → deux bouteilles, `RuntimeInstanceId` distincts, un seul spawn par point (logs `sbox-dev.log` du 2026-07-12 16:28:19 : un `[Host][Spawned]` par point, avant retrait des logs temporaires).
- **Test B — host + joiner déjà connecté à l'évaluation** : ✅ validé (rejoué après un premier run qui ne couvrait que le late join). `RuntimeInstanceId` identiques host/joiner pour chaque bouteille, distincts entre les deux bouteilles, `Quantity = 1` partout.
- **Test C — late join** : ✅ validé. Un joiner connecté ~2 min 30 après la création des deux bouteilles (`sbox-dev.log`/`sbox.log` du 2026-07-12 16:30) restaure les deux `WorldItem` existants avec leurs `InstanceId` d'origine (confirmé en éditeur), aucun troisième/quatrième item créé.
- **Test D — `SpawnChance = 0`** : ✅ validé. Aucune bouteille créée par le point concerné, `HasEvaluated = true`, `HasSpawned = false`, aucun warning.
- **Test E — `Quantity` invalide** (`Quantity = 2` avec `Item.MaxStack = 1`) : ✅ validé. Log réel obtenu : `[LootSpawnPoint] 'LootSpawnPoint_WaterBottle_C': Quantity (2) is out of range for 'kodoku.consumable.water_bottle' (MaxStack = 1) — cannot spawn.` — aucun spawn, warning clair, aucun crash.
- **Test F — joiner** : ✅ validé. Aucun log `[LootSpawnPoint]`/`[WorldItem]` côté joiner dans `sbox.log`, cohérent avec `OnStart()` qui sort silencieusement sur un client non-host — seul le host a évalué les points.
- **Test G — transmission réelle de `Quantity`** : ✅ validé (`water_bottle.item` temporairement à `MaxStack = 2`, un point à `Quantity = 2`/`SpawnChance = 1`). Une seule `WorldItem` créée, `Quantity = 2` confirmé côté host et joiner, même `InstanceId`. `water_bottle.item` remis à `MaxStack = 1` après le test — confirmé sans résidu (`git diff` vide sur ce fichier).

Aucune erreur (`Log.Error`) ni crash sur l'ensemble des sept tests.

## Système Inventory natif s&box — piste en cours d'évaluation

Le moteur s&box fournit un système natif d'inventaire et d'armes (`Sandbox.BaseInventoryComponent`/`BaseInventoryItem`/`BaseCombatWeapon`, plus la réserve de munitions abstraite `BaseAmmoResource`/`BaseAmmoPickup`), découvert le 2026-07-12. Il est organisé en slots (modes `Hotbar` ou `Buckets`), pas en grille spatiale, et peut fournir gratuitement pickup/drop/switch/transfert entre inventaires, autorité host et, pour les armes, prédiction de tir côté propriétaire — voir le résumé factuel complet dans [../research/SBOX_BUILTIN_INVENTORY_EVALUATION.md](../research/SBOX_BUILTIN_INVENTORY_EVALUATION.md).

Différence structurelle centrale avec l'architecture actuelle : un `BaseInventoryItem` natif est un GameObject/composant vivant en permanence dans la hiérarchie de l'inventaire, alors qu'`ItemInstance` (ci-dessus) est délibérément une classe C# pure, indépendante du moteur. Toute utilisation du système natif devrait clarifier laquelle des deux couches porterait l'identité stable, la quantité et la sauvegarde, pour éviter deux sources de vérité.

**Aucune décision d'adoption, de rejet ou d'architecture hybride n'est prise à ce stade.** Voir le document de recherche pour l'analyse complète (correspondance avec Kodoku, stratégies possibles, questions ouvertes, prototype recommandé non réalisé).

## Éléments encore ouverts

- Inventaire (conteneurs, transferts, équipement) : non implémenté.
- Validation de l'unicité globale de `ItemId` et des champs obligatoires vides (`ItemId`/`DisplayName`) — reportée à un futur registre/chargeur global, pas simulée par une logique fragile au niveau d'une seule ressource.
- Modèle d'autorité réseau pour la création/destruction/mutation d'`ItemInstance` (probable host-authoritative par cohérence avec ADR-0002, à confirmer au moment de la conception). `ItemDefinition` elle-même est une donnée statique, référencée par `ItemId`, et ne nécessite aucune réplication runtime.
- Réplication de `WorldItemComponent` : état minimal implémenté dans ce jalon (`NetworkInstanceId`/`NetworkItemId`/`NetworkQuantity`, host-authoritative), **validé par un test réel à deux instances, Tests A à E ✅** — voir « Tests manuels » ci-dessus.
- Support natif de `Guid` par `[Sync]` — non confirmé (aucun précédent trouvé dans le code moteur inspecté ni dans ce projet) ; contourné par une représentation en chaîne canonique pour `NetworkInstanceId`. À revisiter si un jour confirmé, pour simplifier.
- RPC de spawn demandé par un joiner — aucun mécanisme de spawn (debug ou gameplay) n'existe plus dans le code depuis le retrait du spawner debug ; reporté, hors périmètre de ce jalon.
- Modèle d'autorité réseau pour la mutation d'une `ItemInstance` déjà créée (`TrySetQuantity`/`TryAddQuantity`/`TryRemoveQuantity` restent localement appelables sans aucune garde réseau — un futur système d'inventaire devra les entourer d'une autorité explicite, probablement `[Rpc.Host]`).
- Portée de la réplication des conteneurs (contenu d'un conteneur networké en temps réel, ou seulement son existence).
- Cas de test `ItemInstance` documentés mais non exécutables (aucune infrastructure de tests automatisés dans le projet actuel — voir `CLAUDE.md`) : création valide ; restauration avec `InstanceId` conservé ; refus d'une `Definition` nulle ; refus d'un `InstanceId` `Guid.Empty` en restauration ; refus d'une quantité à 0 ou négative ; refus d'une quantité supérieure à `MaxStack` ; ajout valide ; retrait valide ; refus d'un dépassement de `MaxStack` ; refus d'un retrait ramenant la quantité à 0 ; stack accepté entre deux instances de même `ItemDefinition` avec `MaxStack > 1` ; stack refusé si `MaxStack = 1` ; stack refusé entre définitions différentes.
- Cas de test `WorldItemComponent` (non networké) — trois validés manuellement lors du jalon précédent : initialisation valide ; deux instances → deux `InstanceId` différents ; refus si `InitialQuantity > Definition.MaxStack` (warning, sans crash). Non revalidés depuis la réécriture de l'autorité (`IsProxy` → `Networking.IsHost`).
- Cas de test réseau de ce jalon (Tests A à E, y compris le scénario strict du Test C) — tous validés manuellement à deux instances le 2026-07-12 (voir « Tests manuels » ci-dessus).
- `LootTableDefinition` (entrées pondérées, plusieurs items par point, rareté/tiers, biomes/catégories de bâtiment, seed de zone) — reportée volontairement tant qu'un seul type d'item (`water_bottle`) existe ; `LootSpawnPointComponent` V1 référence directement une `ItemDefinition` unique.
- Respawn automatique du loot et persistance de zone (quels points ont déjà été évalués/vidés doivent survivre à une sauvegarde) — non implémentés, hors périmètre de ce jalon.
- Cas de test `LootSpawnPointComponent` (Tests A à G) — tous validés manuellement à deux instances le 2026-07-12, y compris la transmission réelle de `Quantity` (Test G) — voir « Tests manuels A à G » ci-dessus.
