# World Container — Whole-item transfers : rapport de validation runtime

**Statut : matrice T0-A à T0-O intégralement exécutée et validée — 15/15 PASS.** Ce document est le rapport historique permanent de cette validation (branche `feature/world-container-transfers`, à partir de `main`/`dcaff8e`). Il documente l'architecture testée, les preuves runtime réelles (logs, host + jusqu'à deux clients distants, plusieurs sessions), et les outils TEMP utilisés — tous supprimés après validation, conformément à la politique déjà appliquée aux jalons précédents du projet.

Fait suite au World Container Core V1 (voir [WORLD_CONTAINER_CORE_TESTS.md](WORLD_CONTAINER_CORE_TESTS.md)) et à l'audit de conception préalable mené avant l'implémentation (historique de conversation, non reproduit ici).

## 1. Architecture testée

Un transfert whole-item déplace une `ItemInstance` existante d'un `InventoryContainer` source vers un `InventoryContainer` destination, sans jamais :

- cloner l'instance ou changer son `InstanceId` ;
- créer une nouvelle instance ;
- fusionner ou empiler avec un item existant de la destination ;
- laisser l'item dupliqué (présent dans les deux conteneurs) ou perdu (absent des deux), sauf anomalie explicitement journalisée (jamais observée dans les tests réels — voir section 4).

### Transactions host-authoritative

`RequestTakeItem(string instanceId)` et `RequestStoreItem(string instanceId)` sont portées par `WorldContainerComponent` (jamais par le pawn), toutes deux `[Rpc.Host]`, résolvant réellement `Rpc.Caller` — jamais un identifiant simulé. Chacune délègue à une méthode métier non-RPC (`TryTakeItemAuthoritative`/`TryStoreItemAuthoritative`), qui partagent le même algorithme privé (`TryTransferItem`) pour les deux sens, seuls `source`/`destination` étant inversés par l'appelant :

- **Take** (conteneur monde → joueur) : source = `WorldContainerComponent.Container`, destination = `PlayerInventoryComponent.Container` de l'appelant.
- **Store** (joueur → conteneur monde) : source = `PlayerInventoryComponent.Container` de l'appelant, destination = `WorldContainerComponent.Container`.

### Ordre de validation réel

1. `Networking.IsHost` ;
2. `requester` non nul ;
3. pawn résolu (`KodokuPlayerComponent.FindByConnection`) ;
4. `PlayerController` valide ;
5. `PlayerInventoryComponent` et son `Container` valides ;
6. `Container` du conteneur monde valide ;
7. `requester` encore présent dans `_viewers` ;
8. distance revalidée (`IsWithinRange`) — un échec ici invalide et retire le viewer avant de refuser ;
9. `InstanceId` parsé (`Guid.TryParse`) **seulement après** les étapes 1-8 — un non-viewer transmettant également un identifiant invalide reçoit donc `NotViewer`, jamais `InvalidInstanceId` (confirmé par test réel, section 3, Phase 5) ;
10. item présent dans la source ;
11. préflight de la destination ;
12. mutation ;
13. notifications (uniquement après succès complet) ;
14. résultat ciblé (toujours, succès ou échec).

### Préflight pur

`InventoryContainer.TryFindFirstFit(item, allowRotation = true)` valide qu'un item peut être placé par first-fit **sans jamais muter le conteneur** — même validation, même refus `AlreadyContained`, même ordre de scan (normale d'abord, tournée ensuite si autorisée et supportée) que `TryAddFirstFit`, avec lequel elle partage la sélection de candidat via une méthode privée commune (`FindFirstFitCandidate`, aucune logique de placement dupliquée). Le flux de transfert appelle ce préflight sur la **destination** avant toute mutation de la source : un inventaire destination plein (ou un item qui ne peut structurellement pas y tenir) ne touche donc jamais la source — confirmé par test réel (T0-E, T0-F).

### Retrait avant ajout, aucun second scan

Une fois le préflight réussi, l'algorithme retire l'item de la source (`TryRemove`) puis l'ajoute à la destination **à la position exacte planifiée par le préflight** (`TryAdd`, jamais un second scan first-fit). Même référence `ItemInstance`, même `InstanceId` — jamais de duplication temporaire utilisée comme stratégie normale.

### Rollback — filet de sécurité, jamais le chemin normal

