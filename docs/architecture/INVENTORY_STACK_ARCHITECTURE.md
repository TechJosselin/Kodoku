# Architecture — Stacks, split, merge et transferts partiels de quantité

**Statut : document de conception, produit par un audit de code réel. Aucune ligne de code de gameplay n'a été écrite pour ce jalon.** Toute affirmation ci-dessous distingue explicitement :

- **[IMPLÉMENTÉ]** — déjà présent et validé dans `main` (vérifié par lecture directe du code à la date de cette mission, commit `963a8e9`).
- **[AUDITÉ]** — comportement actuel du code, vérifié par lecture directe (pas une hypothèse), mais pas encore un objectif de conception.
- **[RECOMMANDÉ]** — conception proposée par ce document, non implémentée, à valider par l'utilisateur avant tout code.
- **[OUVERT]** — question non tranchée, à lever explicitement plus tard.

Ce document a été produit par une mission d'audit dédiée (voir [docs/status/CURRENT_STATE.md](../status/CURRENT_STATE.md) pour la date). Il précède l'implémentation, sur le même modèle que [WORLD_CONTAINER_ARCHITECTURE.md](WORLD_CONTAINER_ARCHITECTURE.md) avant le jalon conteneurs — voir « Pourquoi un document de conception plutôt qu'un ADR » en fin de document pour la justification de ce choix de format.

**Corrections apportées par une seconde mission de relecture dédiée** (même jour, sans nouvelle inspection de code — relecture et correction de conception uniquement) : trois conclusions de la version initiale se sont révélées insuffisantes et ont été révisées, marquées explicitement « corrigé » aux sections concernées —

- **section 5** : l'orchestration deux-conteneurs, initialement recommandée directement dans `WorldContainerComponent`, est remplacée par une couche de transaction C# pure dédiée (`InventoryStackTransactions`) ;
- **section 7** : la précondition de fraîcheur, initialement limitée à `ExpectedSourceQuantity`, est étendue à `TargetInstanceId`/`ExpectedTargetQuantity` pour toute opération ciblant une pile existante ;
- **section 10** : la protection de l'équipement, initialement limitée à une garde runtime, est étendue à une double protection (validation de contenu + runtime).

Le reste du document (identité, sémantique split/merge, révisions, poids, pickup, drop partiel, UI) n'a pas été remis en cause par cette relecture — seulement mis à jour où ces trois corrections s'y répercutaient (roadmap, matrice de tests, questions ouvertes).

**Mise à jour du 2026-07-23 (Jalon 1 implémenté et validé, voir section 14 ci-dessous)** : le Jalon 1 — Stack Core pur — décrit par ce document est désormais **[IMPLÉMENTÉ]** et validé (65 scénarios PASS, exécution réelle en éditeur, noyau non networké — une session solo suffit, voir [multiplayer.md](../../.claude/rules/multiplayer.md)). Fichiers réels : `Code/Items/Inventory/InventoryContainer.cs` (nouvelle primitive `TryGrowQuantity`), `InventoryStackTransactions.cs`, `StackTransactionResult.cs`, `StackTransactionFailureReason.cs`, plus l'outil de test temporaire `Code/Debug/InventoryStackTransactionsDebugComponent.cs`. Les noms d'API retenus correspondent à ceux déjà cités par ce document (section 5) ; aucune décision de conception n'a été modifiée pendant l'implémentation, à une précision près : `TryMergeExact` distingue explicitement `TargetFull` (capacité restante nulle) d'`InsufficientTargetCapacity` (capacité restante insuffisante mais non nulle) — les deux raisons étaient déjà listées comme distinctes par la mission d'implémentation, ce document ne le précisait pas explicitement. Les items de test ont été construits entièrement en mémoire (`new ItemDefinition { ... }`) — aucune ressource `.item` créée, l'item de validation décrit en section 17 reste donc non créé en tant qu'asset. Jalons 2 à 5 (réseau) restent non implémentés.

**Mise à jour du 2026-07-24 (relecture technique et corrections post-implémentation)** : une relecture dédiée du commit d'implémentation a identifié un défaut bloquant et plusieurs défauts important/mineurs (voir rapport de mission, non reproduit ici) — tous corrigés, revalidés par une nouvelle exécution runtime complète (65/65 PASS, en remplacement des 59 scénarios initiaux, décompte exact en section 14). Décisions de conception affectées, aucune autre :

- **Atomicité après validation finale (défaut bloquant corrigé)** : `InventoryStackTransactions` valide désormais l'état des conteneurs affectés (`TryValidateState`) **avant** toute mutation, puis **après** les deux mutations. Si cette validation finale échoue alors que les deux mutations avaient individuellement réussi, un rollback complet est tenté (retrait de la pile ajoutée côté cible, restauration de la quantité/du placement d'origine côté source, via les mêmes primitives déjà contrôlées — `TryRemove`/`TryGrowQuantity`/`TryConsume`/`TryAdd`, jamais une nouvelle `ItemInstance`). `StackTransactionFailureReason.InvariantViolation` n'est retourné que si ce rollback réussit et que l'état restauré revalide correctement ; `RollbackFailed` sinon. Avant cette correction, un échec de validation finale retournait `InvariantViolation` en laissant les deux mutations en place — violation de l'invariant « échec ⇒ aucune mutation observable ». **Ce chemin précis (les deux mutations individuellement infaillibles échouant néanmoins à la validation finale) reste non provocable proprement en runtime sans fault injection dédiée, explicitement écartée** — la correction est validée par lecture de code et par le fait que les primitives de rollback réutilisées sont elles-mêmes exercées par les 65 scénarios, pas par un test qui force cette branche exacte.
- **`targetContainer == null`** : `TransferPartialToEmptyCore` et `ResolveMergePair` distinguent désormais explicitement `sourceContainer == null` (`SourceNotFound`) de `targetContainer == null` (`TargetNotFound`) — un seul test (`T-12`) confirme ce cas pour le transfert partiel.
- **Convention `RequestedAmount`** : voir section 14 (Jalon 1) ci-dessous pour le détail complet — `TryMergeUntilCapacity` ne reçoit aucune quantité explicite de l'appelant et rapporte donc toujours `RequestedAmount = 0` (succès comme échec), `MovedAmount` seul portant la quantité réelle.
- **Ordre de résolution `ResolveMergePair`** : l'existence de la source et de la cible est désormais vérifiée avant le test d'identité `sourceId == targetId`, pour qu'une source/cible inexistante soit toujours rapportée comme `SourceNotFound`/`TargetNotFound` plutôt que masquée par `SameSourceAndTarget` (`M-16`).
- **Couverture `SourceNotStackable`** : désormais exercée par un test dédié (`M-15`), auparavant un chemin de code jamais atteint par la suite.
- **Garde défensive `StackTransactionResult.Fail`** : refuse désormais explicitement (`ArgumentException`) une raison `None`, erreur de programmation plutôt qu'échec métier (`Reg-04`).

---

## 1. Périmètre audité

Fichiers inspectés par lecture directe (liste complète, pas un échantillon) :

- `Code/Items/Definitions/ItemDefinition.cs`, `Code/Items/Instances/ItemInstance.cs`
- `Code/Items/Inventory/InventoryContainer.cs`, `InventoryPlacement.cs`, `InventoryOperationResult.cs`, `InventoryFailureReason.cs`
- `Code/Players/Inventory/PlayerInventoryComponent.cs`, `PlayerItemUseComponent.cs`, `PlayerItemDropComponent.cs`, `InventorySnapshotEntry.cs`, `EquipmentSnapshotEntry.cs`, `PlayerInventoryMoveFailureReason.cs`
- `Code/World/Containers/WorldContainerComponent.cs` (intégralement, 1276 lignes), `WorldContainerSnapshotEntry.cs`, `WorldContainerTransferFailureReason.cs`, `WorldContainerMoveFailureReason.cs`
- `Code/Items/World/WorldItemComponent.cs`, `Code/Items/Interaction/WorldItemPickupComponent.cs`, `Code/Items/Loot/LootSpawnPointComponent.cs`
- `Code/UI/Menu/Pages/InventoryPage.razor` (intégralement, 1194 lignes), `Code/UI/Inventory/InventoryGridItem.cs`, `InventoryGridGhost.cs`
- `docs/status/CURRENT_STATE.md`, `ROADMAP.md`, `OPEN_QUESTIONS.md`, `docs/architecture/ITEM_ARCHITECTURE.md`, `WORLD_CONTAINER_ARCHITECTURE.md`, `docs/decisions/ADR-0005-CUSTOM-INVENTORY.md`

