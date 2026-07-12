# Évaluation du système Inventory/Weapons natif de s&box

**Mise à jour du 12 juillet 2026 — cette évaluation est conservée comme document de recherche historique.** Kodoku n'a pas adopté `BaseInventoryComponent`/`BaseInventoryItem` comme architecture de production. La décision actuelle est de construire un inventaire Kodoku personnalisé autour de `ItemDefinition`, `ItemInstance`, `InventoryContainer` et `InventoryPlacement` (noyau local validé, Tests A à O, 15/15 — voir [../architecture/ITEM_ARCHITECTURE.md](../architecture/ITEM_ARCHITECTURE.md)). Le système natif reste une référence pour les principes d'autorité host, de validation avant mutation, d'opérations atomiques et de transfert qu'il implémente déjà. Il est **adapté aux inventaires à slots/hotbar et aux armes natives**, mais **non adapté directement à la grille spatiale, aux stacks physiques, aux munitions/chargeurs physiques et à la persistance visée par Kodoku** — voir aussi le spike expérimental qui a suivi cette étude, [NATIVE_INVENTORY_SPIKE_RESULTS.md](NATIVE_INVENTORY_SPIKE_RESULTS.md), et [ADR-0005](../decisions/ADR-0005-CUSTOM-INVENTORY.md) pour la décision complète. Le contenu ci-dessous, non modifié depuis l'étude originale du 12 juillet 2026, reste tel quel comme référence factuelle.

**Nature de ce document : étude, pas une architecture adoptée** au moment de sa rédaction. Aucune décision d'implémentation n'était prise à l'origine. Voir la section 7 pour la marche à suivre proposée (non réalisée telle quelle — un spike différent a été mené, voir [NATIVE_INVENTORY_SPIKE_RESULTS.md](NATIVE_INVENTORY_SPIKE_RESULTS.md)) et la section 6 pour les questions qui restent ouvertes.

**Sources consultées** :

- Guide officiel `Facepunch/sbox-docs`, branche `master`, commit `d073589fd123683a94f301415a17f18e1804a2d2` (consulté le 2026-07-12) — pages `docs/gameplay/inventory-weapons/{index,inventory,weapons,weapon-models}.md`. Confiance : **haute** (source officielle, mais un guide narratif, pas la référence complète).
- Commentaires XML de `Sandbox.Engine.dll`/`Sandbox.Engine.xml`, installation locale s&box, `buildid` Steam `24152323` (fichier DLL daté du 2026-07-11), lus directement (`grep`/lecture ciblée), pas via `sbox.game/api/*` — voir note ci-dessous. Confiance : **haute** (source primaire, exacte pour cette build précise de l'éditeur, mais peut différer d'une build future).
- `sbox.game/api/Sandbox.BaseInventoryComponent` et `sbox.game/api/Sandbox.BaseAmmoResource` : **tentative infructueuse**. Ces pages sont rendues côté client (SPA) et n'ont renvoyé aucun contenu exploitable via récupération HTTP simple — non utilisées comme source. Les commentaires XML de l'assembly locale ci-dessus ont servi de substitut, plus fiable qu'une supposition.
- Aucune classe `BaseCombatWeapon`/`BaseInventoryComponent` n'existe en tant que source C# lisible dans `addons/base/code/` de l'installation locale (recherche effectuée, aucun résultat) : ces types sont compilés dans `Sandbox.Engine.dll`, pas fournis en source dans cette installation.

**Ce qui a déclenché cette étude** : découverte, dans l'éditeur s&box, de composants natifs affichés sous les noms « Inventory », « Inventory Item » et « Ammo Pickup ». Les deux premiers correspondent à `Sandbox.BaseInventoryComponent`/`Sandbox.BaseInventoryItem` (confirmé par les sources ci-dessus). Le troisième correspond à `Sandbox.BaseAmmoPickup`, confirmé par les commentaires XML de l'assembly locale (un pickup consommable qui alimente la réserve de munitions du ramasseur sans occuper de slot — voir section 2.4) — **pas confirmé via le guide narratif seul**, qui ne mentionne aucun composant de pickup de munitions physique.