Si l'ajout planifié échoue malgré tout (anomalie, jamais provoquée volontairement en production) : log `[WorldContainerTransfer][UnexpectedPlannedAddFailure]`, tentative de rollback exact dans la source à la position d'origine, puis `InternalError` (rollback réussi, aucune notification) ou `RollbackFailed` (rollback échoué, log `[WorldContainerTransfer][CriticalRollbackFailure]`). **Jamais observé dans les tests réels** — le rollback n'est déclenché par aucun scénario de la matrice, y compris `DestinationNoSpace`, où le préflight refuse déjà avant tout retrait.

### Notifications — ordre réel, couplé révision+snapshot

`PlayerInventoryComponent.NotifyMutated()` et `WorldContainerComponent.NotifyContentMutated()` couplent chacune, en un seul appel synchrone, l'incrément de révision **et** l'envoi immédiat du snapshot correspondant — aucune API ne sépare ces deux étapes.

- **Take** : `NotifyContentMutated()` (conteneur, source) d'abord, puis `NotifyMutated()` (joueur, destination).
- **Store** : `NotifyMutated()` (joueur, source) d'abord, puis `NotifyContentMutated()` (conteneur, destination).

Confirmé par l'ordre exact des logs host dans toutes les sessions réelles (section 3).

### Résultat RPC ciblé — non canonique

`WorldContainerComponent.ReceiveTransferResult` (`[Rpc.Broadcast]` + `Rpc.FilterInclude(caller)` — jamais `[Rpc.Owner]`, ce conteneur n'a pas de `Network Owner` joueur, ADR-0006) envoie un accusé de traitement au seul appelant, exactement une fois par requête traitée (après les deux notifications en cas de succès, immédiatement après la décision en cas d'échec). Il ne porte que `Success`/`Direction`/`InstanceId`/`FailureReason` — **aucune donnée canonique**. Le cache local côté client (`HasLocalTransferResult`/`LastTransferResult`/`LocalTransferResultSequence`) n'est **jamais** une preuve que les snapshots de contenu ont déjà été appliqués localement — seules `LocalRevision` (conteneur et joueur) en font foi pour cela. Ce cache local reste dans le code de production (pas un outil TEMP) : c'est le contrat de retour réseau de la fonctionnalité elle-même.

### Hypothèse de traitement host séquentiel

Comme pour tout le reste du projet (`IsClaimed`, `HasEvaluated`, `TryEquipAuthoritative`), la garantie d'atomicité repose sur l'hypothèse que le host traite les RPC de façon séquentielle. **Cette hypothèse reste une convention de projet, pas une garantie formellement documentée par le moteur s&box** — T0-M (section 3) l'a confirmée empiriquement avec deux clients réellement distincts, sans jamais constituer une preuve générale de l'ordonnancement interne du moteur.

### Caches clients jamais utilisés comme autorité

Aucune méthode métier ne lit `LocalEntries`, `LocalRevision`, `LastTransferResult` ou tout autre cache de présentation pour décider d'une mutation — seuls `Container` (host) et `_viewers` (host) font foi.

## 2. Ce qui reste non implémenté

Ce jalon ne couvre que le transfert whole-item bidirectionnel. Restent explicitement hors périmètre, non commencés :

- transfert partiel de quantité, stacking/merge ;
- drag and drop, UI de production ;
- interaction monde `Component.IPressable` sur `WorldContainerComponent` ;
- prefab `Wooden_Crate` de production ;
- fermeture/invalidation si le conteneur disparaît pendant la consultation ;
- `StableContainerId`, persistance ;
- permissions avancées, conteneurs verrouillés ou personnels.

## 3. Sessions de validation runtime réelles

Trois sessions distinctes, host + un ou deux clients réels, logs applicatifs à l'appui. Rôles identifiés par pseudonyme in-game (non stables d'une session à l'autre).

### Session 1 — Phases 0 à 1 (baseline, T0-A, T0-C)

Identités : H = Jo (host), A = Barney, B = Ned (non-viewer).

**Phase 0 — baseline** : H et A ouvrent explicitement le conteneur (`ViewerCount=2`), B reste non-viewer (aucune ligne `[WorldContainer]` dans son log). `Add World Test Water Bottle` depuis H crée W1 (`1d15b9e4-a045-472a-896a-2dc0b1364673`), un seul incrément de révision monde (0→1), visible chez H et A, absent chez B.

