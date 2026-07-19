# Architecture — Conteneurs du monde (V1)

**Statut : conception terminée, transport réseau multi-viewer validé par test runtime réel (Spike S0), conteneur lui-même non implémenté.** Ce document a été produit par une mission de conception dédiée (branche `design/world-containers`, à partir de `main`/`df52f64`) — audit de l'existant puis architecture proposée, **aucun code de production écrit**. Le mécanisme de diffusion multi-viewer (section 7) a depuis été confirmé par un spike technique isolé exécuté en runtime réel avec un host et deux clients distants (branche `spike/world-container-multiviewer-rpc`, voir [docs/research/WORLD_CONTAINER_MULTIVIEWER_SPIKE.md](../research/WORLD_CONTAINER_MULTIVIEWER_SPIKE.md) pour les logs et le détail scénario par scénario). **Rien du conteneur lui-même n'est implémenté ni validé par un test réel** — seul le transport générique l'est. Toute mention de comportement futur du conteneur reste une proposition à implémenter/vérifier dans une mission séparée, jamais un jalon gameplay terminé. Voir [../status/ROADMAP.md](../status/ROADMAP.md) et [../status/CURRENT_STATE.md](../status/CURRENT_STATE.md) pour l'état factuel du projet.

Objectif final visé (hors périmètre de cette V1, cité pour contexte) : caisses, coffres, casiers, armoires, sacs posés dans le monde, conteneurs de loot partagés entre plusieurs joueurs — voir « Périmètre V1 » ci-dessous pour ce qui est réellement couvert maintenant.

---

## 1. État actuel vérifié

Vérifié par lecture directe du code et de la documentation à la date de cette mission — pas déduit de la roadmap.

### Ce qui est réellement implémenté et validé