## 1. Contexte

Kodoku possède déjà une fondation d'items propre, implémentée et validée par tests réels à deux instances (voir [ITEM_ARCHITECTURE.md](../architecture/ITEM_ARCHITECTURE.md)) :

```text
ItemDefinition (GameResource .item, données statiques)
    ↓
ItemInstance (classe C# runtime pure : InstanceId, Definition, Quantity)
    ↓
WorldItemComponent (représentation monde, réplication réseau host-authoritative minimale)
    ↓
LootSpawnPointComponent (génération de loot host-authoritative, V1 mono-item)
```

Aucun inventaire joueur, pickup interactif, conteneur, équipement ou arme n'est implémenté à ce jour. Cette découverte intervient donc **avant** la construction de l'inventaire V2 (étape 8 de la [ROADMAP.md](../status/ROADMAP.md)), ce qui rend son évaluation peu coûteuse : aucun code Kodoku existant ne serait à défaire si le système natif s'avérait pertinent.

## 2. Résumé factuel du système natif

### 2.1 `Sandbox.BaseInventoryComponent`

Composant de type inventaire à slots, posé sur le GameObject qui doit pouvoir porter des objets (typiquement le pawn joueur).

- **Slots** : `MaxSlots` (int, slots `0..MaxSlots-1`) et `Behaviour` (`InventoryBehaviour.Hotbar` ou `.Buckets`).
  - **Hotbar** : un item par slot ; si le slot préféré (`BaseInventoryItem.PreferredSlot`) est pris, l'item va dans le premier slot vide.
  - **Buckets** : un slot est une catégorie, plusieurs items peuvent y cohabiter, toujours dans leur slot préféré ; le tri interne au bucket suit `SlotOrder`.