**Phase 1 — T0-A (Take réussi)** : A clique Take sur W1. Ordre confirmé dans le log host : `NotifyContentMutated` (snapshot monde, revision 1→2) → `NotifyMutated` (snapshot inventaire A, revision 0→1) → log de synthèse `[WorldContainerTransfer][Take][Success]` → `Result Send` vers A. W1 disparaît du conteneur, apparaît dans l'inventaire de A avec le même `InstanceId` (**T0-C confirmé**). Snapshot monde reçu par H et A, jamais par B. Résultat ciblé reçu uniquement par A (`LocalTransferResultSequence` 0→1).

### Session 2 — Phases 2 à 8 (T0-B, T0-D à T0-L)

Mêmes identités (H = Jo, A = Barney, B = Ned), poursuite de la Session 1 (W1 dans l'inventaire de A, monde à révision 2).

- **T0-B/T0-D (Store réussi)** : A stocke W1. Ordre confirmé : `NotifyMutated` (joueur, 1→2) → `NotifyContentMutated` (monde, 2→3) — inverse de Take, symétrique. Même `InstanceId` conservé.
- **T0-L (double requête) + T0-G/T0-H (item absent)** : Double Take puis Double Store sur W1. Dans les deux cas : premier Success, second immédiatement `ItemNotFound` (même horodatage), un seul couple de révisions malgré deux résultats reçus, aucun snapshot pour l'échec.
- **T0-I (identifiant invalide)** : `not-a-guid` sur Take et Store → `InvalidInstanceId` dans les deux cas, aucune mutation.
- **T0-J (non-viewer)** : B (Ned, toujours non-viewer) tente Take avec un `InstanceId` valide puis avec `not-a-guid` — **les deux retournent `NotViewer`**, confirmant que la vérification d'appartenance aux viewers précède le parsing de l'identifiant (étape 7 avant étape 9 de l'ordre de validation, section 1).
- **T0-K (hors portée)** : A éloigné puis Take tenté — ordre confirmé `Invalidate` → `ViewerRemoved` → résultat `OutOfRange`, `ViewerCount` 2→1, aucune mutation. Réouverture ultérieure : `ViewerCount` revient à 2, snapshot courant reçu, **aucune révision créée par l'ouverture** (testé et reproduit deux fois indépendamment dans cette session).
- **T0-E (destination joueur pleine)** : `Fill Caller Inventory Until No Space` (18 items ajoutés, un seul `NotifyMutated`) puis Take de W1 → `DestinationNoSpace`, aucune mutation, aucun rollback, aucun `UnexpectedPlannedAddFailure`.
- **T0-F (destination monde pleine)** : item P1 ajouté à l'inventaire de A, `Fill World Container Until No Space` côté H (7 items ajoutés, un seul `NotifyContentMutated`), puis Store de P1 → `DestinationNoSpace`, aucune mutation, aucun rollback.

Toutes les actions de setup (`Add`/`Clear`/`Fill`) apparaissent sous le tag distinct `[WorldContainerTransferDebug]`, jamais confondues avec un transfert testé (`[WorldContainerTransfer]`).

### Session 3 — T0-M (deux appelants distincts)

Nouvelle session : H = Jo (host), deux clients réellement distincts = Krusty et Wiggum, tous deux viewers (`ViewerCount=3` avec H). Item testé : W3 (`fa7b0e72-5917-4adc-8923-73e4dd14b071`).

Le helper TEMP dédié (section 5) a permis un déclenchement déterministe : Krusty et Wiggum s'arment localement (aucune RPC de transfert à ce stade), puis **Jo déclenche une seule fois** — chaque client armé reçoit le broadcast, se désarme immédiatement, et appelle lui-même `RequestTakeItem` (vraie RPC de production, jamais court-circuitée). Les deux appels sont partis à 20 ms d'intervalle, depuis deux `Connection` réellement distinctes.

Traitement host, dans cet ordre exact (même horodatage à la milliseconde) : `Take][Success] 'Krusty'` (conteneurRevision 1→2, joueurRevision 0→1) **puis** `Take][Fail] 'Wiggum'` (`ItemNotFound`). Un seul snapshot monde diffusé aux 3 viewers, un seul snapshot inventaire (Krusty uniquement), résultat `Success` uniquement pour Krusty, résultat `ItemNotFound` uniquement pour Wiggum. Aucune duplication, aucune perte, un seul couple d'incréments de révision malgré deux appelants.

## 4. Anomalies critiques recherchées — aucune trouvée

Sur l'ensemble des trois sessions : aucun `[WorldContainerTransfer][UnexpectedPlannedAddFailure]`, aucun `[WorldContainerTransfer][CriticalRollbackFailure]`, aucune exception RPC/sérialisation liée au code de transfert, aucune duplication d'`InstanceId`, aucune perte d'`InstanceId`, aucune révision incrémentée sur un échec, aucun snapshot monde envoyé à un non-viewer, aucun snapshot inventaire envoyé à un autre joueur que son propriétaire, aucun résultat de transfert envoyé au mauvais appelant.

Bruit sans rapport observé en fin de session (déconnexion des clients) : `TcpChannel`/`SocketException 10054`, erreurs `Reflection`/`NativeResourceCache leak` — artefacts de fermeture moteur standard, apparaissant après la déconnexion effective des clients, sans lien démontré avec le code de transfert.

## 5. Outillage TEMP utilisé — supprimé après validation

Tous les fichiers suivants ont existé uniquement pour cette validation et ont été supprimés intégralement une fois la matrice T0-A à T0-O confirmée PASS — aucune trace dans le code final (recherche exhaustive de résidus effectuée dans `Code/` et `Assets/`, zéro occurrence) :

- `Code/Debug/WorldContainerTransferDebugTools.cs`
- `Code/UI/Debug/WorldContainerTransferDebugPanel.razor` / `.razor.scss`
- `Assets/scenes/Tests/WorldContainerTransferTest.scene` (et ses fichiers générés associés)

### Setup (jamais via les RPC de transfert elles-mêmes)

Add World/Caller Test Water Bottle, Clear World Container/Caller Inventory, Fill World Container/Caller Inventory Until No Space — toutes créaient de vraies `ItemInstance` via `ItemInstance.CreateNew`, passaient par les primitives `InventoryContainer` existantes, notifiaient exactement une fois par action si une mutation avait réellement eu lieu, n'appelaient jamais `RequestTakeItem`/`RequestStoreItem`/`Try*ItemAuthoritative`.

### Test (toujours la vraie RPC de production)

Take/Store By Id, Invalid Take/Store, Double Take/Store — appelaient toujours `WorldContainerComponent.RequestTakeItem`/`RequestStoreItem`, jamais `Try*ItemAuthoritative` directement, jamais de résultat falsifié.

### Helper de concurrence déterministe (T0-M)

`LocalConcurrentTakeArmed` (état local non réseau, jamais `[Sync]`), `ToggleConcurrentTakeArmed` (armement local, aucune RPC), `TriggerConcurrentTake` (`[Rpc.Host]`, déclenchement host unique, relaie sans muter ni émettre de requête lui-même), `BroadcastTriggerConcurrentTake` (`[Rpc.Broadcast]`, reçue par toutes les instances, seule une instance armée se désarme puis appelle `RequestTakeItem` localement — garantissant un `Rpc.Caller` réellement distinct par client, jamais simulé). Le host ne s'est jamais armé lui-même pendant la session réelle.

## 6. Limite connue de l'outillage (documentée, sans conséquence sur la validation)

`WorldContainerComponent.HostRevision`/`ViewerCount` ne sont pas répliqués (`[Sync]`) — leur valeur n'est correcte que lue depuis l'instance host elle-même (limitation du composant `WorldContainerComponent` lui-même, hérité du World Container Core, non introduite par ce jalon). Un panneau affiché sur un client pur affiche `0` pour ces deux champs même quand des transferts réussissent réellement ; seuls `LocalRevision`/`LocalEntries` (alimentés par les snapshots reçus) reflètent l'état côté client — c'est d'ailleurs sur ces derniers que toutes les vérifications de ce rapport reposent, jamais sur `HostRevision`/`ViewerCount` lus depuis un client.

## 7. Réserve historique restante

Le `Network Owner` du GameObject conteneur n'a été observé directement dans l'inspecteur à aucun moment de ces sessions — le code et les comportements runtime restent conformes à un objet réseau partagé sans owner joueur (ADR-0006), et aucune prise d'ownership n'a été observée indirectement pendant les sessions multi-client (aucun comportement de type mono-viewer, aucun message d'erreur d'ownership). Réserve mineure, non bloquante, héritée du World Container Core.
