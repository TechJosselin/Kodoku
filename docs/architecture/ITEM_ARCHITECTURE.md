# Architecture des items

**Statut : `ItemDefinition` (fondation statique), `ItemInstance` (état runtime minimal) et `WorldItemComponent` (représentation monde minimale, test local uniquement) implémentées, non encore validées par compilation/test dans l'éditeur — voir [../status/CURRENT_STATE.md](../status/CURRENT_STATE.md).** L'inventaire (conteneurs, transferts, équipement) reste une architecture visée, non implémentée. Seuls des PNG d'icônes de l'ancienne UI ont été conservés au-delà du code (voir [../development/ASSET_MIGRATION.md](../development/ASSET_MIGRATION.md)).

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

## `WorldItemComponent` — V1 (test local uniquement, non validé multiplayer)

`WorldItemComponent` (`Code/Items/World/WorldItemComponent.cs`, namespace `Kodoku.Items`) relie `ItemDefinition` → `ItemInstance` → une représentation dans une scène, pour un premier test observable dans l'éditeur. Ce n'est **pas** un système de ramassage/interaction/inventaire — voir « Hors périmètre » ci-dessous.

- **Configuration** (inspecteur) : `Definition` (`ItemDefinition`, obligatoire), `InitialQuantity` (int, défaut 1, `[Range(1, 999)]` — doit rester `≤ Definition.MaxStack`, jamais clampée silencieusement : une valeur invalide empêche l'initialisation avec un message explicite plutôt que d'être corrigée en silence).
- **Runtime** (lecture seule, `[ReadOnly]`) : `IsInitialized`, `RuntimeInstanceId`, `RuntimeItemId`, `RuntimeQuantity` — dérivées de `Instance`, jamais une copie éditable de l'`ItemInstance` elle-même.
- **`Instance`** (`ItemInstance`, lecture seule depuis le code) : seule source de vérité après initialisation. `TryInitializeFromInstance` réassigne `Definition` depuis `Instance.Definition` si nécessaire pour garantir qu'elles ne divergent jamais (`WorldItemComponent.Definition` configuré ≠ `Instance.Definition` restaurée → refus explicite, pas de correction silencieuse).

**Deux chemins d'initialisation**, chacun refusant une seconde initialisation, une `Definition`/instance nulle, ou une quantité hors bornes (`Log.Warning` + `bool` retourné, jamais d'exception — cohérent avec le style `Try*` déjà établi pour `ItemInstance`) :
- `TryInitializeNew()` — appelle `ItemInstance.CreateNew(Definition, InitialQuantity)`. Utilisé par `OnStart()` pour ce premier jalon de test local.
- `TryInitializeFromInstance(ItemInstance instance)` — prépare les besoins futurs (chargement, réplication, spawn depuis un inventaire, drop) sans les implémenter.

