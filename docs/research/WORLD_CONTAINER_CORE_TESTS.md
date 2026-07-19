# World Container Core V1 — rapport historique de validation (C0-A à C0-J)

**Statut : noyau implémenté et validé par test runtime réel, host + clients distants, matrice C0-A à C0-J intégralement exécutée.** Ce document est le rapport permanent de cette validation (branche `feature/world-container-core`, partie de `main`/`34fac2fcc9041f52a8478c3fc3b3e7a8a65b2a5a`). Les outils TEMP utilisés pour l'exécuter ont été supprimés après validation — ce document, comme [docs/research/WORLD_CONTAINER_MULTIVIEWER_SPIKE.md](WORLD_CONTAINER_MULTIVIEWER_SPIKE.md) avant lui, reste la trace permanente.

Le contexte de conception complet (architecture visée, décisions V1, transport multi-viewer validé par le Spike S0) vit dans [docs/architecture/WORLD_CONTAINER_ARCHITECTURE.md](../architecture/WORLD_CONTAINER_ARCHITECTURE.md) et [ADR-0006](../decisions/ADR-0006-WORLD-CONTAINER-VIEWER-TRANSPORT.md).

## Périmètre validé par cette matrice

Inclus et validé : `WorldContainerComponent` canonique host-only, ouverture/fermeture de session, invalidation ciblée, snapshot initial et resynchronisation explicite, révision de contenu, diffusion multi-viewer après mutation, rejet d'un snapshot obsolète, nettoyage à la déconnexion, validation de distance (y compris hors portée en cours de session), ouverture dupliquée idempotente, arrivée d'une nouvelle connexion (late join).

Exclu (toujours hors périmètre de cette V1) : tout transfert d'item (conteneur → joueur, joueur → conteneur), interaction monde (`Component.IPressable`), persistance, `StableContainerId`, prefab `Wooden_Crate` de production, UI de production.

## Fichiers de production

- `Code/World/Containers/WorldContainerComponent.cs`
- `Code/World/Containers/WorldContainerSnapshotEntry.cs`
- `Code/World/Containers/WorldContainerFailureReason.cs`
- `Code/World/Containers/WorldContainerOperationResult.cs`

Aucun fichier existant modifié (`InventoryContainer`, `PlayerInventoryComponent`, `KodokuPlayerComponent`, systèmes pickup/drop/use/equipment, `GameplayTest.scene` — tous inchangés sur toute la durée de cette branche).

## Outils TEMP utilisés (supprimés après validation)

- `Code/Debug/WorldContainerDebugTools.cs` — actions client (`OpenLocalContainer`/`CloseLocalContainer`/`RequestResync`), actions host-only (`HostAddTestWaterBottle`/`HostClearTestContents`), et pour C0-I une méthode dédiée (`RequestInjectStaleSnapshot`, `[Rpc.Host]`) injectant un snapshot volontairement obsolète (révision `HostRevision - 1`, payload différent) via `WorldContainerComponent.ReceiveSnapshot` déjà publique — aucune méthode métier n'a jamais été ajoutée au composant de production pour ce besoin.
- `Code/UI/Debug/WorldContainerDebugPanel.razor` (+ `.razor.scss`) — panneau TEMP affichant l'état local et exposant les actions ci-dessus.
- `Assets/scenes/Tests/WorldContainerCoreTest.scene` — scène isolée (host, spawn points, sol, un `WorldContainer_Test` portant `WorldContainerComponent` + les outils TEMP).

Ces quatre fichiers ont été supprimés après l'exécution complète de la matrice. Ils n'étaient jamais suivis par Git — leur suppression n'apparaît donc pas comme un diff dans l'historique. Une recherche exhaustive (`WorldContainerDebugTools`, `WorldContainerDebugPanel`, `WorldContainerCoreTest`, `StaleSnapshot`, etc.) confirme zéro occurrence restante dans `Code/` et `Assets/`.

## Matrice C0-A à C0-J — résultats

Exécutée sur plusieurs sessions réelles (host + un ou deux clients distants), identités de connexion variées (Jo/Barney, Jo/Smithers/Wiggum, Jo/Maggie/Krusty, Jo/Krusty/Milhouse/Maggie), 2026-07-19.

### C0-A — Ouverture par le host — PASS (réserves mineures)

