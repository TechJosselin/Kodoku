# Architecture — Split et merge réseau de l'inventaire joueur (Jalon 2)

**Statut : Jalon 2 — conception auditée et corrigée, implémentation non commencée.** Document produit par un audit de code réel (pas d'hypothèse non vérifiée) — même méthodologie et même distinction que [INVENTORY_STACK_ARCHITECTURE.md](INVENTORY_STACK_ARCHITECTURE.md) :

- **[IMPLÉMENTÉ]** — déjà présent et validé dans `main` au moment de cette mission (HEAD `793da32`).
- **[RECOMMANDÉ]** — conception proposée par ce document, non implémentée, à valider par l'utilisateur avant tout code.
- **[OUVERT]** — question non tranchée.

**Ce document ne répète pas ce qui est déjà tranché par [INVENTORY_STACK_ARCHITECTURE.md](INVENTORY_STACK_ARCHITECTURE.md)** (politique d'identité section 3, qui crée les instances section 4, architecture à trois couches section 5, sémantique split/merge section 6, préconditions de fraîcheur section 7, révisions section 8, poids section 9) — il y renvoie et **connecte** ces décisions déjà actées au système réseau concret de `PlayerInventoryComponent`, en tranchant les points que ce document précédent laissait explicitement ouverts pour l'implémentation (section 16 de ce même document : noms exacts des RPC/types).

Aucune ligne de code de gameplay n'a été écrite pour ce jalon. Aucun fichier C#/Razor/SCSS n'a été modifié par cette mission.

**Corrections apportées par une relecture dédiée du 2026-07-24** (même jour, sans nouvelle inspection de code au-delà de la revérification des précédents déjà cités — relecture et correction de conception uniquement) : cinq défauts de la version initiale ont été identifiés et corrigés, marqués explicitement « corrigé » aux sections concernées —

- **section 15/17/18** : le résultat réseau (`PlayerInventoryStackResult`) portait initialement aucune révision, au motif que le snapshot suffit et précède toujours le résultat — **insuffisant**, l'ordre d'*envoi* de deux RPC ne garantit pas l'ordre de leur *traitement* côté client. Un champ `AppliedRevision` est ajouté, et la règle de déverrouillage du pending est corrigée en conséquence (`LocalRevision >= AppliedRevision`, pas simplement « au résultat »).
- **section 17** : la stratégie `RollbackFailed` n'était pas distinguée assez précisément d'un échec ordinaire vis-à-vis de la révision — corrigée en une politique à trois cas explicites (échec ordinaire : aucune révision ; `InvariantViolation` avec rollback réussi : aucune révision ; `RollbackFailed` : révision forcée + snapshot forcé), jamais présentée au client comme une annulation propre.
- **section 19** : la structure pending restait décrite de façon insuffisamment précise sur l'atomicité d'enregistrement/libération à plusieurs identifiants — précisée avec une structure conceptuelle dédiée (`PendingStackOperation`) et des règles d'enregistrement/libération explicitement atomiques.
- **section 24** : la quantité de split (« moitié arrondie ») était une formulation ambiguë — remplacée par la formule exacte `splitAmount = source.Quantity / 2` (division entière), avec exemples chiffrés.
- **section 23** : la matrice de tests ne couvrait pas explicitement les nouveaux scénarios de révision/pending multi-items/quantité exacte de split introduits par les corrections ci-dessus — complétée (sections I, J, K, plus un scénario H supplémentaire).

Le reste du document (architecture retenue, signatures RPC, préconditions de fraîcheur, mapping des erreurs hors révision) n'a pas été remis en cause par cette relecture — seulement mis à jour où ces cinq corrections s'y répercutaient.

**Corrections apportées par une relecture finale dédiée du 2026-07-24** (même jour, architecture review en lecture seule suivie d'une mission de correction ciblée) — quatre défauts importants et cinq défauts mineurs supplémentaires identifiés et corrigés :

- **section 24/26** : `RequestMergeExact` était présentée comme « API réseau implémentée et testée », contredisant le statut conception-uniquement du document — reformulée au futur (« devra être implémentée et couverte par la matrice de tests »).
- **section 17** : le mécanisme exact de `RollbackFailed` ne précisait pas explicitement qu'un seul appel à `NotifyMutated()` suffit (celle-ci effectuant déjà l'incrément et l'envoi du snapshot) — un futur code du type `HostRevision++; NotifyMutated();` aurait doublé la révision. Règle normative ajoutée : un seul appel à `NotifyMutated()`, jamais un incrément manuel séparé.
- **section 14/18** : la table des scénarios `RequestId` affirmait encore « le snapshot arrive toujours en premier », contredisant directement la correction de la section 18 — corrigée pour refléter que les deux ordres d'application sont possibles et gérés ; un cas explicite « révision plus récente » (`LocalRevision > AppliedRevision`) a été ajouté section 18.
- **section 15** : la valeur de `RequestedAmount` dans le résultat de `RequestMergeUntilCapacity` n'était jamais explicitée — précisée à `0` (succès comme échec), avec table récapitulative par opération.
- Corrections mineures : trois cross-références stales « section 15 » corrigées vers « section 24 » (périmètre UI) ; l'identifiant du scénario `RollbackFailed` aligné sur la série H sous le nom `N2-H4`, pour rester cohérent avec `N2-H1` à `N2-H3` de la même section ; bannière de statut alignée sur « auditée et corrigée » ; `CriticalRollbackFailure` reformulé comme tag de log plutôt que symbole de code ; scénario « résultat d'une ancienne page UI » ajouté à la matrice (`N2-F5`), portant le total à **63 scénarios documentés**.

---

## 1. Périmètre inspecté

Fichiers lus intégralement pour cet audit :

- `Code/Items/Inventory/InventoryContainer.cs`, `InventoryStackTransactions.cs`, `StackTransactionResult.cs`, `StackTransactionFailureReason.cs`, `InventoryOperationResult.cs`, `InventoryFailureReason.cs`, `InventoryPlacement.cs`
- `Code/Items/Instances/ItemInstance.cs`, `Code/Items/Definitions/ItemDefinition.cs`
- `Code/Players/Inventory/PlayerInventoryComponent.cs` (intégral, 612 lignes), `InventorySnapshotEntry.cs`, `PlayerInventoryMoveFailureReason.cs`, `PlayerInventoryMoveResult.cs`, `EquipmentFailureReason.cs`, `EquipmentOperationResult.cs`, `EquipmentSnapshotEntry.cs`, `DropFailureReason.cs`, `DropResult.cs`, `PlayerItemDropComponent.cs` (intégral), `ItemUseFailureReason.cs`, `ItemUseOperationResult.cs`
- `Code/World/Containers/WorldContainerTransferFailureReason.cs`, `WorldContainerMoveFailureReason.cs`, `WorldContainerTransferResult.cs` (précédent cross-conteneur, pour cohérence de nommage)
- `Code/UI/Menu/Pages/InventoryPage.razor` (intégral, 1193 lignes), `Code/UI/Inventory/InventoryGridItem.cs`, `InventoryGridGhost.cs`
- `docs/architecture/INVENTORY_STACK_ARCHITECTURE.md` (intégral, 521 lignes), `docs/status/CURRENT_STATE.md`, `ROADMAP.md`, `OPEN_QUESTIONS.md`

Aucune contradiction entre documentation et code réel n'a été trouvée pendant cet audit — `INVENTORY_STACK_ARCHITECTURE.md` reflète fidèlement l'état de `main` après le Jalon 1.

---

## 2. Architecture réseau actuelle — ce qui existe déjà à réutiliser

`PlayerInventoryComponent` porte déjà, pour quatre domaines distincts (déplacement, équipement, drop, usage), **exactement le même patron**, sans exception :

1. Une méthode `[Rpc.Host] RequestX(...)` — transport uniquement : résout `Rpc.Caller`, vérifie `GameObject.Network.Owner == caller` (jamais `KodokuPlayerComponent.FindByConnection` pour ce premier filtre, la RPC cible déjà directement le pawn de l'appelant), puis délègue à la méthode métier et logue le résultat.
2. Une méthode `TryXAuthoritative(Connection requester, ...)` — transaction métier complète, **pas une RPC**, prend une `Connection` explicite (permet un futur appel direct depuis un test déterministe) : revalide tout (ownership via `KodokuPlayerComponent.FindByConnection` + comparaison `pawn.GameObject == GameObject`, existence, compatibilité, fraîcheur), mute `Container` seulement après validation complète, appelle `NotifyMutated()` sur succès.
3. Un type `XResult` (`readonly record struct`) + un type `XFailureReason` (`enum`) **dédiés à ce domaine précis** — jamais un type partagé entre domaines, même quand un concept se répète (`StaleSource` est redéfini indépendamment dans `PlayerInventoryMoveFailureReason`, `WorldContainerMoveFailureReason` et `WorldContainerTransferFailureReason` — trois enums distincts, même nom de valeur, aucune tentative de les unifier).
4. Pour les opérations qui ont un état canonique observable même en cas d'échec incertain (déplacement) : un canal de résultat ciblé dédié, `[Rpc.Owner] ReceiveXResult(...)`, corrélé par un `RequestId` **généré côté client** (`InventoryPage.TryRegisterPending`, jamais recalculé côté host). Pour les opérations sans ambiguïté possible (équiper/déséquiper/drop/use), aucun canal de résultat dédié n'existe — le client déduit le succès de la différence observée dans le prochain snapshot.

**Ce jalon suit ce patron à l'identique, sans exception.** Split et merge sont des opérations de quantité, avec le même besoin de corrélation qu'un déplacement (un échec de fraîcheur doit être distingué d'un succès pour que le pending UI se libère correctement) — elles reçoivent donc un canal de résultat dédié, exactement comme `RequestMoveItem`/`ReceiveMoveResult`.

---

## 3. Contrat du Stack Core existant (Jalon 1, rappel des signatures)

Toutes ces méthodes sont **déjà implémentées et validées** (`Code/Items/Inventory/InventoryStackTransactions.cs`) — le réseau ne fait que les appeler après ses propres validations, jamais ne les modifie :

```csharp
StackTransactionResult TrySplit( InventoryContainer container, Guid sourceInstanceId, int amount, ItemInstance preparedInstance, bool allowRotation = true )
StackTransactionResult TrySplitAt( InventoryContainer container, Guid sourceInstanceId, int amount, ItemInstance preparedInstance, int targetX, int targetY, bool targetRotated )
StackTransactionResult TryMergeExact( InventoryContainer sourceContainer, InventoryContainer targetContainer, Guid sourceInstanceId, Guid targetInstanceId, int amount )
StackTransactionResult TryMergeUntilCapacity( InventoryContainer sourceContainer, InventoryContainer targetContainer, Guid sourceInstanceId, Guid targetInstanceId )
```

Pour l'inventaire joueur (Jalon 2), `sourceContainer` et `targetContainer` sont **toujours le même `InventoryContainer`** (celui du pawn de l'appelant) — jamais deux conteneurs distincts, ce cas n'apparaît qu'au Jalon 3 (`WorldContainerComponent`).

**Préconditions déjà garanties par le Stack Core** (le réseau ne doit jamais les revalider en double) :
- bornes de `amount` (`1 <= amount <= source.Quantity - 1` pour un split, `amount <= source.Quantity` pour un merge exact) ;
- validité de l'instance préparée (non nulle, `InstanceId` neuf, `Quantity == amount`, compatible avec la source) ;
- compatibilité de pile (`CanStackWith`) ;
- validité spatiale de la destination (`CanPlace`/`TryFindFirstFit`) ;
- atomicité/rollback interne (`InvariantViolation`/`RollbackFailed`).

**Préconditions qui restent une responsabilité réseau exclusive** (le Stack Core n'en a et ne doit en avoir aucune notion) :
- fraîcheur (`ExpectedSourceQuantity`/`ExpectedTargetQuantity`/`ExpectedSourceX/Y/Rotated`) ;
- ownership/autorité (`Rpc.Caller`, `GameObject.Network.Owner`) ;
- `RequestId`, révision, snapshot, résultat corrélé.

---

## 4. Architecture réseau comparée — verdict

### Option A — Méthodes directement dans `PlayerInventoryComponent` — **[RECOMMANDÉ], retenue**

C'est déjà, sans exception, comment `RequestMoveItem`/`RequestEquip`/`RequestUnequip`/`RequestDrop` (ce dernier dans un composant frère, mais même patron) sont construits dans ce projet. Aucun de ces quatre domaines n'a de handler dédié — chacun vit dans le composant qui porte l'état canonique qu'il mute.

### Option B — Handler interne dédié (`PlayerInventoryStackRequestHandler`) — rejetée

Introduirait une abstraction sans second cas d'usage : un seul composant (`PlayerInventoryComponent`) aurait besoin de ce handler pour ce seul jalon. `.claude/rules/csharp.md` : « pas de couche d'abstraction sans besoin concret ». Le handler devrait de toute façon détenir une référence au composant pour accéder à `Container`/`NotifyMutated`/`HostRevision`, sans gagner de testabilité réelle par rapport à une méthode publique non-RPC déjà directement testable (`TryMoveItemAuthoritative` l'est déjà, aucun harness dédié n'a jamais été nécessaire pour l'appeler directement).

### Option C — RPC générique de transaction (`RequestStackTransaction(StackOperationType, ...)`) — rejetée

Un seul point d'entrée RPC avec un paramètre d'enum de mode introduirait des états impossibles à représenter (ex. `TargetInstanceId` rempli pour un split, ou `AllowRotation` rempli pour un merge) — chaque appelant devrait connaître quels champs sont pertinents pour quel mode, une classe de bug que des signatures distinctes éliminent structurellement. Aucun précédent dans ce projet n'utilise ce patron (RPC + enum de mode) ; chaque domaine expose des méthodes nommées explicitement.

**Verdict retenu** : quatre méthodes `[Rpc.Host]` nommées, directement sur `PlayerInventoryComponent` — `RequestSplitItem`, `RequestSplitItemAt`, `RequestMergeExact`, `RequestMergeUntilCapacity` — chacune déléguant à sa propre méthode non-RPC `TryXAuthoritative`, exactement le patron des quatre domaines déjà implémentés.

---

## 5. Requêtes réseau distinctes ou unifiées — verdict

Même raisonnement que la section précédente, appliqué spécifiquement au nombre de RPC (question distincte de « où vit le code ») :

- **Option A (requêtes séparées par opération)** — **[RECOMMANDÉ], retenue**. `RequestSplitItem`/`RequestSplitItemAt`/`RequestMergeExact`/`RequestMergeUntilCapacity`, quatre signatures, chacune avec exactement les champs qu'elle utilise réellement.
- **Option B (une requête générique avec enum d'opération)** — rejetée, même raison que l'Option C de la section 4.
- **Option C (deux familles `RequestSplit`/`RequestMerge` avec paramètres ciblés ou first-fit optionnels)** — rejetée : mélangerait dans une seule signature des champs mutuellement exclusifs (`TargetX`/`TargetY` pertinents seulement en mode ciblé), le genre d'ambiguïté que `.claude/rules/csharp.md` désigne implicitement en préférant des identifiants et contrats stables à une signature « à options ».

**Précision sur le merge (question distincte, section 6)** : `TryMergeExact`/`TryMergeUntilCapacity` sont déjà, au niveau du Stack Core, deux méthodes distinctes avec des contrats différents (l'une reçoit un `amount`, l'autre non) — le réseau reflète cette distinction déjà actée plutôt que de l'unifier artificiellement. Voir section 9 pour le verdict détaillé sur `requestedAmount == 0` comme signal de mode.

**Le split first-fit appartient-il réellement au Jalon 2 ?** Oui, aux deux variantes ciblée et first-fit — voir section 24 (périmètre UI) : le bouton « Split » du panneau d'actions (first-fit, aucune destination choisie) et le glisser-déposer avec modificateur clavier (ciblé, destination choisie par le drag) sont **deux gestes UI V1 distincts**, déjà recommandés par `INVENTORY_STACK_ARCHITECTURE.md` section 13 — chacun a besoin de sa propre variante réseau.

---

## 6. Signatures conceptuelles retenues

```csharp
// --- Split ---

[Rpc.Host]
public void RequestSplitItem( string requestId, string sourceInstanceId, int requestedAmount,
    int expectedSourceQuantity, int expectedSourceX, int expectedSourceY, bool expectedSourceRotated,
    bool allowRotation );

[Rpc.Host]
public void RequestSplitItemAt( string requestId, string sourceInstanceId, int requestedAmount,
    int expectedSourceQuantity, int expectedSourceX, int expectedSourceY, bool expectedSourceRotated,
    int targetX, int targetY, bool targetRotated );

// --- Merge ---

[Rpc.Host]
public void RequestMergeExact( string requestId, string sourceInstanceId,
    int expectedSourceQuantity, int expectedSourceX, int expectedSourceY, bool expectedSourceRotated,
    string targetInstanceId, int expectedTargetQuantity, int requestedAmount );

[Rpc.Host]
public void RequestMergeUntilCapacity( string requestId, string sourceInstanceId,
    int expectedSourceQuantity, int expectedSourceX, int expectedSourceY, bool expectedSourceRotated,
    string targetInstanceId, int expectedTargetQuantity );

// --- Transactions métier (non-RPC, même patron que TryMoveItemAuthoritative) ---

public PlayerInventoryStackResult TrySplitAuthoritative( Connection requester, string requestId, string sourceInstanceId,
    int requestedAmount, int expectedSourceQuantity, int expectedSourceX, int expectedSourceY, bool expectedSourceRotated,
    bool allowRotation );

public PlayerInventoryStackResult TrySplitAtAuthoritative( Connection requester, string requestId, string sourceInstanceId,
    int requestedAmount, int expectedSourceQuantity, int expectedSourceX, int expectedSourceY, bool expectedSourceRotated,
    int targetX, int targetY, bool targetRotated );

public PlayerInventoryStackResult TryMergeExactAuthoritative( Connection requester, string requestId, string sourceInstanceId,
    int expectedSourceQuantity, int expectedSourceX, int expectedSourceY, bool expectedSourceRotated,
    string targetInstanceId, int expectedTargetQuantity, int requestedAmount );

public PlayerInventoryStackResult TryMergeUntilCapacityAuthoritative( Connection requester, string requestId, string sourceInstanceId,
    int expectedSourceQuantity, int expectedSourceX, int expectedSourceY, bool expectedSourceRotated,
    string targetInstanceId, int expectedTargetQuantity );

// --- Résultat corrélé (même transport que ReceiveMoveResult) ---

[Rpc.Owner]
public void ReceiveStackResult( bool success, string requestId, string sourceInstanceId, string targetInstanceId,
    string newInstanceId, int requestedAmount, int movedAmount, int appliedRevision, PlayerInventoryStackFailureReason failureReason );
```

**Correction (relecture dédiée du 2026-07-24)** : `appliedRevision` ajouté — absent de la première version de ce document, défaut corrigé section 15/17/18 ci-dessous. Toujours une valeur valide, jamais de sentinelle `-1` (section 15).

`sourceInstanceId`/`targetInstanceId`/`newInstanceId` en `string` (convention `Guid.ToString()`), pas en `Guid` — même choix que partout ailleurs dans ce projet (`InventorySnapshotEntry.InstanceId`, `PlayerInventoryMoveResult.InstanceId`), aucun précédent de support natif de `Guid` par les RPC.

---

## 7. Création host-only de la nouvelle `ItemInstance` — flux exact

Reprend section 4 d'`INVENTORY_STACK_ARCHITECTURE.md` (Option B, déjà tranchée) et la rend concrète pour `TrySplitAuthoritative`/`TrySplitAtAuthoritative` :

1. `RequestSplitItem[At]` (RPC) vérifie `GameObject.Network.Owner == Rpc.Caller`, délègue.
2. `TrySplitAuthoritative` résout le pawn (`KodokuPlayerComponent.FindByConnection`), vérifie `pawn.GameObject == GameObject`, `Container is not null`.
3. Parse `sourceInstanceId` (`Guid.TryParse`, échec → `InvalidInstanceId`).
4. Résout le placement source (`Container.GetPlacement`) — absent → `SourceNotFound`.
5. **Précondition de fraîcheur, avant toute allocation** : `placement.Item.Quantity == expectedSourceQuantity` (sinon `StaleSourceQuantity`) **et** `placement.X/Y/IsRotated == expectedSourceX/Y/Rotated` (sinon `StaleSourcePlacement`) — dans cet ordre, quantité avant position (aucune raison de préférer un ordre à l'autre en pratique, mais un ordre fixe et documenté évite une divergence future entre deux implémentations du même chemin).
6. **Validation de bornes avant allocation** : `requestedAmount >= 1 && requestedAmount < placement.Item.Quantity` — dupliquée volontairement ici (le Stack Core la revalidera de toute façon) uniquement pour éviter d'allouer un `Guid`/`ItemInstance` neuf pour une requête déjà structurellement invalide. Une requête qui échouerait de toute façon au Stack Core ne doit pas payer le coût (négligeable, mais non nul) d'un `ItemInstance.CreateNew`.
7. **Seulement maintenant** : `var prepared = ItemInstance.CreateNew(placement.Item.Definition, requestedAmount);` — jamais avant l'étape 6, jamais dans le Stack Core (qui reste inchangé, section 4 d'`INVENTORY_STACK_ARCHITECTURE.md`, « ne crée jamais de nouvelle `ItemInstance` »).
8. Appel à `InventoryStackTransactions.TrySplit(Container, parsedSourceId, requestedAmount, prepared, allowRotation)` (ou `TrySplitAt` avec `targetX/Y/Rotated`).
9. Échec → `prepared` n'a jamais été ajoutée à aucun conteneur (`TrySplit`/`TrySplitAt` ne l'ajoutent qu'en cas de succès complet, section 5 d'`INVENTORY_STACK_ARCHITECTURE.md`) — elle devient simplement éligible au GC, **aucun rollback nécessaire** pour cette instance (rien à défaire, elle n'a jamais existé nulle part ailleurs que sur la pile d'appel).
10. Succès → `NotifyMutated()` (revision++, snapshot) — capturer `HostRevision` **immédiatement après ce retour** dans une variable locale (ex. `int appliedRevision = HostRevision;`), jamais relu une seconde fois plus tard dans la méthode (une opération concurrente traitée entre-temps par le host aurait déjà pu l'incrémenter de nouveau, section 17/18) — puis `ReceiveStackResult` avec `newInstanceId = result.NewInstanceId.Value.ToString()` et cet `appliedRevision`.
11. Échec (à quelque étape que ce soit) → `appliedRevision = HostRevision` courante, lue au moment de construire le résultat d'échec (aucune mutation n'a eu lieu, il n'existe pas de « nouvelle » valeur à capturer — voir section 15/17 pour la règle générale : le champ est toujours renseigné, jamais une sentinelle).

**`prepared.Quantity == requestedAmount` garanti par construction** — `ItemInstance.CreateNew(definition, requestedAmount)` fixe `Quantity` dans son constructeur, aucune étape intermédiaire ne peut la faire diverger avant l'appel au Stack Core (contrairement à un scénario où l'instance serait construite puis mutée — non applicable ici).

**Merge (`TryMergeExactAuthoritative`/`TryMergeUntilCapacityAuthoritative`) ne crée jamais de nouvelle instance** — les étapes 6-7-9 ci-dessus n'existent pas pour ces deux chemins ; ils résolvent en plus la cible (`Container.GetPlacement(targetInstanceId)`, absente → `TargetNotFound`, présente mais `Quantity != expectedTargetQuantity` → `StaleTargetQuantity`) avant d'appeler `TryMergeExact`/`TryMergeUntilCapacity`.

---

## 8. `TargetMismatch` — nécessaire ou non pour l'inventaire joueur ?

`INVENTORY_STACK_ARCHITECTURE.md` section 7 introduit `TargetMismatch` (« la cible existe mais un autre `InstanceId` l'occupe désormais ») comme distinct de `TargetNotFound`. Pour l'inventaire joueur (Jalon 2), **la cible est toujours résolue par `InstanceId` direct** (`Container.GetPlacement(targetInstanceId)`), jamais par une cellule x/y qu'un autre item aurait pu occuper entre-temps — contrairement à un transfert conteneur ciblé par cellule. **`TargetMismatch` n'a donc pas de sens ici** : soit `targetInstanceId` résout à un placement (peu importe ce qu'il y avait avant à cette position), soit il ne résout à rien (`TargetNotFound`). Cette distinction ne redevient pertinente qu'au Jalon 3, si un transfert cross-conteneur ciblé par cellule (pas par identité) est un jour introduit — non retenue ici, évite d'exposer une valeur d'enum qu'aucun chemin de code ne peut jamais produire dans ce jalon.

---

## 9. Merge exact vs merge jusqu'à capacité — exposition réseau

**Verdict : deux RPC distincts (`RequestMergeExact`/`RequestMergeUntilCapacity`), jamais `requestedAmount == 0` comme signal de mode au niveau réseau.**

Le Stack Core utilise déjà `RequestedAmount == 0` en interne comme convention documentée (`StackTransactionResult.RequestedAmount`, section 14 d'`INVENTORY_STACK_ARCHITECTURE.md`) — mais c'est une convention **entre deux couches C# pures qui partagent déjà le même type de résultat**, pas un contrat que le client doit deviner. Au niveau réseau, un client qui enverrait `RequestedAmount = 0` pour signaler « jusqu'à capacité » créerait une ambiguïté qu'aucune autre RPC de ce projet ne tolère (une valeur numérique qui change de sens selon sa valeur, plutôt qu'un nom de méthode qui déclare l'intention). Deux RPC nommées explicitement éliminent cette ambiguïté à la source, au prix de deux méthodes de plus — coût négligeable, cohérent avec la duplication déjà acceptée `TrySplit`/`TrySplitAt` et `TryAdd`/`TryAddFirstFit`.

---

## 10. Requête de split — champs, challengés

### First-fit (`RequestSplitItem`)

| Champ | Obligatoire | Justification |
|---|---|---|
| `RequestId` | Oui | Corrélation UI, généré client, jamais recalculé host (section 12) |
| `SourceInstanceId` | Oui | Identité de la pile à diviser |
| `RequestedAmount` | Oui | Quantité à extraire — jamais déduite côté host : l'UI calcule `source.Quantity / 2` (division entière, formule exacte section 24) et l'envoie explicitement, le host ne fait aucune hypothèse sur l'intention du geste qui a produit ce nombre |
| `ExpectedSourceQuantity` | **Oui** | Voir ci-dessous — challengée puis confirmée obligatoire |
| `ExpectedSourceX/Y/Rotated` | Oui | Déjà obligatoires pour tout accès à une pile existante dans ce projet (`TryMoveItemAuthoritative`) — un split lit et peut faire disparaître ce placement, mêmes garanties requises |
| `AllowRotation` | **Non retenu** — voir ci-dessous |

**`ExpectedSourceQuantity` obligatoire, confirmé** : sans elle, deux splits concurrents sur la même source avec des montants différents pourraient tous les deux réussir contre des quantités totales différentes, produisant un total incohérent (ex. source à 10, deux splits de 4 traités l'un après l'autre sans précondition → 4 puis encore 4 sur une source qui n'avait plus que 6 → dépassement silencieux détecté seulement par l'échec `InvalidAmount` du second, mais sans que le client sache *pourquoi* son montant, valide au moment de l'envoi, ne l'est plus). Avec la précondition, le second reçoit `StaleSourceQuantity`, un message actionnable — cohérent avec le principe déjà établi section 7 d'`INVENTORY_STACK_ARCHITECTURE.md`.

**Préconditions spatiales source, confirmées obligatoires** : un split peut faire disparaître le placement source (transfert total — non, un split ne fait jamais disparaître la source totalement par construction du Stack Core, `amount < Quantity` toujours) mais peut le voir déplacé par une autre opération concurrente entre-temps (un `RequestMoveItem` sur la même instance) — sans précondition spatiale, un split pourrait réussir contre une position que le client ne montre plus à l'écran, produisant une nouvelle pile positionnée en fonction d'un layout obsolète (impact limité pour le first-fit puisque la destination est indépendante de la position source, mais la précondition protège contre la modification concurrente de la source elle-même, pas seulement contre son déplacement).

**Révision globale attendue transmise en plus ?** Non — voir section 13 (verdict dédié, réponse identique pour toutes les opérations de ce jalon).

**`AllowRotation` — non retenu, simplifié.** Le patron déjà établi (`InventoryContainer.TryAddFirstFit(item, allowRotation = true)`) traite la rotation comme un paramètre de confort, jamais transmis explicitement par un appelant réseau existant (`TryUnequipAuthoritative` appelle `TryAddFirstFit(item)` sans le spécifier). Pour la cohérence, `RequestSplitItem` peut omettre ce champ et toujours autoriser la rotation si `Definition.CanRotate` (comportement par défaut du Stack Core) — aucun geste UI recommandé (bouton « Split », section 24) n'a besoin de le refuser explicitement. **Simplification retenue** : supprimer ce champ de la signature ci-dessus par rapport à une première ébauche, `RequestSplitItem` appelle toujours `TrySplit(..., allowRotation: true)`.

### Ciblé (`RequestSplitItemAt`)

Mêmes champs que ci-dessus, `RequestedAmount`/`ExpectedSourceQuantity`/`ExpectedSourceX/Y/Rotated` inchangés, `AllowRotation` remplacé par `TargetX`/`TargetY`/`TargetRotated` (le client choisit la rotation de la pile extraite — cohérent avec `RequestMoveItem`, qui laisse déjà le client choisir `rotated` pour la destination d'un déplacement simple).

**Le client doit-il choisir la rotation de la pile extraite ?** Oui, par cohérence avec `RequestMoveItem` — aucune raison de traiter un split ciblé différemment d'un déplacement ciblé sur ce point précis, la rotation demandée est toujours revalidée par `CanPlace` côté host de toute façon.

---

## 11. Requête de merge — champs, challengés

Communs à `RequestMergeExact`/`RequestMergeUntilCapacity` :

| Champ | Obligatoire | Justification |
|---|---|---|
| `RequestId` | Oui | Idem section 10 |
| `SourceInstanceId` | Oui | |
| `ExpectedSourceQuantity` | Oui | Idem section 10 |
| `ExpectedSourceX/Y/Rotated` | Oui | Idem section 10 |
| `TargetInstanceId` | Oui | Résolution par identité, jamais par cellule (section 8) |
| `ExpectedTargetQuantity` | Oui | Voir ci-dessous |
| `RequestedAmount` | Seulement pour `RequestMergeExact` | `RequestMergeUntilCapacity` ne porte pas ce champ — pas de valeur métier à transmettre pour une opération qui ne reçoit jamais de montant explicite (même raisonnement que le Stack Core, section 6.2 d'`INVENTORY_STACK_ARCHITECTURE.md`) |

**Position/rotation attendue de la cible — Politique minimale retenue, pas la politique stricte.**

- **Politique minimale (retenue)** : `TargetInstanceId`/`ExpectedTargetQuantity` seuls.
- **Politique stricte (rejetée)** : ajouter `ExpectedTargetX/Y/Rotated`.

Une pile cible, contrairement à la source, **n'est jamais lue par sa position par cette opération** — le merge absorbe une quantité dans une identité déjà résolue, il ne mute et ne dépend jamais de la position de la cible (`TryMergeExact`/`TryMergeUntilCapacity` ne prennent même pas x/y en paramètre, section 3). Exiger une précondition spatiale sur une donnée que l'opération n'utilise jamais ajouterait un rejet (`StaleTargetPlacement`) pour un déplacement de la cible qui n'affecte en rien la validité de la fusion — un déplacement de la cible entre la capture cliente et le traitement host ne change ni sa `Quantity`, ni sa capacité restante, ni sa compatibilité. **Risque ABA spatial jugé non pertinent ici** : contrairement à une source (dont la position fait partie de l'identité visuelle que le client croit vraie), la position de la cible n'entre dans aucune décision de ce chemin.

**Risque ABA de quantité (10 → 7 → 10)** : `ExpectedTargetQuantity` ne protège effectivement pas contre ce cas précis (la précondition est satisfaite par coïncidence). **Jugé acceptable pour la V1**, exactement la même tolérance déjà actée section 7 d'`INVENTORY_STACK_ARCHITECTURE.md` pour le cas spatial équivalent (`StaleSource`, limite ABA documentée et tolérée depuis le 2026-07-21) — pas un nouvel abaissement du niveau de sécurité, une extension cohérente d'une tolérance déjà acceptée.

**Ces risques justifient-ils une révision globale ou un jeton par pile ?** Non — voir section 13.

---

## 12. Fraîcheur — enum dédié

**Verdict, cohérent avec la section 2** : un enum dédié, `PlayerInventoryStackFailureReason` — pas de réutilisation de `PlayerInventoryMoveFailureReason` (les deux domaines restent distincts, même si `StaleSource` existe conceptuellement des deux côtés — c'est déjà la norme de ce projet, pas une incohérence à corriger).

**`StaleSourcePlacement`, pas `StaleSourcePosition`/`StaleSourceRotation` séparées** — suit exactement le précédent déjà établi (`PlayerInventoryMoveFailureReason.StaleSource` groupe déjà X/Y/Rotated en une seule raison). Séparer position et rotation n'apporterait aucune information actionnable de plus pour le client (dans les deux cas, la réponse est la même : « votre vue de la source est obsolète, recomposez depuis le snapshot actuel »).

**Raisons spatiales de destination distinguées, pas collapsées** — suit le précédent de `WorldContainerTransferFailureReason` (`OutOfBounds`/`Overlapping`/`RotationNotAllowed` distincts pour la variante ciblée, jamais collapsés sur `InternalError` ni sur un `DestinationInvalid` unique) plutôt que celui du Stack Core lui-même (`StackTransactionFailureReason.DestinationInvalid` + `SpatialFailureReason` imbriqué) — au niveau réseau, un enum plat correspond à ce que les composants réseau existants font déjà pour leurs propres refus de destination ciblée.

```csharp
public enum PlayerInventoryStackFailureReason
{
    None,
    InvalidCaller,
    OwnershipRejected,
    InvalidInstanceId,
    InventoryNotReady,

    SourceNotFound,
    StaleSourceQuantity,
    StaleSourcePlacement,

    TargetNotFound,
    StaleTargetQuantity,

    InvalidAmount,
    SourceNotStackable,
    IncompatibleStacks,
    TargetFull,
    InsufficientTargetCapacity,

    OutOfBounds,
    Overlapping,
    RotationNotAllowed,
    NoAvailableSpace,

    InternalError,
}
```

---

## 13. Révision globale attendue (`ExpectedInventoryRevision`) — verdict

**Comparaison des trois politiques :**

- **A. Préconditions par item uniquement** — **[RECOMMANDÉ], retenue**.
- **B. Révision globale uniquement** — rejetée.
- **C. Préconditions par item + révision globale** — rejetée.

**Verdict : Option A, sans `ExpectedInventoryRevision`.** C'est déjà, implicitement, la politique en vigueur pour `RequestMoveItem` (préconditions par item, `ExpectedSourceX/Y/Rotated`, jamais de révision globale attendue) — étendre le même principe aux préconditions de quantité est une continuité, pas une nouvelle décision. Une révision globale invaliderait une requête de split sur l'item A à cause d'un merge sans rapport sur l'item B traité entre-temps par le host — un rejet dont la seule cause réelle serait « quelque chose d'autre a changé », strictement moins informatif qu'une précondition par item qui ne peut échouer que si **la donnée réellement utilisée par cette opération précise** a changé. `INVENTORY_STACK_ARCHITECTURE.md` ne mentionne d'ailleurs jamais cette option — cette section comble ce point explicitement laissé de côté par le document précédent.

---

## 14. RequestId et idempotence — confirmé, pas de nouvelle décision

**Déjà tranché par `INVENTORY_STACK_ARCHITECTURE.md` section 7 : Option A (préconditions exactes, sans cache host), confirmée pour ce jalon.**

Comportement actuel vérifié dans le code (`InventoryPage.TryRegisterPending`) : `RequestId` est un `Guid.NewGuid().ToString("N")` **généré côté client**, jamais recalculé côté host, utilisé exclusivement pour la corrélation UI (`_pendingByRequestId`/`_pendingRequestIdByInstanceId`) — le host actuel (`RequestMoveItem`/`TryMoveItemAuthoritative`) ne le mémorise dans aucun cache, ne le compare jamais à une requête précédente ; il le reçoit, le fait transiter tel quel jusqu'à `ReceiveMoveResult`. Split/merge suivent exactement ce même chemin, aucune divergence.

- **Option A (retenue)** — une requête dupliquée revalidée après qu'une première application a déjà changé la `Quantity` échoue proprement (`StaleSourceQuantity`/`StaleTargetQuantity`).
- **Option B (cache borné d'idempotence host)** — rejetée pour ce jalon, mêmes termes que la section 7 du document précédent : aucun état de ce type n'existe ailleurs dans le projet, gain marginal sur un risque déjà toléré.
- **Option C (déduplication sans replay, `DuplicateRequest`)** — rejetée : nécessiterait le même état host que l'Option B (mémoriser les `RequestId` déjà vus) pour un bénéfice inférieur (un rejet moins informatif que la fraîcheur déjà produite naturellement par Option A).

**Scénarios de doublon, comportement V1 attendu :**

| Scénario | Comportement |
|---|---|
| Même `RequestId`, même payload (retry réseau) | Premier traité normalement ; second revalidé contre l'état déjà muté par le premier → échec de fraîcheur si une mutation a eu lieu, sinon rejoué à l'identique (si aucune mutation entre les deux, le second réussit aussi — pas un bug, la précondition ne peut pas distinguer un vrai retry d'une seconde requête légitime avec les mêmes valeurs) |
| Même `RequestId`, payload différent | Traité selon son propre payload — le host n'associe jamais un `RequestId` à un payload attendu, seulement reflété dans le résultat |
| `RequestId` différent, même payload | Deux requêtes indépendantes du point de vue host — la première mute, la seconde échoue en fraîcheur si elle arrive après |
| Résultat reçu après un snapshot déjà plus récent | Sans effet indésirable — le host envoie le snapshot avant le résultat dans son flux de traitement, mais le client ne doit jamais supposer que leur application locale suivra ce même ordre (section 18) ; la libération dépend de `LocalRevision >= AppliedRevision`, jamais du snapshot seul |
| Snapshot reçu avant le résultat, ou résultat reçu avant le snapshot | Les deux ordres sont valides et doivent être gérés (section 18) — l'ordre d'envoi ne garantit pas l'ordre d'application côté client |

---

## 15. Résultats réseau — type unique, pas trois

**Verdict : un seul type, `PlayerInventoryStackResult`, réutilisé par les quatre opérations** — pas trois types séparés (`PlayerInventorySplitResult`/`PlayerInventoryMergeResult`/`PlayerInventoryStackFailureReason` comme structure de départ). `StackTransactionResult`, au niveau du Stack Core, prouve déjà qu'une seule forme (avec `TargetInstanceId?`/`NewInstanceId?` nullables) suffit pour split, merge et transfert partiel — le même raisonnement s'applique au niveau réseau, pour la même raison (éviter trois structures quasi identiques, cohérent avec `.claude/rules/csharp.md`).

**Correction (relecture dédiée du 2026-07-24) : `AppliedRevision` ajouté — défaut de la version précédente de ce document.** La version initiale excluait explicitement toute révision du résultat, au motif que le snapshot suffit et précède toujours le résultat (section 18 alors formulée). **Ce raisonnement était incomplet** : il supposait que « le snapshot précède toujours le résultat » suffit à garantir que le client a déjà appliqué ce snapshot au moment où le résultat arrive — or l'ordre d'**envoi** de deux RPC ne garantit pas l'ordre de leur **traitement** complet côté client (deux callbacks distincts, rien n'empêche le second de s'exécuter avant que le premier n'ait fini de mettre à jour `LocalEntries`). Sans un identifiant de révision porté par le résultat lui-même, le pending ne peut pas vérifier que l'état affiché correspond réellement à la mutation dont ce résultat rend compte — il ne peut que supposer que le snapshot est déjà appliqué. `AppliedRevision` corrige ce défaut : le résultat porte désormais la révision exacte qu'il faut avoir atteinte localement pour que la libération du pending soit sûre (section 18).

```csharp
public readonly record struct PlayerInventoryStackResult
{
    public bool Success { get; }
    public string RequestId { get; }
    public string SourceInstanceId { get; }
    public string TargetInstanceId { get; }   // null pour un split
    public string NewInstanceId { get; }      // non-null seulement pour un split réussi
    public int RequestedAmount { get; }
    public int MovedAmount { get; }
    public int AppliedRevision { get; }
    public PlayerInventoryStackFailureReason FailureReason { get; }
}
```

**Sémantique exacte d'`AppliedRevision` :**

| Cas | Valeur |
|---|---|
| Succès (split ou merge) | `HostRevision` immédiatement après le `NotifyMutated()` de cette opération |
| Échec ordinaire, sans mutation | `HostRevision` courante au moment du résultat (inchangée par cette requête, mais toujours une valeur réelle) |
| `InvariantViolation`, rollback réussi | `HostRevision` courante — **aucune révision n'a été émise** pour cette tentative (section 17), la valeur reportée est celle d'avant la tentative |
| `RollbackFailed` | `HostRevision` **après** l'incrément forcé et la publication du snapshot forcé (section 17) — jamais la valeur d'avant tentative |

**Toujours une révision valide, jamais de sentinelle `-1`** — retenu explicitement : une valeur sentinelle obligerait chaque appelant à vérifier « est-ce une vraie révision ou un marqueur d'absence » avant de comparer `LocalRevision >= AppliedRevision`, une branche supplémentaire pour un cas qui n'a pas besoin d'exister (`HostRevision` existe toujours, dès `OnStart`, qu'une mutation ait eu lieu ou non).

**Convention `RequestedAmount`/`MovedAmount` par opération — précisée explicitement, pas seulement pour la requête.** Le champ `RequestedAmount` existe pour les quatre opérations dans ce type de résultat unifié, mais `RequestMergeUntilCapacity` ne reçoit jamais de montant explicite en requête (section 11) — sa valeur dans le *résultat* doit donc être définie sans ambiguïté :

| Opération | `RequestedAmount` | `MovedAmount` en succès | `MovedAmount` en échec |
|---|---:|---:|---:|
| Split first-fit | quantité demandée | quantité déplacée | 0 |
| Split ciblé | quantité demandée | quantité déplacée | 0 |
| Merge exact | quantité demandée | quantité déplacée | 0 |
| Merge until capacity | **toujours 0** | quantité réellement déplacée | 0 |

Pour `RequestMergeUntilCapacity`, `RequestedAmount` vaut **toujours `0` dans `PlayerInventoryStackResult`, succès comme échec** — même convention que `StackTransactionResult.RequestedAmount` au niveau du Stack Core (section 14 d'`INVENTORY_STACK_ARCHITECTURE.md` : « il n'existe pas de valeur métier "demandée" à rapporter »), directement reflétée sans traduction. `MovedAmount` seul porte la quantité réellement déplacée pour cette opération.

**Le réseau ne doit jamais utiliser `RequestedAmount == 0` pour choisir le mode de merge** (rappel de la section 9) — le mode est déjà entièrement déterminé par la RPC explicitement appelée (`RequestMergeExact` ou `RequestMergeUntilCapacity`), jamais déduit d'une valeur de champ.

**Champs explicitement exclus, et pourquoi :**

- **`SourceQuantityAfter`/`TargetQuantityAfter`** — exclus. Aucun résultat existant dans ce projet (`PlayerInventoryMoveResult`, `DropResult`, `EquipmentOperationResult`) ne porte d'état post-mutation ; le snapshot (`InventorySnapshotEntry.Quantity`) reste l'unique source de vérité pour l'état observable, jamais dupliquée dans un résultat.
- **Raison core sous-jacente (`StackTransactionFailureReason`)** — exclue du contrat réseau : `PlayerInventoryStackFailureReason` est déjà une traduction complète (section 12/16), exposer en plus la raison interne dupliquerait l'information sans qu'aucun consommateur réseau existant n'ait jamais eu besoin de la raison « brute » d'une couche interne.

**`AppliedRevision`, seule exception au principe « pas de duplication d'état canonique dans un résultat »** — retenue précisément parce qu'elle ne duplique aucune donnée déjà présente ailleurs (ce n'est pas une quantité, pas un placement, seulement un numéro de séquence servant de condition de synchronisation) et qu'elle corrige un défaut réel de conception (ci-dessus), contrairement à `SourceQuantityAfter`/`TargetQuantityAfter` qui resteraient une pure duplication sans défaut à corriger.

---

## 16. Mapping des erreurs core vers réseau

| `StackTransactionFailureReason` (core) | `PlayerInventoryStackFailureReason` (réseau) |
|---|---|
| `InvalidAmount` | `InvalidAmount` |
| `SourceNotFound` | `SourceNotFound` (ne devrait jamais survenir après la résolution réseau préalable — défense en profondeur) |
| `TargetNotFound` | `TargetNotFound` (idem) |
| `SameSourceAndTarget` | `InvalidAmount` (le client ne peut normalement jamais produire ce cas via l'UI — voir note ci-dessous) |
| `SourceNotStackable` | `SourceNotStackable` |
| `IncompatibleStacks` | `IncompatibleStacks` |
| `InsufficientSourceQuantity` | `InvalidAmount` |
| `TargetFull` | `TargetFull` |
| `InsufficientTargetCapacity` | `InsufficientTargetCapacity` |
| `InvalidPreparedInstance` | `InternalError` (bug de construction côté host, jamais un état atteignable par un client, même s'il envoie des valeurs arbitraires — la précondition amount/fraîcheur déjà validée avant la construction de `prepared` empêche structurellement ce cas) |
| `PreparedInstanceAlreadyPresent` | `InternalError` (même raison — un GUID fraîchement généré par `Guid.NewGuid()` ne peut par construction jamais déjà être présent) |
| `DestinationInvalid` + `SpatialFailureReason` | Mappé vers `OutOfBounds`/`Overlapping`/`RotationNotAllowed`/`NoAvailableSpace` selon la valeur de `SpatialFailureReason` |
| `UnexpectedMutationFailure` | `InternalError` |
| `RollbackFailed` | `InternalError` (voir ci-dessous, verdict dédié) |
| `InvariantViolation` | `InternalError` (voir ci-dessous, verdict dédié) |

**`SameSourceAndTarget` mappé sur `InvalidAmount` plutôt qu'une valeur dédiée** — aucun geste UI recommandé (section 24 pour le périmètre UI) ne peut produire ce cas légitimement (on ne fusionne jamais un item avec lui-même par construction du drag-and-drop, qui exige deux entrées de grille distinctes) ; une valeur réseau dédiée pour un cas qu'aucun client légitime ne peut jamais atteindre ajouterait une branche jamais exercée par un scénario réel — `InvalidAmount` suffit comme fourre-tout pour ce cas frontière.

**`InvariantViolation`/`RollbackFailed` — verdict : tous deux convertis en `FailureReason = InternalError` côté client, jamais exposés tels quels.** Cohérent avec le mapping déjà universel de ce projet (chaque domaine — équipement, drop, usage, déplacement — retombe sur son propre `InternalError` pour tout échec inattendu de sa couche sous-jacente, jamais une raison interne détaillée transmise au client). Le détail complet (`InvariantViolation` vs `RollbackFailed`, quel conteneur, quelle opération) doit être **loggé côté host** (`Log.Error`, section 21) — jamais transmis au client.

**Ces deux cas restent cependant distincts dans leur traitement de révision** (`AppliedRevision`, section 15/17) même si `FailureReason` les confond volontairement : `InvariantViolation` avec rollback réussi ne produit aucune révision (l'état canonique est revenu exactement à son point de départ, rien à publier) ; `RollbackFailed` force au contraire une révision et un snapshot immédiat (l'état canonique n'a pas pu être restauré avec certitude, le client doit converger vers l'état réel). Le client n'a jamais besoin de distinguer les deux à partir de `FailureReason` seul — la convergence se fait uniquement via `AppliedRevision`/le prochain snapshot, jamais par une branche de code différente selon la raison exacte.

---

## 17. Stratégie de révision selon la nature de l'échec — corrigée

**Défaut de la conception initiale, corrigé par cette relecture.** La règle générale déjà établie section 8 d'`INVENTORY_STACK_ARCHITECTURE.md` (« succès ⇒ une révision, échec ⇒ aucune révision ») **reste correcte pour tout échec ordinaire**, mais elle est **insuffisante pour `RollbackFailed`** : ce chemin précis signifie que la tentative de rollback elle-même a échoué après que les deux mutations aient individuellement réussi — l'inventaire canonique peut donc être **réellement modifié**, dans un état que ni le client ni le host ne peuvent plus supposer identique à celui d'avant la requête. Présenter ce cas comme « échec ⇒ aucune révision » masquerait une mutation réelle derrière un résultat qui prétend qu'il ne s'est rien passé.

**Politique finale, en trois cas distincts — pas deux :**

| Cas | Révision | Snapshot | `FailureReason` client | Log host |
|---|---|---|---|---|
| **Échec ordinaire sans mutation** (fraîcheur, capacité, incompatibilité, bornes) | Aucune | Aucun | Raison métier précise (section 12/16) | `Log.Warning` (refus normal) |
| **`InvariantViolation` avec rollback réussi** | Aucune — l'état restauré est, par définition de ce succès de rollback, strictement identique à celui d'avant la tentative | Aucun | `InternalError` | `Log.Error` détaillé (raison core exacte, conteneurs affectés) |
| **`RollbackFailed`** | **Incrémentée** (forcée, un seul appel à `NotifyMutated()`) | **Envoyé immédiatement**, reflétant l'état canonique réellement présent | `InternalError` | **Log critique** (même verbosité que le tag de log `[Drop][CriticalRollbackFailure]` de `PlayerItemDropComponent.TryRollback`) |

**Comparaison des politiques pour `RollbackFailed` spécifiquement :**

- **Option A (incrémenter la révision, publier immédiatement l'état réel)** — **[RECOMMANDÉ], retenue**, sans le refus de nouvelles mutations que proposait une variante plus lourde.
- **Option B (ne rien publier, inventaire considéré bloqué)** — rejetée : introduirait un état « inventaire verrouillé » qui n'existe nulle part ailleurs dans ce projet, sans mécanisme de déverrouillage conçu — et laisserait le client durablement désynchronisé d'un état qui, lui, a réellement changé.
- **Option C (log critique + révision + snapshot forcé + refus des nouvelles mutations)** — le refus des nouvelles mutations est rejeté spécifiquement : nécessiterait un nouvel état durable (« ce `PlayerInventoryComponent` est en mode dégradé ») qu'aucun autre système de ce projet ne porte, pour un chemin jamais observé en pratique (même réserve que Jalon 1, section 14 d'`INVENTORY_STACK_ARCHITECTURE.md`) — `.claude/rules/csharp.md` : pas d'infrastructure pour un besoin non démontré.

**Retenu (Option A) : log critique + incrément de `HostRevision` + publication immédiate du snapshot reflétant l'état réel du conteneur, quel qu'il soit** (même si `TryValidateState` échoue encore à ce moment — le snapshot reste sérialisable placement par placement, il refléterait simplement un état géométriquement incohérent, préférable à un client qui resterait bloqué sur un snapshot obsolète). Aucun verrouillage additionnel.

**Mécanisme conceptuel exact — normatif, pas seulement descriptif.** `RollbackFailed` doit provoquer **exactement une** incrémentation de `HostRevision` et **exactement un** snapshot complet — jamais deux appels de publication cumulés. `NotifyMutated()` (code déjà existant, `PlayerInventoryComponent.cs`) effectue déjà les deux actions en un seul appel :

```text
NotifyMutated()
1. HostRevision++ ;
2. SendFullSnapshotToOwner().
```

Le traitement du chemin `RollbackFailed` doit donc appeler **`NotifyMutated()` une seule fois**, exactement comme n'importe quelle mutation réussie ordinaire — jamais une primitive séparée, jamais un incrément manuel avant ou après cet appel. **Il est interdit de documenter ou d'implémenter** :

```text
HostRevision++;
NotifyMutated();   // NotifyMutated() incrémente déjà — ceci double la révision
```

ni deux appels séparés de publication (un pour l'incrément, un pour l'envoi) — `NotifyMutated()` est déjà l'unique point d'entrée qui combine les deux, pour ce chemin comme pour tout autre.

Ordre conceptuel complet du chemin `RollbackFailed` :

```text
1. le Stack Core retourne RollbackFailed ;
2. log critique côté host (Log.Error, tag [InventoryStack][RollbackFailed] ou équivalent) ;
3. appel unique à NotifyMutated() ;
4. HostRevision est incrémentée exactement une fois (interne à NotifyMutated()) ;
5. le snapshot complet de l'état réel est publié (interne à NotifyMutated()) ;
6. AppliedRevision reçoit la valeur de HostRevision immédiatement après cet appel ;
7. résultat client FailureReason = InternalError.
```

**« Snapshot forcé » signifie précisément** : la publication immédiate et exceptionnelle du snapshot réel par cet unique appel à `NotifyMutated()` — jamais l'introduction d'une seconde méthode d'envoi cumulée avec `NotifyMutated()`, jamais un mécanisme de resynchronisation distinct. Le caractère « forcé » tient uniquement au fait que cette publication a lieu alors qu'aucune mutation de gameplay n'a été validée comme réussie au sens ordinaire (contrairement à un succès normal) — pas à un mécanisme technique différent.

**À documenter explicitement, pas seulement en filigrane :**
- L'état peut n'être **que partiellement restauré** — le snapshot forcé n'est pas une preuve que tout est redevenu cohérent, seulement la meilleure vue disponible de ce qui existe réellement côté host à cet instant.
- Le rôle du snapshot forcé est de **faire converger le propriétaire vers l'état host réel**, jamais de prétendre que l'opération a été proprement annulée — `AppliedRevision` (section 15) reporté par le résultat correspond à cette révision forcée, pas à la révision d'avant tentative.
- Le log critique est le **seul canal de diagnostic** de cet incident — rien de cette information ne doit fuiter vers le client (section 16).
- **`RollbackFailed` ne doit jamais être exposé comme information technique détaillée à l'UI** — le client ne reçoit que `FailureReason = InternalError` et l'`AppliedRevision` forcée, jamais la raison core exacte ni le détail du rollback manqué.

Ce chemin reste, comme au Jalon 1, **non provocable proprement en runtime sans fault injection** — cette politique est une recommandation de robustesse pour un cas qui ne devrait jamais se produire, validée par lecture de code (section 19/23-H), pas par un scénario runtime réel. Si ce chemin est un jour réellement observé en production, cette décision devra être révisée à ce moment — elle n'est pas figée par idéologie, seulement par absence de preuve qu'une infrastructure plus lourde serait justifiée aujourd'hui.

---

## 18. Ordre résultat / révision / snapshot — corrigé

**L'ordre d'envoi est confirmé par le code existant** : `TryMoveItemAuthoritative` appelle déjà `NotifyMutated()` (qui incrémente `HostRevision` **et** envoie `ReceiveSnapshot` via RPC) **avant** que `RequestMoveItem` n'appelle `ReceiveMoveResult`.

```text
1. transaction Stack Core réussie ;
2. NotifyMutated() ;
3. HostRevision++ ;
4. envoi du snapshot propriétaire avec cette révision (ReceiveSnapshot) ;
5. envoi du résultat avec AppliedRevision = HostRevision (ReceiveStackResult).
```

Split/merge suivent cet ordre d'envoi, sans exception — `TryXAuthoritative` appelle `NotifyMutated()` avant de retourner son résultat, `RequestX` appelle `ReceiveStackResult` après avoir reçu ce résultat.

**Défaut de la conception initiale, corrigé : l'ordre d'envoi ne garantit pas l'ordre d'application côté client.** La version précédente de ce document affirmait que « le résultat reçu avant snapshot » ne peut pas se produire, en se fondant uniquement sur l'ordre d'*envoi* des deux RPC. **C'est insuffisant** : deux RPC distinctes arrivent et s'exécutent comme deux callbacks séparés — rien ne garantit que le traitement complet de `ReceiveSnapshot` (remplacement de `LocalEntries`/`LocalRevision`) se termine avant que `ReceiveStackResult` ne s'exécute à son tour, même si le message a été *envoyé* en second. Le client doit donc **réconcilier les deux canaux**, jamais supposer que l'un implique l'autre.

### Règle de pending, corrigée

**En cas d'échec** : libérer immédiatement les items verrouillés au résultat corrélé — inchangé, aucune révision n'entre en jeu (sauf `RollbackFailed`, qui reste un échec du point de vue `FailureReason` mais publie tout de même un `AppliedRevision` forcé, section 17 ; le pending se libère malgré tout immédiatement au résultat, la convergence de révision se fait indépendamment).

**En cas de succès** :
1. mémoriser le résultat (`ResultReceived = true`, `ResultSuccess = true`) et son `AppliedRevision` ;
2. ne libérer les verrous que lorsque `LocalRevision >= AppliedRevision`.

**Conséquence pour chaque ordre d'arrivée réel :**

| Cas | Comportement |
|---|---|
| **Résultat reçu avant snapshot** | Le pending est **conservé** — `ResultReceived`/`AppliedRevision` mémorisés, mais `LocalRevision` (pas encore mis à jour par le snapshot) est encore strictement inférieure à `AppliedRevision` ; libéré dès que le snapshot correspondant arrive et porte `LocalRevision >= AppliedRevision` |
| **Snapshot reçu avant résultat** | `LocalRevision` est déjà suffisante au moment où le résultat arrive — le pending se libère **dès la réception du résultat corrélé** (la condition `LocalRevision >= AppliedRevision` est déjà vraie) |
| **Révision plus récente** (`LocalRevision > AppliedRevision` au moment où le résultat arrive — un snapshot d'une opération *ultérieure*, sans rapport, est déjà arrivé et appliqué avant le résultat de celle-ci) | Le résultat libère le pending normalement — `LocalRevision >= AppliedRevision` reste vrai (`>` implique `>=`), l'état local a déjà atteint ou dépassé la révision correspondant à cette opération, aucune condition supplémentaire nécessaire |
| **Résultat ancien ou orphelin** (`RequestId` ne correspond plus à l'entrée active pour cet `InstanceId`) | Ignoré — ne libère jamais un verrou appartenant à une requête plus récente sur le même item (logique déjà existante de `MarkResultReceived`, inchangée) |

**Le snapshot seul ne doit jamais suffire à confirmer la réussite d'une requête donnée** — il ne porte aucun `RequestId`, un changement de révision peut provenir d'une tout autre opération (y compris d'un autre joueur pour une future extension, ou simplement d'une opération différente du même joueur). **Le résultat seul ne doit jamais suffire à confirmer que l'UI affiche déjà l'état correspondant** — c'est exactement le défaut corrigé ci-dessus, la raison d'être d'`AppliedRevision`.

**Cas particuliers déjà couverts, sans changement de conclusion :**

| Cas | Conséquence |
|---|---|
| Affichage temporairement obsolète (fenêtre entre snapshot et résultat, ou entre résultat et snapshot) | Cosmétique — le pending reste correctement verrouillé tant que la condition `LocalRevision >= AppliedRevision` n'est pas satisfaite, aucun risque de double-envoi |
| Résultat perdu mais snapshot reçu | Non applicable sur le canal RPC fiable de ce projet (aucune perte observée sur toute la campagne M1) — si cela devait se produire, le pending resterait bloqué indéfiniment sur cet item, limite déjà implicitement acceptée par le mécanisme existant (`RequestMoveItem`), pas une régression propre à ce jalon |
| Snapshot perdu mais résultat reçu | Le pending resterait bloqué (condition `LocalRevision >= AppliedRevision` jamais satisfaite) — même remarque, non applicable sur ce transport, pas une garantie nouvelle à construire ici |

---

## 19. Pending UI — structure multi-items, corrigée

**Confirmé, section 13 d'`INVENTORY_STACK_ARCHITECTURE.md` : extension réelle, pas une réutilisation telle quelle.** `TryRegisterPending`/`_pendingRequestIdByInstanceId` ne verrouillent aujourd'hui **qu'un seul** `InstanceId` par `RequestId`, et `PendingOperation` ne porte encore aucune notion de révision — un merge introduit un second identifiant à verrouiller (la cible) et la correction de la section 18 exige de mémoriser `AppliedRevision` par opération.

### Structure conceptuelle retenue

```csharp
sealed class PendingStackOperation
{
    public string RequestId;
    public PendingOperationKind Kind;              // Split, MergeExact, MergeUntilCapacity (nouvelles valeurs)
    public IReadOnlyList<string> LockedInstanceIds; // 1 élément pour un split, 2 pour un merge (source, cible)
    public bool ResultReceived;
    public bool ResultSuccess;
    public int AppliedRevision;                     // valide seulement si ResultReceived == true
}
```

`LockedInstanceIds` :
- **split** : `SourceInstanceId` uniquement.
- **merge** : `SourceInstanceId` et `TargetInstanceId`.

**Distinction préservée avec les pending existants** : `PendingStackOperation` (ce jalon) coexiste avec les structures déjà en place pour Move et Store/Take (`InventoryPage.PendingOperation`, section 13 déjà existante du code) — **il ne s'agit pas d'une refonte générale du mécanisme pending**, seulement d'une extension du même `PendingOperationKind`/des mêmes deux dictionnaires (`_pendingByRequestId`/`_pendingRequestIdByInstanceId`) avec des valeurs et une charge utile supplémentaires pour les nouvelles opérations de pile. Move et Store/Take restent verrouillés sur un seul `InstanceId` chacun, inchangés.

### Enregistrement atomique — `TryRegisterPending`, étendu

```csharp
bool TryRegisterPending( IReadOnlyList<string> instanceIds, PendingOperationKind kind, out string requestId )
```

Ordre de validation, **tout ou rien** (aucun verrou partiel jamais posé) :
1. reçoit la liste `instanceIds` (1 pour split, 2 pour merge) ;
2. rejette (retourne `false`, n'enregistre rien) si un identifiant de la liste est vide/`null` ;
3. rejette si un identifiant de la liste est **déjà verrouillé** (présent dans `_pendingRequestIdByInstanceId`) — pour un merge, testé pour la source **et** la cible avant tout enregistrement ;
4. rejette si la liste contient un **doublon** (cas dégénéré, ne devrait jamais se produire côté UI — une source ne peut pas être sa propre cible par construction du drag, mais la garde reste explicite plutôt qu'implicite) ;
5. si une seule de ces validations échoue, **rien n'est ajouté** — pas de verrou posé sur la source si la cible est déjà prise, jamais l'inverse non plus ;
6. génère un seul `requestId` (`Guid.NewGuid().ToString("N")`, inchangé) ;
7. enregistre ce **même** `requestId` pour chaque `InstanceId` de la liste dans `_pendingRequestIdByInstanceId`, et une nouvelle entrée `PendingStackOperation` dans `_pendingByRequestId`.

### Libération atomique — `ReleasePending`, étendu

- retire l'opération de `_pendingByRequestId` par `RequestId` ;
- retire **chaque** `InstanceId` de `LockedInstanceIds` de `_pendingRequestIdByInstanceId`, mais **seulement** si sa valeur active y référence encore ce `requestId` précis (logique déjà existante, appliquée à plusieurs identifiants plutôt qu'à un seul) ;
- ne libère **jamais** un verrou appartenant à une requête plus récente sur le même `InstanceId` (une opération plus récente a pu remplacer l'entrée active entre-temps — même garde que l'existant, section 4 du code actuel).

**`IsItemOperationPending(instanceId)` reste inchangée** — teste un seul `InstanceId` à la fois, déjà correct pour verrouiller indépendamment source et cible (une requête sur la cible seule, par exemple, doit échouer même si seule la cible fait partie de `LockedInstanceIds` d'une opération active).

### Cas analysés

| Cas | Comportement |
|---|---|
| Deux requêtes différentes ciblent le même item | La seconde ne peut pas s'enregistrer (`TryRegisterPending` rejette dès qu'un seul identifiant de sa liste est déjà pris) — même garde que l'existant, étendue |
| Une opération locale tente de cibler la source déjà verrouillée par un merge en cours | Refusée localement (`IsItemOperationPending(sourceId)` vrai) |
| Une opération locale tente de cibler la cible déjà verrouillée par un merge en cours | Refusée localement (`IsItemOperationPending(targetId)` vrai) — même verrou, appliqué à la cible comme à la source |
| Échec d'un merge | Les deux verrous (source et cible) libérés immédiatement au résultat — section 18 |
| Succès d'un merge | Les deux verrous libérés ensemble, seulement lorsque `LocalRevision >= AppliedRevision` — jamais l'un avant l'autre (un seul `ReleasePending` retire les deux entrées de `LockedInstanceIds` en une fois) |
| Enregistrement partiel tenté (cible déjà verrouillée) | Impossible par construction (étape 3 ci-dessus) — aucun verrou n'est jamais posé sur la source seule si la cible est indisponible |
| Résultat reçu après un snapshot plus récent | Sans effet sur la libération — voir section 18 |
| Requête abandonnée (drag annulé avant relâchement) | Aucun `RequestId` n'a jamais été généré (`TryRegisterPending` n'est appelé qu'à `ReleaseDrag`, jamais pendant le drag lui-même) — rien à libérer |
| Fermeture du menu | `InventoryPage` est détruite, tout son état pending local disparaît avec elle — un résultat tardif pour un `RequestId` qui n'existe plus dans une nouvelle instance de page est silencieusement ignoré (`MarkResultReceived` vérifie déjà `_pendingByRequestId.TryGetValue`) |
| Déconnexion | Même conséquence que la fermeture du menu, côté client — côté host, `Container` reste en mémoire tant que le `GameObject` existe (`NetworkOrphaned = Destroy`, comportement déjà confirmé pour ce projet) |
| Changement de propriétaire | Non applicable à `PlayerInventoryComponent` en pratique (le pawn change rarement de propriétaire après spawn) — si cela devait se produire, `StopControl` vide déjà `LocalEntries`/`LocalEquipment`, un futur pending resterait orphelin mais sans danger (même filet que « fermeture du menu ») |

---

## 20. Sécurité et autorité

Aucune nouveauté par rapport au patron déjà établi — appliqué à l'identique :

- **Filtre RPC** : `GameObject.Network.Owner == Rpc.Caller`, avant tout traitement, dans chaque `RequestX`.
- **Défense en profondeur** : `TryXAuthoritative` revalide `pawn.GameObject == GameObject` après résolution par `KodokuPlayerComponent.FindByConnection` — protège un futur appel direct (test déterministe) qui contournerait le filtre RPC.
- **Requête d'un non-propriétaire** : rejetée silencieusement au niveau RPC (`Log.Warning`, aucun `ReceiveStackResult` envoyé) — même comportement que les quatre domaines existants, jamais un résultat renvoyé à un appelant qui n'a pas le droit de savoir qu'un pawn qui n'est pas le sien existe.
- **Requête avant initialisation complète** (`Container is null`, cas théorique — `Container` est assigné dans `OnStart` avant tout `RequestSnapshot`) : `InventoryNotReady`, valeur déjà réservée dans l'enum section 12, jamais atteinte en pratique par le flux normal mais couvrant le même style de garde défensive que `Inventory is null || Inventory.Container is null` déjà présent dans `PlayerItemDropComponent.TryDropAuthoritative`.
- **Ne jamais faire confiance à autre chose que `InstanceId`/montants/positions comme préconditions à revalider** — aucune donnée cliente (définition, quantité réelle, contenu de la cellule) n'est jamais acceptée comme vraie sans revalidation contre `Container`.

---

## 21. Logs et diagnostics

Même verbosité et même format que les quatre domaines existants (`[Domaine][Étape]` préfixe, ex. `[InventoryMove][Fail]`) :

- Succès : `[InventoryStack][Split][Success]`/`[InventoryStack][Merge][Success]` — `caller`, `sourceInstanceId`, `targetInstanceId` (si applicable), `requestedAmount`, `movedAmount`, `newInstanceId` (si applicable).
- Échec métier normal (fraîcheur, capacité, incompatibilité) : `[InventoryStack][Split|Merge][Fail]` avec `FailureReason` — `Log.Warning`, même niveau que les refus `StaleSource` existants (un refus de gameplay normal, pas une anomalie).
- `InternalError` provenant d'`InvariantViolation`/`RollbackFailed` : `Log.Error`, avec le détail complet non exposé au client (raison core exacte, `sourceInstanceId`, `targetInstanceId`, montant, état `TryValidateState` avant/après tentative de rollback) — seul canal où cette information existe.
- **Éviter les logs PASS verbeux en production** — même politique déjà appliquée (les outils de debug temporaires de la campagne M1 ont tous été retirés après validation ; aucun log de succès trivial n'est laissé actif en permanence au-delà de ce que les quatre domaines existants font déjà).
- Aucune trace UI (nom d'affichage, icône) ne doit jamais apparaître dans un log host — seul `ItemId`/`InstanceId`, cohérent avec l'existant.

---

## 22. Architecture comparée — synthèse et verdict final

| Critère | Archi 1 (RPC directs dans `PlayerInventoryComponent`) | Archi 2 (types/handler dédiés) | Archi 3 (RPC générique) |
|---|---|---|---|
| Lisibilité | Haute — même patron que 4 domaines existants | Moyenne — un fichier de plus à ouvrir pour suivre le flux | Basse — un seul point d'entrée cache la diversité réelle des opérations |
| Risque de payload invalide | Faible — signatures dédiées | Faible | Élevé — champs non pertinents selon le mode |
| Taille du composant | `PlayerInventoryComponent` grossit (déjà 613 lignes) mais reste dans le même ordre de grandeur que l'ajout de l'équipement (section 5 d'`INVENTORY_STACK_ARCHITECTURE.md` a déjà écarté cette préoccupation pour ce fichier précis, contrairement à `WorldContainerComponent`) | Composant plus petit, mais complexité déplacée, pas réduite | Composant plus petit mais méthode `RequestStackTransaction` elle-même complexe (dispatch interne par enum) |
| Testabilité | Haute — `TryXAuthoritative` déjà directement appelable, patron déjà validé quatre fois | Haute en théorie, aucun gain démontré en pratique | Basse — un seul point d'entrée à tester avec toutes les combinaisons de mode |
| Évolution vers Jalon 3 | `WorldContainerComponent` réutilisera le même patron indépendamment (déjà son propre existant pour Take/Store) — aucun couplage entre les deux composants réseau | Un handler par composant reproduirait la duplication qu'il prétendait éviter | Généraliser encore le générique sur deux composants aggraverait le problème |
| Duplication | Aucune au-delà de ce que le patron existant accepte déjà (quatre fois le même squelette RPC→Try→Result) | Réduite en théorie, déplacée en pratique | Réduite en façade, complexité interne du dispatch en plus |
| Couplage réseau/core | Faible — chaque `TryXAuthoritative` appelle directement `InventoryStackTransactions`, aucune indirection | Identique, un niveau d'indirection en plus sans bénéfice | Identique |

**Verdict final : Architecture 1, sans réserve.** Aucun des deux facteurs qui justifieraient un handler dédié ou un RPC générique ailleurs dans ce projet (fichier déjà démesuré, second cas d'usage concret pour l'abstraction) ne s'applique à `PlayerInventoryComponent` pour ce jalon.

---

## 23. Matrice de tests

### A. Host local (solo, host uniquement — mêmes scénarios que P-01/P-03 mais sans second client)

| # | Scénario | Attendu |
|---|---|---|
| N2-A1 | Split first-fit valide | Une révision, deux placements, `NewInstanceId` distinct |
| N2-A2 | Split ciblé valide | Idem, position exacte respectée |
| N2-A3 | Merge exact partiel | Cible augmentée exactement de `RequestedAmount`, source décrémentée |
| N2-A4 | Merge exact total | Placement source disparaît, cible absorbe tout |
| N2-A5 | Merge until capacity partiel | `MovedAmount < source.Quantity`, reste en place côté source |
| N2-A6 | Merge until capacity total | Source disparaît, cible pleine ou source épuisée |
| N2-A7 | Quantité invalide (`RequestedAmount <= 0` ou `>= Quantity`) | `InvalidAmount`, aucune mutation |
| N2-A8 | Source absente | `SourceNotFound` |
| N2-A9 | Cible absente (merge) | `TargetNotFound` |
| N2-A10 | Source non empilable (`MaxStack == 1`) | `SourceNotStackable` |
| N2-A11 | Piles incompatibles (`ItemId` différent) | `IncompatibleStacks` |
| N2-A12 | Cible pleine | `TargetFull` |
| N2-A13 | Destination invalide (split ciblé sur cellule occupée) | `Overlapping`, aucune mutation |

### B. Joiner propriétaire (host + client)

Même matrice minimale que A, exécutée depuis un client distant, avec vérification supplémentaire :

| # | Vérification |
|---|---|
| N2-B1 | Mutation effectuée par le host (jamais côté client) |
| N2-B2 | Snapshot reçu uniquement par le propriétaire (jamais un autre client) |
| N2-B3 | Résultat corrélé par `RequestId` (`ReceiveStackResult`), reçu après le snapshot |
| N2-B4 | Pending libéré exactement à la réception du résultat, jamais avant |
| N2-B5 | Host et joiner convergents (mêmes quantités, mêmes `InstanceId`) après résolution |

### C. Autorité

| # | Scénario | Attendu |
|---|---|---|
| N2-C1 | Requête d'un non-propriétaire (cible le pawn d'un autre joueur) | Rejet silencieux au niveau RPC, aucun `ReceiveStackResult` |
| N2-C2 | Requête après perte de propriété (cas rare, voir section 19) | `Container` toujours résolu pour le nouveau propriétaire, ancien état pending orphelin sans danger |
| N2-C3 | Inventaire non prêt (`Container is null`, cas théorique) | `InventoryNotReady` |
| N2-C4 | Payload invalide (`InstanceId` non-GUID, chaîne vide) | `InvalidInstanceId` |

### D. Fraîcheur

| # | Scénario | Attendu |
|---|---|---|
| N2-D1 | `ExpectedSourceQuantity` obsolète | `StaleSourceQuantity`, aucune mutation |
| N2-D2 | Source déplacée entre-temps (`ExpectedSourceX/Y/Rotated` obsolète) | `StaleSourcePlacement` |
| N2-D3 | `ExpectedTargetQuantity` obsolète | `StaleTargetQuantity` |

### E. Concurrence (host + deux clients ou deux requêtes préparées/envoyées, méthodologie M1-N/M1-O)

Reprend directement P-09/P-10/P-11 d'`INVENTORY_STACK_ARCHITECTURE.md`, adaptés à `PlayerInventoryComponent` :

| # | Scénario | Attendu |
|---|---|---|
| N2-E1 (≡ P-09) | Deux splits sur la même source (10 → demandes de 4 et 3, même `ExpectedSourceQuantity = 10`) | Une seule réussit, l'autre `StaleSourceQuantity`, aucune duplication |
| N2-E2 (≡ P-10) | Deux merges vers la même cible (4/10, sources 6 et 3, même `ExpectedTargetQuantity = 4`) | Une seule réussit, l'autre `StaleTargetQuantity`, jamais de dépassement de `MaxStack` |
| N2-E3 | Deux merges identiques (même source, même cible, requêtes distinctes) | Le premier traité réussit, le second `StaleSourceQuantity` ou `StaleTargetQuantity` selon l'ordre exact de mutation |
| N2-E4 | Deux opérations indépendantes (sources et cibles distinctes) | Les deux réussissent, aucune interférence (confirme l'absence de révision globale bloquante, section 13) |
| N2-E5 | Déplacement concurrent de la source (un `RequestMoveItem` entre la capture et le traitement du split) | `StaleSourcePlacement` |
| N2-E6 | Déplacement concurrent de la cible (un `RequestMoveItem` sur la cible avant un merge) | Sans effet — la position de la cible n'est jamais une précondition (section 11) ; le merge réussit normalement si `ExpectedTargetQuantity` reste valide |

### F. `RequestId`

| # | Scénario | Attendu |
|---|---|---|
| N2-F1 | Même `RequestId`, même payload (retry) | Premier traité, second en fraîcheur si une mutation a eu lieu entre les deux (section 14) |
| N2-F2 | Même `RequestId`, payload différent | Chacun traité selon son propre payload |
| N2-F3 | `RequestId` différent, même payload | Indépendants, premier traité mute, second en fraîcheur si mutation entre-temps |
| N2-F4 | Résultat reçu après un snapshot plus récent | Libération du pending correcte, sans double-libération (section 19) |
| N2-F5 | Résultat provenant d'une ancienne instance d'`InventoryPage` — une page détruite avait démarré une opération sous `RequestId A` ; la page est détruite puis recréée ; une nouvelle opération sur le même `InstanceId` démarre sous `RequestId B` depuis la nouvelle page ; le résultat retardé de `A` arrive ensuite | Aucune entrée pending de la nouvelle page ne correspond à `RequestId A` ; le résultat `A` est ignoré ; aucun verrou appartenant à `B` n'est libéré ; l'opération sous `B` n'est aucunement affectée |

### G. Révisions et snapshots

| # | Scénario | Attendu |
|---|---|---|
| N2-G1 | Une révision sur tout succès (split ou merge) | `HostRevision` incrémenté exactement une fois |
| N2-G2 | Aucune révision sur tout échec | `HostRevision` inchangé |
| N2-G3 | Aucun snapshot intermédiaire pendant une transaction | Un seul `ReceiveSnapshot` par opération réussie |
| N2-G4 | Nouveau `InstanceId` visible dans le snapshot après un split | `LocalEntries` contient une entrée avec `NewInstanceId` |
| N2-G5 | Source disparue du snapshot après un merge total | `LocalEntries` ne contient plus l'ancien `SourceInstanceId` |
| N2-G6 | Source conservée après un merge partiel | `LocalEntries` contient toujours `SourceInstanceId`, `Quantity` réduite |

### H. Erreurs internes (validées par inspection, pas déclarées runtime PASS — non provocables sans fault injection)

| # | Scénario | Statut de validation |
|---|---|---|
| N2-H1 | `InvariantViolation` avec rollback restauré | Inspection de code uniquement (même réserve que Jalon 1) |
| N2-H2 | `RollbackFailed` | Inspection de code uniquement — stratégie section 17 validée par lecture, jamais provoquée en runtime |
| N2-H3 | Stratégie de resynchronisation forcée après `RollbackFailed` | Inspection de code uniquement |
| N2-H4 | `RollbackFailed` — vérification complète de la politique section 17 : résultat client `InternalError` ; log critique host ; un seul appel à `NotifyMutated()` ; `HostRevision` incrémentée exactement une fois (forcée) ; snapshot complet de l'état réel publié ; `AppliedRevision` du résultat correspond exactement à cette révision forcée (pas à la révision d'avant tentative) | Inspection de code uniquement — non provoqué en runtime sans fault injection, jamais déclaré PASS runtime |

### I. Révisions et pending (corrigé — `AppliedRevision`)

| # | Scénario | Attendu |
|---|---|---|
| N2-R01 | Succès split | `HostRevision +1` ; snapshot porte la nouvelle révision ; résultat porte `AppliedRevision` égal à cette révision |
| N2-R02 | Succès merge | Même comportement que N2-R01 |
| N2-R03 | Résultat reçu avant snapshot | Verrous (source/cible) conservés ; libérés lorsque `LocalRevision` atteint `AppliedRevision` |
| N2-R04 | Snapshot reçu avant résultat | Pending conservé jusqu'à réception du résultat corrélé ; libéré immédiatement à cette réception (`LocalRevision` déjà suffisante) |
| N2-R05 | Échec (ordinaire ou `RollbackFailed`) | Libération immédiate au résultat, dans les deux cas — aucune attente de révision pour un échec |
| N2-R06 | Résultat ancien/orphelin reçu après qu'une opération plus récente a pris le même `InstanceId` | Ne libère aucun verrou plus récent (`MarkResultReceived` vérifie déjà que le `RequestId` correspond à l'entrée active) |

### J. Pending multi-items (merge — source et cible)

| # | Scénario | Attendu |
|---|---|---|
| N2-P01 | Merge démarré | Verrouille `SourceInstanceId` **et** `TargetInstanceId` sous le même `RequestId` |
| N2-P02 | Une opération locale distincte tente de cibler la source déjà verrouillée | Refusée localement (`IsItemOperationPending(sourceId)` vrai) |
| N2-P03 | Une opération locale distincte tente de cibler la cible déjà verrouillée | Refusée localement (`IsItemOperationPending(targetId)` vrai) |
| N2-P04 | Échec du merge | Les deux verrous (source et cible) libérés immédiatement |
| N2-P05 | Succès du merge | Les deux verrous libérés ensemble, seulement après `LocalRevision >= AppliedRevision` |
| N2-P06 | Enregistrement partiel tenté — cible déjà verrouillée par une autre opération | Aucun verrou ajouté, y compris sur la source (`TryRegisterPending` échoue avant tout ajout) |

### K. Split UI — quantité exacte

| # | Scénario | Attendu |
|---|---|---|
| N2-U01 | `Quantity = 2` | `RequestedAmount = 1`, source après split = 1 |
| N2-U02 | `Quantity = 3` | `RequestedAmount = 1`, source après split = 2, nouvelle pile = 1 |
| N2-U03 | `Quantity = 5` | `RequestedAmount = 2`, source après split = 3, nouvelle pile = 2 |
| N2-U04 | `Quantity = 1` | Action indisponible — aucune requête envoyée (garde locale, pas un aller-retour réseau qui échouerait) |
| N2-U05 | Bouton Split et split avec modificateur clavier, même `Quantity` de départ | Les deux gestes produisent exactement le même `RequestedAmount` |

**Décompte total de la matrice de ce jalon** : 13 (A) + 5 (B) + 4 (C) + 3 (D) + 6 (E) + 5 (F, `N2-F5` ajouté) + 6 (G) + 4 (H, incluant `N2-H4`) + 6 (I) + 6 (J) + 5 (K) = **63 scénarios documentés** — tous non exécutés à ce stade (conception uniquement), la portion H (incluant `N2-H4`) restant explicitement validée par inspection de code, jamais runtime PASS, conformément à la contrainte de cette mission.

---

## 24. Périmètre UI du Jalon 2

**Réseau complet + UI minimale, pas de sélecteur de quantité** — confirme et rend concret le verdict déjà posé section 13 d'`INVENTORY_STACK_ARCHITECTURE.md`.

### Règle exacte de la quantité de split — corrigée, formule sans ambiguïté

**Défaut corrigé** : la formulation initiale (« moitié arrondie vers le bas ») laissait place à interprétation. Règle exacte, retenue sans exception :

```text
splitAmount = source.Quantity / 2   (division entière)
```

- La **nouvelle pile** (extraite) reçoit `splitAmount`.
- La **source** conserve `source.Quantity - splitAmount` (toujours ≥ `splitAmount`, jamais l'inverse — la moitié inférieure part, la moitié supérieure ou égale reste).
- **Action indisponible si `Quantity < 2`** (`splitAmount` vaudrait alors `0`, une valeur déjà rejetée par `RequestedAmount` obligatoire ≥ 1 — le bouton/le modificateur ne doivent donc même pas déclencher de requête réseau dans ce cas, garde purement locale).

| `Quantity` initiale | `splitAmount` (nouvelle pile) | Source après split |
|---|---|---|
| 2 | 1 | 1 |
| 3 | 1 | 2 |
| 5 | 2 | 3 |
| 10 | 5 | 5 |

**Le bouton Split et le split avec modificateur clavier utilisent exactement la même formule** — aucune divergence entre les deux gestes UI, `RequestedAmount` calculé une seule fois selon cette règle avant d'appeler `RequestSplitItem`/`RequestSplitItemAt` selon le geste.

### Rotation du split first-fit — confirmée sans ambiguïté

Pour `RequestSplitItem` (sans destination ciblée) : appelle `InventoryStackTransactions.TrySplit(..., allowRotation: true)` — le Stack Core choisit le premier placement valide selon sa logique first-fit déjà existante (orientation normale d'abord, tournée ensuite si nécessaire et autorisée) ; aucune rotation spécifique n'est imposée par le client, cohérent avec la section 10 (`AllowRotation` non exposé dans le RPC).

Pour `RequestSplitItemAt` (ciblé) : `TargetRotated` est choisi par le client (comme pour un déplacement ciblé), le host revalide la rotation et la destination via `InventoryStackTransactions.TrySplitAt`/`InventoryContainer.CanPlace` — aucune confiance accordée à cette valeur avant revalidation complète.

**Le client ne choisit jamais la nouvelle `InstanceId`** — dans les deux cas, elle est générée côté host par `ItemInstance.CreateNew` (section 7), jamais transmise par le client, jamais dérivée d'une valeur cliente.

### Gestes UI retenus

- **Bouton « Split » dans le panneau d'actions** (`InventoryPage.razor`, à côté de Drop/Equip/Use) — appelle `RequestSplitItem` avec `RequestedAmount = source.Quantity / 2` (formule ci-dessus), first-fit, aucune position ciblée. Bouton grisé/absent si `Quantity < 2`.
- **Glisser-déposer avec modificateur clavier** — une nouvelle action nommée (même patron que `InventoryRotate`, jamais une touche codée en dur), pressée au début d'un drag pour convertir un déplacement normal en split ciblé : `RequestedAmount = source.Quantity / 2` (même formule), destination = cellule de relâchement du drag (comme un déplacement normal) → `RequestSplitItemAt`. Drag non déclenché (ou annulé) si `Quantity < 2`.
- **Glisser une pile entière sur une pile compatible** — bascule automatique vers `RequestMergeUntilCapacity` au lieu de `RequestMoveItem`, décidée à `ReleaseDrag` : si la cellule cible contient déjà un `InstanceId` différent et compatible (`CanStackWith` côté validation locale indicative), envoyer un merge plutôt qu'un déplacement. **Détail d'implémentation à trancher au moment du code, pas ici** : `IsPlayerPlacementValid` devra être étendu pour reconnaître ce cas comme valide (aujourd'hui, une cellule occupée est toujours un rejet de chevauchement).
- **Aucun sélecteur de quantité (fenêtre/slider, champ numérique)** — explicitement hors périmètre, cohérent avec la recommandation déjà actée.
- **`RequestMergeExact` devra être implémentée et couverte par la matrice de tests au même titre que les trois autres RPC — aucune interaction UI dédiée n'est obligatoire dans ce jalon.** L'API réseau et son traitement authoritative devront être disponibles et testés (matrice section 23-A/B), tandis que l'UI principale (glisser-déposer) utilisera exclusivement `RequestMergeUntilCapacity` pour le drag sur une pile compatible — aucun geste V1 n'appelle `RequestMergeExact`, elle reste programmatiquement disponible pour les tests et un futur jalon UI si un geste à quantité explicite est un jour retenu. Pas de bouton « Merge » explicite dans ce jalon.
- **Hors périmètre également** : refonte visuelle générale de la grille, animations complexes, multi-sélection.

---

## 25. Sujets différés (hors périmètre explicite du Jalon 2)

- Transferts partiels joueur ↔ conteneur du monde (`WorldContainerComponent`) — Jalon 3.
- Drop partiel — Jalon 4.
- Auto-merge au pickup — Jalon 5.
- Sélecteur de quantité UI — jalon UI dédié ultérieur, non planifié.
- Cache d'idempotence host (`RequestId`) — reporté indéfiniment, à réévaluer seulement si un incident réel de duplication est observé (section 14).
- `TargetMismatch` — non retenu pour ce jalon (section 8), pourrait redevenir pertinent au Jalon 3.
- Poids maximal bloquant — étape 8 de la roadmap générale, distincte de ce jalon.
- Durabilité par instance et son impact sur `CanStackWith` — hors périmètre tant qu'aucun cas d'usage réel n'existe.

---

## 26. Décisions — récapitulatif explicite

1. **Nombre et forme des RPC** : quatre RPC nommées (`RequestSplitItem`, `RequestSplitItemAt`, `RequestMergeExact`, `RequestMergeUntilCapacity`), directement sur `PlayerInventoryComponent`.
2. **Signatures conceptuelles** : section 6.
3. **Création host-only des `ItemInstance`** : après validation complète (fraîcheur + bornes), jamais avant — section 7.
4. **Préconditions source** : `ExpectedSourceQuantity` + `ExpectedSourceX/Y/Rotated`, toutes deux obligatoires pour toute opération — section 10.
5. **Préconditions cible** : `TargetInstanceId` + `ExpectedTargetQuantity` seuls (politique minimale, pas de précondition spatiale) — section 11.
6. **`ExpectedInventoryRevision`** : absente, préconditions par item uniquement — section 13.
7. **Rôle du `RequestId`** : corrélation UI exclusivement, jamais une clé d'idempotence host — section 14.
8. **Stratégie d'idempotence** : Option A (préconditions exactes, sans cache) — section 14.
9. **Types de résultats réseau** : un seul type unifié, `PlayerInventoryStackResult`, avec `AppliedRevision` ; `RequestedAmount` toujours `0` pour `RequestMergeUntilCapacity`, succès comme échec — section 15 (corrigé).
10. **Mapping des raisons d'échec** : table section 16 ; `InvariantViolation`/`RollbackFailed` convertis en `InternalError`, distingués uniquement par leur traitement de révision (section 17).
11. **Révision sur succès** : une seule, après mutation complète ; échec ordinaire et `InvariantViolation` avec rollback réussi : aucune révision ; `RollbackFailed` : révision forcée — trois cas distincts, pas deux — section 17 (corrigé).
12. **Stratégie `RollbackFailed`** : log critique + un seul appel à `NotifyMutated()` (jamais un incrément manuel séparé, jamais deux publications) + `AppliedRevision` correspondant à cette révision forcée, sans verrouillage additionnel, jamais présenté comme une annulation propre — section 17 (corrigé).
13. **Ordre résultat/snapshot** : mutation → révision → snapshot → résultat en envoi, confirmé par le code existant — mais l'ordre d'envoi ne garantit pas l'ordre d'application côté client, le client réconcilie via `AppliedRevision` — section 18 (corrigé).
14. **Règle de déverrouillage pending** : échec → immédiat au résultat ; succès → seulement lorsque `LocalRevision >= AppliedRevision`, jamais au résultat seul — section 18 (corrigé).
15. **Structure pending multi-items** : `PendingStackOperation` (`LockedInstanceIds`, `AppliedRevision`), enregistrement/libération atomiques sur 1 (split) ou 2 (merge) identifiants — section 19 (corrigé).
16. **Formule exacte du split** : `splitAmount = source.Quantity / 2` (division entière), identique pour le bouton et le modificateur clavier, action indisponible si `Quantity < 2` — section 24 (corrigé).
17. **Rotation du split first-fit** : `allowRotation: true` toujours, aucune rotation imposée par le client ; split ciblé : rotation choisie par le client, revalidée par le host — section 24.
18. **Comportement drag-to-merge** : bascule automatique vers `RequestMergeUntilCapacity` si la cellule cible contient une pile compatible — section 24.
19. **Rôle de `RequestMergeExact` dans l'UI** : devra être implémentée et couverte par la matrice de tests comme les trois autres RPC, aucune interaction UI dédiée obligatoire dans ce jalon — section 24.
20. **Périmètre UI du Jalon 2** : réseau complet + bouton Split + drag modificateur + drag-to-merge, jamais de sélecteur de quantité — section 24.
21. **Matrice de tests** : section 23 (A à K, 63 scénarios documentés — corrigé, sections I/J/K ajoutées, `N2-F5` ajouté, identifiant du scénario `RollbackFailed` aligné sur la série H sous le nom `N2-H4`).

---

## 27. Questions réellement encore ouvertes

Mise à jour par la relecture dédiée du 2026-07-24 : les questions désormais tranchées (stratégie `RollbackFailed`, `AppliedRevision`, condition de libération du pending, formule exacte de la quantité de split, rotation du split first-fit) sont retirées de cette liste — voir sections 15/17/18/19/24. Restent réellement ouvertes :

- **[OUVERT]** Nom exact de l'action clavier pour le split modificateur (ex. `InventorySplit`, cohérent avec `InventoryRotate` déjà existant) — détail d'`Input.config`, à trancher à l'implémentation.
- **[OUVERT]** Détail exact de l'extension d'`IsPlayerPlacementValid`/`IsContainerPlacementValid` pour reconnaître une cible compatible comme un aperçu « valide » distinct d'un déplacement — mécanique de rendu (couleur d'aperçu différente pour un merge vs un déplacement ?) non conçue ici, question UI pure.
- **[OUVERT]** Comportement exact si `CanRotate` change la géométrie d'un split ciblé en cours de drag — même question déjà notée ouverte section 16 d'`INVENTORY_STACK_ARCHITECTURE.md`, non résolue par ce document non plus (dépend du mécanisme exact de rotation en cours de drag, à valider à l'implémentation).
- **[OUVERT]** Cache d'idempotence host (`RequestId`) — reporté indéfiniment (section 14), à réévaluer seulement si un incident réel de duplication est un jour observé, pas une question à trancher préventivement.
- **[OUVERT]** Éventuelle interaction UI future dédiée à `RequestMergeExact` (sélecteur de quantité ou geste équivalent) — l'API réseau est prête (section 24), aucune conception d'interaction n'existe pour ce jalon, à concevoir seulement si un besoin réel de merge à quantité explicite apparaît côté UI.