**Décision d'autorité pour ce jalon (à confirmer, pas une ADR)** : la réplication de `Instance` n'existe pas encore. `OnStart()` distingue :
- `GameObject.Network.Active == false` (objet non networké) → `TryInitializeNew()` appelé directement, sûr pour un test local.
- `GameObject.Network.Active == true` et `IsProxy == true` → **aucune instance créée**, `Log.Warning` uniquement. `IsProxy` (sémantique confirmée dans l'API : vrai si networké et simulé par un autre client, ou par le host si non possédé) est utilisé comme signal de « suis-je celui qui simule réellement cet objet ? », plutôt que de coder en dur `Networking.IsHost` — un choix host-only n'est pas encore tranché pour la création d'`ItemInstance` (voir Éléments encore ouverts), et `IsProxy` est la donnée déjà confirmée et déjà utilisée ailleurs dans le projet (`KodokuPlayerComponent`) pour cette distinction.
- `GameObject.Network.Active == true` et `IsProxy == false` (propriétaire, ou host simulant un objet non possédé) → `TryInitializeNew()` appelé, comme le cas non networké. **Non testé à deux instances** — sans réplication, un vrai test host+client verrait uniquement le host (ou le propriétaire) initialisé, tout proxy restant volontairement vide. Le test multi-instances de ce jalon se limite donc à un GameObject non networké.

**Prefab de la bouteille** : `WorldItemComponent` ajouté à `Assets/Prefabs/Items/Consumables/Drinks/water_bottle.prefab` (`Definition` → `water_bottle.item`, `InitialQuantity = 1`), aux côtés des composants visuels/physiques déjà présents (`ModelRenderer`, `ModelCollider`, `Rigidbody`). Rien d'autre ajouté au prefab.

**Validé manuellement dans l'éditeur** (GameObject non networké uniquement — voir ci-dessus pour ce qui reste non testé) : `InitialQuantity = 1` crée une `ItemInstance` correctement ; deux bouteilles instanciées produisent deux `InstanceId` distincts ; une configuration invalide (`InitialQuantity = 2` avec `Definition.MaxStack = 1`) refuse l'initialisation avec un `Log.Warning`, sans crash. **Le networking (comportement sur un GameObject networké, réplication vers un proxy) n'est pas validé** — voir « Décision d'autorité » ci-dessus, resté non testé à deux instances.

**Hors périmètre explicite de ce jalon** : raycast, interaction, pickup, drop, inventaire, placement de grille, loadout, propriétaire/`PlayerId`, conteneur parent, durabilité, eau restante, consommation, synchronisation réseau complète, RPC, sauvegarde, registre d'items, spawner global, respawn.

## Éléments encore ouverts

- Inventaire (conteneurs, transferts, équipement) : non implémenté.
- Validation de l'unicité globale de `ItemId` et des champs obligatoires vides (`ItemId`/`DisplayName`) — reportée à un futur registre/chargeur global, pas simulée par une logique fragile au niveau d'une seule ressource.
- Modèle d'autorité réseau pour la création/destruction/mutation d'`ItemInstance` (probable host-authoritative par cohérence avec ADR-0002, à confirmer au moment de la conception). `ItemDefinition` elle-même est une donnée statique, référencée par `ItemId`, et ne nécessite aucune réplication runtime.
- Réplication de `WorldItemComponent.Instance` — non implémentée ; un proxy réseau reste volontairement non initialisé jusqu'à ce qu'un mécanisme de réplication existe (voir section `WorldItemComponent` ci-dessus). Le choix de qui a le droit de créer une instance sur un objet networké non-proxy (propriétaire vs. host strictement) n'est pas encore tranché.
- Portée de la réplication des conteneurs (contenu d'un conteneur networké en temps réel, ou seulement son existence).
- Cas de test `ItemInstance` documentés mais non exécutables (aucune infrastructure de tests automatisés dans le projet actuel — voir `CLAUDE.md`) : création valide ; restauration avec `InstanceId` conservé ; refus d'une `Definition` nulle ; refus d'un `InstanceId` `Guid.Empty` en restauration ; refus d'une quantité à 0 ou négative ; refus d'une quantité supérieure à `MaxStack` ; ajout valide ; retrait valide ; refus d'un dépassement de `MaxStack` ; refus d'un retrait ramenant la quantité à 0 ; stack accepté entre deux instances de même `ItemDefinition` avec `MaxStack > 1` ; stack refusé si `MaxStack = 1` ; stack refusé entre définitions différentes.
- Cas de test `WorldItemComponent` — trois validés manuellement en éditeur (non networké, voir section `WorldItemComponent` ci-dessus) : initialisation valide ; deux instances → deux `InstanceId` différents ; refus si `InitialQuantity > Definition.MaxStack` (warning, sans crash). Documentés mais non exécutés (aucune infrastructure de tests automatisés, et networking non testable sans deuxième instance dédiée à ce jalon) : refus si `Definition` nulle ; refus d'une seconde initialisation ; `TryInitializeFromInstance` aligne `Definition` sur `Instance.Definition` ; `TryInitializeFromInstance` refuse une incohérence entre `Definition` configurée et `Instance.Definition` ; proxy réseau reste non initialisé avec un seul warning (pas de log répété par frame).
