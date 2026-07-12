# Architecture des items

**Statut : `ItemDefinition` (fondation statique) implémentée, non encore validée par compilation/test dans l'éditeur — voir [../status/CURRENT_STATE.md](../status/CURRENT_STATE.md).** `ItemInstance`, la représentation dans le monde (`WorldItemComponent`) et l'inventaire restent une architecture visée, non implémentée. Seuls des PNG d'icônes de l'ancienne UI ont été conservés au-delà du code (voir [../development/ASSET_MIGRATION.md](../development/ASSET_MIGRATION.md)).

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

## Éléments encore ouverts

- `ItemInstance`, `WorldItemComponent`, inventaire : non implémentés — voir ci-dessus.
- Validation de l'unicité globale de `ItemId` et des champs obligatoires vides (`ItemId`/`DisplayName`) — reportée à un futur registre/chargeur global, pas simulée par une logique fragile au niveau d'une seule ressource.
- Modèle d'autorité réseau pour la création/destruction d'`ItemInstance` (probable host-authoritative par cohérence avec ADR-0002, à confirmer au moment de la conception). `ItemDefinition` elle-même est une donnée statique, référencée par `ItemId`, et ne nécessite aucune réplication runtime.
- Portée de la réplication des conteneurs (contenu d'un conteneur networké en temps réel, ou seulement son existence).