Aucun `.item` du projet n'a `MaxStack > 1` (`water_bottle`, `test_helmet`, `test_body_armor` valent tous 1) — **aucun scénario impliquant réellement une pile n'a jamais pu être testé en runtime dans ce projet**, y compris pour des fonctionnalités déjà livrées (le drop conserve une quantité, mais seulement `1`, jamais testé `>1` — voir `ITEM_ARCHITECTURE.md`, « Drop d'item — V1 »).

---

## 2. Ce que le code fait déjà (fondations pour les stacks)

### 2.1 `ItemInstance` — déjà quantité-consciente

- `Quantity` : invariant strict `1 <= Quantity <= Definition.MaxStack`, jamais 0, jamais hors bornes (exception à la construction, refus silencieux via `TrySetQuantity` à la mutation).
- `TrySetQuantity`/`TryAddQuantity`/`TryRemoveQuantity` existent déjà, complets, testés par construction (bornes vérifiées).
- `CanStackWith(other)` existe déjà : compatible si `MaxStack > 1`, `InstanceId` différent, et même référence de `Definition` **ou** même `ItemId` (tolère deux `GameResource` distincts pointant le même item logique). Ne fusionne rien — répond seulement à la question.
- Deux chemins de construction seulement : `CreateNew` (nouveau GUID) et `Restore` (GUID fourni, pour la réplication/late join). **Aucun troisième chemin n'existe** — un split devra donc passer par `CreateNew`.

### 2.2 `InventoryContainer` — déjà un précédent pour la mutation de quantité

`TryConsume(instanceId, amount)` **[IMPLÉMENTÉ]** est le précédent le plus proche d'un split/merge : il applique déjà l'invariant central que ce jalon devra réutiliser partout — **une `Quantity` ne peut jamais atteindre 0** ; une consommation totale retire le *placement* (pas l'instance à quantité nulle), une consommation partielle conserve le même `InstanceId` et ne fait que décrémenter. C'est exactement la règle d'identité recommandée en section 3 pour toute décrémentation partielle.

En revanche, **aucune méthode de fusion/stacking n'existe nulle part** — vérifié par lecture complète de la classe. `TryAddFirstFit`/`TryAdd` ne recherchent jamais une instance compatible existante ; ils ne refusent un doublon que par `InstanceId` exact (`AlreadyContained`), jamais par compatibilité de pile. `CanPlace` ne teste qu'une collision géométrique, jamais une compatibilité d'empilement. La classe respecte strictement son propre invariant documenté : **« Ne crée jamais de nouvelle `ItemInstance` »**.

### 2.3 Transferts déjà atomiques et déjà à deux variantes (first-fit / ciblé)

`WorldContainerComponent` porte déjà, en double (Take/Store), le même algorithme en deux versions :

- `TryTransferItem` (first-fit, whole-item) — préflight pur (`TryFindFirstFit`) avant toute mutation, retrait puis ajout planifié, rollback filet de sécurité si l'ajout planifié échoue malgré le préflight.
- `TryTransferItemTo` (ciblé, drag-and-drop) — même algorithme, destination validée par `CanPlace` à une cellule précise plutôt qu'un scan, **plus une précondition de fraîcheur `expectedSourceX/Y/Rotated`** revalidée avant toute mutation (`StaleSource`).

C'est déjà, dans les faits, le patron exact qu'un transfert partiel devra étendre — pas un patron à inventer.

### 2.4 `PlayerInventoryComponent.RequestMoveItem`/`WorldContainerComponent.RequestMoveItem` — déjà validés en concurrence

Le déplacement interne (drag-and-drop en grille, sans changer de conteneur) est **[IMPLÉMENTÉ]** et validé par test réel à deux instances avec concurrence réelle (campagne M1, `docs/status/CURRENT_STATE.md`, mise à jour du 2026-07-21 : M1-N/M1-O, deux fenêtres réellement simultanées). Ce mécanisme — `RequestId` de corrélation généré côté client, préconditions `expectedSourceX/Y/Rotated`, un seul résultat RPC ciblé par requête — est la fondation directe de toute la stratégie de concurrence proposée en section 7.

### 2.5 UI — déjà conçue pour des opérations spatiales corrélées par `RequestId`

`InventoryPage.razor` porte déjà (voir audit complet, section « UI » du rapport de mission) : sélection par `InstanceId`, verrou pending par item (`_pendingRequestIdByInstanceId`), double dictionnaire pending/verrou pour éviter qu'un résultat périmé ne libère une opération plus récente, réconciliation par disparition de la source **et** réception du résultat authoritative (jamais l'un sans l'autre), et affichage de la quantité (`x@Quantity`) déjà présent dans les deux grilles. Un split/merge s'insère dans cette machinerie existante — il ne la remplace pas.

### 2.6 Ce qui n'existe pas du tout

- Aucune précondition de quantité (`expectedSourceQuantity`) — seules les préconditions spatiales existent.
- Aucun cache host de `RequestId` (idempotence) — le `RequestId` actuel sert **exclusivement** de corrélation UI côté client (voir section 7).
- Aucune validation croisée `EquipmentSlot`/`MaxStack`.
- Aucun item de validation empilable dans le projet.

---

## 3. Identité des piles — politique recommandée

Politique de la mission challengée point par point, puis retenue **avec une simplification** (voir section 7 pour la partie préconditions, où deux des trois champs suggérés se sont révélés inutiles).

| Opération | `InstanceId` de la source | `InstanceId` du résultat | Justification |
|---|---|---|---|
| Déplacement spatial (`TryMove`) | conservé | — | **[IMPLÉMENTÉ]**, déjà le comportement de `InventoryContainer.TryMove` — un déplacement remplace le `InventoryPlacement` (position) mais jamais l'`ItemInstance`. |
| Transfert complet vers case vide | conservé | — | **[IMPLÉMENTÉ]**, déjà vérifié par audit direct de `TryAddFirstFit`/`TryTransferItem` (`WORLD_CONTAINER_ARCHITECTURE.md`, section 9) : jamais de fusion, jamais de nouvelle instance. |
| Split | conservé sur la pile restante | **nouveau GUID** pour la pile extraite | La pile restante reste « la même pile », juste plus petite (même raisonnement que `TryConsume` partiel). La pile extraite occupe une nouvelle position et doit avoir une identité propre — deux `InventoryPlacement` distincts référençant la même `ItemInstance` seraient un état incohérent (l'index `_byInstanceId` de `InventoryContainer` suppose un `InstanceId` unique par placement). |
| Transfert partiel vers case vide | conservé côté source | **nouveau GUID** côté destination | Équivalent d'un split immédiatement suivi d'un déplacement inter-conteneur, exécuté comme une seule transaction host (jamais deux étapes observables séparément — voir section 6). |
| Merge partiel | conservé côté cible ; conservé (décrémenté) côté source | — | La pile cible absorbe une quantité sans changer d'identité (plus simple pour le snapshot : une seule `Quantity` change en place) ; la source décrémentée reste « la même pile », comme `TryConsume`. |
| Merge total (source entièrement absorbée) | **disparaît** côté source ; conservé côté cible | — | Même règle que `TryConsume` : une quantité qui atteint 0 fait disparaître le *placement*, jamais une instance à quantité nulle. |
| Transfert complet vers pile existante compatible | **disparaît** (fusion) ; conservé côté cible | — | Cas particulier de merge total, cross-conteneur. |
| Drop partiel | conservé côté inventaire (décrémenté) | **nouveau GUID** côté objet-monde | Voir section 11 — divergence assumée avec le drop complet actuel, qui réutilise délibérément la même instance. |
| Pickup d'une pile | inchangé (identité du `WorldItemComponent` devient celle du placement) | — | **[IMPLÉMENTÉ]**, comportement actuel non modifié tant que l'auto-merge (section 12) n'est pas retenu. |

**Verdict retenu, sans modification par rapport à la proposition de la mission** — elle est directement cohérente avec le seul précédent déjà écrit et déjà testé dans le code (`InventoryContainer.TryConsume`), donc c'est le choix qui introduit le moins de nouveauté conceptuelle. Une alternative envisagée et rejetée : émettre systématiquement un GUID neuf des deux côtés à chaque mutation de quantité (traiter `ItemInstance` comme une valeur immuable versionnée) — rejetée parce qu'elle casserait toutes les préconditions de fraîcheur déjà construites autour d'un `InstanceId` stable par placement (`expectedSourceX/Y/Rotated`, verrou pending UI par `InstanceId`), pour un bénéfice de « symétrie » non demandé.

**Précision importante, à ne pas perdre de vue à l'implémentation** : la garantie historique déjà documentée ailleurs dans ce projet (`WORLD_CONTAINER_ARCHITECTURE.md`, section 9 : « un transfert conserve toujours le même `InstanceId` ») **n'est valable que pour un transfert complet vers un emplacement vide**. Elle cesse d'être vraie dès qu'un split, un merge ou un transfert partiel entre en jeu (voir le tableau ci-dessus : quatre des neuf lignes font apparaître ou disparaître un `InstanceId`). Un pointeur vers cette précision a été ajouté dans `WORLD_CONTAINER_ARCHITECTURE.md` (voir section 12 du rapport de mission) ; la garantie elle-même n'y est pas réécrite — elle reste exacte telle quelle pour son périmètre déjà implémenté (transfert complet), seule la nuance « pas au-delà de ce périmètre » y est ajoutée par renvoi vers ce document.

---

## 4. Qui crée les nouvelles instances — verdict

**[RECOMMANDÉ] Option B, avec une clarification par rapport à la mission** : ni un composant host de façon générique, ni une factory dédiée — la création reste **au même niveau que l'orchestration réseau qui l'entoure** (`PlayerInventoryComponent`/`WorldContainerComponent`, exactement là où vivent déjà `TryEquipAuthoritative`/`TryDropAuthoritative`), via l'unique point d'entrée déjà existant, `ItemInstance.CreateNew(definition, amount)` — jamais un nouveau chemin de construction sur `ItemInstance`.

Comparaison :

- **Option A (le cœur crée l'instance)** — rejetée. `InventoryContainer` porte, en commentaire de classe, l'invariant « ne crée jamais de nouvelle `ItemInstance` », répété dans `ITEM_ARCHITECTURE.md` et `WORLD_CONTAINER_ARCHITECTURE.md`. Le casser pour ce seul jalon contredirait une garantie déjà documentée et déjà citée comme argument de conception ailleurs (ADR-0005, section « Conséquences positives » : « `ItemInstance` reste l'unique source de vérité »).
- **Option B (le composant host crée, puis fournit)** — retenue. Le composant appelant connaît déjà tout ce qu'il faut (`Definition` de la pile source, montant demandé) au moment où il valide la requête, avant tout appel au cœur. Aucune donnée supplémentaire à faire transiter.
- **Option C (factory/service dédié)** — rejetée pour ce jalon. Un service séparé n'ajouterait rien qu'un appel direct à `ItemInstance.CreateNew` ne fait déjà, et introduirait une abstraction sans second cas d'usage concret — contraire à `.claude/rules/csharp.md` (« Pas de couche d'abstraction sans besoin concret »). Les GUID restent générés par `Guid.NewGuid()` (non déterministes) exactement comme partout ailleurs dans le projet ; rien dans ce jalon ne justifie un générateur déterministe (aucun système de sauvegarde n'existe encore pour en avoir besoin).

Conséquence pratique : les nouvelles méthodes du cœur (section 5) prennent toujours une `ItemInstance` déjà construite en paramètre (même signature que `TryAdd(ItemInstance, x, y, rotated)` aujourd'hui), jamais un `(ItemDefinition, int amount)` qu'elles construiraient elles-mêmes — vrai aussi bien pour `InventoryContainer` que pour la couche de transaction pure introduite en section 5 : le composant réseau construit l'instance, la transmet à la couche de transaction, qui la transmet à son tour à `InventoryContainer.TryAdd`. Aucune des deux couches internes ne construit jamais elle-même une `ItemInstance`.

---

## 5. Architecture du cœur — verdict corrigé

**Correction par rapport à la version précédente de ce document.** La version précédente rejetait une couche de transaction séparée et proposait de garder l'orchestration deux-conteneurs directement dans `WorldContainerComponent`. **Cette conclusion est corrigée** : une mission de relecture dédiée a identifié que cette approche recolle exactement le problème qu'elle prétendait éviter — elle aurait fait porter à `WorldContainerComponent` (déjà 1276 lignes) la totalité de la logique d'atomicité/rollback pour les transferts partiels, **en plus** de tout ce qu'il porte déjà (RPC, ownership, viewer, portée, session, notification, révision), sans aucun endroit pur et testable hors runtime pour cette logique. La conception retenue distingue désormais **trois responsabilités**, pas deux.

### `InventoryContainer` — inchangé dans son rôle, étendu de primitives locales

Reste responsable uniquement des opérations et invariants d'**un seul** conteneur : validation spatiale (`CanPlace`), recherche de placement (`GetPlacement`), ajout/retrait de placement (`TryAdd`/`TryAddFirstFit`/`TryRemove`), déplacement interne (`TryMove`), mutation locale de quantité (`TryConsume`, déjà existant — décrémente ou retire le placement si la quantité atteint zéro, exactement la primitive de « retrait côté source » réutilisable telle quelle pour split/merge), validation des invariants internes (`TryValidateState`). Une seule primitive locale manque réellement : **augmenter** la quantité d'un placement existant sans dépasser `Definition.MaxStack` (le pendant « croissance » de `TryConsume`, nécessaire pour la fusion). Nom provisoire, à trancher à l'implémentation (`TryGrowQuantity`/`TryAddQuantityTo` — la mission cite aussi `TrySplitLocal`/`TryMergeLocal`/`TryAddQuantity`/`TryRemoveQuantity` comme noms possibles, tous équivalents pour ce document). `InventoryContainer` ne connaît toujours qu'**un seul** conteneur à la fois — jamais deux références simultanées.

### Couche de transaction pure — nouvelle, retenue

**[RECOMMANDÉ]** Introduire une classe C# pure, sans dépendance à s&box, sans `Component`, sans `GameObject`, sans RPC — par exemple `InventoryStackTransactions` (opérations statiques ou méthodes sur une instance sans état), `StackTransactionResult`, `StackTransactionFailureReason`. Elle orchestre les opérations atomiques portant sur **un ou deux** `InventoryContainer` :

- split (un conteneur) ;
- merge (un conteneur) ;
- transfert partiel vers une case vide (deux conteneurs) ;
- transfert partiel vers une pile compatible (deux conteneurs) ;
- rollback de chacune de ces opérations ;
- conservation des invariants (jamais de mutation partielle observable, jamais de `Quantity` hors bornes) ;
- application de la politique d'identité (section 3) — **c'est elle**, pas le composant réseau, qui décide quel `InstanceId` survit à quelle étape, puisque cette règle est identique que l'opération soit déclenchée par le joueur ou par un conteneur.

Elle ne fait jamais : de réseau, de gestion de viewers, de publication de snapshot, d'incrément direct de révision réseau, de création implicite d'`ItemInstance` (le composant host fournit toujours la nouvelle instance nécessaire à un split ou un transfert partiel, exactement comme convenu section 4 — cette couche ne fait que la placer, jamais la construire elle-même).

Elle appelle les primitives déjà exposées par `InventoryContainer` (`GetPlacement`, `CanPlace`/`TryFindFirstFit`, `TryConsume`, `TryAdd`/`TryAddFirstFit`, et la nouvelle primitive de croissance ci-dessus) — elle ne réimplémente aucune validation spatiale ou de quantité déjà couverte par ces méthodes, elle les compose.

### Composants réseau — rôle inchangé, réduit à ce qu'ils faisaient déjà

`PlayerInventoryComponent`/`WorldContainerComponent` restent responsables de l'autorité host, de la validation du propriétaire/viewer/portée/session, des RPC, des préconditions de fraîcheur (section 7), des `RequestId`, des révisions, des snapshots, de la publication du résultat — exactement leur rôle actuel pour les transferts complets, non modifié. Ils appellent la couche de transaction, jamais l'inverse.

### Pourquoi cette séparation, pas les deux autres approches

- **Approche 1 (tout inline, dupliqué dans chaque composant réseau)** — rejetée : dupliquerait la logique de split/merge/rollback entre `PlayerInventoryComponent` (split/merge dans sa propre grille) et `WorldContainerComponent` (split/merge dans sa grille **et** transfert partiel entre les deux), sans bénéfice.
- **Approche « primitives sur `InventoryContainer` + orchestration directe dans `WorldContainerComponent` » (version précédente de ce document)** — corrigée, pour les raisons ouvrant cette section : elle grossit encore le fichier le plus volumineux du projet avec une logique qui n'a strictement rien à voir avec le réseau, et elle ne peut être testée qu'à travers un `Component` (donc en théorie via l'éditeur/runtime, jamais en C# pur isolé) — contrairement à `InventoryContainer` lui-même, déjà testé en solo (Tests A-O).
- **Éviter de grossir encore `WorldContainerComponent`.** Le fichier fait déjà 1276 lignes ; toute la logique d'atomicité/rollback des opérations à quantité (potentiellement la plus complexe de tout le système d'inventaire) vit désormais ailleurs, dans un fichier dédié et petit.
- **Éviter la duplication avec `PlayerInventoryComponent`.** Les deux composants réseau ont besoin des mêmes opérations de split/merge internes à un conteneur (chacun dans sa propre grille) — sans couche partagée, cette logique serait écrite deux fois ; avec elle, chaque composant réseau se contente d'appeler la même méthode `InventoryStackTransactions.TrySplit(...)`/`TryMerge(...)`.
- **Testabilité sans runtime s&box.** Une classe C# pure peut être testée exactement comme `InventoryContainer` l'est déjà (Tests A-O, exécution solo en éditeur ou même un futur harness `dotnet test` indépendant) — jamais possible pour une logique vivant à l'intérieur d'un `Component`.
- **Centralisation de l'atomicité et du rollback.** Un seul endroit contient la logique « valider tout avant de muter quoi que ce soit, annuler proprement en cas d'échec inattendu » pour toutes les opérations de quantité — pas une copie par composant réseau qui pourrait diverger avec le temps.
- **Préparation des contraintes futures.** Un futur poids maximal bloquant ou un futur sous-conteneur n'auraient qu'un seul endroit à modifier (cette couche), jamais deux composants réseau distincts à maintenir en synchronisation.

Cette couche doit rester **petite et explicite**, centrée sur les transactions de quantité listées ci-dessus — ce n'est pas un framework générique de transactions, pas une réécriture de `InventoryContainer`, pas une abstraction réutilisable au-delà de ce périmètre précis. Si elle devait un jour porter une logique métier sans rapport avec les stacks, ce serait un signal qu'elle a mal grossi, pas un précédent à suivre.

**Verdict** : `InventoryContainer` (primitives locales, un seul conteneur) → `InventoryStackTransactions` (orchestration pure, un ou deux conteneurs, identité, atomicité, rollback) → `PlayerInventoryComponent`/`WorldContainerComponent` (réseau, autorité, fraîcheur, révisions, snapshots) — trois couches, chacune avec une responsabilité unique et non chevauchante.

---

## 6. Sémantique exacte

### 6.1 Split

- **Quantité minimale extraite** : 1.
- **Quantité maximale extraite** : `source.Quantity - 1` — **`amount == source.Quantity` est un échec explicite** (`InvalidQuantity`), pas un repli silencieux vers un déplacement complet. Un appelant qui veut déplacer la pile entière utilise `TryMove`, pas `TrySplit` — contrats distincts, jamais interchangeables silencieusement.
- **`MaxStack == 1`** : rejeté automatiquement, sans code spécial — `Quantity` vaut alors toujours 1, donc `amount < Quantity` ne peut jamais être vrai. Aucune branche dédiée nécessaire.
- **Destination** : deux variantes, comme l'existant (`TrySplit` first-fit, `TrySplitAt` ciblée x/y/rotated) — même dualité que `TryAdd`/`TryAddFirstFit` et `TryTransferItem`/`TryTransferItemTo`.
- **Nouveau GUID** : fourni par l'appelant (section 4), jamais construit par `InventoryContainer`.
- **Ordre des validations** (avant toute mutation, exécutées par la couche de transaction pure, section 5) : 1) source existe (`ItemNotFound`) ; 2) `1 <= amount <= source.Quantity - 1` (`InvalidQuantity`) ; 3) destination valide (`CanPlace`/`TryFindFirstFit`, préflight pur, aucune mutation en cas d'échec). La précondition réseau `ExpectedSourceQuantity` (section 7) est revalidée par le composant réseau **avant** d'appeler la couche de transaction — un split n'a pas de cible existante, donc jamais de précondition `ExpectedTargetQuantity`/`TargetInstanceId` (celles-ci ne concernent que le merge et le transfert partiel vers une pile compatible, section 6.2/6.3).
- **Ordre des mutations** (dans `InventoryStackTransactions`, jamais directement dans un composant réseau) : décrément de la source (`InventoryContainer.TryConsume`, ne peut plus échouer, bornes déjà validées) puis insertion du nouveau placement (`InventoryContainer.TryAdd`, ne peut plus échouer, position déjà validée par le préflight).
- **Rollback** : **aucun mécanisme dédié nécessaire** — une fois le préflight complet passé, les deux mutations restantes sont infaillibles par construction (même raisonnement déjà appliqué à l'équipement : « atomicité par ordre des mutations, sans rollback dédié »). Un split ne touche jamais le réseau/spawn — contrairement au drop, il n'existe aucune étape externe pouvant échouer après la première mutation. La couche de transaction reste malgré tout l'endroit où un rollback serait implémenté si ce raisonnement s'avérait faux à l'usage — centralisé, jamais dupliqué par composant réseau.
- **Révisions** : une seule (même conteneur, voir section 8).
- **Résultat** : `StackTransactionResult` (section 5) portant succès/échec + les deux placements résultants (source décrémentée, nouvelle instance) — pour le logging/les tests, le snapshot réel reflétant de toute façon tout le conteneur.

### 6.2 Merge

- **Compatibilité** : réutilise `ItemInstance.CanStackWith` telle quelle — aucun changement nécessaire. Si un futur état propre à l'instance apparaît (durabilité, contenu restant), `CanStackWith` devra alors aussi comparer cet état — **non construit maintenant**, pas de besoin actuel (voir `.claude/rules/csharp.md`, contre la généralisation prématurée).
- **Deux sémantiques distinctes, pas une seule** — c'est la clarification principale de ce document par rapport à la question posée par la mission :
  - **Merge par glisser-déposer d'une pile entière sur une pile compatible** (le geste UI principal, section 13) : **remplit autant que possible** la cible, laisse le reste en place à sa position d'origine si tout ne rentre pas. Ce n'est pas une « réduction silencieuse d'une demande explicite » — il n'y a jamais eu de quantité explicitement demandée dans ce geste, seulement une intention « rapprocher ces deux piles autant que possible ». Cible déjà pleine → échec propre (`DestinationFull`), zéro transfert.
  - **Merge à quantité explicite** (un futur sélecteur de quantité, hors périmètre V1 — voir section 13) : quantité demandée exacte, échoue entièrement si elle ne rentre pas (`InsufficientTargetSpace`), jamais de réduction silencieuse — cohérent avec la préférence initiale de la mission, retenue pour ce cas précis uniquement.
- **Fraîcheur de la cible** (correction section 7) : toute opération réseau de merge ciblé transmet `TargetInstanceId`/`ExpectedTargetQuantity`, revalidés avant toute mutation par le composant réseau, en plus d'`ExpectedSourceQuantity` — la couche de transaction pure elle-même reste indifférente à l'origine réseau de ces valeurs, elle reçoit simplement des identifiants et quantités déjà revalidés au moment où elle est appelée (jamais de logique de fraîcheur dans `InventoryStackTransactions`/`InventoryContainer`, qui restent tous deux de purs exécutants sans notion de « ce que le client croyait vrai » — cette notion appartient exclusivement à la couche réseau).
- **Merge total** : `amount == source.Quantity` et la cible a la place → placement source disparaît (même règle que `TryConsume`), cible absorbe, position/rotation de la cible inchangées.
- **Merge partiel** : cible absorbée jusqu'à `amount`, position/rotation de la source et de la cible toutes deux inchangées, seule `Quantity` change des deux côtés.
- **Cellule libérée** : uniquement en cas de merge total (source disparaît) — jamais en cas de merge partiel.

### 6.3 Transferts partiels joueur ↔ conteneur

- **Destination vide** : la fraction devient une nouvelle pile (nouveau GUID, section 3), placée en first-fit ou ciblée — exécuté comme une seule transaction host (voir garantie ci-dessous), jamais deux étapes réseau séparées. Précondition réseau : `SourceInstanceId`/`ExpectedSourceQuantity` uniquement (pas de `TargetInstanceId` — il n'y a rien à cibler par identité dans une cellule vide), plus les préconditions spatiales de destination pour la variante ciblée (`TargetX`/`TargetY`/`TargetRotated`) — la destination est revérifiée libre au moment de la mutation (section 7), aucune réservation séparée.
- **Destination occupée par une pile compatible** : fusion de la fraction, sémantique **« quantité exacte, échec net si insuffisant »** (section 6.2, cas explicite) — cohérent avec la préférence initiale de la mission, retenue ici parce qu'un transfert partiel porte toujours une quantité explicitement demandée par le client (contrairement au glisser-déposer de pile entière). Précondition réseau complète : `SourceInstanceId`/`ExpectedSourceQuantity` **et** `TargetInstanceId`/`ExpectedTargetQuantity` (section 7, correction) — un transfert partiel cross-conteneur vers une pile existante est structurellement identique à un merge ciblé, seule la présence de deux `InventoryContainer` distincts change.
- **Aucune réduction automatique** de la quantité demandée dans ce chemin — raison développée en section 6.2 et reprise ici : cohérence avec le reste du projet (`TryMove` ne « snap » jamais vers une cellule proche valide, `TryEquipAuthoritative` ne swap jamais un slot occupé — le host ne décide jamais silencieusement autre chose que ce qui a été demandé) ; et les types de résultat réseau actuels (`WorldContainerTransferResult` etc.) sont tous binaires succès/échec, sans champ « quantité réellement transférée » — ajouter une réussite partielle demanderait d'élargir tous ces contrats pour un besoin non démontré.
- **Ordre** : validations réseau (ownership, viewer, portée, fraîcheur source **et** cible le cas échéant) → appel à `InventoryStackTransactions` (préflight destination pur, retrait/décrément source, ajout/fusion destination — section 5) → notification source puis destination (ou l'inverse selon le sens, symétrique à l'existant) → révisions. Les préconditions de fraîcheur sont strictement une responsabilité réseau (section 7) — jamais vérifiées à l'intérieur de la couche de transaction elle-même.
- **Garantie d'atomicité observable** (déjà établie pour les transferts complets, `WORLD_CONTAINER_ARCHITECTURE.md` section « Garanties ») **doit être préservée à l'identique** : aucun snapshot n'est jamais construit entre le début et la fin complète d'un transfert partiel — sans quoi le poids (section 9) ou la quantité pourraient apparaître transitoirement incohérents à un observateur.

---

## 7. Fraîcheur et concurrence — verdict corrigé

**Correction par rapport à la version précédente de ce document.** La version précédente concluait qu'une seule précondition nouvelle (`expectedSourceQuantity`) suffisait, et rejetait `expectedTargetInstanceId`/`expectedTargetQuantity` au motif que le recalcul en direct côté host (traitement séquentiel déjà confirmé, T0-M) suffit à empêcher tout dépassement de `MaxStack`. **Cette conclusion est corrigée** : elle protégeait correctement contre le bug dur (dépassement de capacité), mais pas contre un problème plus large — une opération peut réussir en se fondant sur un état de la cible que le joueur n'a jamais réellement vu, produisant un résultat différent de celui qu'il a décidé. Le recalcul en direct garantit la *sécurité* de l'état final, pas le *déterminisme* attendu par le joueur qui a initié la requête.

### Le scénario qui invalide la conclusion précédente

```text
La cible visible par le client contient 4/10.
Le joueur demande d'y transférer 6.
Une autre requête ajoute 2 à cette même cible avant que la première ne soit traitée.
La cible réelle contient désormais 6/10 (capacité restante : 4) au moment où le host traite la première requête.
```

Sans précondition sur la cible, cette requête ne peut alors que soit échouer pour une raison que le client ne peut pas distinguer d'un problème de capacité générique, soit (si on avait retenu une réduction silencieuse — explicitement rejetée section 6.2/6.3) transférer une quantité que le joueur n'a jamais demandée. Aucune des deux issues n'est acceptable : la première prive le joueur d'une information utile (« votre vue de la cible est obsolète » est un message différent de « pas assez de place ») ; la seconde viole directement la règle « aucune réduction silencieuse » déjà retenue par ailleurs dans ce document.

### Préconditions retenues, corrigées

**[RECOMMANDÉ]** Pour toute opération ciblant une pile existante (merge interne ou transfert partiel vers une pile compatible), la requête doit porter au minimum :

```text
SourceInstanceId
ExpectedSourceQuantity

TargetInstanceId
ExpectedTargetQuantity
```

Les préconditions spatiales déjà existantes restent applicables **là où elles le sont déjà aujourd'hui** — c'est-à-dire pour les variantes ciblées par glisser-déposer, exactement comme `TryMoveItemAuthoritative`/`TryTransferItemTo` les utilisent déjà pour la source (`ExpectedSourceX`/`ExpectedSourceY`/`ExpectedSourceRotated`) et pour une destination précise (`TargetX`/`TargetY`/`TargetRotated`, ou l'équivalent déjà utilisé par le code existant) : ni ajoutées ni retirées par cette correction, seulement confirmées comme orthogonales aux préconditions de quantité — une opération ciblée peut avoir besoin des deux familles de préconditions à la fois (position **et** quantité), une opération first-fit n'a jamais besoin de préconditions spatiales (elle n'en a jamais eu).

`TargetInstanceId` n'est pas un champ entièrement nouveau dans son principe — la cible est déjà résolue par identité (`GetPlacement`) dans toute la logique de transfert existante — mais il doit devenir un **paramètre explicite de précondition revalidé avant toute mutation**, au même titre que `SourceInstanceId`, plutôt qu'une simple étape de résolution interne : si la cible a changé d'identité entre-temps (un autre item occupe désormais cette cellule), la requête doit échouer avec une raison distincte (`TargetMismatch`) plutôt que d'être traitée comme une absence pure et simple.

Nouvelles raisons d'échec conceptuelles (noms définitifs ouverts à l'implémentation) :

```text
StaleSourceQuantity   — la quantité source a changé depuis la capture côté client
StaleTargetQuantity   — la quantité cible a changé depuis la capture côté client
TargetMissing         — la cible n'existe plus du tout (placement disparu)
TargetMismatch        — la cible existe mais un autre InstanceId l'occupe désormais
```

Comportement V1 déterministe attendu, tel que documenté par la mission de correction : **la première mutation valide réussit ; la requête concurrente reçoit une erreur de fraîcheur (`StaleSourceQuantity`/`StaleTargetQuantity`/`TargetMismatch` selon le cas) ; le snapshot déjà à jour côté client (reçu après la première mutation) permet de recomposer une nouvelle requête avec les valeurs actuelles.** Aucune fusion automatique sur la base d'un état recalculé en direct sans que le client en ait été informé — la correction retenue ici est précisément d'exiger que le client déclare ce qu'il croyait vrai, pour que le host puisse distinguer « cette requête est fondée sur un état encore actuel » de « cette requête est fondée sur un état déjà dépassé », plutôt que de silencieusement rejouer la requête contre un état différent.

### Destination vide

Pour un transfert partiel vers une zone vide (pas de `TargetInstanceId`, puisqu'il n'y a rien à cibler par identité), la transaction doit **revérifier que la destination est toujours libre au moment de la mutation** — via `CanPlace`/`TryFindFirstFit`, exactement comme aujourd'hui pour un transfert complet. **Une réservation séparée n'est pas nécessaire pour cette V1** dans la mesure où la transaction host reste synchrone et atomique (déjà confirmé empiriquement pour ce projet, T0-M) : la revérification en direct au moment de la mutation suffit, aucun jeton de réservation préalable n'est requis.

### Rôle du `RequestId` — inchangé par cette correction

**RequestId : reste une corrélation UI uniquement, jamais une clé d'idempotence host — décision explicite, confirmée par la mission de correction, pas un oubli.** Comparaison :

- **Option A (préconditions exactes, sans cache host)** — **retenue**. Une requête dupliquée (retry réseau, double clic) revalidée après qu'une première application a déjà changé la `Quantity` source **ou** cible échoue proprement (`StaleSourceQuantity`/`StaleTargetQuantity`/`TargetMismatch` selon le cas) — même niveau de protection déjà accepté pour le cas spatial existant (limite ABA documentée et explicitement tolérée depuis le 2026-07-21, voir `CURRENT_STATE.md`). Étendre cette même tolérance à la quantité, des deux côtés (source et cible), est cohérent, pas un abaissement du niveau de sécurité déjà accepté ailleurs dans ce projet.
- **Option B (cache host de `RequestId`)** — rejetée pour ce jalon. Fermerait le seul angle mort restant (retry exact avant toute autre mutation intercurrente, où les préconditions resteraient valides par coïncidence) au prix d'un état host nouveau (durée de vie du cache, nettoyage) qui n'existe nulle part ailleurs dans le projet, pour un gain marginal sur un risque déjà toléré pour le cas spatial équivalent. **Conservée explicitement comme amélioration future** — un cache borné de `RequestId` déjà traités peut être introduit plus tard si les mécanismes de retry réseau le rendent nécessaire en pratique, mais **ne doit pas être une dépendance du premier jalon**.
- **Option C (jeton de mutation par pile/placement)** — la plus robuste en théorie (un compteur opaque remplacerait les couples `ExpectedSourceQuantity`/`ExpectedTargetQuantity`), mais `InventoryPlacement` est déjà remplacé (jamais muté en place) à chaque déplacement — porter un jeton stable à travers ces remplacements demanderait de le déplacer sur `ItemInstance` elle-même, un changement structurel plus large que ce jalon ne justifie pas.

**Verdict, confirmé** : Option A pour ce jalon — un comportement où une requête dupliquée peut produire « un premier succès, puis une erreur de fraîcheur pour la seconde » est **acceptable pour la V1, à condition qu'il soit explicite et testé** (voir matrice de tests, section 15). Option B/C restent un durcissement différé, à reconsidérer seulement si un incident réel de duplication est un jour observé (aucun ne l'a été, y compris pendant la campagne de concurrence M1 déjà réalisée pour le cas spatial) ou si les mécanismes de retry réseau du projet évoluent au point de le rendre nécessaire.

---

## 8. Révisions

**Aucune nouvelle règle nécessaire — le mécanisme existant généralise déjà correctement.** Règle déjà en vigueur, simplement confirmée applicable sans modification :

- **Un conteneur touché → une révision, une seule fois, seulement après succès complet.** Split/merge dans un même conteneur : un seul conteneur touché → une révision (même si l'opération modifie deux placements en interne — la révision compte des transactions, pas des mutations élémentaires, déjà vrai pour `TryMove` aujourd'hui).
- **Transfert partiel entre deux conteneurs** : deux révisions, une par conteneur, même ordre déjà établi et documenté (source d'abord, destination ensuite, symétrique selon le sens Take/Store) — aucun changement à cet ordre.
- **Échec, à quelque étape que ce soit** : zéro révision, des deux côtés.
- **Rollback** : le patron déjà utilisé partout (`PlayerItemDropComponent.TryRollback`, le filet de sécurité de `WorldContainerComponent`) n'incrémente **jamais** une révision avant la confirmation finale — un rollback n'a donc jamais besoin de « décrémenter » une révision, parce qu'aucune n'a été envoyée avant que l'échec ne soit détecté. **Invariant à préserver explicitement pour les stacks** : ne jamais appeler `NotifyMutated`/`NotifyContentMutated` avant que l'état ne soit définitivement stabilisé des deux côtés d'un transfert partiel.

---

## 9. Poids

**Aucun nouveau mécanisme nécessaire.** `CurrentWeight = Σ Definition.Weight * Quantity` est déjà correct par construction pour toute opération qui respecte l'arithmétique exacte des sections 6/7 (jamais d'arrondi, jamais de clamp silencieux de `amount`) :

- Split/merge dans un même conteneur : poids total du conteneur inchangé par construction (la somme des deux quantités résultantes égale toujours la quantité initiale).
- Transfert partiel : le poids qui quitte la source égale exactement `Definition.Weight * amount`, qui apparaît dans la destination — aucune double comptabilisation possible tant que la garantie « aucun snapshot construit entre le début et la fin d'un transfert » (section 6.3) est respectée, garantie déjà validée pour les transferts complets et à préserver à l'identique.

---

## 10. Équipement — double protection, corrigée

**Correction par rapport à la version précédente de ce document.** La version précédente ne retenait qu'une seule garde (runtime, dans la transaction d'équipement) et rejetait explicitement un validateur de contenu séparé comme spéculatif. **Cette conclusion est corrigée** : les deux niveaux ne sont pas redondants, ils protègent contre deux échecs différents à deux moments différents, et le rejet précédent du niveau contenu se fondait sur une incertitude d'implémentation (« la capacité de l'éditeur à exposer ce hook n'a pas été vérifiée ») qui n'empêche pas de **recommander** le principe — seule sa forme exacte reste ouverte.

**[RECOMMANDÉ]** Conserver la règle de fond `EquipmentSlot != None => MaxStack == 1`, appliquée à **deux niveaux distincts, tous deux retenus** :

### Validation de contenu (nouveau, retenu)

Un validateur ou diagnostic doit signaler comme incohérente toute ressource `.item` telle que `EquipmentSlot != None && MaxStack > 1` — pendant le développement, jamais en runtime de production. Forme exacte **volontairement laissée ouverte** (section 16) : selon les capacités réellement disponibles dans ce projet (non vérifiées par cet audit), cela peut être une erreur de validation d'asset si l'éditeur s&box expose un tel hook pour `GameResource`, un avertissement explicite affiché par un outil de diagnostic déjà existant ou à créer, ou une assertion de contenu déclenchée en environnement de développement (par exemple au chargement de `ResourceLibrary.GetAll<ItemDefinition>()`, déjà utilisé ailleurs dans le projet — `InventoryPage.EnsureDefinitionCache`). **Ne jamais** corriger silencieusement `MaxStack` depuis `EquipmentSlot` (ou l'inverse) dans un setter de `ItemDefinition` — resterait une coercition surprenante pour l'auteur de contenu, sans précédent dans cette classe (les setters existants clampent une valeur contre une constante, jamais contre une autre propriété).

### Validation runtime (déjà retenue, confirmée)

La transaction d'équipement (`TryEquipAuthoritative` ou son futur équivalent) doit toujours refuser **`item.Quantity != 1`** au moment de l'équipement — pas `definition.MaxStack > 1`. Tester la quantité réelle plutôt que la borne maximale est la condition minimale suffisante (un item `MaxStack = 1` a de toute façon toujours `Quantity == 1`, donc ce test couvre aussi ce cas sans branche séparée), et reste correcte même si un futur type de slot avait une sémantique différente d'un « vêtement porté ». Nouvelle `EquipmentFailureReason` dédiée (ex. `NotSingular`), cohérente avec le patron déjà utilisé pour chaque refus de ce composant.

### Pourquoi les deux niveaux ne sont pas redondants

- **La validation de contenu détecte tôt une mauvaise définition** — avant qu'elle n'atteigne jamais une session de jeu réelle, au moment où un auteur de contenu peut encore la corriger sans qu'aucun joueur n'ait été affecté. Elle ne protège rien en runtime : un `.item` déjà mal configuré et déjà chargé continuerait de se comporter de façon incohérente si seule cette couche existait.
- **La validation runtime protège l'état du jeu** — y compris contre une ressource incohérente qui aurait échappé à la validation de contenu (diagnostic non exécuté, ajouté après coup, ou simplement absent tant qu'il n'est pas encore construit), et contre toute façon dont une instance pourrait légitimement se retrouver avec `Quantity > 1` malgré une définition par ailleurs correcte (aucun chemin de ce type n'existe aujourd'hui, mais la garde runtime ne dépend d'aucune hypothèse sur les chemins de code qui pourraient exister plus tard — c'est exactement le principe déjà appliqué systématiquement dans ce projet : chaque `TryXAuthoritative` revalide tout côté host, jamais une convention seulement documentée ou détectée en amont).
- Une ressource incohérente qui échappe aux deux niveaux resterait un item injouable de façon cohérente (une pile équipée), pas un item exploitable pour dupliquer ou casser un état réseau — la garde runtime reste la seule des deux qui soit une exigence de correction, la validation de contenu est une amélioration de qualité de développement.

---

## 11. Pickup et auto-merge

**[RECOMMANDÉ] Option A pour le premier jalon stack : le pickup continue de toujours créer un placement distinct, comportement inchangé.** L'auto-merge (**Option C préférée à B** — fusionner dans au plus une pile compatible, jamais distribuer sur plusieurs) est explicitement reporté à un jalon séparé, ultérieur (Jalon 5, section 14), jamais dans la même branche que le cœur des stacks.

Justification du report, pas seulement de l'ordre :

- Le pickup (`WorldItemPickupComponent`) est l'un des chemins les plus anciens et les plus testés du projet (huit scénarios déjà validés). Le modifier dans le **premier** jalon stack coupleraient la correction d'un système déjà stable à un code neuf et non prouvé — contraire à la règle du projet de signaler plutôt qu'effectuer silencieusement une modification connexe hors périmètre strict (`core-safety.md`).
- Construire l'auto-merge (Option B ou C) exige exactement les mêmes primitives que la section 5 propose de toute façon (split/merge) — ce n'est donc pas du travail perdu à différer, seulement un site d'appel de plus, à brancher une fois ces primitives déjà éprouvées séparément par des jalons plus petits et plus faciles à valider isolément.
- Atomicité non triviale : un pickup qui distribuerait une quantité entrante sur plusieurs piles compatibles **et** un nouveau placement pour le reste devrait rester tout-ou-rien (comme le pickup actuel : rien n'est détruit avant confirmation) — ceci exige un préflight à travers potentiellement plusieurs cibles avant de committer quoi que ce soit, non conçu par cet audit, explicitement hors périmètre du premier jalon.

---

## 12. Drop partiel

Conception uniquement, non implémentée.

- `RequestDrop(string instanceId, int amount, int expectedSourceQuantity)` — étend le point d'entrée existant, précondition de fraîcheur cohérente avec la section 7.
- **Divergence assumée avec le drop complet actuel** : celui-ci réutilise délibérément la *même* `ItemInstance` de bout en bout (`TryInitializeFromInstance`, jamais `CreateNew`) — garantie de continuité stricte d'`InstanceId` déjà validée sur huit cycles pickup/drop. Un drop **partiel** ne peut pas préserver cette continuité par construction (l'instance d'origine reste dans l'inventaire, décrémentée ; ce qui part dans le monde est nécessairement une fraction, donc une nouvelle identité — section 3). Le nouvel objet-monde doit donc passer par le chemin `TryInitializeAuthoritativeNew` (nouvelle instance), pas par `TryInitializeFromInstance` (instance existante) — un embranchement réel dans `WorldItemComponent`, pas un simple paramètre supplémentaire.
- **Ordre recommandé** : clone du prefab → pose d'une **nouvelle** `ItemInstance` (`CreateNew(definition, amount)`) sur le clone, avant spawn (même raison qu'aujourd'hui : bloquer structurellement la création concurrente par `OnStart`) → décrément (jamais retrait complet) de la source → `NetworkSpawn()` → publication réseau → notification.
- **Rollback plus simple que l'existant** : si le spawn réseau échoue après le décrément, il suffit de **rendre la quantité retirée** à la source (`TryAddQuantity`) — jamais besoin de réinsérer un placement entier à sa position d'origine (il n'a jamais quitté la grille), contrairement au rollback du drop complet actuel qui doit recréer tout un `InventoryPlacement`.
- **Jalon séparé, pas le même que le cœur des stacks** — seul chemin de ce jalon qui touche le spawn/despawn réseau d'objets-monde, profil de risque différent des opérations purement en mémoire (split/merge/transfert). Voir section 14, Jalon 4.

---

## 13. UI — recommandation V1, non codée

- **Glisser une pile sur une case vide** : déplace la pile entière (comportement par défaut inchangé).
- **Glisser une pile sur une pile compatible** : fusionne autant que possible, laisse le reste en place si ça ne rentre pas entièrement (section 6.2) — aucune UI supplémentaire nécessaire pour ce geste par défaut.
- **Glisser une pile sur une pile incompatible** : refus, déjà couvert par le mécanisme de validation locale existant (`IsPlayerPlacementValid`/`IsContainerPlacementValid`) — `CanStackWith` faux retombe naturellement sur le rejet de chevauchement déjà en place, aucune nouvelle branche.
- **Split avec modificateur clavier** : **recommandé pour la V1** — une nouvelle action nommée (même patron que `InventoryRotate`, jamais une touche codée en dur), déclenchant « extraire la moitié, arrondie vers le bas, et faire glisser cette moitié ». Règle fixe, pas de saisie de quantité — cohérent avec le principe déjà appliqué à chaque jalon précédent (« surface UI minimale par incrément »).
- **Sélecteur de quantité (fenêtre/slider)** : **explicitement déconseillé pour la V1.** Surface UI et complexité d'état pending significativement plus grandes (au-delà de la machinerie `_pendingByRequestId` déjà substantielle) pour un jalon dont l'objectif principal est de prouver la logique réseau/cœur, pas le confort d'UI. À reporter à un jalon UI dédié, une fois split/merge/transfert déjà prouvés via le geste « split moitié » plus simple.
- **Clic droit** : non retenu pour la V1, même raisonnement — resterait une entrée alternative possible pour « split moitié » plus tard (accessibilité), pas nécessaire pour livrer une V1 fonctionnelle.
- **Bouton « Split » dans le panneau d'actions** : recommandé, même patron que les boutons Drop/Equip/Use déjà existants (« split moitié, first-fit dans le même conteneur »), sans position ciblée — alternative à faible effort au glisser-déposer avec modificateur.
- **Pending sur source ET cible** : **extension réellement nécessaire**, pas juste une réutilisation telle quelle. `IsItemOperationPending`/`_pendingRequestIdByInstanceId` ne verrouillent aujourd'hui que l'`InstanceId` source — un merge introduit un second `InstanceId` pertinent (la cible), qui doit être verrouillé de la même façon pour empêcher une seconde opération locale concurrente de cibler cette même pile avant que le premier résultat ne revienne.
- **Affichage du résultat** : réutilise `PendingOperationKind`/`MarkResultReceived`/`ReconcileSourceDisappearance` tels quels, étendus avec `Split`/`Merge` — pour un merge spécifiquement, la libération de l'opération pending doit attendre la confirmation des **deux** changements observables (quantité source diminuée **et** quantité cible augmentée), même esprit que l'attente actuelle de « disparition de la source » pour Store/Take.

---

## 14. Roadmap proposée

Ordre challengé et confirmé par rapport à la suggestion de la mission — les dépendances techniques réelles (section 5) imposent déjà cet ordre, pas seulement une préférence :

### Jalon 0 — Audit et décisions documentaires
- **Statut : ce document.** Objectif atteint par cette mission. Aucune dépendance.

### Jalon 1 — Stack core pur — **[IMPLÉMENTÉ] et validé le 2026-07-23**
- **Objectif** : trois éléments, pas un seul —
  1. primitives locales dans `InventoryContainer` (décrément déjà couvert par `TryConsume`, plus `TryGrowQuantity` — nom retenu pour la primitive de croissance bornée par `MaxStack`) ;
  2. la couche `InventoryStackTransactions`/`StackTransactionResult`/`StackTransactionFailureReason`, orchestrant split/merge/transfert partiel sur un ou deux `InventoryContainer`, y compris la politique d'identité (section 3) et le rollback ;
  3. un item de test empilable — construit **entièrement en mémoire** dans l'outil de debug (`new ItemDefinition { ItemId = "kodoku.debug.stackable", ... }`), pas comme ressource `.item` (non nécessaire, voir section 17).
- **Dépendances** : Jalon 0.
- **Fichiers/domaines réels** : `Code/Items/Inventory/InventoryContainer.cs` (primitive `TryGrowQuantity`), `Code/Items/Inventory/InventoryStackTransactions.cs`, `StackTransactionResult.cs`, `StackTransactionFailureReason.cs`, `Code/Debug/InventoryStackTransactionsDebugComponent.cs` (outil de test temporaire).
- **Hors périmètre** : tout réseau, tout RPC, toute UI, toute précondition de fraîcheur (section 7 — celles-ci sont une responsabilité réseau, jamais vérifiées par la couche de transaction elle-même). Confirmé respecté : aucun fichier réseau/UI touché.
- **Critères de validation** : harness C# pur, même méthodologie que Tests A-O (`InventoryContainer`/`InventoryStackTransactions` restent tous deux un noyau non networké — une session solo suffit, voir `.claude/rules/multiplayer.md`) — **65 scénarios PASS, exécution réelle en éditeur, 2026-07-24** (régression ×4, split ×16, merge exact ×16, merge jusqu'à capacité ×8, transfert partiel ×12, merge cross-conteneur ×8, séquence combinée d'invariants ×1). Nombre initial de 59, porté à 65 par une relecture technique dédiée du 2026-07-24 ayant ajouté `Reg-04` (`StackTransactionResult.Fail(None, ...)` refusé), `M-15` (`SourceNotStackable` désormais exercé, chemin auparavant non couvert), `M-16` (identifiant source/cible identique mais absent → `SourceNotFound`, pas `SameSourceAndTarget`), `F-07`/`F-08` (`RequestedAmount == 0` sur succès et sur échec pour `TryMergeUntilCapacity`), `T-12` (`targetContainer == null` → `TargetNotFound`).
  - **Convention `RequestedAmount`/`MovedAmount`** (`StackTransactionResult`) : pour `TryMergeExact`/`TrySplit`/`TrySplitAt`/`TryTransferPartialToEmpty`/`TryTransferPartialToEmptyAt`, `RequestedAmount` porte la quantité explicitement fournie par l'appelant, égale à `MovedAmount` en cas de succès. Pour `TryMergeUntilCapacity`, qui ne reçoit aucune quantité explicite (elle calcule `min(source.Quantity, capacité restante)`), `RequestedAmount` vaut **toujours `0`**, succès comme échec — il n'existe pas de valeur métier « demandée » à rapporter pour cette opération ; seul `MovedAmount` porte la quantité réellement déplacée (ou `0` en cas d'échec).
  - **Atomicité après validation finale** : chaque transaction valide désormais l'état des conteneurs affectés (`TryValidateState`) avant toute mutation, puis à nouveau après les deux mutations. Si cette validation finale échoue malgré deux mutations individuellement réussies, un rollback complet est tenté avec les mêmes primitives déjà contrôlées (`TryRemove`/`TryGrowQuantity` pour un transfert partiel ; `TryConsume`/`TryGrowQuantity`/`TryAdd` pour un merge — jamais de nouvelle `ItemInstance`). `StackTransactionFailureReason.InvariantViolation` n'est retourné que si ce rollback réussit et que l'état restauré revalide correctement ; `StackTransactionFailureReason.RollbackFailed` sinon — un `Fail` ne laisse donc plus jamais subsister une mutation observable, y compris sur ce chemin. **Limite assumée, honnêtement déclarée** : ce chemin précis (échec de la seule validation finale après deux mutations individuellement infaillibles) reste non provocable proprement en runtime sans fault injection dédiée — explicitement écartée pour ce jalon. Il est validé par lecture de code, et indirectement par le fait que les primitives de rollback réutilisées (`TryRemove`, `TryGrowQuantity`, `TryConsume`, `TryAdd`) sont elles-mêmes exercées par les 65 scénarios — pas par un test qui force cette branche exacte (même réserve déjà acceptée pour l'atomicité de l'équipement, et pour le rollback de la seconde mutation seule).
- **Risques** : faibles — aucune surface réseau touchée, confirmé.

### Jalon 2 — Split et merge réseau dans l'inventaire joueur (corrigé : préconditions cible incluses)
- **Objectif** : `PlayerInventoryComponent.RequestSplitItem`/`RequestMergeItem` (ou noms équivalents, section 16), appelant `InventoryStackTransactions` — préconditions réseau complètes (`RequestId`, `ExpectedSourceQuantity`, et pour un merge ciblé `TargetInstanceId`/`ExpectedTargetQuantity`, section 7), pending source **et** cible côté UI, révisions, snapshot inchangé dans sa forme (juste des `Quantity` différentes), erreurs de fraîcheur (`StaleSourceQuantity`/`StaleTargetQuantity`/`TargetMismatch`).
- **Dépendances** : Jalon 1 (couche de transaction déjà éprouvée en isolation).
- **Fichiers/domaines** : `Code/Players/Inventory/PlayerInventoryComponent.cs`, `Code/UI/Menu/Pages/InventoryPage.razor` (verrou pending étendu à la cible, section 13).
- **Hors périmètre** : conteneurs du monde, drop, pickup.
- **Critères de validation** : host + client, split/merge dans son propre inventaire, double requête, quantité source obsolète, quantité cible obsolète, cible remplacée par un autre item (`TargetMismatch`) — matrice « Réseau joueur », section 15.
- **Risques** : moyens — première fois qu'une quantité, source **et** cible, devient une précondition réseau.

### Jalon 3 — Transferts partiels joueur ↔ conteneur (corrigé : merge ciblé explicite)
- **Objectif** : `WorldContainerComponent.TryTransferPartialItem`/`TryTransferPartialItemTo` appelant `InventoryStackTransactions` avec deux `InventoryContainer` — destination vide **et** merge ciblé vers une pile compatible (section 6.3), deux révisions (une par conteneur), concurrence source **et** cible.
- **Dépendances** : Jalon 2 (réutilise les mêmes primitives et les mêmes préconditions de quantité, étendues à deux conteneurs).
- **Fichiers/domaines** : `Code/World/Containers/WorldContainerComponent.cs`, `Code/UI/Menu/Pages/InventoryPage.razor` (verrou pending source+cible, déjà étendu au Jalon 2, réutilisé ici).
- **Hors périmètre** : drop, pickup, auto-merge.
- **Critères de validation** : host + deux clients, matrice de la section 15 (sous-ensemble « conteneur multi-viewers », incluant désormais explicitement la concurrence source **et** la concurrence cible), concurrence à deux fenêtres (même méthodologie que M1-N/M1-O).
- **Risques** : moyens-élevés — combine quantité (source et cible), multi-viewer et deux conteneurs simultanément, trois sources de complexité déjà individuellement prouvées mais jamais ensemble.

### Jalon 4 — Drop partiel
- **Objectif** : `PlayerItemDropComponent.RequestDrop` étendu avec une quantité (section 12), embranchement `TryInitializeAuthoritativeNew` pour la fraction déposée.
- **Dépendances** : Jalon 1 (split), Jalon 2 (précondition de quantité déjà éprouvée).
- **Fichiers/domaines** : `Code/Players/Inventory/PlayerItemDropComponent.cs`, `Code/Items/World/WorldItemComponent.cs`.
- **Hors périmètre** : auto-merge au pickup.
- **Critères de validation** : host + client, continuité d'identité de la fraction restante vs. nouvelle identité de la fraction déposée, rollback (plus simple que l'existant, section 12).
- **Risques** : élevés relativement aux autres — seul jalon touchant le spawn/despawn réseau d'objets-monde.

### Jalon 5 — Auto-merge au pickup (si retenu)
- **Objectif** : `WorldItemPickupComponent` tente une fusion (Option C, section 11) avant de créer un nouveau placement.
- **Dépendances** : Jalons 1-3 (primitives déjà éprouvées séparément).
- **Fichiers/domaines** : `Code/Items/Interaction/WorldItemPickupComponent.cs` uniquement.
- **Hors périmètre** : distribution sur plusieurs piles compatibles (réservé à une itération ultérieure si un besoin réel apparaît).
- **Critères de validation** : host + client, non-régression complète des huit scénarios de pickup déjà validés (aucune modification silencieuse de leur comportement pour un item non empilable).
- **Risques** : le seul risque réel est la non-régression d'un chemin déjà stable — à traiter comme la priorité n°1 des tests de ce jalon, pas comme un détail.

**Ordre non challengé par rapport à la mission** — les dépendances techniques réelles imposent déjà exactement cette séquence (chaque jalon réutilise des primitives du précédent), aucune meilleure dépendance identifiée par cet audit.

---

## 15. Matrice de tests proposée

### Core (Jalon 1, solo éditeur)

| # | Scénario | Attendu |
|---|---|---|
| S-01 | Split valide, destination first-fit libre | Deux placements, `InstanceId` distincts, quantités correctes |
| S-02 | Split valide, destination ciblée libre | Idem, position exacte respectée |
| S-03 | Split `amount == Quantity` | Échec `InvalidQuantity`, aucune mutation |
| S-04 | Split `amount <= 0` | Échec `InvalidQuantity` |
| S-05 | Split sans espace disponible | Échec `NoAvailableSpace`, source inchangée |
| S-06 | Split sur `MaxStack == 1` | Échec `InvalidQuantity` (structurel, `Quantity` toujours 1) |
| S-07 | Merge partiel, cible avec place suffisante | Cible augmentée, source décrémentée, deux `InstanceId` survivent |
| S-08 | Merge total, cible avec place suffisante | Cible augmentée, placement source disparaît, `InstanceId` source absent de `Placements` |
| S-09 | Merge, pile incompatible (`ItemId` différent) | Échec, aucune mutation |
| S-10 | Merge, cible déjà pleine (`MaxStack` atteint) | Échec explicite, aucune mutation |
| S-11 | Split puis merge (round-trip), poids conservé dans un même conteneur | `CurrentWeight` avant == après, sur le même `InventoryContainer` |
| S-12 | Absence de mutation sur tout chemin d'échec (S-03 à S-10) | `TryValidateState` reste cohérent après chaque échec |
| S-13 | Invariants internes après une longue séquence split/merge/move | `TryValidateState` toujours vrai |
| S-14 | Split : `InstanceId` de la pile extraite distinct de celui de la source | Deux GUID différents, source conserve le sien (section 3) |
| S-15 | Merge partiel : les deux `InstanceId` (source et cible) survivent | Source toujours présente, quantité réduite ; cible toujours présente, quantité augmentée |
| S-16 | Merge total : `InstanceId` de la source disparaît | Absent de `Placements`/`_byInstanceId` après l'opération ; cible conserve le sien |
| S-17 | Rollback complet d'une opération à deux conteneurs interrompue avant sa fin | État des deux `InventoryContainer` strictement identique à avant la tentative (aucune mutation partielle observable) |
| S-18 | Poids déplacé exactement entre deux conteneurs (transfert partiel simulé en C# pur, sans réseau) | `Definition.Weight * amount` quitte la source, apparaît dans la destination, aucune double comptabilisation transitoire |

### Réseau joueur (Jalon 2, host + client)

| # | Scénario | Attendu |
|---|---|---|
| P-01 | Split host, dans son propre inventaire | Une révision, snapshot à jour, deux entrées |
| P-02 | Split client | Idem, snapshot uniquement chez le propriétaire |
| P-03 | Merge host/client | Une révision, une entrée absorbée |
| P-04 | Double requête split identique (même `RequestId`, retry) | Première réussit, seconde `StaleSourceQuantity` (quantité déjà changée) — comportement V1 acceptable, voir section 7 |
| P-05 | `ExpectedSourceQuantity` obsolète (une autre opération a changé la quantité entre-temps) | `StaleSourceQuantity`, aucune mutation |
| P-06 | Destination obsolète pour un split ciblé (cellule occupée entre-temps) | Échec de placement, aucune mutation |
| P-07 | Ownership rejeté (cible le pawn d'un autre joueur) | Refus silencieux, même patron que l'existant |
| P-08 | Late join après un split déjà effectué | Snapshot initial reflète l'état déjà splitté |
| P-09 | **Concurrence source** — pile initiale à 10 ; client A demande de retirer 4 avec `ExpectedSourceQuantity = 10` ; client B demande de retirer 3 avec `ExpectedSourceQuantity = 10`, avant que la première ne soit traitée | Une seule requête réussit (la première traitée par le host, séquentiel) ; l'autre reçoit `StaleSourceQuantity` ; aucune duplication, aucune perte, quantité finale jamais négative |
| P-10 | **Concurrence cible** — pile cible initiale à 4/10 ; deux merges concurrents (sources différentes, 6 et 3) ciblent cette même pile avec `TargetInstanceId` identique et `ExpectedTargetQuantity = 4` chacun | Une seule requête réussit ; l'autre reçoit `StaleTargetQuantity` ; aucun transfert fondé silencieusement sur la nouvelle capacité recalculée en direct |
| P-11 | Cible remplacée par un autre item entre la capture cliente et le traitement host | `TargetMismatch`, aucune mutation |

### Conteneur multi-viewers (Jalon 3, host + deux clients)

| # | Scénario | Attendu |
|---|---|---|
| C-01 | Take partiel, destination joueur vide | Nouvelle pile chez le joueur, conteneur décrémenté |
| C-02 | Store partiel, destination conteneur vide | Symétrique de C-01 |
| C-03 | Take/Store partiel, destination compatible (merge cross-conteneur) | Fusion exacte, échec net si insuffisant (pas de réduction silencieuse) |
| C-04 | Take/Store partiel, destination pleine | Échec, source inchangée des deux côtés |
| C-05 | **Concurrence source** — pile source à 10 dans le conteneur ; viewer A demande de prendre 4 avec `ExpectedSourceQuantity = 10` ; viewer B demande de prendre 3 avec `ExpectedSourceQuantity = 10` | Une seule requête réussit (host séquentiel) ; l'autre reçoit `StaleSourceQuantity` ; quantités jamais négatives, aucune duplication, aucune perte |
| C-06 | **Concurrence cible** — pile cible à 4/10 (dans le conteneur ou dans l'inventaire selon le sens testé) ; deux sources distinctes (6 et 3) fusionnées vers cette même cible, chacune avec `TargetInstanceId` identique et `ExpectedTargetQuantity = 4` | Une seule requête réussit ; l'autre reçoit `StaleTargetQuantity` ; jamais de dépassement de `MaxStack`, jamais de transfert fondé silencieusement sur la capacité déjà changée |
| C-07 | **Destination vide concurrente** — deux requêtes (Take ou Store) ciblent la même cellule vide au même moment | Une seule transaction réussit ; l'autre échoue car la destination n'est plus libre au moment de sa mutation (revérifiée en direct, section 7) ; aucun chevauchement de placements |
| C-08 | **Requête dupliquée** — même `RequestId` et mêmes préconditions reçus deux fois pour un Take/Store partiel | Première requête réussie ; seconde rejetée par fraîcheur (`StaleSourceQuantity`/`StaleTargetQuantity` selon le cas) ; une seule mutation réelle observée côté hôte — un replay exact du premier résultat nécessiterait un cache d'idempotence futur (section 7, Option B, non requis pour ce jalon) |
| C-09 | Sortie de portée pendant un transfert partiel en cours | Refus `OutOfRange`, viewer retiré, aucune mutation partielle |
| C-10 | Fermeture de session pendant une requête en vol | Requête déjà en traitement va à son terme (host séquentiel) ; toute requête ultérieure `NotViewer` |
| C-11 | Late join après plusieurs merges/splits déjà appliqués au conteneur | Snapshot initial cohérent, aucune duplication |
| C-12 | Poids observé côté client jamais incohérent pendant un transfert partiel | Aucun snapshot intermédiaire, `CurrentWeight` toujours correct avant/après |

### Monde (Jalon 4, host + client)

| # | Scénario | Attendu |
|---|---|---|
| W-01 | Drop partiel, quantité restante > 0 | Nouvelle `ItemInstance`/GUID au sol, source décrémentée, ancien `InstanceId` conservé en inventaire |
| W-02 | Drop partiel avec rollback (spawn échoué) | Quantité rendue à la source (`TryAddQuantity`), aucun résidu au sol |
| W-03 | Pickup de la pile partiellement déposée | Nouvelle pile en inventaire, `InstanceId` distinct de celui resté en inventaire d'origine |
| W-04 | Quantité réseau conservée sur plusieurs cycles drop partiel/pickup | Somme des quantités inventaire+monde toujours égale au total initial |
| W-05 | Double requête de drop partiel | Première réussit, seconde `StaleSourceQuantity` |

### Équipement (validation de contenu + runtime, section 10)

| # | Scénario | Attendu |
|---|---|---|
| E-01 | Ressource `.item` avec `EquipmentSlot != None` et `MaxStack > 1` | Signalée par la validation de contenu (diagnostic de développement) — forme exacte ouverte, section 16 |
| E-02 | Tentative d'équiper une `ItemInstance` avec `Quantity > 1` | Refusée par la transaction d'équipement (`EquipmentFailureReason.NotSingular` ou équivalent), aucune mutation |
| E-03 | Tentative d'équiper une `ItemInstance` avec `Quantity == 1`, toutes les autres règles satisfaites | Acceptée, comportement inchangé par rapport à l'équipement déjà validé (dix scénarios A-J existants) |

**Scénarios explicitement différés, pas ignorés** : auto-merge au pickup (Jalon 5, matrice propre à définir à ce moment — non produite ici, prématurée) ; sélecteur de quantité UI (hors périmètre réseau, pas de matrice réseau associée) ; conteneurs imbriqués, conteneurs verrouillés, durabilité par instance (aucun n'a de conception actée, voir section 16).

---

## 16. Questions restant réellement ouvertes

Mise à jour par la mission de correction : deux questions précédemment ouvertes sont désormais tranchées et retirées de cette liste — la nécessité de préconditions de fraîcheur côté cible (`TargetInstanceId`/`ExpectedTargetQuantity`, section 7) et la double protection équipement (contenu + runtime, section 10). Restent réellement ouvertes :

- **[OUVERT]** Nom exact des nouvelles méthodes/RPC et des types de la couche de transaction (`RequestSplitItem` vs `RequestSplit`, `InventoryStackTransactions` vs un autre nom, noms définitifs de `StaleSourceQuantity`/`StaleTargetQuantity`/`TargetMissing`/`TargetMismatch`, etc.) — détails d'implémentation, à trancher au moment des Jalons 1-2, pas ici.
- **[OUVERT]** Forme exacte de la validation de contenu pour l'équipement (section 10) — erreur d'import d'asset, outil de diagnostic dédié, ou assertion de développement au chargement : dépend de capacités du projet/de l'éditeur s&box non vérifiées par cet audit.
- **[OUVERT]** Interaction UI du sélecteur de quantité (fenêtre/slider) — explicitement hors périmètre V1 (section 13), sa conception détaillée reste à faire pour un jalon UI dédié ultérieur.
- **[OUVERT]** Faut-il un bouton « Merge » explicite dans le panneau d'actions, en plus du glisser-déposer ? Non traité par cet audit (pas demandé explicitement par la mission), à trancher au Jalon 3 avec l'UI.
- **[OUVERT]** Comportement exact si `CanRotate` change la géométrie d'un split ciblé en cours de drag (déjà un cas géré pour le déplacement simple via `Input.Pressed("InventoryRotate")` — un split devra probablement réutiliser exactement ce mécanisme, non vérifié en détail ici).
- **[OUVERT]** Éventuelle extraction future de `WorldContainerComponent` en `partial class` si sa taille continue de croître après les Jalons 3-5 — signalé en section 5 comme un point à réévaluer, pas une décision. Moins pressant depuis la correction de la section 5 (la logique de quantité vit désormais dans la couche de transaction, pas dans `WorldContainerComponent` lui-même), mais pas retiré pour autant : le fichier reste volumineux pour ses seules responsabilités réseau.
- **[OUVERT]** Durabilité ou tout autre état propre à une instance, et son impact sur `CanStackWith` — explicitement hors périmètre tant qu'aucun cas d'usage réel n'existe (section 6.2).
- **[OUVERT]** Poids maximal bloquant (roadmap étape 8, distinct de ce jalon) — son interaction avec un split/merge n'a pas été analysée ici, à reprendre quand ce jalon sera conçu.
- **[OUVERT]** Cache d'idempotence futur (Option B, section 7) — explicitement non requis pour le premier jalon ; à réévaluer seulement si les mécanismes de retry réseau du projet évoluent au point de le rendre nécessaire, ou si un incident réel de duplication est observé.

---

## 17. Item de validation futur (caractéristiques proposées, non créé)

Conformément à la mission — **non créé pendant cet audit**.

```text
ItemId       : kodoku.debug.stackable
DisplayName  : Debug Stackable
MaxStack     : 10
GridWidth    : 1
GridHeight   : 1
CanRotate    : false
Weight       : 0.25
EquipmentSlot: None
ThirstRestoreAmount : 0 (aucun effet de consommation)
```

Ne pas réutiliser `water_bottle.item` pour ce rôle (mission, section « Item de validation futur ») — son état interne futur (contenu restant, etc.) pourrait rendre son empilement ambigu au moment où il serait effectivement rendu empilable.

---

## 18. Pourquoi un document de conception plutôt qu'un ADR

Ce jalon touche une dizaine de décisions distinctes réparties sur au moins cinq fichiers/composants existants (identité, architecture du cœur, concurrence, révisions, sémantique de six opérations différentes, UI, roadmap) — un périmètre comparable à celui qui a produit [WORLD_CONTAINER_ARCHITECTURE.md](WORLD_CONTAINER_ARCHITECTURE.md) (un document de conception dédié, pas un ADR) avant l'implémentation des conteneurs du monde, pas à celui d'un ADR existant de ce projet (ADR-0002, ADR-0003, ADR-0005, ADR-0006 portent chacun une seule décision durable et binaire). Suivre le même précédent de format pour un périmètre de taille comparable.

Si une décision individuelle de ce document s'avère, après implémentation, suffisamment durable et transversale pour mériter sa propre traçabilité (le candidat le plus probable : la politique d'identité de la section 3), un ADR dédié pourra être créé à ce moment-là, référençant ce document plutôt que le dupliquer — non fait maintenant, faute de motif suffisant tant qu'aucune implémentation n'existe pour confirmer que cette politique tient dans la pratique.