Confirmé : GameObject de scène network-actif (RPC atteint le host sans `NetworkSpawn()` explicite — mode « Network Snapshot »), ouverture host réussie, snapshot initial `revision=0, entries=0, dims=4x4` reçu, `ViewerCount` 0→1, `HostRevision` restée à 0, aucune mutation d'inventaire joueur, un seul GameObject porte `WorldContainerComponent`.

**Réserves** : la distance réelle pawn→conteneur n'a pas été mesurée précisément (l'ouverture ayant réussi, elle a été acceptée indirectement, pas chiffrée) ; l'absence de Network Owner joueur sur le GameObject conteneur est appuyée par la configuration réseau observée (`Owner Transfer: Takeover`, `Orphaned Mode: Host`) et par l'absence de tout chemin de code qui l'assignerait, mais n'a pas été lue directement comme une valeur runtime affichée (« Owner: none »).

### C0-B — Ouverture par un client distant — PASS

Validé sur plusieurs sessions et plusieurs clients (Smithers, Krusty, Maggie). `ViewerAdded` puis `Snapshot Send` ciblé uniquement à l'appelant puis `Open][Success]`, `ViewerCount` incrémenté exactement de 1. Le host ne reçoit jamais de copie supplémentaire. Un non-viewer témoin présent pendant l'ouverture (ex. Wiggum pendant l'ouverture de Smithers) n'a reçu strictement aucune ligne `[WorldContainer]` avant sa propre ouverture.

### C0-C — Mutation debug et diffusion multi-viewer — PASS

Plusieurs mutations distinctes exécutées au fil des sessions (ajout de bouteille, vidage, nouvel ajout) — **chacune a produit une seule incrémentation de révision**, jamais une révision partagée entre plusieurs mutations. Exemple observé : `HostRevision` 0→1→2→3 sur une même session, à trois appels `Host Add Test Water Bottle` distincts, chacun suivi d'exactement une diffusion. La diffusion à plusieurs viewers simultanés a été confirmée directement (`revision=2, entries=0, dims=4x4, viewers=2` reçu au même contenu par le host et par un client distant au même instant). Les non-viewers présents au moment d'une mutation n'ont jamais reçu la diffusion. Aucun inventaire joueur affecté par ces mutations (aucun `[InventorySync]` corrélé).

### C0-D — Resynchronisation — PASS

Validé sur plusieurs clients. `RequestSnapshot()` renvoie la révision courante inchangée, ciblée au seul demandeur, avec les mêmes entrées (même `InstanceId`) que le cache déjà détenu. `HostRevision` jamais modifiée par une resync.

### C0-E — Fermeture volontaire — PASS

Ordre confirmé exact : invalidation ciblée envoyée **avant** le retrait de la collection de viewers (conforme au Spike S0-D). Cache local vidé côté client (`LocalRevision=-1`, dimensions à 0×0, session fermée). `ViewerCount` décrémenté de 1 exactement. Contenu et révision canoniques inchangés. Les autres viewers, s'il y en avait, sont restés inchangés (aucune invalidation reçue par eux).

### C0-F — Requête hors distance — PASS

Scénario exact validé : un client déjà viewer s'éloigne, clique Resync ; `RequestSnapshot` atteint le host, la distance est revalidée et rejetée (`OutOfRange`), une invalidation ciblée est envoyée **avant** le retrait du viewer de la collection (même ordre que la fermeture volontaire), aucune donnée de snapshot n'est envoyée. `ViewerCount` décrémenté de 1. `HostRevision` et contenu canonique inchangés. Confirmé également, dans une session antérieure, que la même validation de distance rejette proprement une tentative d'**ouverture** hors portée (`Open][Fail] : OutOfRange`, deux fois consécutives), sans effet de bord — même helper de distance (`IsWithinRange`) exercé par les deux chemins.

### C0-G — Déconnexion d'un viewer — PASS

**Trois observations indépendantes**, sur trois connexions différentes (Krusty puis Maggie dans une session, Milhouse dans une autre) : `Component.INetworkListener.OnDisconnected` s'exécute immédiatement à la déconnexion d'un viewer actif, produit le log `[WorldContainer][DisconnectedCleanup]`, et `ViewerCount` est corrigé exactement d'une unité à chaque fois. Aucune tentative d'envoi vers la connexion morte. Aucune exception applicative (seule la `TcpChannel`/`SocketException 10054` déjà documentée comme bruit moteur bénin apparaît, jamais associée à une erreur `WorldContainer`).

### C0-H — Late join — PASS

Une nouvelle `Connection` (après le départ propre d'un précédent client) n'est jamais automatiquement ajoutée aux viewers, ne reçoit aucun snapshot ni historique avant son ouverture explicite, et reçoit — dès l'ouverture — uniquement la révision courante (jamais `revision=0` ni un état vide par défaut). Aucune réutilisation ni résidu de l'ancienne connexion dans `_viewers` (le compte de viewers converge exactement, `Total` correspondant toujours à la somme des connexions réellement présentes). Ce comportement a été confirmé y compris dans une session **déjà multi-viewer** (late join alors qu'un autre client était déjà viewer actif) — le nouveau venu rejoint le compte correct sans perturber le viewer déjà présent.

### C0-I — Rejet d'un snapshot obsolète — PASS

Preuves exactes : `HostRevision=1`, `staleRevision=0` (calculée par le helper TEMP comme `HostRevision - 1`), payload volontairement différent du cache réel (entrées vide, dimensions 0×0), injecté uniquement chez l'appelant via `Rpc.FilterInclude(caller)` + la RPC de réception de production déjà existante (`WorldContainerComponent.ReceiveSnapshot`, jamais modifiée pour ce test). Côté client, le log de production `[WorldContainer][Snapshot][Ignored] : reçu revision=0, déjà à revision=1 — ignoré` confirme le rejet **avant** toute affectation — la garde `if (revision < LocalRevision) { log; return; }` retourne avant d'écrire `LocalRevision`/`LocalEntries`/`LocalWidth`/`LocalHeight`/`IsLocalSessionOpen`, donc le cache courant (y compris la bouteille alors présente) reste garanti inchangé par construction du code, pas seulement par observation. `HostRevision` et l'état canonique host restent inchangés (aucun `NotifyContentMutated` déclenché par ce test).

InstanceId observé pendant ce test précis (trace de session uniquement, jamais une valeur codée en dur) : `199a6a40-a109-4c2d-a5ce-6b862af0a97c`.

### C0-J — Ouverture dupliquée — PASS

Un second `RequestOpen()` par un viewer déjà présent est un succès idempotent : `ViewerCount` reste inchangé (aucun doublon dans le `HashSet<Connection>`), un snapshot courant est simplement renvoyé à l'appelant, `HostRevision` inchangée. Comportement confirmé conforme au choix de conception (pas de raison d'échec `AlreadyViewer` — une seconde ouverture n'est jamais un refus).

## Résultat transversal (toutes sessions confondues)

- Aucun transfert d'item n'a été testé ni implémenté — hors périmètre strict de cette V1.
- Aucun non-viewer n'a jamais reçu de contenu du conteneur, sur l'ensemble des sessions.
- Aucune duplication de snapshot, aucun doublon de viewer observé.
- `HostRevision` n'a évolué que lors de mutations debug explicites (`Host Add Test Water Bottle`/`Host Clear Test Contents`) — jamais lors d'une ouverture, fermeture, resync, invalidation ou déconnexion.
- Aucun inventaire joueur ni aucune valeur de vitals n'a été affecté par une action sur le conteneur, sur l'ensemble des sessions.
- Aucune exception RPC ni erreur de sérialisation liée à `WorldContainer` — seul le bruit moteur déjà documenté (`TcpChannel`/`SocketException 10054` sur déconnexion brutale) est apparu, jamais corrélé à une anomalie applicative.
- Aucune fuite de viewer constatée sur des déconnexions propres (voir C0-G) ; une session antérieure avait laissé un doute sur ce point après une manipulation confuse de fenêtres client, entièrement levé par les observations propres et répétées de C0-G et C0-H.
- Tests réalisés sur trois sessions distinctes et sept identités de connexion au total.

## Build

`dotnet build Code/kodoku_test.csproj --no-incremental` : **0 erreur**, exactement les deux warnings `CS1574` préexistants (`KodokuPlayerComponent.cs`, non liés à cette branche) — vérifié après chaque ajout de code de production et après le nettoyage final des outils TEMP.

## Ce que cette validation ne couvre pas

Ce document valide le **noyau** (sessions, snapshots, révision, distance, déconnexion) — pas le système complet de conteneurs du monde. Restent non implémentés et non testés : transfert conteneur→joueur, transfert joueur→conteneur, interaction monde de production, prefab `Wooden_Crate`, UI de production, toute décision de persistance. Voir [docs/status/ROADMAP.md](../status/ROADMAP.md) pour la prochaine étape et [docs/status/OPEN_QUESTIONS.md](../status/OPEN_QUESTIONS.md) pour ce qui reste explicitement ouvert.