- **`InventoryContainer`/`InventoryPlacement`/`InventoryOperationResult`/`InventoryFailureReason`** (`Code/Items/Inventory/`) — noyau pur de grille 2D, sans réseau, sans `Component`. `TryAdd`/`TryAddFirstFit`/`TryMove`/`TryRemove`/`TryConsume` : validation complète (`CanPlace`) avant toute mutation, jamais d'exception pour un refus de gameplay normal. `TryConsume` (ajouté pour Item Use) respecte l'invariant « `Quantity` ne peut jamais valoir 0 » en retirant le placement plutôt qu'en tentant `Quantity = 0`. Validé Tests A à O (15/15, exécution solo en éditeur — suffisant pour ce noyau non networké).
- **`ItemInstance`** (`Code/Items/Instances/`) — classe C# pure, `InstanceId` (`Guid`) stable, `Definition`, `Quantity` borné `[1, MaxStack]`. `CreateNew`/`Restore` sont les deux seuls chemins de construction ; aucune notion de propriétaire/conteneur/réseau sur la classe elle-même.
- **`ItemDefinition`** (`Code/Items/Definitions/`) — `GameResource`, porte désormais `EquipmentSlot` (`EquipmentSlotType`, défaut `None`) et `ThirstRestoreAmount` (`float`, défaut `0`), en plus de `GridWidth`/`GridHeight`/`CanRotate`/`Weight`/`MaxStack`/`WorldModel`/`WorldPrefabOverride`.
- **`WorldItemComponent`** (`Code/Items/World/`) — relie `ItemDefinition` → `ItemInstance` → représentation monde networkée. Autorité : `Networking.IsHost`, jamais `IsProxy`. État réseau minimal (`NetworkInstanceId`/`NetworkItemId`/`NetworkQuantity`, `[Sync(SyncFlags.FromHost)]`, `Guid` en chaîne canonique — aucun support natif de `Guid` par `[Sync]` confirmé dans ce projet). Deux chemins d'initialisation : `TryInitializeAuthoritativeNew()` (nouvelle instance, host, toujours **après** `NetworkSpawn()`) et `TryInitializeFromInstance(instance)` (instance déjà existante, peut être appelée **avant** `NetworkSpawn()` — exploité par le drop pour rendre impossible par construction la création d'une seconde instance sur un clone). `PublishAuthoritativeNetworkState()` sépare explicitement la pose locale de la publication réseau.
- **`LootSpawnPointComponent`** (`Code/Items/Loot/`) — marqueur non networké, host-only (`Networking.IsHost` direct, sans garde `GameObject.Network.Active`, car le composant lui-même n'est pas networké). Clone un prefab (`Item.WorldPrefabOverride`), `NetworkSpawn()`, puis `TryInitializeAuthoritativeNew()`. `HasEvaluated`/`HasSpawned` protègent contre une réévaluation.
- **`WorldItemPickupComponent`** (`Code/Items/Interaction/`) — `Component.IPressable`, branché sur le système de pression stock du `PlayerController` (aucun raycast Kodoku écrit). `Press()` ne fait que déclencher `RequestPickup()` (`[Rpc.Host]`, résout `Rpc.Caller`) → délègue à `TryPickupAuthoritative(Connection requester)` (non-RPC, séparée du transport). `IsClaimed` : réservation host-only, **non networkée** (pas de `[Sync]`), pose avant la mutation d'inventaire pour bloquer une seconde transaction concurrente. Portée effective = `min(ReachLength, MaxPickupDistance)`, ligne de vue revalidée par trace depuis `EyePosition`/`EyeAngles.Forward`, résolution robuste par `GetComponentInParent`.
- **`PlayerInventoryComponent`** (`Code/Players/Inventory/`) — `InventoryContainer Container`, **host-only** (`null` ailleurs). Snapshot **combiné** grille+équipement (`InventorySnapshotEntry[]`/`EquipmentSnapshotEntry[]`), une seule révision (`HostRevision`/`LocalRevision`), envoyé **uniquement au propriétaire du pawn** via `[Rpc.Owner]` (`ReceiveSnapshot`). `NotifyMutated()` = point d'entrée central après toute mutation validée (incrémente la révision, reconstruit et pousse le snapshot). `RequestSnapshot()` (`[Rpc.Host]`) sert à la fois de déclencheur initial et de resynchronisation explicite ; vérifie `GameObject.Network.Owner == Rpc.Caller` avant tout envoi. **Ce composant est structurellement mono-viewer** : toute sa mécanique de réplication suppose un seul destinataire légitime (le propriétaire du pawn) — voir « Architectures comparées » ci-dessous pour pourquoi ceci empêche une réutilisation directe pour un conteneur partagé.
- **Équipement** (intégré à `PlayerInventoryComponent`) — deux slots (`Head`/`Body`), `_equippedSlots : Dictionary<EquipmentSlotType, ItemInstance>`, host-only. `TryEquipAuthoritative`/`TryUnequipAuthoritative` : atomicité **par ordre des mutations**, sans rollback dédié — l'étape faillible (retrait de grille pour équiper ; `TryAddFirstFit` pour déséquiper) s'exécute toujours avant l'étape infaillible (écriture de dictionnaire). Un item équipé n'est **jamais** présent dans `Container` (déjà exploité par `PlayerItemUseComponent`, qui ne fait aucune vérification spéciale — `GetPlacement` renvoie `null` naturellement).
- **`PlayerItemDropComponent`** (`Code/Players/Inventory/`) — dépose une pile entière depuis `Container` vers le monde. Ordre délibéré : `TryInitializeFromInstance` (pose locale de l'identité) **avant** retrait canonique et **avant** `NetworkSpawn()`, pour rendre structurellement impossible la création d'une nouvelle instance par `WorldItemComponent.OnStart()` sur le clone. Retrait canonique **après** que le clone soit prêt. `NetworkSpawn()` puis `PublishAuthoritativeNetworkState()` en dernier (seul ordre validé par test réel — voir Tests A à G du loot). **Rollback réel nécessaire et implémenté** ici : contrairement à l'équipement, un `GameObject` intermédiaire peut échouer à se networker *après* le retrait canonique — `TryRollback` réinsère l'instance à sa position d'origine si `NetworkSpawn()`/`PublishAuthoritativeNetworkState()` échoue.
- **`PlayerItemUseComponent`** (`Code/Players/Inventory/`) — composant séparé de `PlayerInventoryComponent` (orchestration à cheval sur deux domaines : inventaire **et** vitals). Consommation via `Container.TryConsume`, puis `PlayerVitalsComponent.RestoreThirst` — consommation d'abord (étape la plus complexe), effet ensuite (infaillible, clampé).
- **`KodokuPlayerComponent`** (`Code/Players/`) — `Local` (statique, pawn local courant), `FindByConnection(Scene, Connection)` (résolution d'un pawn depuis une connexion réseau, via comparaison à `GameObject.Network.Owner` — **`OwnerConnection` est marqué obsolète dans le build moteur installé localement**, confirmé par `dotnet build`, warning CS0618 : « Moved to Owner »). Expose désormais `PlayerController`/`PlayerVitals`/`PlayerInventory`/`PlayerItemDrop`/`PlayerItemUse`, tous résolus en `OnStart` (jamais `OnAwake`, pour une résolution fiable des composants frères).
- **`InventoryDebugPanel.razor`** (`Code/UI/Debug/`) — panneau debug local (Tab), lit uniquement `KodokuPlayerComponent.Local?.PlayerInventory`. N'affiche jamais un pawn distant. Boutons Drop/Equip/Unequip/Use n'envoient que l'`InstanceId` (et le slot pour l'équipement) — aucune validation métier côté client, tout est revalidé côté host. `ResolveDefinition(itemId)` résout localement une `ItemDefinition` depuis `ResourceLibrary.GetAll<ItemDefinition>()` pour décider quel bouton afficher — lecture seule, jamais une preuve d'autorisation.

### Ce qui est seulement documenté (recommandation/vision, pas de code)

- Aucun `WorldContainerComponent` ni équivalent — recherché explicitement dans `Code/`, absent.
- `docs/status/OPEN_QUESTIONS.md` et `docs/architecture/ITEM_ARCHITECTURE.md` mentionnaient déjà les conteneurs du monde comme « explicitement reportés » (décision du 2026-07-15), avec pour condition d'entrée la validation des transactions atomiques d'équipement et du snapshot propriétaire — **cette condition est remplie depuis le 2026-07-18** (équipement et item use validés par test réel, dix et treize scénarios).
- Les assets `Assets/Models/Cardboard_box/`, `Assets/Materials/Cardboard_box/`, `Assets/Textures/Cardboard_box/` existent **localement, non suivis par Git, non intégrés** (aucun prefab/composant ne les référence) — asset personnel de l'utilisateur, strictement hors périmètre de cette mission, non ouverts ni modifiés.
- **Un modèle visuel est désormais disponible pour le futur premier conteneur du monde** : `Assets/Models/Containers/Wooden_Crate/` et `Assets/Textures/Containers/Wooden_Crate/` (plus le matériau associé, `Assets/Materials/Containers/Wooden_Crate/`) — ajoutés volontairement par l'utilisateur, localement, non suivis par Git. La ressource s&box correspondante existe déjà à `models/containers/wooden_crate/wooden_crate.vmdl`. **Ceci n'est qu'un asset visuel** : aucun prefab de conteneur n'existe encore, et ni `WorldContainerComponent`, ni la collision/interactabilité, ni la configuration de grille associée ne sont créés — la présence de ce modèle ne constitue en aucun cas une implémentation, même partielle, des conteneurs du monde. Ces fichiers ne sont ni ouverts, ni modifiés, ni référencés par du code pendant cette mission documentaire.

### Ce qui manque (vérifié absent)

- **Aucun mécanisme réseau multi-destinataire ciblé n'est utilisé ni testé dans ce projet.** Tous les push réseau existants sont soit `[Rpc.Owner]` (un seul destinataire fixe, le propriétaire du `GameObject`), soit `[Rpc.Host]` (un seul destinataire fixe, le host), soit `[Sync(SyncFlags.FromHost)]` (broadcast implicite à **tous** les clients, utilisé par `WorldItemComponent` — une donnée publique, pas un secret par viewer). `Rpc.FilterInclude`/`FilterExclude` sont des **API documentées ou identifiées comme disponibles, mais leur comportement exact n'est pas encore vérifié dans ce projet** — aucun appel n'existe dans le code Kodoku actuel, aucun test réel ne les a exercées. **Un spike runtime host + deux clients est bloquant avant l'implémentation.** C'est le point technique le plus important de cette conception — voir section 7.
- **Aucune notion de « session d'ouverture » ou de « liste de viewers »** n'existe dans le code actuel. `WorldItemPickupComponent.IsClaimed` est le précédent le plus proche (réservation host-only non networkée), mais c'est un booléen unique pour un seul consommateur, pas un ensemble de connexions.
- **Aucun `SaveManager`, aucun code de sauvegarde, aucun identifiant stable indépendant du runtime** n'existe (`SaveManager` est un `GameObject` placeholder **vide** dans `Assets/scenes/Tests/GameplayTest.scene`, confirmé par lecture directe de la scène — aucun composant dessus). Aucun système de chargement/déchargement de zone n'existe (`docs/architecture/SCENE_ARCHITECTURE.md` confirme une seule scène, `GameplayTest.scene`).
- **Aucun mécanisme de spawn/despawn générique de conteneurs** — les seuls précédents de spawn host-authoritative sont `LootSpawnPointComponent` (au chargement de la scène, une seule fois) et `PlayerItemDropComponent` (à la demande, clone+spawn d'un `WorldItem`).
- **Aucun transfert entre deux conteneurs quels qu'ils soient** n'existe — seuls pickup (monde → joueur) et drop (joueur → monde) existent, tous deux à sens unique et déjà validés séparément.

### Ce qui est obsolète

- Rien d'obsolète trouvé spécifiquement lié aux conteneurs — la seule correction apportée à la documentation avant cette mission (voir commit `df52f64`) concernait une mention résiduelle de l'équipement comme « conçu, non implémenté » dans `OPEN_QUESTIONS.md`, déjà corrigée.

---

## 2. Besoins

Dérivés des exemples cités par l'utilisateur (caisse, coffre, casier, armoire, sac posé dans le monde, conteneur de loot partagé) et du plan d'ensemble de la roadmap (étape 9, « Objets du monde »).

- Un joueur doit pouvoir ouvrir un conteneur placé dans le monde et consulter son contenu.
- Plusieurs joueurs doivent pouvoir interagir avec le **même** conteneur au cours d'une session de jeu (pas forcément simultanément selon la décision de portée — voir périmètre V1).
- Un item doit pouvoir transiter conteneur ↔ inventaire joueur sans jamais être dupliqué ni perdu, y compris en présence d'actions concurrentes.
- Le système doit rester cohérent avec les principes déjà établis du projet : host-authoritative (ADR-0002), présentation locale jamais répliquée comme état global (ADR-0003), validation avant mutation, résultats explicites (jamais un booléen seul), transport RPC séparé de la transaction métier.
- Le système doit pouvoir évoluer vers des variantes futures (verrouillage, conteneurs personnels, sacs portés, véhicules, corps lootables) sans réécriture complète — sans pour autant les concevoir maintenant.

---

## 3. Périmètre V1

### Doit fonctionner

- Un `WorldContainerComponent` placé dans le monde, avec une grille `InventoryContainer` canonique côté host (réutilisation directe du noyau déjà validé — aucune modification requise sur `InventoryContainer`/`InventoryPlacement`).
- Ouverture par un joueur autorisé, via le système de pression stock déjà validé pour le pickup (`Component.IPressable`) — pas de nouveau scanner d'interaction.
- Consultation du contenu (snapshot réseau vers les viewers autorisés).
- Transfert d'un item **entier** (pas de split) du conteneur vers l'inventaire du joueur.
- Transfert d'un item **entier** de l'inventaire du joueur vers le conteneur.
- Révision réseau fiable et resynchronisation explicite (mêmes garanties que le snapshot d'inventaire joueur déjà validé : révision monotone, resync reconstruit fidèlement l'état).
- **Plusieurs joueurs dans une même session** — décidé explicitement ci-dessous, pas laissé implicite.
- Aucune duplication ni perte d'item, dans tous les scénarios de concurrence de la matrice de tests (section 16).

### Décisions explicites sur les points laissés ouverts par la demande

| Question | Décision V1 | Raison |
|---|---|---|
| Plusieurs joueurs peuvent-ils regarder le même conteneur simultanément ? | **Oui.** | Correspond à l'exemple « conteneur de loot partagé » explicitement cité, et au besoin minimal listé ci-dessus. Un modèle single-viewer serait plus simple mais ne couvrirait pas le cas d'usage principal demandé. Voir section 10 pour la concurrence que ceci implique. |
| Un seul utilisateur à la fois ? | **Non** (conséquence directe du point précédent) — pas de verrou d'exclusivité en V1. | Un verrou ajouterait un état supplémentaire (qui détient le verrou, que se passe-t-il s'il se déconnecte en le détenant) sans besoin prouvé pour cette V1. |
| Fermeture automatique par distance ? | **Non, pas de vérification périodique proactive.** Revalidation **paresseuse** : la distance/l'appartenance à la session sont revérifiées à chaque tentative de transfert, pas par une boucle qui expulse activement un viewer éloigné. | Évite une boucle `OnUpdate`/tick supplémentaire pour un bénéfice principalement cosmétique (le panneau resterait affiché un peu plus longtemps qu'idéal chez un joueur qui s'éloigne, mais aucune action réelle n'est permise à distance excessive). Peut être ajouté plus tard sans changer le modèle de données. |
| Maintien du contenu après fermeture ? | **Oui**, trivialement — fermer une session ne vide que la liste des viewers, jamais `Container`. | Cohérent avec le fait qu'un conteneur du monde n'a pas de notion de « propriétaire qui se déconnecte » comme un pawn. |
| Maintien du contenu après reconnexion (d'un viewer) ? | **Oui** pour le contenu du conteneur lui-même (il ne dépend d'aucune connexion précise) ; **non** pour la session de consultation (le viewer doit rouvrir). | Le contenu est un état du `GameObject` conteneur, indépendant de toute connexion particulière — seule la session d'un viewer donné disparaît à sa déconnexion. |
| Maintien du contenu après rechargement de scène ? | **Hors V1, non garanti.** | Aucun système de rechargement de scène n'existe encore dans le projet (une seule scène, voir audit) — question à retraiter avec la persistance (section 13) et l'étape 11 de la roadmap. |
| Piles partielles ? | **Non.** Transfert d'une pile entière uniquement, comme le drop actuel. | Cohérent avec la limitation déjà acceptée du drop (« pas de quantité choisie, pas de split ») — ne pas résoudre split/merge deux fois dans deux jalons différents. |
| Split/merge ? | **Non**, hors V1. | Appartient au futur jalon « Stacks, split/merge et quantités » de la roadmap (étape 8), qui n'est pas encore construit indépendamment des conteneurs. |
| Déplacement manuel dans la grille (drag & drop, position choisie) ? | **Non.** Placement toujours via `TryAddFirstFit` (déterministe), comme le pickup et le déséquipement actuels. | Pas de précédent de placement manuel networké dans le projet ; l'UI finale (hors V1) est le moment naturel d'introduire ce besoin. |
| Conteneurs verrouillés ? | **Non conçu.** | Aucun cas d'usage concret listé pour cette V1 — à concevoir quand un besoin réel (clé, code) apparaît. |
| Conteneurs personnels (un seul propriétaire autorisé) ? | **Non conçu pour cette V1** — le modèle proposé (section 6-7) n'empêche pas de le construire plus tard comme une variante de validation d'autorisation, mais aucun conteneur personnel n'est livré ici. | Les exemples cités (caisse, coffre, casier, armoire, sac, loot partagé) sont tous des conteneurs partagés par défaut. |
| Conteneurs imbriqués (un conteneur dans un sac) ? | **Non.** | Complexité de récursion (quel est le viewer d'un conteneur contenu dans un autre ? quelle grille borne quoi ?) sans besoin actuel prouvé — violerait « pas de généralisation prématurée ». |

---

## 4. Hors périmètre (explicite)

- Split de pile, quantité partielle, merge de stacks.
- Déplacement manuel/rotation dans la grille du conteneur (au-delà du placement déterministe `TryAddFirstFit`).
- Conteneurs verrouillés, personnels, imbriqués.
- Persistance/sauvegarde réelle (seules les décisions qui *n'empêchent pas* une future sauvegarde sont prises maintenant — voir section 13).
- Reconnexion avec restauration d'une session de consultation ouverte.
- Rechargement/déchargement de scène (aucun système de zones n'existe).
- Destruction d'un conteneur et éjection de son contenu au sol (aucune mécanique de destruction de conteneur n'est prévue dans cette V1 — voir section 15).
- UI finale (panneau riche, drag & drop, icônes) — seule une extension du panneau debug existant est envisagée.
- Génération procédurale de placement de conteneurs dans une scène.
- Test de concurrence déterministe automatisé (même réserve que pour le pickup/drop/équipement : conception qui le permettrait, outil non construit dans cette passe).

---

## 5. Architectures comparées

### Approche A — composant monde autonome (`WorldContainerComponent`)

Un composant dédié, indépendant de tout pawn, possède son propre `InventoryContainer`, sa propre révision, sa propre liste de viewers et son propre transport réseau (RPC scoped à ce `GameObject`, comme `WorldItemPickupComponent`/`LootSpawnPointComponent` le sont déjà).

- **Responsabilité** : une seule — « héberger et répliquer le contenu d'un conteneur du monde ». Ne connaît rien du pawn au-delà de ce qui est nécessaire pour valider une requête (résolution via `KodokuPlayerComponent.FindByConnection`, déjà le patron utilisé par pickup/drop/équipement).
- **Couplage au joueur** : minimal — aucune dépendance structurelle à `PlayerInventoryComponent`, seulement une invocation de ses opérations publiques (`Container.TryAddFirstFit`/`TryRemove`, `NotifyMutated()`) au moment du transfert, exactement comme `WorldItemPickupComponent` invoque déjà `PlayerInventoryComponent.Container`/`NotifyMutated()` aujourd'hui.
- **Réutilisation réelle** : élevée — réutilise `InventoryContainer`/`InventoryPlacement`/`ItemInstance` tels quels (aucune modification), et le patron RPC-transport/méthode-métier-non-RPC déjà présent partout ailleurs.
- **Complexité réseau** : la partie la plus neuve est le modèle multi-viewer (section 7) — inévitable quelle que soit l'approche, puisque c'est intrinsèque au besoin (« plusieurs joueurs »), pas à un choix d'architecture.
- **Risque de régression** : **nul sur le code existant** — aucun fichier de `PlayerInventoryComponent`/`WorldItemPickupComponent`/`PlayerItemDropComponent`/`PlayerItemUseComponent` n'a besoin d'être modifié (au-delà d'éventuels appels publics déjà exposés).
- **Persistance** : naturelle — un conteneur du monde a une identité de `GameObject`/prefab stable (contrairement à un pawn qui va et vient avec une connexion), point d'ancrage propre pour un futur `StableContainerId` (section 13).
- **Testabilité** : élevée — même style que `LootSpawnPointComponent`/`WorldItemPickupComponent`, déjà validés à deux instances avec ce patron exact.
- **Évolution** (sacs, coffres, véhicules, corps lootables) : chaque variante devient une configuration ou une petite spécialisation du même composant (dimensions, autorisation), pas une nouvelle architecture.

### Approche B — réutilisation directe de `PlayerInventoryComponent`

Le conteneur monde utiliserait ou dériverait du composant d'inventaire joueur existant.

- **Rejetée.** `PlayerInventoryComponent` est structurellement **mono-viewer** par conception actuelle : son snapshot est envoyé par `[Rpc.Owner]`, donc à **un seul** destinataire fixe (le propriétaire du `GameObject`). Un conteneur du monde n'a pas de propriétaire unique au sens réseau — c'est précisément le problème à résoudre (section 6-7). Réutiliser ce composant obligerait soit à réécrire son modèle de réplication pour le rendre multi-viewer (ce qui reviendrait, de fait, à construire l'Approche A à l'intérieur d'un composant qui porte déjà une responsabilité différente et déjà validée — équipement, snapshot propriétaire), soit à sous-classer une classe aujourd'hui `sealed` et conçue pour un seul pawn (elle expose `EquippedCount`/`EquippedContents`, des concepts sans aucun sens pour une caisse). Ceci violerait directement [csharp.md](../../.claude/rules/csharp.md) (« un composant fait une chose ») et risquerait une régression sur un composant déjà validé par vingt-trois scénarios de test réel cumulés (huit snapshot + dix équipement + treize item use, ce dernier n'y touchant pas directement mais dépendant du même fichier).

### Approche C — composant d'inventaire partagé générique

Une couche générique (`InventoryHostComponent<T>` ou équivalent) porterait l'état canonique, les snapshots et les révisions pour les inventaires joueur **et** monde.

- **Rejetée pour cette V1**, pas par principe mais par manque de besoin prouvé. Les deux cas d'usage diffèrent sur des points structurants, pas cosmétiques :
  - mono-viewer avec `[Rpc.Owner]` (joueur) vs multi-viewer avec un mécanisme encore non confirmé dans ce projet (monde) ;
  - snapshot **combiné** grille+équipement (joueur) vs snapshot **grille seule** (monde, pour cette V1) ;
  - autorisation par ownership de `GameObject` (joueur) vs autorisation par appartenance à un ensemble de viewers (monde).
  Construire une abstraction commune maintenant, avant même qu'un deuxième cas d'usage monde n'existe (un seul type de conteneur est visé par cette V1), correspondrait exactement à l'anti-patron déjà signalé dans [csharp.md](../../.claude/rules/csharp.md) : « pas d'interface ou de système générique pour un seul cas d'usage actuel ». **À reconsidérer** une fois qu'au moins deux composants hébergeant un `InventoryContainer` avec réplication existent et qu'une duplication de code réellement identique (pas seulement similaire) est constatée — pas avant.

### Recommandation

**Approche A.** Nouveau composant autonome `WorldContainerComponent`, dans un nouveau namespace de domaine dédié (voir section 8), réutilisant tel quel le noyau `InventoryContainer`/`ItemInstance`/`ItemDefinition` déjà validé, et le patron RPC-transport/méthode-métier déjà utilisé par pickup/drop/équipement/item-use. Aucune modification du code existant n'est requise.

---

## 6. État canonique proposé

- **Propriétaire de l'`InventoryContainer` canonique** : le composant `WorldContainerComponent` lui-même, construit uniquement côté host (`Networking.IsHost`, même garde que `PlayerInventoryComponent.OnStart`), `null` ailleurs.
- **Dimensions de grille** : `[Property] int Width`/`Height`, configurées par instance/prefab (données locales non networkées — chaque client possède déjà la même donnée de scène/prefab, exactement comme `PlayerInventoryComponent.Width`/`Height` ne sont pas `[Sync]` aujourd'hui et n'ont pourtant jamais posé de problème, la configuration étant identique sur toutes les instances).
- **Identifiant runtime du conteneur** : `RuntimeContainerId` (`Guid`, généré une fois côté host à l'initialisation, publié en `[Sync(SyncFlags.FromHost)]` sous forme de chaîne canonique — même convention que `WorldItemComponent.NetworkInstanceId`, pour les mêmes raisons : aucun support natif de `Guid` confirmé). Utilité : logs/débogage clairs quand plusieurs conteneurs coexistent (voir Test O, indépendance de deux conteneurs) ; base possible pour un futur système de sauvegarde runtime, sans engagement.
- **Identifiant persistant éventuel** : voir section 13 — recommandé comme champ réservé maintenant (`StableContainerId`, `string`, vide par défaut), non exploité en V1.
- **Révision du contenu** : `HostRevision` (int, host-only, incrémentée uniquement après une mutation confirmée) — même patron que `PlayerInventoryComponent.HostRevision`.
- **Liste des viewers** : `HashSet<Connection> _viewers` (host-only, **jamais networkée** — chaque client n'a besoin de connaître que son propre statut « je consulte ce conteneur », pas la liste complète des autres viewers pour cette V1). Précédent le plus proche dans le code actuel : `WorldItemPickupComponent.IsClaimed`, un état host-only non répliqué qui protège une transaction — la liste de viewers en est l'extension naturelle à N éléments plutôt qu'un booléen.
- **État ouvert/fermé** : dérivé de `_viewers.Count > 0` — pas un booléen séparé qui pourrait diverger.
- **Portée publique ou privée du contenu** : **privée aux viewers actuels**, jamais un `[Sync(FromHost)]` public à tous les clients (contrairement à l'état minimal de `WorldItemComponent`, qui est une donnée sans caractère confidentiel — un joueur peut voir un objet posé dans le monde). Le contenu d'un coffre n'est pas une information que tout client doit connaître en permanence, seulement les viewers actifs — décision cohérente avec le choix déjà pris pour l'inventaire joueur (snapshot vers le seul propriétaire, jamais public).
- **Séparation état canonique / cache client** : mêmes garanties que `PlayerInventoryComponent.Container` (host, jamais `null` faux-négatif) vs `LocalEntries` (cache de présentation local, jamais une preuve d'autorisation) — un cache `LocalContainerEntries`/`LocalContainerRevision`, présent uniquement chez un viewer actif, jamais utilisé pour valider une mutation.
- **Comportement quand personne ne regarde** : `Container` reste en mémoire côté host, aucun travail réseau n'est effectué (pas de tick, pas de push périodique) — purement passif jusqu'à la prochaine ouverture ou mutation.
- **Network Owner du `GameObject`** : **aucun propriétaire joueur.** Le `GameObject` reste host-owned (ou sans owner explicite assigné à un joueur), exactement comme `LootSpawnPointComponent`/les objets loot spawnés — l'autorité de mutation est déterminée par `Networking.IsHost`, jamais par ownership. **Un conteneur partagé n'est jamais réassigné au premier joueur qui l'ouvre** — ouvrir un conteneur ajoute une connexion à `_viewers`, ça ne touche jamais `GameObject.Network.Owner`. C'est une garde explicite contre la tentation naturelle de réutiliser `[Rpc.Owner]` (qui exigerait un propriétaire unique, contredisant le besoin multi-viewer).

---

## 7. Modèle réseau multi-viewer — point central de cette conception

Le risque technique principal du système a été levé : le mécanisme n'a plus aucun précédent testé manquant dans ce projet, il a été confirmé par un test runtime réel (Spike S0, host + deux clients distants, branche `spike/world-container-multiviewer-rpc`). Le détail scénario par scénario (logs, séquences, verdicts) vit dans [docs/research/WORLD_CONTAINER_MULTIVIEWER_SPIKE.md](../research/WORLD_CONTAINER_MULTIVIEWER_SPIKE.md) — ce document n'en reprend que la décision et ses conséquences pour l'architecture.

### Transport de snapshots — décision validée

Le transport retenu est :

- une RPC `[Rpc.Broadcast]` ;
- enveloppée dans `using ( Rpc.FilterInclude( viewers ) ) { ... }` avec la collection courante des viewers autorisés.

Confirmé par test runtime réel (S0-B, S0-C, S0-H) : un seul appel host-side livre la même séquence/payload à un seul viewer ciblé, à plusieurs viewers simultanément, et un late joiner ajouté après coup ne reçoit que les envois postérieurs à son ajout — jamais un historique.

`Rpc.FilterExclude` **n'a pas été testé** et n'est pas nécessaire au design retenu — ne jamais le présenter comme validé dans une future révision de ce document.

### Collection vide

Confirmé par test runtime réel (S0-A) : `Rpc.FilterInclude` avec une collection vide de viewers n'envoie à personne — ni aux non-viewers, ni au host appelant lui-même s'il n'est pas explicitement inclus dans la collection — et ne retombe jamais sur un broadcast global. Aucune exception observée.

Le futur `WorldContainerComponent` peut néanmoins éviter l'appel RPC lorsque `_viewers.Count == 0`, pour ne pas produire un envoi réseau inutile — c'est une optimisation, pas une condition de correction (le comportement est déjà sûr sans cette garde).

### Invalidation ciblée

Confirmé par test runtime réel (S0-D), ordre validé exactement tel que conçu :

1. `Rpc.FilterInclude` sur la seule `Connection` à invalider ;
2. envoi de la RPC d'invalidation — reçue par ce seul viewer ;
3. retrait de cette `Connection` de la collection de viewers ;
4. les envois suivants (snapshots de mutation) ne l'incluent plus.

### Déconnexion d'un viewer

Mécanisme principal retenu, confirmé par test runtime réel (S0-G) : `Component.INetworkListener.OnDisconnected(Connection)`. Le composant qui héberge `_viewers` doit implémenter cette interface — le callback s'est déclenché immédiatement à la déconnexion (avant même le prochain envoi de snapshot), retirant la connexion sans exception et sans tentative d'envoi vers une connexion morte.

La future implémentation doit conserver, en complément, une purge défensive légère (retirer toute `Connection` avec `IsActive == false`) avant chaque envoi — mais cette purge n'est **pas** la source principale du nettoyage validé, et ne doit jamais être présentée comme telle : c'est un filet de sécurité, `OnDisconnected` est le mécanisme réellement responsable dans le test observé.

### Idempotence — réserve non bloquante

Les gardes d'idempotence (ajout d'une connexion déjà viewer, retrait d'une connexion déjà absente) reposent sur de simples vérifications de collection C# côté host, sans composante réseau. **Elles n'ont pas été exercées en runtime réel** pendant le Spike S0 (scénarios S0-E/S0-F non exécutés — l'outil debug utilisé pour le spike n'exposait pas de bouton permettant de déclencher l'action invalide). Ceci ne bloque pas la décision d'architecture ci-dessus, mais ne doit jamais être présenté comme « validé en runtime » dans une future révision de ce document ou d'`OPEN_QUESTIONS.md`. Une confirmation runtime future passerait soit par une UI de test dédiée forçant l'action invalide, soit par un outil de test déterministe appelant directement les méthodes hors RPC (même patron que `TryPickupAuthoritative`/`TryDropAuthoritative`).

### Options rejetées

- **Réplication globale** (`[Sync(SyncFlags.FromHost)]` du contenu complet, visible par tous les clients en permanence) — rendrait le contenu de tout conteneur du monde public en permanence (violerait la décision de portée privée aux viewers, section 6), et représenterait un coût réseau permanent même sans aucun viewer. Rejetée pour confidentialité **et** coût.
- **Ownership temporaire** (réassigner `GameObject.Network.Owner` à chaque viewer successivement pour utiliser `[Rpc.Owner]`) — rejetée explicitement. Un `GameObject` n'a qu'un seul owner à la fois ; ceci ne supporterait jamais plusieurs viewers simultanés et contredirait la garde de la section 6 (« un coffre partagé n'appartient pas au premier joueur qui l'ouvre »).
- **Envoi individuel en boucle** (une RPC par viewer, filtre à un seul élément à chaque itération) — non nécessaire : le broadcast filtré à N éléments (option retenue ci-dessus) couvre déjà le cas multi-viewer en un seul appel, confirmé par S0-C/S0-H. Resterait une option de repli si un besoin futur (hors périmètre connu aujourd'hui) exigeait un traitement différencié par viewer.

### Reste du modèle réseau

- **Resynchronisation après ouverture** : même patron que `PlayerInventoryComponent.RequestSnapshot()` — un viewer nouvellement ajouté reçoit un snapshot complet immédiatement (revision courante, jamais un delta).
- **Retrait d'un viewer** (fermeture explicite, éloignement détecté lors d'une requête, déconnexion) : retiré de `_viewers` host-side ; aucun message de confirmation nécessaire vers ce client au-delà de la réponse normale à sa requête de fermeture (voir section 8).
- **Arrivée d'un nouveau viewer pendant qu'un autre consulte** : n'affecte en rien les viewers déjà présents — chacun reçoit son propre snapshot initial au moment de son ouverture ; une mutation ultérieure republie à **tous** les viewers courants (y compris celui déjà présent), jamais un message différencié « bienvenue » vs « mise à jour ». Confirmé par S0-H (late join sans effet sur un viewer déjà présent).
- **Fermeture/destruction du conteneur** : si le `GameObject` est détruit alors que des viewers existent encore, recommandé (mais non bloquant pour le critère de fin de V1) : notifier les viewers restants d'une fermeture forcée pour qu'ils vident leur cache local proprement, plutôt que de les laisser avec un panneau figé sur une dernière révision jamais invalidée — mécanisme identique à l'invalidation ciblée déjà validée ci-dessus.

---

## 8. Session d'ouverture

Cycle complet, conçu comme un aller-retour requête/réponse **unique** par étape (jamais une chaîne de RPC déclenchées depuis l'intérieur d'un handler de réponse — contrainte confirmée empiriquement et documentée dans [MULTIPLAYER_ARCHITECTURE.md](MULTIPLAYER_ARCHITECTURE.md#rpc-confirmé--contrainte-spécifique-kodoku)) :

1. Le joueur interagit avec le conteneur — même mécanisme que le pickup, `Component.IPressable` sur `WorldContainerComponent` (ou un composant sœur dédié à l'interaction, à trancher à l'implémentation ; aucun nouveau scanner de raycast dans tous les cas).
2. `Press()` déclenche `RequestOpen()` (`[Rpc.Host]`), qui résout `Rpc.Caller` — la requête atteint le host.
3. Le host valide l'appelant : `KodokuPlayerComponent.FindByConnection(Scene, Rpc.Caller)` doit résoudre un pawn valide (même patron que pickup/drop/équipement).
4. Le host valide la distance (portée effective, même formule que le pickup : `min(ReachLength, MaxOpenDistance)`) et l'état du conteneur (`GameObject.IsValid()`, pas de raison métier de refuser une seconde ouverture puisque le multi-viewer est autorisé — voir section 3).
5. Le joueur devient viewer autorisé : `_viewers.Add(caller)`, host-only, non networké.
6. Un snapshot initial est envoyé — **au sein du même appel host**, pas depuis un handler séparé déclenché plus tard (respecte la contrainte anti-chaînage RPC) : le host construit le snapshot courant et le pousse via le mécanisme de la section 7, filtré à ce nouveau viewer (ou à l'ensemble des viewers si plus simple d'implémentation — le résultat pour les viewers existants est inchangé soit qu'on les réinclue soit qu'on les exclue de cet envoi ponctuel, puisqu'ils ont déjà la révision courante).
7. Côté client, réception du snapshot → le cache local (`LocalContainerEntries`/`LocalContainerRevision`) passe de « jamais reçu » à la révision courante → l'UI (panneau debug étendu, voir section 14) s'ouvre en réaction à ce changement (même patron de réactivité que `InventoryDebugPanel`, piloté par `BuildHash()`).
8. Les opérations (transferts) sont autorisées tant que la session reste valide — **revalidée à chaque requête**, pas seulement à l'ouverture (voir ci-dessous).
9. La session se ferme : **volontairement** (le client appelle `RequestClose()`, `[Rpc.Host]`, retire sa propre connexion de `_viewers`) ou **automatiquement** par échec de revalidation lors d'une tentative d'action (le joueur s'est éloigné, ou le conteneur n'est plus valide) — pas de vérification périodique proactive (décision de portée, section 3).
10. Le cache client est invalidé à la fermeture — soit par un message explicite du host (option propre, recommandée) soit par une invalidation locale immédiate dès que le joueur ferme lui-même l'UI (le serveur retire le viewer de toute façon ; le client n'a pas besoin d'attendre une confirmation réseau pour vider son propre affichage local).

### Décisions sur les mécanismes de session

| Mécanisme | Décision V1 |
|---|---|
| Identifiant/token de session | **Non nécessaire.** L'appartenance à `_viewers` (par `Connection`, déjà l'identifiant réseau canonique utilisé partout ailleurs dans ce projet) suffit — un token supplémentaire n'apporterait rien qu'une `Connection` ne fournit déjà. |
| Dernier numéro de révision connu transmis par le client | **Non.** Cohérent avec le reste du projet : aucun système existant ne fait confiance à un état côté client pour décider d'une mutation (pickup/drop/équipement/item-use ignorent tous un éventuel état supposé du client, ne validant que l'existence actuelle de l'`InstanceId` côté host). Le client peut *lire* sa propre révision locale pour son affichage, mais ne l'envoie jamais au serveur comme preuve. |
| Validation de distance à chaque action | **Oui**, revalidée à chaque `RequestTransferToContainer`/`RequestTransferFromContainer`/`RequestClose`, pas seulement à `RequestOpen` — cohérent avec le principe « ne jamais faire confiance à un état client mis en cache », y compris l'état « je suis un viewer autorisé » qui pourrait être obsolète si le joueur s'est éloigné entre l'ouverture et l'action. |
| Vérification périodique (tick) | **Non** — décision de portée V1, section 3. |
| Validation de ligne de vue à chaque action | **Non** — seulement à l'ouverture (comme le pickup). Une fois la session ouverte, les actions suivantes viennent de clics dans un panneau UI, pas d'un nouveau visée du joueur ; revalider la ligne de vue à chaque clic ajouterait une contrainte sans bénéfice de sécurité réel (la distance suffit à empêcher une action à travers un mur lointain ; un mur juste devant le conteneur alors que le joueur le consulte déjà est un cas limite non traité en V1). |
| Timeout de session | **Non** — la fermeture est explicite ou déclenchée par échec de revalidation lors d'une action, pas par une horloge. |

**Solution minimale sûre retenue** : appartenance à `_viewers` (par `Connection`) + revalidation de distance à chaque requête mutante + aucune confiance dans un état client. Pas de token, pas de tick, pas de verrou.

### Comportement après un rejet de distance — l'absence de fermeture automatique ne dispense pas de la sécurité

**Précision explicite, pour éviter toute ambiguïté** : l'absence de vérification périodique proactive (décision de portée, section 3) ne signifie **en aucun cas** qu'un joueur hors de portée peut continuer à transférer des items. Toute requête mutante (`RequestTransferToPlayer`/`RequestTransferToContainer`) revalide la distance **avant** toute mutation (voir section 9, étape 1 de chaque sens de transfert) — un rejet de distance est un refus propre, aucune mutation, exactement comme un rejet de session ou d'`InstanceId` absent.

Une fois un rejet de distance constaté par le host lors d'une tentative de transfert, la session **doit** être invalidée côté host (retrait immédiat de `_viewers`) — un joueur hors de portée qui a échoué une fois ne doit pas rester considéré comme viewer valide pour une tentative suivante.

**Un mécanisme de notification ciblée est confirmé disponible** (Spike S0, S0-D, section 7) : le host notifie explicitement ce client de la fermeture forcée de sa session (`Rpc.FilterInclude` sur cette seule `Connection`), pour qu'il vide son cache local et ferme son panneau immédiatement, puis retire la connexion de `_viewers` — comportement propre, retenu comme comportement standard de cette V1, pas un simple repli optionnel.

---

## 9. Transfert atomique entre deux inventaires

### Audit préalable — comportement réel de `InventoryContainer.TryAddFirstFit`

**Point critique vérifié par lecture directe du code** (`Code/Items/Inventory/InventoryContainer.cs`, méthodes `TryAddFirstFit`/`FindFirstFit`/`CanPlace`/`AddPlacement`), pas supposé :

- `TryAddFirstFit(ItemInstance item, bool allowRotation = true)` ne fait **jamais** de fusion de pile. Aucune méthode de fusion/stacking n'existe nulle part dans `InventoryContainer` — seules `TryAdd`/`TryAddFirstFit`/`TryMove`/`TryRemove`/`TryConsume` existent, et aucune ne recherche une instance existante de même `ItemId` pour y ajouter une quantité.
- `TryAddFirstFit` refuse uniquement (`AlreadyContained`) si le **même** `InstanceId` est déjà présent dans le conteneur — il ne regarde jamais s'il existe une *autre* instance compatible pour y fusionner la quantité.
- La seule vérification d'occupation de cellule est `CanPlace` (collision géométrique rectangle contre rectangle, via `InventoryPlacement.Overlaps`) — jamais une compatibilité de pile.
- `AddPlacement(placement)` crée un nouvel `InventoryPlacement` qui enveloppe **la référence `ItemInstance` passée en paramètre**, sans jamais en cloner l'état ni en modifier la `Quantity`. La `Quantity` de l'instance transférée n'est donc jamais changée par `TryAddFirstFit` — elle reste rigoureusement celle qu'elle avait avant l'appel.
- Conséquence directe et vérifiée : **un appel `TryAddFirstFit(item)` réussi place toujours exactement la même `ItemInstance`/`InstanceId` que celle passée en argument, dans un emplacement vide géométriquement compatible — jamais fusionnée avec une autre instance, jamais avec une quantité modifiée.**

**Portée de cette garantie** : elle décrit le code **actuel**, pas une garantie contractuelle documentée sur la signature de `TryAddFirstFit` elle-même (aucun commentaire XML sur la méthode n'interdit explicitement une future fusion). Si un futur système de stacking (« Stacks, split/merge et quantités », roadmap étape 8) modifie un jour `TryAddFirstFit` ou lui ajoute un comportement de fusion automatique, **cette conception de transfert de conteneur devra être revue** — recommandation actée ci-dessous plutôt que supposée acquise pour toujours.

### Invariant V1 obligatoire pour un transfert de pile entière

Un transfert conteneur ↔ joueur doit garantir, et le garantit avec le comportement **actuellement vérifié** de `TryAddFirstFit` :

- aucune duplication ;
- aucune perte ;
- aucune présence finale (c'est-à-dire observable par un snapshot) dans les deux inventaires à la fois ;
- quantité conservée (`TryAddFirstFit` ne la modifie jamais) ;
- identité conservée (même `InstanceId`, puisque `TryAddFirstFit` ne crée ni ne clone d'instance) ;
- aucune fusion implicite qui ferait disparaître l'`InstanceId` source sans décision explicite (vérifié : aucune fusion n'existe dans le code actuel).

### Stratégie retenue — alternative minimale, pas une nouvelle primitive de noyau

Puisque le comportement réel de `TryAddFirstFit` satisfait déjà l'invariant ci-dessus (vérifié par lecture de code, pas supposé), la primitive dédiée envisagée (`TryTransferWhole(...)` ou équivalent conceptuel — réservation sans mutation, retrait de la source après validation complète, ajout de la même instance, aucune fusion) **n'est pas nécessaire pour cette V1**. Elle resterait une généralisation prématurée pour un seul appelant concret (violerait [csharp.md](../../.claude/rules/csharp.md)) tant que le comportement actuel de `TryAddFirstFit` suffit.

**Décision retenue : alternative minimale.** La V1 documente explicitement que le transfert de conteneur **interdit toute fusion automatique** et déplace toujours une instance complète vers un emplacement vide compatible, en s'appuyant sur le comportement vérifié de `TryAddFirstFit` — pas sur une hypothèse. **Recommandation de suivi, non bloquante pour cette V1** : le jour où un système de stacking est construit sur `InventoryContainer`, vérifier explicitement qu'il ne modifie pas le contrat de `TryAddFirstFit` exploité ici (ou introduire à ce moment-là une primitive de transfert dédiée qui contourne volontairement toute logique de fusion, si le stacking en ajoute une à `TryAddFirstFit` lui-même). Ce point reste une question ouverte de suivi, pas une action de cette mission — voir [OPEN_QUESTIONS.md](../status/OPEN_QUESTIONS.md).

### Comparaison des stratégies

| Stratégie | Applicable ici ? |
|---|---|
| Vérifier la destination, retirer la source, ajouter la destination | Risque : si l'ajout à la destination échouait après le retrait de la source, l'item serait perdu — à éviter sauf si l'ajout ne peut structurellement plus échouer après la vérification (ce qui n'est pas le cas ici : `TryAddFirstFit` peut échouer pour `NoAvailableSpace`, un fait dynamique qui peut changer entre la vérification et l'exécution s'il y avait un entrelacement — mais le host étant mono-thread, vérifier et exécuter dans la même méthode sans céder la main entre les deux revient au même que l'option suivante). |
| **Ajouter la destination avant de retirer la source** | **Retenue.** L'étape faillible (`TryAddFirstFit`, peut échouer si la destination est pleine) s'exécute en premier ; si elle réussit, le retrait de la source (`TryRemove` par `InstanceId` déjà vérifié présent) ne peut plus échouer sous l'hypothèse mono-thread déjà actée pour `LootSpawnPointComponent.HasEvaluated`/`WorldItemPickupComponent.IsClaimed`/l'équipement. **Exactement le patron déjà utilisé et validé par le déséquipement** (`TryUnequipAuthoritative` : ajout à la grille avant libération du slot). |
| Réservation préalable (verrou explicite) | **Non nécessaire.** Le host mono-thread garantit déjà qu'aucune seconde transaction ne peut s'intercaler entre la validation et la mutation d'une même méthode — un verrou explicite ajouterait un état à gérer (acquisition/libération, oubli de libération en cas d'exception) sans bénéfice au-delà de ce que l'ordre des opérations fournit déjà. |
| Transaction dédiée avec rollback | **Non nécessaire pour cette V1.** Un rollback réel n'est justifié que quand l'étape faillible **doit** s'exécuter après une mutation irréversible (cas du drop, où un `GameObject` réseau doit être créé et peut échouer après le retrait canonique). Ici, aucune création de `GameObject` n'est impliquée — les deux `InventoryContainer` (joueur et conteneur) sont deux structures en mémoire côté host, exactement comme pour l'équipement. L'ordre « faillible avant infaillible » suffit à garantir l'atomicité par construction, sans rollback dédié — **même raisonnement que la section « Atomicité et rollback » de l'équipement**, étendu à deux conteneurs indépendants au lieu d'un conteneur + un dictionnaire. |
| Primitive de transfert atomique au niveau du noyau (`InventoryContainer.TryTransferTo(other, instanceId)`) | **Envisageable comme raffinement futur**, pas nécessaire pour la V1 : le même résultat s'obtient en composant les primitives déjà existantes (`TryAddFirstFit`+`TryRemove`) au niveau du composant appelant, sans modifier `InventoryContainer`. Ajouter une méthode combinée au noyau maintenant serait une généralisation prématurée pour un seul appelant concret. |

### Conteneur → joueur

Étapes distinguées explicitement (validation de capacité / planification du placement / mutation de la source / mutation de la destination / notification des deux inventaires / diffusion des deux révisions — jamais fusionnées en une seule étape floue) :

1. **Résolution et validation** : `KodokuPlayerComponent.FindByConnection(Scene, Rpc.Caller)` (même patron que pickup/drop/équipement — **pas** de comparaison à `GameObject.Network.Owner`, puisque le conteneur n'a pas de owner joueur) ; vérification de session (`_viewers.Contains(caller)`, sinon refus propre) ; revalidation de distance (section 8) ; résolution de l'`InstanceId` dans **le conteneur** (`Container.GetPlacement(instanceId)`, absent → `ItemNotFound`). Aucune mutation à ce stade.
2. **Validation de capacité / planification du placement (destination)** : `playerInventory.Container.TryAddFirstFit(item)` — c'est l'étape **faillible** (échoue si l'inventaire du joueur est plein, `NoAvailableSpace`). Vérifié par audit de code (voir ci-dessus) : cet appel ne fusionne jamais l'instance transférée avec une autre, ne modifie jamais sa `Quantity`, et place la même référence `ItemInstance`/`InstanceId` dans un nouveau `InventoryPlacement`. Si cette étape échoue, **rien d'autre n'a été modifié** — refus propre, l'item reste dans le conteneur.
3. **Mutation de la source** : seulement si l'étape 2 a réussi — `containerInventory.Container.TryRemove(instanceId, out _)`. Ne peut plus échouer à ce stade sous l'hypothèse mono-thread déjà actée pour `LootSpawnPointComponent.HasEvaluated`/`WorldItemPickupComponent.IsClaimed`/l'équipement (l'existence a déjà été confirmée à l'étape 1, et aucune autre exécution ne peut s'intercaler entre temps sur un host mono-thread).
4. **État transitoire, jamais observable de l'extérieur** : entre la fin de l'étape 2 et la fin de l'étape 3, la même `ItemInstance`/`InstanceId` existe momentanément dans les deux `InventoryContainer` (ajoutée côté joueur, pas encore retirée côté conteneur) — ceci se produit **entièrement à l'intérieur d'un seul appel host synchrone**, avant tout envoi réseau. **Aucun snapshot ne doit être construit ni envoyé entre les étapes 2 et 3** — c'est une exigence d'implémentation explicite, pas seulement une conséquence attendue du mono-thread.
5. **Notification des deux inventaires, seulement une fois les deux mutations terminées** : révision du joueur incrémentée et snapshot envoyé **au propriétaire uniquement**, via `PlayerInventoryComponent.NotifyMutated()` (mécanisme déjà existant et inchangé) ; révision du conteneur incrémentée et snapshot poussé à **tous les viewers courants** (mécanisme de la section 7, sous réserve du spike). Chaque révision est incrémentée **une fois exactement**.
6. **Diffusion** : les deux snapshots (joueur, conteneur) sont deux envois réseau **distincts**, pas une seule transaction réseau atomique — ils peuvent être reçus dans un ordre différent par des observateurs différents (le joueur reçoit son propre snapshot ; les viewers du conteneur reçoivent le leur), mais les deux convergent vers **une seule transaction host déjà entièrement terminée** au moment où l'un ou l'autre est envoyé — jamais un état canonique partiellement mutué à l'origine d'un snapshot.

### Joueur → conteneur

Symétrique, mêmes étapes distinguées :

1. **Résolution et validation** : résolution du joueur, vérification de session, revalidation de distance — identiques. Résolution de l'`InstanceId` dans **l'inventaire du joueur** (`playerInventory.Container.GetPlacement(instanceId)`, absent → `ItemNotFound`). Un item actuellement équipé n'est **jamais** présent dans `Container` (déjà vrai aujourd'hui, exploité sans code supplémentaire — même constat que `PlayerItemUseComponent`), donc `ItemNotFound` couvre déjà ce cas sans vérification dédiée.
2. **Validation de capacité / planification du placement (destination = conteneur)** : `containerInventory.Container.TryAddFirstFit(item)` — étape faillible (conteneur plein). Même garantie vérifiée qu'au sens inverse : aucune fusion, `Quantity` inchangée, même `InstanceId`.
3. **Mutation de la source** : `playerInventory.Container.TryRemove(instanceId, out _)`, seulement si l'étape 2 a réussi — ne peut plus échouer à ce stade.
4. **État transitoire non observable, notification, diffusion** — mêmes garanties que le sens inverse (étapes 4 à 6 ci-dessus), symétriques : révision du conteneur (tous les viewers), révision du joueur (propriétaire seul), chacune une fois.

### Garanties

- Aucune duplication ni disparition **observable** — la même référence `ItemInstance` peut transitoirement exister dans les deux `InventoryContainer` entre les étapes 2 et 3 d'une même méthode synchrone (voir ci-dessus), mais **jamais** dans un état communiqué par un snapshot : à tout moment où un snapshot est construit, l'état canonique des deux côtés est déjà entièrement stabilisé (soit avant le début du transfert, soit après sa fin complète).
- Aucune révision partielle visible — chaque snapshot poussé reflète un état déjà entièrement mutué des deux côtés, jamais un état intermédiaire (le host ne construit ni n'envoie de snapshot entre les étapes 2 et 3).
- Aucun rollback nécessaire — conséquence directe de l'ordre choisi (faillible avant infaillible) **et** du comportement vérifié de `TryAddFirstFit` (pas de fusion, pas de mutation de quantité, même `InstanceId`) — pas d'un mécanisme de compensation après coup. **Cette absence de rollback dépend explicitement du comportement audité de `TryAddFirstFit` ci-dessus, pas d'une hypothèse.**
- Les deux snapshots (joueur, conteneur) ne forment **pas** une transaction réseau globale atomique — ce sont deux envois séparés qui peuvent arriver dans un ordre différent chez des observateurs différents — mais ils décrivent tous les deux le résultat d'une **transaction host unique déjà terminée**, jamais un état partagé partiellement construit.

---

## 10. Concurrence

| Scénario | Analyse |
|---|---|
| Deux joueurs prennent simultanément le même item (conteneur → joueur, deux appelants) | Le host mono-thread sérialise les deux appels `RequestTransferToPlayer` reçus. Le premier exécuté trouve le placement, le retire. Le second, exécuté juste après, ne le trouve plus (`GetPlacement` renvoie `null`) → `ItemNotFound`, refus propre. Même garantie que le pickup (`AlreadyClaimed`) et le drop (double requête déjà validée par test réel). Aucun verrou explicite nécessaire. |
| Un joueur prend un item pendant qu'un autre en dépose un (directions opposées, items différents) | Deux mutations indépendantes sur le même `InventoryContainer`, sérialisées par le host — aucune interaction problématique, chaque appel complet s'exécute avant que le suivant ne commence. |
| Double clic sur le même `InstanceId` | Identique au premier scénario — la seconde requête arrive après que la première a déjà muté l'état, `ItemNotFound` ou refus de destination pleine selon le cas, jamais de double effet. |
| Requête reçue après la fermeture (le viewer a fermé son panneau, ou a été retiré) | Revérifiée à chaque requête (section 8) — `_viewers.Contains(caller)` échoue → refus propre, aucune mutation. |
| Requête basée sur un snapshot obsolète | Non pertinent par construction — le host ne lit jamais un état « attendu » envoyé par le client, seulement l'`InstanceId` à résoudre fraîchement contre l'état canonique courant à chaque appel. |
| Joueur déconnecté pendant une transaction | Le modèle d'exécution observé dans ce projet est un appel host synchrone de bout en bout (aucune preuve de RPC asynchrone à callback différé dans le code actuel) — une déconnexion ne peut pas interrompre un appel déjà en cours d'exécution côté host. Ce qui peut arriver : une `Connection` présente dans `_viewers` devient invalide après coup. Nettoyage confirmé par test runtime réel (Spike S0, S0-G) via `Component.INetworkListener.OnDisconnected(Connection)`, déclenché immédiatement à la déconnexion, avec une purge défensive légère en complément avant chaque envoi (voir section 7). |
| Conteneur détruit ou scène déchargée pendant la consultation | Aucun système de déchargement de scène n'existe (une seule scène). La destruction directe d'un `GameObject` conteneur (cas rare en V1, aucune mécanique ne la déclenche) devrait, si elle survient, notifier les viewers restants (voir section 7) — pas bloquant pour cette V1 tant qu'aucune mécanique de destruction de conteneur n'est livrée (voir section 15). |
| Deux ouvertures simultanées (deux joueurs ouvrent en même temps) | Autorisé par design (V1 multi-viewer) — chacune ajoute sa propre `Connection` à `_viewers`, aucun conflit. |

**La sérialisation actuelle des appels host (mono-thread, confirmée par l'hypothèse déjà actée pour plusieurs composants existants) suffit pour la V1.** Aucun verrou par conteneur, aucun flag `IsProcessing`, aucune file d'opérations ne sont nécessaires — chaque requête est traitée par une méthode qui valide puis mute en une seule exécution synchrone, sans jamais cédé la main entre validation et mutation. Ajouter un verrou serait une protection contre un problème qui n'existe pas dans ce modèle d'exécution.

---

## 11. Snapshots et révisions

- **Structure** : nouveau type dédié, `WorldContainerSnapshotEntry` (namespace du conteneur, voir section « Composants proposés ») — mêmes six champs que `InventorySnapshotEntry` (`InstanceId` en chaîne canonique, `ItemId`, `Quantity`, `X`, `Y`, `IsRotated`). **Décision : ne pas réutiliser directement `InventorySnapshotEntry`** (namespace `Kodoku.Player.Inventory`, conceptuellement lié au joueur) ni refactorer ce type existant pour le rendre partagé — éviter tout changement sur un fichier déjà validé par huit scénarios de test réel, pour un gain de déduplication qui reste cosmétique (deux structures identiques en forme mais distinctes en intention documentée). **Reconsidérer un type partagé** dans `Code/Items/Inventory/` si un troisième cas d'usage identique apparaît un jour (cohérent avec le principe anti-généralisation prématurée déjà appliqué en section 5).
- **Contenu du snapshot** : `revision` (int), `entries` (`WorldContainerSnapshotEntry[]`). Pas de dimensions ni d'identifiant de conteneur dans la charge utile — les dimensions sont une donnée locale déjà identique sur chaque client (section 6), et l'identifiant n'est pas nécessaire au routage (chaque snapshot arrive scoped à l'instance de composant qui l'a envoyé, exactement comme aucune RPC existante du projet ne transmet d'identifiant de `GameObject` cible).
- **État ouvert** : dérivé de la présence d'un cache local non vide/valide côté client, jamais transmis comme champ séparé.
- **Erreurs éventuelles** : les échecs de transfert restent des `record struct` de résultat explicite (voir section 12), jamais encodés dans le snapshot lui-même — cohérent avec le style déjà établi (`PickupResult`/`DropResult`/`EquipmentOperationResult`/`ItemUseOperationResult`).
- **Snapshots de même révision** : acceptés (nécessaires pour une resynchronisation explicite qui doit reconstruire le cache même sans nouvelle mutation) — même règle que `PlayerInventoryComponent.ReceiveSnapshot` (`revision < LocalRevision` → ignoré ; `revision >= LocalRevision` → accepté).
- **Rejet des snapshots plus anciens** : identique.
- **Resynchronisation explicite** : `RequestContainerSnapshot()` (`[Rpc.Host]`), même rôle que `PlayerInventoryComponent.RequestSnapshot()` — revalide `_viewers.Contains(caller)` avant d'envoyer quoi que ce soit (pas de comparaison à un `Owner`, puisqu'il n'y en a pas ici).
- **Fermeture et vidage du cache local** : au `RequestClose()` réussi (ou à la réception d'une notification de fermeture forcée, section 7), le client vide son cache local immédiatement — même patron que `PlayerInventoryComponent.StopControl()`.
- **Cohérence multi-viewer** : le snapshot (revision + entries) est construit **une seule fois** par mutation, dans la même méthode host qui vient de muter `Container`, puis diffusé à l'ensemble courant de `_viewers` dans le même appel — jamais reconstruit séparément par viewer (ce qui pourrait, en théorie, désynchroniser deux viewers si une seconde mutation s'intercalait entre deux constructions — impossible ici puisque tout se passe en un seul appel synchrone, mais la garantie vient de la **discipline d'implémentation**, pas d'un mécanisme séparé, et doit être respectée à l'écriture du code).

---

## 12. Sécurité

Toute commande client est non fiable par défaut — cohérent avec chaque composant déjà existant du projet.

### Validations obligatoires côté host, pour toute requête mutante

- Appelant résolvable en pawn valide (`KodokuPlayerComponent.FindByConnection`).
- Le pawn résolu porte bien un `PlayerInventoryComponent` avec un `Container` non nul.
- Le caller est un viewer actuellement autorisé de ce conteneur précis (`_viewers.Contains`) — revérifié à chaque requête, jamais mis en cache côté client comme preuve.
- Proximité revalidée à chaque requête mutante (pas seulement à l'ouverture).
- Le conteneur (`GameObject`) est encore valide (`IsValid()`).
- L'`InstanceId` transmis existe réellement dans la structure source revendiquée (le conteneur pour un transfert vers le joueur, l'inventaire du joueur pour un transfert vers le conteneur) — jamais supposé présent parce que le client l'affiche dans son cache local.
- Quantité : non transmise du tout en V1 (transfert de pile entière uniquement) — rien à valider côté quantité au-delà de ce que `TryRemove`/`TryAddFirstFit` valident déjà en interne.
- Destination valide et place disponible — via le résultat de `TryAddFirstFit`, jamais présupposé par le client.
- Un item équipé n'est jamais transférable directement depuis l'équipement (il n'est simplement pas présent dans `Container`, donc `ItemNotFound` le couvre déjà sans code dédié).
- Aucun cache client (`LocalContainerEntries`, snapshot local du joueur) n'est jamais utilisé comme preuve d'autorisation dans une méthode métier — seule la structure canonique côté host (`Container`, `_viewers`) fait foi, exactement comme pour tous les composants déjà existants.

### Ce que le client est autorisé à transmettre

Commandes minimales, cohérentes avec le style déjà établi (aucune RPC existante du projet ne transmet de définition complète ni d'état final attendu) :

- `RequestOpen()` — aucun paramètre (la cible est implicite : ce `GameObject` précis).
- `RequestClose()` — aucun paramètre.
- `RequestTransferToPlayer(string instanceId)` — un seul paramètre.
- `RequestTransferToContainer(string instanceId)` — un seul paramètre. **Deux méthodes séparées plutôt qu'un paramètre de direction** (`enum Direction`) — cohérent avec le choix déjà fait pour `RequestEquip`/`RequestUnequip` (deux méthodes distinctes plutôt qu'un seul point d'entrée avec un booléen/enum de direction).
- **Aucune position ni rotation** transmise — la V1 n'accepte pas de placement manuel (section 3), `TryAddFirstFit` seul décide.
- **Jamais** : une `ItemDefinition` complète, un état final attendu, une quantité (transfert de pile entière uniquement), une révision supposée.

---

## 13. Persistance

### V1 runtime (ce qui est décidé maintenant, sans construire de sauvegarde)

- Le contenu est conservé tant que le `GameObject` conteneur existe — pas de vidage automatique.
- Comportement à la fermeture d'une session : aucun effet sur `Container`, seule la liste de viewers change.
- Comportement à la déconnexion d'un viewer : retiré de `_viewers` (nettoyage via `INetworkListener.OnDisconnected` ou validation de connexion avant diffusion — voir section 10), `Container` inchangé.

### Persistance future (décisions à ne pas bloquer, sans les implémenter ici)

- **`StableContainerId`** — recommandé comme champ réservé dès cette V1 (`[Property] string StableContainerId`, vide par défaut, assigné manuellement par conteneur placé en scène, comme un futur identifiant de sauvegarde indépendant du `RuntimeContainerId` généré à chaque session). Raison de le réserver maintenant plutôt que plus tard : une fois des dizaines de conteneurs placés dans des scènes sans cet identifiant, l'ajouter rétroactivement demande une passe de migration manuelle par scène ; le réserver maintenant est gratuit (un champ vide inutilisé) et évite ce coût futur. **Ceci est une recommandation, pas une décision figée** — à confirmer explicitement au moment de concevoir la sauvegarde elle-même.
- **Sérialisation des `ItemInstance` contenues** : même forme que `WorldContainerSnapshotEntry` (`InstanceId`, `ItemId`, `Quantity`, `X`, `Y`, `IsRotated`) — le format réseau double naturellement comme format de sauvegarde candidat, cohérent avec la façon dont `InventorySnapshotEntry` pourrait un jour servir au même usage pour l'inventaire joueur.
- **Conteneur vidé ou déjà pillé** : la persistance future devra distinguer « jamais initialisé » de « initialisé puis vidé » si un système de génération de contenu initial (analogue à `LootSpawnPointComponent`) est un jour ajouté aux conteneurs — non nécessaire pour cette V1 (aucune génération automatique de contenu de conteneur n'est prévue ici, contrairement au loot au sol).
- **Reconstruction après chargement** : soumise aux mêmes questions moteur déjà ouvertes pour tout late join (ordre `OnAwake`/`OnStart`/`OnNetworkSpawn` vs disponibilité des `[Sync]`, non confirmé — voir [OPEN_QUESTIONS.md](../status/OPEN_QUESTIONS.md)) — rien de spécifique aux conteneurs à ajouter à cette question déjà connue.
- **Duplication lors du respawn de scène** : si un futur système de sauvegarde reconstruit des `ItemInstance` à partir de données sérialisées, il devra utiliser `ItemInstance.Restore(...)` (déjà existant, jamais `CreateNew`) — même discipline déjà appliquée par `WorldItemComponent.TryRestoreFromNetworkState()` pour les items du monde, à réutiliser telle quelle plutôt qu'inventée à nouveau pour les conteneurs.

**Rien de ce qui précède n'est implémenté dans cette V1** — seul le champ `StableContainerId` est recommandé comme réservation immédiate et sans coût.

---

## 14. UI

Décrite sans développement, en extension du panneau debug déjà établi (`Code/UI/Debug/InventoryDebugPanel.razor`), cohérent avec son rôle actuel (outil de développement local, Tab, jamais une UI finale).

- **Disposition** : inventaire joueur d'un côté, conteneur ouvert de l'autre (même panneau étendu ou un second panneau juxtaposé — détail d'implémentation, pas une décision d'architecture réseau).
- **État de chargement initial** : le panneau du conteneur n'affiche rien tant qu'aucun snapshot n'a été reçu depuis l'ouverture — même patron que `InventoryDebugPanel` actuel, qui n'affiche rien tant que `Inventory` est `null`.
- **Fermeture** : bouton explicite appelant `RequestClose()`, vide immédiatement le cache local côté client (pas besoin d'attendre une confirmation réseau pour l'affichage, le serveur retire le viewer de toute façon).
- **Rafraîchissement après snapshot** : réactivité pilotée par `BuildHash()` incluant la révision du conteneur — même mécanisme déjà validé pour `InventoryDebugPanel`/`GameHud`.
- **Retour d'échec** : un transfert refusé (conteneur/inventaire plein, item disparu) n'a besoin d'aucun message dédié pour cette V1 debug — l'absence de changement de révision est déjà le signal (cohérent avec le fait qu'aujourd'hui, Drop/Equip/Use échoués ne produisent qu'un log serveur, visible en debug, pas un message UI dédié).
- **Fermeture automatique** : non applicable en V1 (pas de fermeture par distance, section 3) — seule une fermeture forcée par le host (conteneur détruit) devrait, si implémentée, vider le cache local en réaction à un message explicite (section 7).
- **Item disparu à cause d'un autre joueur** : géré naturellement par le rafraîchissement piloté par révision — aucun code spécial, le prochain snapshot ne contient plus l'entrée disparue.
- **Réouverture** : identique à une première ouverture — pas d'état intermédiaire particulier à gérer.
- **Cache local du conteneur** : `LocalContainerEntries`/`LocalContainerRevision`, exactement au même niveau de confiance que `PlayerInventoryComponent.LocalEntries` (jamais une preuve d'autorisation, uniquement pour l'affichage).

### Ce que l'UI doit référencer

- Un composant local : la référence au `WorldContainerComponent` actuellement ouvert, obtenue **localement** (au moment où l'interaction réussit, pas via un état networké partagé) — jamais un `GameObject`/`Component` sérialisé comme `[Property]` d'éditeur (contraire à [csharp.md](../../.claude/rules/csharp.md), référence fragile), mais une référence en mémoire côté client, résolue à l'ouverture réussie et effacée à la fermeture — même esprit que `KodokuPlayerComponent.Local` (statique, réévalué par événements, jamais un lien de scène figé).
- Pas de session/token distinct (section 8) — l'appartenance à `_viewers` côté host suffit, l'UI n'a besoin de rien de plus qu'une référence locale au composant.

---

## 15. Relation avec les systèmes existants

| Système | Interaction avec les conteneurs du monde |
|---|---|
| `WorldItemPickupComponent` | Système séparé, inchangé. Un item retiré d'un conteneur va dans l'inventaire du joueur (jamais directement re-matérialisé comme `WorldItem` — ce serait un drop, pas un transfert de conteneur). |
| `PlayerItemDropComponent` | **Reste une opération séparée** (décision explicite ci-dessous). Le drop au sol garde son propre chemin (clone de prefab, `NetworkSpawn`), le conteneur un chemin purement en mémoire (deux `InventoryContainer`) — pas de fusion des deux systèmes. |
| `PlayerItemUseComponent` | **Un item du conteneur ne peut pas être utilisé directement en V1** — décision explicite : il doit d'abord être transféré vers l'inventaire du joueur, puis utilisé normalement. Étendre `PlayerItemUseComponent` pour lire un conteneur arbitraire multiplierait la surface de validation (quel conteneur, session valide, etc.) pour un gain d'ergonomie non prouvé nécessaire à cette V1 — réévaluable plus tard. |
| `PlayerInventoryComponent` | Aucune modification requise. Les transferts invoquent ses opérations publiques déjà existantes (`Container.TryAddFirstFit`/`TryRemove`, `NotifyMutated()`) exactement comme `WorldItemPickupComponent` le fait déjà aujourd'hui pour le pickup. |
| `EquipmentSlotType`/équipement | **Un item équipé ne peut pas être déposé directement dans un conteneur en V1** — décision explicite : il doit d'abord être déséquipé (`RequestUnequip`, déjà existant), ce qui le replace dans `Container`, puis transféré normalement. Un seul chemin clair par mouvement d'item, pas une combinatoire de raccourcis. |
| `ItemDefinition` | Aucune modification requise — un conteneur du monde n'a besoin d'aucune nouvelle propriété sur `ItemDefinition` (contrairement à l'équipement/l'usage, qui ont ajouté `EquipmentSlot`/`ThirstRestoreAmount`). |
| `InventoryContainer` | Réutilisé tel quel, aucune modification de son API publique nécessaire pour cette V1 (voir section 9 sur la primitive de transfert combinée, envisageable mais non nécessaire). |
| `LootSpawnPointComponent` | Système séparé et sans dépendance — un conteneur du monde n'est **pas** un point de spawn de loot ; il pourrait en théorie être *rempli* par un mécanisme de génération initiale similaire un jour, mais ceci est explicitement hors périmètre de cette V1 (aucune génération automatique de contenu de conteneur n'est conçue ici). |
| Interaction monde | Réutilise le même mécanisme stock (`Component.IPressable`) déjà validé pour le pickup — aucun nouveau scanner. |
| Futures sauvegardes | Voir section 13 — seule la réservation du champ `StableContainerId` est recommandée maintenant. |
| Destruction d'un conteneur → éjection du contenu au sol | **Non prévu en V1** — aucune mécanique de destruction de conteneur n'existe. Si construite plus tard, elle devrait réutiliser la machinerie de spawn+rollback du drop (un `WorldItem` par entrée éjectée, avec la même prudence sur l'ordre initialisation/spawn), pas le modèle d'atomicité par ordre seul de l'équipement/des conteneurs (puisqu'elle impliquerait la création de plusieurs `GameObject`, donc un risque d'échec après mutation, exactement comme le drop). |

---

## 16. Questions encore ouvertes

Toutes explicitement à trancher/vérifier avant ou pendant l'implémentation, pas résolues arbitrairement ici. Les deux premières questions de la version précédente de ce document (forme exacte de `Rpc.FilterInclude`, nettoyage à la déconnexion) sont **résolues** par le Spike S0 (section 7) et retirées de cette liste.

1. **Notification de fermeture forcée aux viewers restants** (conteneur détruit pendant consultation) — mécanisme de transport confirmé (invalidation ciblée, section 7), mais le contenu exact du message et le déclenchement précis (quand un `GameObject` conteneur est détruit) restent à concevoir à l'implémentation.
2. **Ordre late join des `[Sync]` vs cycle de vie des composants** — question déjà ouverte pour tout le projet ([OPEN_QUESTIONS.md](../status/OPEN_QUESTIONS.md)), pas spécifique aux conteneurs mais pertinente pour `RuntimeContainerId` si un late joiner doit un jour le lire avant toute interaction.
3. **`StableContainerId`** — décision de le réserver maintenant recommandée, pas encore actée formellement (à confirmer avec l'utilisateur avant l'implémentation si jugé nécessaire, ou à trancher à l'implémentation elle-même comme un détail réversible).
4. **Test de concurrence déterministe** — conception qui le permettrait (séparation transport RPC/méthode métier, comme pour pickup/drop/équipement), outil non construit — même réserve que les jalons précédents.
5. **Comportement exact quand un viewer s'éloigne pendant que le panneau reste ouvert** (pas de fermeture automatique, section 3/8) — accepté comme limite connue de cette V1, pas une question bloquante mais un compromis explicite à documenter dans un futur retour utilisateur si jugé gênant en pratique.
6. **Idempotence ajout/retrait de viewer non exercée en runtime** (S0-E/S0-F, section 7) — garde de code existante, non bloquante, à confirmer un jour par un outil de test déterministe si jugé utile.

---

## 17. Matrice de tests proposée — Spike S0 exécuté et validé, matrice A à R non exécutée

**Ces scénarios sont des validations runtime réelles à plusieurs instances (host + clients distants), pas des tests automatisés — aucune suite de tests automatisée n'existe dans ce projet (voir [CLAUDE.md](../../CLAUDE.md#compilation-et-tests)).**

### Spike S0 — transport multi-viewer — **EXÉCUTÉ ET VALIDÉ EN RUNTIME RÉEL**

Exécuté avec un host et deux clients distants (branche `spike/world-container-multiviewer-rpc`, composant jetable `MultiViewerRpcSpikeComponent`, depuis supprimé — voir section « Nettoyage » du plan d'implémentation). Résultats détaillés (logs, séquences, verdicts scénario par scénario) : [docs/research/WORLD_CONTAINER_MULTIVIEWER_SPIKE.md](../research/WORLD_CONTAINER_MULTIVIEWER_SPIKE.md).

Résumé des verdicts :

- **S0-A** (aucun viewer) — PASS.
- **S0-B** (un viewer) — PASS.
- **S0-C** (deux viewers) — PASS.
- **S0-D** (invalidation ciblée puis retrait) — PASS.
- **S0-E** (ajout dupliqué) — **non exécuté**, limite de l'outil debug utilisé, non bloquant (voir section 7, « Idempotence »).
- **S0-F** (retrait idempotent) — **non exécuté**, même réserve.
- **S0-G** (déconnexion d'un viewer) — PASS.
- **S0-H** (late join) — PASS.
- **S0-I** (réouverture) — PASS.

**Ce spike ne valide pas les conteneurs eux-mêmes** — il confirme uniquement que le mécanisme réseau dont toute la section 7 dépend se comporte comme désormais documenté. L'implémentation de `WorldContainerComponent` peut commencer (voir « Plan d'implémentation » ci-dessous).

### Matrice A à R — exécutable maintenant que le Spike S0 est validé, non exécutée dans cette mission

| # | État initial | Action | Résultat host attendu | Snapshot attendu | Révisions attendues | État final | Duplication/perte |
|---|---|---|---|---|---|---|---|
| A | Conteneur vide, host à portée | Host ouvre le conteneur | Succès, host ajouté aux viewers | Snapshot initial (0 entrée) reçu par le host | Conteneur : révision 0 | Conteneur ouvert côté host | Aucune |
| B | Conteneur avec 1 item, client à portée | Client distant ouvre le conteneur | Succès, client ajouté aux viewers | Snapshot initial (1 entrée) reçu **uniquement** par le client | Conteneur : révision inchangée (pas de mutation, juste une ouverture) | Conteneur ouvert côté client, contenu visible | Aucune |
| C | Conteneur avec 1 item, joueur l'a ouvert | Transfert conteneur → inventaire joueur | Succès | Snapshot conteneur (0 entrée) à tous les viewers ; snapshot joueur (1 entrée) au propriétaire seul | Conteneur +1, joueur +1 | Item dans l'inventaire joueur, absent du conteneur, même `InstanceId` | Aucune |
| D | Joueur possède 1 item, conteneur ouvert et avec de la place | Transfert inventaire joueur → conteneur | Succès | Snapshot joueur (0 entrée) au propriétaire ; snapshot conteneur (1 entrée) à tous les viewers | Joueur +1, conteneur +1 | Item dans le conteneur, absent de l'inventaire joueur, même `InstanceId` | Aucune |
| E | Inventaire joueur plein, conteneur avec 1 item, conteneur ouvert | Transfert conteneur → joueur | Échec (`InventoryFull` ou équivalent) | Aucun snapshot envoyé | Aucune révision changée | Item toujours dans le conteneur | Aucune |
| F | Conteneur plein, joueur possède 1 item, conteneur ouvert | Transfert joueur → conteneur | Échec (`ContainerFull` ou équivalent) | Aucun snapshot envoyé | Aucune révision changée | Item toujours dans l'inventaire joueur | Aucune |
| G | Conteneur avec 2 items | Deux joueurs (host + client) ouvrent le même conteneur | Les deux succès, les deux ajoutés aux viewers | Chacun reçoit son propre snapshot initial identique (2 entrées, même révision) | Conteneur : révision inchangée | Les deux voient le même contenu | Aucune |
| H | Conteneur avec 1 item, deux joueurs l'ont ouvert | Les deux tentent de transférer le **même** `InstanceId` vers leur inventaire, quasi simultanément | Le premier exécuté réussit, le second échoue (`ItemNotFound`) | Le gagnant reçoit son snapshot joueur (+1) ; les deux viewers reçoivent le snapshot conteneur (0 entrée) | Conteneur +1 (une seule fois), joueur gagnant +1, joueur perdant inchangé | Un seul joueur a l'item, jamais les deux | Aucune |
| I | Conteneur avec 1 item, joueur l'a ouvert | Le joueur envoie deux fois la même requête de transfert pour le même `InstanceId` | La première réussit, la seconde échoue (`ItemNotFound`) | Un seul snapshot de mutation, la seconde requête n'en déclenche aucun | Une seule incrémentation de chaque révision concernée | Item transféré une seule fois | Aucune |
| J | Joueur a fermé la session (`RequestClose` déjà envoyé) | Le joueur envoie quand même une requête de transfert | Échec (`NotAViewer` ou équivalent) | Aucun snapshot | Aucune révision changée | Aucun changement | Aucune |
| K | Joueur s'est éloigné du conteneur sans fermer explicitement | Le joueur tente un transfert | Échec (distance revalidée, hors portée) | Aucun snapshot | Aucune révision changée | Aucun changement | Aucune |
| L | Client viewer d'un conteneur | Client se déconnecte brutalement | Connexion retirée de `_viewers` côté host, aucune exception applicative | Aucun snapshot dû à ce viewer (il n'existe plus) | Aucune révision changée par la déconnexion elle-même | Conteneur intact, viewer restant (le cas échéant) non affecté | Aucune |
| M | Conteneur avec 2 items, créé avant la connexion d'un nouveau joueur | Nouveau joueur (late join) se connecte puis ouvre le conteneur | Succès | Snapshot initial avec les 2 entrées, mêmes `InstanceId` que ceux créés avant sa connexion | Conteneur : révision courante renvoyée telle quelle | Late joiner voit le contenu réel, pas un état vide par défaut | Aucune |
| N | Cache local d'un viewer volontairement corrompu (outil debug) | `RequestContainerSnapshot()` (resync explicite) | Succès | Snapshot complet renvoyé, identique à l'état canonique | Révision inchangée par la resync elle-même | Cache local reconstruit fidèlement | Aucune |
| O | Deux conteneurs distincts, chacun avec un item différent | Transfert depuis le conteneur A uniquement | Succès sur A, B totalement inaffecté | Seul A envoie un snapshot de mutation ; B n'envoie rien | Révision de A incrémentée, révision de B inchangée | Contenu de B strictement identique à avant | Aucune |
| P | Item transféré conteneur ↔ joueur plusieurs fois de suite (cycles A→B→A→B) | Cycles successifs de transfert | Chaque transfert réussit | Snapshots cohérents à chaque étape | Révisions incrémentées à chaque étape, des deux côtés concernés | Le même `InstanceId` est conservé à travers tous les cycles, jamais régénéré | Aucune |
| Q | Inventaire joueur plein, conteneur ouvert | Tentative de transfert conteneur → joueur qui échoue | Échec propre | Aucun snapshot | Aucune révision changée | Aucune mutation partielle observable (ni le conteneur ni l'inventaire joueur n'ont changé) | Aucune |
| R | Conteneur ouvert par un ou plusieurs viewers | Conteneur détruit/désactivé pendant la consultation | Viewers restants notifiés (si implémenté) ou, a minima, aucune exception applicative lors d'une tentative d'action ultérieure | Notification de fermeture forcée (si implémentée) ou refus propre à la prochaine requête | Aucune révision incohérente | Cache local des viewers vidé (immédiatement si notifié, ou au prochain refus sinon) | Aucune |

**Ne pas exécuter ces tests dans cette passe** — conforme à la demande, aucune instance n'a été lancée, aucun log réel n'existe pour ce document.

---

## 18. Plan d'implémentation proposé

Découpage réel dérivé de l'audit :

1. ~~**Spike technique multi-viewer isolé** (Spike S0, section 17)~~ — **FAIT.** Exécuté en runtime réel, composant jetable supprimé après validation (voir section « Nettoyage »).
2. ~~**Décision/ADR courte sur le transport retenu**~~ — **FAIT.** Voir section 7 de ce document (mise à jour avec le résultat réel) et [ADR-0006](../decisions/ADR-0006-WORLD-CONTAINER-VIEWER-TRANSPORT.md).
3. **Primitive de transfert entier ou planification de placement** — sur la base du comportement déjà audité de `TryAddFirstFit` (section 9) : soit confirmer que la composition `TryAddFirstFit`+`TryRemove` suffit telle quelle (probable, vu l'audit), soit introduire à ce moment-là une primitive dédiée si un test réel révèle un écart avec le comportement audité en lecture de code.
4. **`WorldContainerComponent` canonique** — `InventoryContainer` host-only, dimensions, pas encore de réseau ni de session. Validable en solo (comme `InventoryCoreDebugComponent` l'a été pour `InventoryContainer`). **Prochaine étape réelle du projet** — non commencée.
5. **Sessions/viewers et snapshots** — `RequestOpen`/`RequestClose`, `_viewers`, revalidation de distance (réutilisant la formule déjà validée du pickup), snapshot (`WorldContainerSnapshotEntry`) branché sur le mécanisme confirmé à l'étape 1/2 (`Rpc.FilterInclude` + `[Rpc.Broadcast]`), resynchronisation explicite.
6. **Transfert conteneur → joueur.**
7. **Transfert joueur → conteneur.**
8. **UI debug minimale** — extension du panneau debug existant (ouverture/fermeture, liste d'entrées, boutons de transfert).
9. **Robustesse multi-viewer** — scénarios de concurrence (G, H, I, J, K, L de la matrice).
10. **Tests runtime A à R** — exécution complète de la matrice de la section 17, y compris les scénarios non couverts par l'étape 9 (M, N, O, P, Q, R).
11. **Nettoyage et documentation** — retrait de tout outil de debug temporaire, mise à jour de ce document avec les résultats réels, plus `CURRENT_STATE.md`/`ROADMAP.md`/`OPEN_QUESTIONS.md` une fois l'implémentation validée par test réel.

Les étapes peuvent être ajustées selon l'audit, mais aucune implémentation de conteneur (étape 4 et suivantes) n'a encore commencé. Chaque étape est testable et revue séparément.