- **État interrogeable** : `Items` (tous les items présents, triés par slot puis `SlotOrder` — inclut les items désactivés mais pas ceux en attente de destruction ; **exclut explicitement les items d'un inventaire imbriqué**, voir 2.5), `ActiveItem` (item déployé, `null` si aucun — mutation host-authoritative, passer par `Switch`), `GetSlot(n)`, `GetSlotItems(n)`, `FindEmptySlot()`, `GetItem<T>()`, `HasItem<T>()`, `GetBestItem()` (item le plus haut en `Value` switchable).
- **Opérations** : `Add(item, slot)`, `Pickup(GameObject, slot)`/`Pickup(string, slot)` (host only, renvoie l'item spawné ou `null`), `Remove(item)` (détruit), `Drop(item)` (délègue le placement monde à l'item lui-même via `BaseInventoryItem.OnDrop`, puis switch vers le meilleur item restant), `Transfer(item, autreInventaire, slot)` (voir 2.3), `Switch(item, bool)`/`ForceSwitch`/`ForceHolster`/`SwitchToBest()`, `MoveSlot(from, to)`.
- **Hooks d'extension côté inventaire** : `OnAdding`, `OnItemAdded`, `OnRemoving`, `OnDropping`, `OnMovingSlot` — permettent de refuser ou réagir à chaque opération.
- **Ramassage monde** (`PickupMode` : `None`/`Touch`/`Use`, `PickupRadius` pour `Touch`) : `PickupWorldItem(item)`, `ShouldAutoSwitchTo(item)`, `CanPickupWorldItem(item)`. **Confirmé par commentaire XML explicite** : « the base deliberately has no range check (like shot claims, what's plausible is game policy) — override to add range or line-of-sight rules. » Autrement dit, **aucun contrôle de distance ou de ligne de vue par défaut** ; c'est un point d'extension, pas une garantie native.
- **Loadout** : `UsesLoadout`, `GiveOnStart`, `StartingItems` (prefabs), `StartingAmmo`, `GiveLoadout()` (host only, à appeler manuellement, ex. au respawn).
- **Munitions** : réserve abstraite portée par l'inventaire (pas par l'arme) — `GetAmmo`/`HasAmmo`/`GiveAmmo`/`SetAmmo`/`TakeAmmo`, tous paramétrés par un type `BaseAmmoResource` (voir 2.4).
- **Autorité** : confirmé à la fois par le guide (« All of this is host authoritative. The owning client can call these too, they route through the host. ») et par les commentaires XML (`ActiveItem` explicitement « host authoritative »).
- **Duplicats** : ramasser une arme déjà détenue ne crée pas de second exemplaire — le doublon donne ses munitions à la réserve.
- **Aucune UI** : confirmé explicitement (« There's no built in inventory or hotbar UI. Build your own from `Items`, `GetSlot` and `ActiveItem`. »).
- **Boucle interne** : `ManualPumping` + `Pump()` — si non activé, le composant tourne sa propre boucle interne ; sinon l'appelant doit invoquer `Pump()` lui-même (ex. pour l'intégrer à une boucle d'input personnalisée).

### 2.2 `Sandbox.BaseInventoryItem`

Classe de base pour tout ce qu'un `BaseInventoryComponent` peut porter.

- **Présentation** : `DisplayName`, `DisplayIcon`, `Value` (détermine ce qu'un auto-switch/`GetBestItem` considère « meilleur »), `PreferredSlot`, `SlotOrder`, `Slot` (lecture, slot courant), `Inventory` (référence à l'inventaire propriétaire), `IsActive`.
- **Hooks de cycle de vie** : `OnEquipped`/`OnHolstered` (tous les pairs, au déploiement/rangement), `OnControl` (chaque frame, client propriétaire uniquement, tant que déployé — lecture d'input), `OnAdded`/`OnRemoved` (host, à l'entrée/sortie de l'inventaire), `OnDrop` (placement monde ; retourner `false` refuse le drop), `OnCanPickup` (refus de ramassage), `OnHolstering` (donne un droit de veto à l'item sortant lors d'un switch).
- Un `BaseInventoryItem` **est un GameObject/composant** vivant dans la hiérarchie de l'inventaire — pas une donnée pure indépendante du moteur (différence structurelle centrale avec `ItemInstance` de Kodoku, voir section 3).

### 2.3 `Transfer` — confirmé

`BaseInventoryComponent.Transfer(item, autreInventaire, slot)`, confirmé par commentaire XML : déplace un item d'un inventaire à un autre **sans drop monde et sans destruction**. Les mêmes portes d'entrée s'appliquent (`OnRemoving` côté source, `OnAdding` + le droit de veto propre de l'item côté destination) ; un refus laisse tout inchangé. **La réserve de munitions ne suit pas l'item** — elle reste sur l'inventaire d'origine, puisqu'elle appartient à l'inventaire, pas à l'item. La destination ne déploie pas automatiquement l'item transféré (peut être un coffre). **Host only** — le commentaire précise explicitement que la politique de qui peut déplacer quoi vers où est laissée au jeu, qui doit router ses propres requêtes vers cette méthode.

### 2.4 Munitions — `BaseAmmoResource` et `BaseAmmoPickup`

Deux notions distinctes, confirmées séparément :

- **`Sandbox.BaseAmmoResource`** : un `GameResource`/asset (créable depuis l'Asset Browser) avec `Title`, `Icon`, `MaxReserve`. Représente un *type* de munition abstrait — pas un objet physique. La réserve réelle (un entier par type) vit sur `BaseInventoryComponent`, partagée entre toutes les armes qui utilisent ce type.
- **`Sandbox.BaseAmmoPickup`** : un composant de pickup monde (le composant « Ammo Pickup » vu dans l'éditeur), confirmé par commentaire XML : « A world pickup that tops up the collector's reserve ammo pool instead of taking a slot. » Propriétés `AmmoType` (`BaseAmmoResource`) et `Amount`. Ne prend jamais de slot d'inventaire — vient uniquement incrémenter la réserve abstraite via `GiveAmmo`. Refuse silencieusement (pas de prompt, reste au sol en mode `Touch`) si la réserve du ramasseur est déjà pleine (`OnCanPickup`).
- **Aucune notion de chargeur physique ramassable ou de munition comme objet du monde individuel** n'a été trouvée dans les sources consultées — uniquement une réserve abstraite (`BaseAmmoResource`/`GetAmmo`/`GiveAmmo`/`TakeAmmo`) et un chargeur logique par arme (`Clip1`/`Clip2`, voir 2.6). Le système natif ne fournit donc **pas** de « munitions physiques » au sens d'un `ItemInstance` de munitions empilable dans un inventaire à grille — c'est une divergence structurelle à noter pour un extraction shooter où les munitions sont typiquement un objet d'inventaire comme un autre.

### 2.5 Inventaires imbriqués — confirmé, portée non détaillée

Confirmé par un commentaire XML sur `BaseInventoryComponent.Items` : « Items inside a nested inventory (a held backpack) belong to that inventory, not this one. » Cela confirme qu'un `BaseInventoryItem` peut lui-même porter un `BaseInventoryComponent` (ex. un sac à dos), et que le moteur distingue explicitement les deux niveaux (l'inventaire parent ne voit pas le contenu de l'inventaire imbriqué dans sa propre liste `Items`).

**Non confirmé par les sources consultées** : le comportement précis de réplication d'un inventaire imbriqué (visibilité pour les autres joueurs, late join, profondeur maximale), et si `Transfer` fonctionne entre un inventaire de premier niveau et un inventaire imbriqué sans règle supplémentaire. Le guide narratif ne mentionne pas du tout les inventaires imbriqués — seule la XML de l'assembly les confirme. **À vérifier par un prototype réel**, pas supposé.

### 2.6 `Sandbox.BaseCombatWeapon`

Étend `BaseInventoryItem`. Base pour armes, armes de mêlée et outils.

- **Chargeurs/munitions** : `UsesAmmo`, `UsesClips`, `ClipMaxSize`, `StartingAmmo`, `PrimaryClipSize` (dérivée), `PrimaryAmmoType`/`SecondaryAmmoType` (`BaseAmmoResource`, si vide → réserve « bottomless » selon le guide, le rythme de rechargement du chargeur restant néanmoins forcé), `Clip1`/`Clip2` (état du chargeur, vérité côté host), `Ammo1`/`Ammo2` (réserve accessible au tir primaire/secondaire), `MaxReserveAmmo`, `GetReserveAmmo`/`TakeReserveAmmo`/`HasPrimaryAmmo`/`HasSecondaryAmmo`/`TakePrimaryAmmo`/`TakeSecondaryAmmo`.
- **Rechargement** : rechargement complet par défaut ; `IncrementalReloading` (type shotgun, une munition à la fois — mentionné dans le guide, non retrouvé nommément dans les commentaires XML consultés) ; `CanCancelReload` ; `AutoReload`.
- **Tir** : `PrimaryDelay`/`SecondaryDelay`, `PrimaryAutomatic`/`SecondaryAutomatic`, `DeployTime`, `NextPrimaryFire`/`NextSecondaryFire` + `SetNextPrimaryFire`/`SetNextSecondaryFire`/`SetNextFire`, `WantsPrimaryAttack`/`WantsSecondaryAttack`/`WantsReload`, `CanPrimaryAttack`/`CanSecondaryAttack`, `FirePrimary` (respecte le taux de tir ; `PrimaryAttack` en direct le contourne), `Think()` (par frame, pour charge-up/spin-down), `DryFire()`.
- **Autorité et prédiction** : confirmé par le guide et cohérent avec les commentaires XML — « host authoritative and owner predicted ». Le porteur tire immédiatement en local (prédiction), les coups sont envoyés au host comme des « claims » (`ShotClaim`, `PelletClaim` — position de tir, ce qui a été touché). `OnValidateShotClaim(ref ShotClaim)` est le point d'extension anti-triche explicite : le commentaire XML précise qu'il sert à *clamp* `Damage`/`Force` contre les propres statistiques de l'arme et à vérifier `Sequence`/temps d'arrivée contre le taux de tir, et à vérifier chaque origine — mais **la validation de base fournie n'est pas détaillée exhaustivement dans les sources consultées** ; à ne pas supposer complète sans lecture du corps de méthode (non accessible, compilé).
- **Effets** : `OnShootEffects(ShotEffect)` (son, muzzle flash, animation — surchargeable), `AttackSound`/`DryFireSound`.
- **Mêlée/outils** : mêlée = traces courte portée au lieu de balistique ; outils (caméra, kit de soin, etc.) = ammo désactivée + `PrimaryAttack` surchargé. Les deux conservent gratuitement déploiement/rangement/viewmodel/switch/gestion d'input.
- **Modèles** (`weapon-models.md`, guide uniquement — non recoupé avec la XML dans cette étude) : `BaseWeaponModel`, viewmodel (1re personne, porteur) et world model (attaché à l'os de la main, vu par les autres) séparés, chacun son propre composant. `HoldType`/`Handedness` pilotent les paramètres d'animation. `MuzzleGameObject`/`ShellEjectGameObject` marquent les points d'origine des effets. Prefabs par défaut pour muzzle flash/éjection de douille/traceurs.

## 3. Correspondance avec Kodoku

| Concept s&box | Concept Kodoku actuel ou futur | Compatibilité apparente | Point à vérifier |
|---|---|---|---|
| `BaseInventoryComponent` (slots Hotbar/Buckets) | Futur inventaire joueur (probable grille spatiale, non tranché — voir [OPEN_QUESTIONS.md](../status/OPEN_QUESTIONS.md)) | Partielle | Le système natif n'offre aucune notion de grille 2D largeur/hauteur/rotation ; `ItemDefinition.GridWidth`/`GridHeight`/`CanRotate` existent déjà côté Kodoku et n'ont pas d'équivalent natif direct |
| `BaseInventoryItem` (GameObject/composant vivant dans l'inventaire) | `ItemInstance` (classe C# pure) + `WorldItemComponent` (représentation monde) | Non directe | `BaseInventoryItem` est un GameObject networké en permanence (y compris rangé) ; `ItemInstance` de Kodoku est explicitement une donnée indépendante du moteur, sans GameObject tant qu'il n'est pas dans le monde |
| `BaseCombatWeapon` | Futur système d'armes (non commencé) | Potentiellement utile | Chargeurs (`Clip1`/`Clip2`), prédiction de tir, `ShotClaim`/`OnValidateShotClaim` déjà câblés ; à évaluer si le modèle de dégâts/`Ballistics` convient au gameplay visé |
| `BaseAmmoResource` (réserve abstraite par type) | Futurs stacks de munitions (probables `ItemInstance` empilables, `MaxStack > 1`) | Potentiellement incompatible | La réserve native est un entier par type sur l'inventaire, pas un objet ramassable/empilable dans une grille ; un système de munitions « objet d'inventaire » à la Tarkov devrait contourner ou ignorer ce mécanisme |
| `BaseAmmoPickup` | Futur pickup de munitions (non commencé) | Partielle | Ne prend jamais de slot ni ne crée d'`ItemInstance` — incompatible tel quel avec un modèle où les munitions occupent un emplacement d'inventaire |
| Inventaire imbriqué (`Items` exclut le contenu d'un inventaire porté) | Sacs/conteneurs Kodoku (conteneurs du monde, sac à dos — voir [ITEM_ARCHITECTURE.md](../architecture/ITEM_ARCHITECTURE.md)) | Prometteur mais non validé | Confirmé comme existant (commentaire XML), mais réplication précise, late join et profondeur non détaillés dans les sources consultées |
| `Transfer(item, autreInventaire, slot)` | Futur transfert entre inventaires (joueur ↔ coffre, joueur ↔ joueur) | Prometteur mais non validé | Host only, confirmé ; comportement en cas de deux transferts simultanés vers le même slot non détaillé dans les sources |
| `PickupMode`/`CanPickupWorldItem` (aucun contrôle de distance/LOS par défaut) | Futur pickup interactif Kodoku | À concevoir dans tous les cas | Confirmé explicitement : le contrôle de portée/visibilité est un point d'extension à la charge du jeu, pas une garantie native |
| Aucune UI d'inventaire/hotbar native | Future UI d'inventaire Kodoku | Neutre | Confirmé — `Items`/`GetSlot`/`ActiveItem` sont conçus pour être lus depuis du HUD custom à chaque frame |

## 4. Trois stratégies possibles

### A. Système natif uniquement

**Avantages possibles** : networking déjà fourni (host authority, routage des requêtes du propriétaire) ; pickup/drop/switch/transfer déjà implémentés et testés par Facepunch ; armes et prédiction de tir disponibles sans code réseau à écrire ; moins de code personnalisé à maintenir et à valider à deux instances.

**Limites probables** : système à slots (Hotbar) ou buckets, pas de grille 2D spatiale documentée nulle part dans les sources consultées — un gameplay extraction-shooter à la Tarkov (grille, rotation, poids, stacking par emplacement précis) ne correspond pas directement à ce modèle. Chaque `BaseInventoryItem` est un GameObject vivant en permanence dans la hiérarchie de l'inventaire, y compris rangé — potentiel coût mémoire/réseau si un joueur porte un grand nombre d'items simultanément, non mesuré. L'état d'un item est fortement lié au cycle de vie du GameObject moteur, à l'opposé du choix déjà fait pour `ItemInstance` (classe C# pure, indépendante du moteur).

### B. Architecture hybride

Schéma conceptuel à analyser, **non validé** :

```text
ItemDefinition Kodoku (inchangé)
        ↓
ItemInstance Kodoku — identité et état persistant (inchangé)
        ↓
Adaptateur / composant dérivé de BaseInventoryItem (nouveau, expérimental)
        ↓
BaseInventoryComponent — transport réseau et cycle pickup/drop/transfer (natif)
```

Cette option semble potentiellement intéressante — elle réutiliserait le transport réseau déjà écrit et testé par Facepunch tout en conservant `ItemInstance` comme source de vérité pour l'identité et l'état persistant — mais reste **explicitement non validée**.

**Risque central identifié, à trancher avant tout code réel** : créer deux sources de vérité pour la même donnée.

| Responsabilité | Couche candidate côté `ItemInstance` Kodoku | Couche candidate côté natif `BaseInventoryItem`/`BaseInventoryComponent` |
|---|---|---|
| Identité de l'instance | `InstanceId` (`Guid`) | Aucune notion native équivalente — le GameObject/composant lui-même sert d'identité implicite |
| Quantité | `Quantity` (invariant validé) | Aucune notion native — pas de propriété `Quantity` sur `BaseInventoryItem` dans les sources consultées |
| Position dans le conteneur | Non porté par `ItemInstance` (futur système) | `Slot`/`SlotOrder`, natif |
| État de l'objet (ex. durabilité) | Futur, non implémenté | Non standard, à ajouter par sous-classe |
| Destruction | Contrôlée par le futur système propriétaire | `Remove(item)` détruit le GameObject |
| Sauvegarde | Futur, non implémenté des deux côtés | Aucun mécanisme natif de sauvegarde documenté dans les sources consultées |
| Réplication | `[Sync(SyncFlags.FromHost)]` explicite, minimal, déjà validé pour `WorldItemComponent` | Native, host authoritative, portée exacte des champs synchronisés non auditée dans cette étude |

Chacune de ces lignes est une question ouverte tant qu'aucun prototype n'a été construit — voir section 6.

### C. Inventaire entièrement personnalisé

**Avantages** : contrôle total de la grille spatiale (largeur/hauteur/rotation, déjà partiellement modélisé côté `ItemDefinition`) ; état indépendant des GameObjects, cohérent avec le choix déjà fait pour `ItemInstance` ; persistance et sérialisation entièrement maîtrisées par Kodoku.

**Coûts** : pickup, transfert, ownership, réplication et prédiction (si armes) à concevoir, écrire et valider à deux instances intégralement — rien de gratuit. Risque réel de recréer, avec plus de bugs potentiels, des fonctions déjà fournies et déjà éprouvées par le moteur (`Transfer`, autorité host, prédiction de tir).

## 5. Écarts fonctionnels à étudier

| Écart | Statut |
|---|---|
| Grille 2D largeur/hauteur | Non native — confirmé absent des sources consultées |
| Rotation d'item | Non native |
| Stacks (empilement) | Non trouvé de propriété native équivalente à `MaxStack`/`Quantity` sur `BaseInventoryItem` |
| Poids | Non trouvé de propriété native de poids sur `BaseInventoryComponent`/`BaseInventoryItem` dans les sources consultées |
| Durabilité | Non native, à ajouter par sous-classe si besoin |
| Quantité interne d'un consommable (ex. eau restante) | Non native |
| Équipement par emplacement corporel | Non trouvé dans les sources consultées — `PreferredSlot`/`Behaviour.Buckets` catégorisent des slots logiques, pas des emplacements corporels typés |
| Sacs/conteneurs imbriqués | Confirmé existant (2.5), portée non détaillée |
| Chargeurs physiques | Non natif — `Clip1`/`Clip2` sont des compteurs logiques sur l'arme, pas des objets ramassables séparés |
| Munitions physiques | Non natif — réserve abstraite uniquement (2.4) |
| Sauvegarde/restauration | Aucun mécanisme natif documenté dans les sources consultées |
| Reconnexion | Non documenté dans les sources consultées |
| Late join | Non documenté dans les sources consultées pour ce système précis (à distinguer du comportement général déjà partiellement observé empiriquement pour `WorldItemComponent` de Kodoku, voir [ITEM_ARCHITECTURE.md](../architecture/ITEM_ARCHITECTURE.md)) |
| Destruction/désactivation des GameObjects rangés | `Items` inclut les items désactivés mais exclut ceux en attente de destruction (confirmé, 2.1) — comportement précis de désactivation vs destruction à la sortie d'inventaire non détaillé plus loin |
| Coût de nombreux items représentés comme GameObjects | Hypothèse de performance, **non mesurée** — pas de donnée chiffrée dans les sources consultées |
| Transfert simultané / prévention de duplication | `Transfer` confirmé host-only avec gates de refus (2.3), mais le comportement précis de deux transferts concurrents vers le même slot n'est pas détaillé dans les sources consultées |
| Validation host de distance/visibilité/droit de pickup | Confirmé **absent par défaut** (`CanPickupWorldItem`, 2.1) — explicitement un point d'extension |
| Relation ownership pawn ↔ ownership items | Non détaillée dans les sources consultées au-delà de « host authoritative, le propriétaire route via le host » |

## 6. Questions ouvertes

- `ItemInstance.InstanceId` peut-il être conservé comme identité stable si un `BaseInventoryItem` est utilisé comme conteneur/adaptateur, ou faut-il une correspondance externe (dictionnaire `InstanceId` ↔ GameObject) ?
- Un `BaseInventoryItem` rangé (non actif) doit-il rester un GameObject networké vivant en permanence, ou peut-il être représenté autrement pour un grand nombre d'items sans coût réseau/mémoire significatif ?
- Comment reconstruire un inventaire (natif ou hybride) après chargement de sauvegarde ou reconnexion, sachant qu'aucun mécanisme de sauvegarde natif n'a été trouvé dans les sources consultées ?
- Le système natif supporte-t-il proprement, sans conflit, un conteneur spatial personnalisé (grille Kodoku) construit au-dessus de ses slots (`Behaviour.Buckets` détourné, ou entièrement en dehors du système natif) ?
- Peut-on utiliser `BaseInventoryComponent` uniquement comme couche de transport réseau (autorité, `Transfer`, routage host) sans laisser ses `Slot`/`SlotOrder` devenir la source de vérité de la position réelle dans une grille Kodoku ?
- Comment éviter une divergence entre un futur `ItemInstance.Quantity` (empilement) et l'absence de notion de quantité côté `BaseInventoryItem` natif ?
- `Transfer` couvre-t-il correctement, sans duplication ni perte, un scénario à trois acteurs (deux joueurs et un coffre, ou un joueur et son propre sac imbriqué) ? Non testé dans cette étude, purement documentaire.
- Les armes natives (`BaseCombatWeapon`) peuvent-elles utiliser des chargeurs physiques personnalisés (objets d'inventaire séparés) sans entrer en conflit avec le système de réserve abstraite natif (`Ammo1`/`Ammo2`/`BaseAmmoResource`) ?
- Le système natif garantit-il un comportement correct pour le late join et la déconnexion dans le cas concret de Kodoku (host qui repart, joiner tardif avec un inventaire déjà peuplé) ? Aucune source consultée dans cette étude ne documente ce point précisément — à vérifier par un test moteur dédié, cohérent avec la lacune déjà identifiée dans [OPEN_QUESTIONS.md](../status/OPEN_QUESTIONS.md) sur le late join en général.
- Le contrôle de distance/ligne de vue pour le pickup, absent par défaut (confirmé), doit-il être ajouté via `CanPickupWorldItem` surchargé, ou entièrement pris en charge par une couche Kodoku indépendante du système natif ?

## 7. Prototype recommandé, non réalisé

Recommandation documentaire uniquement — **rien ci-dessous n'a été implémenté pendant cette mission**.

1. Branche dédiée (ex. `spike/native-inventory-eval`), jamais fusionnée sans décision explicite.
2. Ajouter un `BaseInventoryComponent` (`Behaviour = Hotbar`, `MaxSlots` réduit) sur une copie de test du prefab joueur — pas sur `kodoku_player.prefab` directement.
3. Un seul item de test, basé sur la bouteille d'eau existante (`water_bottle.item`/`water_bottle.prefab`).
4. Un adaptateur expérimental entre `ItemInstance` (Kodoku) et `BaseInventoryItem` (natif) — objectif du spike : déterminer laquelle des trois stratégies (section 4) est réellement viable, pas produire du code de production.
5. Aucune UI définitive — au minimum un affichage de debug (log ou HUD temporaire), cohérent avec la politique déjà appliquée aux composants de debug précédents (retirés après validation, voir [ITEM_ARCHITECTURE.md](../architecture/ITEM_ARCHITECTURE.md)).
6. Tests host + client à effectuer, cohérents avec [TESTING_MULTIPLAYER.md](../development/TESTING_MULTIPLAYER.md) :
   - pickup côté host ;
   - pickup côté client ;
   - pickup simultané par deux joueurs sur le même item monde ;
   - drop ;
   - transfert entre deux inventaires ;
   - inventaire plein (refus propre) ;
   - late join (un joiner rejoint après qu'un inventaire est déjà peuplé) ;
   - déconnexion ;
   - reconnexion, si possible dans le temps du spike ;
   - comparaison explicite des `InstanceId` Kodoku de part et d'autre (host/client) ;
   - absence de duplication d'objet sur l'ensemble des scénarios ci-dessus.

**Critères de décision à définir avant le spike, pas après** (proposition, non tranchée) : le spike devrait au minimum permettre de répondre à — la source de vérité de `Quantity`/`InstanceId` reste-t-elle cohérente sous test réseau ? le coût GameObject par item rangé est-il acceptable pour le nombre d'items visé par le gameplay extraction ? le modèle de slots natif peut-il porter une grille spatiale sans devenir la source de vérité concurrente de la position ? Sans ces critères explicites, le spike risque de produire une préférence subjective plutôt qu'une décision vérifiable.

## 8. Ce qui n'est pas tranché par ce document

- Kodoku n'adopte pas le système natif s&box.
- Kodoku ne l'exclut pas non plus.
- Aucune architecture hybride n'est validée.
- Aucune compatibilité avec une éventuelle grille façon Tarkov n'est confirmée.
- Le networking des items (au-delà de ce que `WorldItemComponent` fait déjà pour la représentation monde) n'est pas considéré comme résolu par cette étude.

La décision définitive appartient à une mission ultérieure, informée par ce document et par le prototype de la section 7 si celui-ci est un jour réalisé.
