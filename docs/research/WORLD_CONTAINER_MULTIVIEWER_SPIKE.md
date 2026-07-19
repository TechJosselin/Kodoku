# Spike S0 — Transport RPC multi-viewer pour les conteneurs du monde

Statut : **exécuté en runtime réel avec un host et deux clients distants, PASS avec réserves non
bloquantes** (voir section 10). Les résultats runtime de ce document ont été reportés dans
[WORLD_CONTAINER_ARCHITECTURE.md](../architecture/WORLD_CONTAINER_ARCHITECTURE.md) (section 7) et
dans [ADR-0006](../decisions/ADR-0006-WORLD-CONTAINER-VIEWER-TRANSPORT.md) — voir
[documentation.md](../../.claude/rules/documentation.md) sur la distinction décision validée /
recommandation / question ouverte. Ce document reste le rapport historique permanent du spike ;
les fichiers de code temporaires qu'il décrit (section 4) ont été supprimés après validation.

Branche : `spike/world-container-multiviewer-rpc`, partie de `d55526b20b7f0c54508efd676629b1da301d8964`.
Ce spike est isolé et temporaire — non destiné à être fusionné tel quel dans `main`.

## 1. Objectif

Avant toute implémentation de `WorldContainerComponent`, vérifier en runtime réel (host + au moins
deux clients) qu'un host peut :

- envoyer un snapshot au client A uniquement ;
- puis à A et B ;
- puis uniquement à B ;
- sans qu'un non-viewer ne reçoive le message ;
- avec un nettoyage correct après fermeture ciblée et après déconnexion.

## 2. API réseau trouvées (audit statique)

Source : XML doc + réflexion (`System.Reflection.MetadataLoadContext`, sans exécution) sur
`Sandbox.Engine.dll` installé localement (`E:\SteamLibrary\steamapps\common\sbox\bin\managed\`,
build utilisé par ce projet — voir CLAUDE.md sur la priorité de l'assembly locale vis-à-vis de la
documentation en ligne). Aucun exemple d'usage réel de `FilterInclude`/`FilterExclude` trouvé nulle
part dans `addons/` du SDK installé (`grep` sur tous les `.cs` du dossier `sbox/` : 0 résultat) —
seule la signature et le résumé XML font foi ici, le comportement précis reste à confirmer en
runtime (section 4).

### `Sandbox.Rpc` (classe statique)

| Membre | Signature | Résumé XML |
|---|---|---|
| `Rpc.Caller` | `Connection get_Caller()` | La `Connection` qui appelle la méthode courante. |
| `Rpc.CallerId` | `Guid get_CallerId()` | Id de `Rpc.Caller`. |
| `Rpc.Calling` | `bool get_Calling()` | Vrai si on est actuellement appelé par une `Connection` distante. |
| `Rpc.FilterInclude(Connection)` | `IDisposable FilterInclude(Connection)` | Restreint les destinataires de toute RPC appelée dans ce scope à **cette seule** connexion. |
| `Rpc.FilterInclude(IEnumerable<Connection>)` | `IDisposable FilterInclude(IEnumerable<Connection>)` | Restreint à cet ensemble de connexions. |
| `Rpc.FilterInclude(Predicate<Connection>)` | `IDisposable FilterInclude(Predicate<Connection>)` | Restreint aux connexions qui satisfont le prédicat. |
| `Rpc.FilterExclude(Connection\|IEnumerable\|Predicate)` | `IDisposable FilterExclude(...)` | Symétrique — exclut plutôt qu'inclut. |
| `Rpc.Resume(WrappedMethod)` | — | « Si l'appelant RPC est notre connexion locale, désactive tout filtre actif puis le restaure ensuite » — implique que le filtre est un état de portée (scope), pas un simple paramètre d'appel. |

**Point clé confirmé par réflexion** (pas seulement par le XML) : `FilterInclude`/`FilterExclude`
retournent `IDisposable` — confirme le patron d'usage `using ( Rpc.FilterInclude( ... ) ) { MaRpc(); }`,
cohérent avec le résumé XML « Filter the recipients of any Rpc called in this scope ».

Non déterminé par l'audit statique (aucune implémentation IL inspectée, aucun exemple dans le SDK) :

- Si l'appelant (le host) reçoit lui-même le message quand il n'est pas explicitement inclus dans le
  filtre.
- Le comportement de deux `FilterInclude` imbriqués (intersection ? le plus interne gagne ? erreur ?).
- Si un filtre vide (liste de viewers vide) envoie à personne ou lève une exception.
- Le comportement exact si une `Connection` du filtre s'est déconnectée entre la construction du
  filtre et l'envoi effectif.

Ces points sont exactement l'objet de la matrice runtime (section 4).

### Attributs RPC (`Sandbox.Rpc.BroadcastAttribute` / `HostAttribute` / `OwnerAttribute`)

- `[Rpc.Broadcast]` — « sera appelée pour tout le monde » (permission par défaut : anyone).
- `[Rpc.Host]` — « sera appelée uniquement sur le host » (déjà utilisé dans le projet, ex.
  `WorldItemPickupComponent.RequestPickup`).
- `[Rpc.Owner]` — « sera appelée uniquement sur le owner de cet objet » (déjà utilisé, ex.
  `PlayerInventoryComponent.ReceiveSnapshot`).
- Les trois attributs ont un constructeur `(NetFlags flags = <valeur par défaut>)` — confirmé par
  réflexion (`RawDefaultValue` renvoie une valeur non nulle pour les trois), ce qui explique
  pourquoi le projet utilise déjà `[Rpc.Host]`/`[Rpc.Owner]` sans argument explicite.

`[Rpc.Owner]` ne convient pas à ce spike : il cible un point fixe (le owner du GameObject), pas un
ensemble dynamique et arbitraire de connexions sans rapport avec l'ownership. D'où le choix de
`[Rpc.Broadcast]` combiné à `Rpc.FilterInclude`/`FilterExclude` pour restreindre dynamiquement la
portée.

### `Sandbox.Connection`

Membres statiques utiles pour l'identification (host UI, section 6 du plan) :

- `Connection.All` — `IReadOnlyList<Connection>`, toutes les connexions actuellement connues.
- `Connection.Host` — la `Connection` du host.
- `Connection.Local` — la `Connection` locale à cette instance.
- `Connection.Find(Guid)` — résolution par id.
- Par instance : `DisplayName`, `Id`, `IsActive`, `Name`, `Address`.

Aucun événement (`event`) exposé directement sur `Connection` pour connexion/déconnexion.

### `Sandbox.Component.INetworkListener` — mécanisme de nettoyage à la déconnexion

Trouvé dans `Sandbox.Engine.xml` : une interface de composant avec :

- `OnConnected(Connection)` — « appelé quand quelqu'un rejoint le serveur. Uniquement côté host. »
- `OnDisconnected(Connection)` — « appelé quand quelqu'un quitte le serveur. Uniquement côté host. »
- `OnActive(Connection)` — « appelé quand quelqu'un est chargé et entré en jeu. Uniquement côté host. »
- `OnBecameHost(Connection)` — transfert d'autorité host.
- `AcceptConnection(Connection, out string reason)` — accepter/refuser une connexion.

C'est un mécanisme documenté et discret (pas un événement C# classique), à implémenter en faisant
hériter le composant de `Component.INetworkListener` explicitement. Choisi comme **stratégie
principale** de nettoyage à la déconnexion (voir section 5) plutôt qu'une pure purge défensive —
mais reste à confirmer par test réel (S0-G) que `OnDisconnected` se déclenche bien et à temps.

## 3. Méthode de ciblage choisie et alternatives rejetées

**Choisi — Candidat A (broadcast filtré)** :

```csharp
using ( Rpc.FilterInclude( _viewers ) )
{
    ReceiveSnapshot( _hostSequence, GameObject.Name ); // [Rpc.Broadcast]
}
```

Raisons : seule méthode pour laquelle une signature *publique et documentée* existe dans l'assembly
installée ; correspond exactement au vocabulaire du plan (« Candidat A ») ; un seul appel RPC par
envoi, quel que soit le nombre de viewers (le filtre restreint le *transport*, pas le nombre
d'invocations logiques côté appelant).

**Rejeté pour cette passe — Candidat B (envoi individuel, une RPC par Connection ciblée)** : pas
d'API `[Rpc.Owner]`-like paramétrable par `Connection` arbitraire trouvée (`[Rpc.Owner]` cible
toujours le owner du GameObject, jamais une connexion passée en argument) ; construire un envoi
individuel obligerait soit à changer dynamiquement `GameObject.Network.Owner` avant chaque appel
(risqué, effet de bord sur l'ownership réel de l'objet), soit à utiliser `Rpc.FilterInclude(Connection)`
un viewer à la fois dans une boucle — ce qui est fonctionnellement un Candidat A dégénéré, pas une
architecture distincte. Le plan (section 10) demande de ne construire une comparaison avec le
Candidat B que si le Candidat A s'avère insuffisant à l'usage — non tranché tant que la matrice
runtime n'a pas été exécutée.

## 4. Fichiers créés (préparation) — **supprimés après validation runtime**

- `Code/Debug/Networking/MultiViewerRpcSpikeComponent.cs` — composant debug temporaire, host-only
  pour la liste de viewers et l'envoi, `Component.INetworkListener` pour le nettoyage à la
  déconnexion. Aucun `InventoryContainer`, aucun `ItemInstance`, aucune logique de conteneur réelle.
- `Code/UI/Debug/MultiViewerRpcSpikePanel.razor` + `.razor.scss` — panneau debug séparé (pas de
  modification de `InventoryDebugPanel`), bannière « TEMP » explicite, liste dynamique de
  `Connection.All` avec un bouton Add/Remove/Force Close par connexion réelle (pas de libellé codé
  en dur Client A/B).
- `Assets/scenes/Tests/WorldContainerRpcSpike.scene` — scène isolée, minimum nécessaire :
  `NetworkHelper` (réutilise `prefabs/players/kodoku_player.prefab`, 2 spawn points), le
  `MultiViewerRpcSpikeComponent` sur un GameObject réseau partagé sous `_Systems` (sans owner
  joueur), une caméra locale, le panneau debug, un sol minimal, une lumière directionnelle, un
  skybox. Aucun modèle `Wooden_Crate`, aucun prefab de caisse.
- `docs/research/WORLD_CONTAINER_MULTIVIEWER_SPIKE.md` — ce document, **conservé** comme rapport
  historique permanent.

Les quatre premiers fichiers ci-dessus ont été supprimés après l'exécution runtime et la
consignation des résultats (section 10) — ils n'étaient jamais suivis par Git et leur suppression
n'apparaît donc pas comme un diff dans l'historique. `GameplayTest.scene` et les fichiers de
production existants (`PlayerInventoryComponent`, `PlayerItemUseComponent`, `PlayerItemDropComponent`,
`InventoryContainer`, `KodokuPlayerComponent`, prefabs joueur) n'ont jamais été modifiés.

## 5. Fonctionnement de la liste de viewers

- Host-only : `List<Connection> _viewers`, jamais `[Sync]`, jamais reconstruite depuis un cache
  client.
- `AddViewer`/`RemoveViewer` vérifient `Networking.IsHost`, `null`, et l'idempotence (pas de doublon
  à l'ajout, retrait sûr même si absent).
- `ClearViewers` sûr sur liste vide.
- `PurgeDisconnected()` (filet de sécurité, retire les `Connection` avec `IsActive == false`) est
  appelée avant chaque `SendSnapshot`, en complément de `OnDisconnected` (stratégie principale).

## 6. Mécanisme d'envoi

`SendSnapshot()` : incrémente `_hostSequence`, log `[MultiViewerSpike][Send]` (séquence, viewers,
mécanisme), puis `using ( Rpc.FilterInclude( _viewers ) ) { ReceiveSnapshot(...); }`.
`ReceiveSnapshot` est `[Rpc.Broadcast]`, met à jour un état local d'affichage uniquement
(`LocalMessageCount`, `LocalLastSequence`, `LocalLastSource`, `LocalSessionOpen`) et log
`[MultiViewerSpike][Receive]`.

## 7. Mécanisme d'invalidation

`SendInvalidation( Connection target )` : log `[MultiViewerSpike][Invalidate]`, envoie
`ReceiveInvalidate` via `Rpc.FilterInclude( target )` **avant** de retirer `target` de `_viewers` —
ordre : (1) notification ciblée, (2) retrait de la collection, (3) exclusion des envois futurs. Cet
ordre est un choix de conception pour ce spike, pas encore confirmé conforme à une contrainte de
l'API (aucune contrainte d'ordre documentée trouvée) — à confirmer par S0-D.

## 8. Stratégie de nettoyage après déconnexion

Principale : `Component.INetworkListener.OnDisconnected( Connection )`, host-only par contrat de
l'interface, appelle `RemoveViewer`. Repli : `PurgeDisconnected()` avant chaque envoi. Aucune
prétention de nettoyage automatique validé tant que S0-G n'a pas été exécuté.

## 9. Build

`dotnet build Code/kodoku_test.csproj --no-incremental` : **0 erreur**, exactement les 2 warnings
préexistants (`CS1574` sur `KodokuPlayerComponent.cs`, non liés à ce spike).

## 10. Matrice runtime S0-A à S0-I — **exécutée le 2026-07-19**

Session réelle : host = Jo, clients = Wiggum et Smithers (Smithers reconnecté plus tard sous le
pseudo Nelson — une nouvelle `Connection` avec un `DisplayName` différent, ce qui a confirmé au
passage que rien dans le composant ne dépend du nom affiché, seulement de l'objet `Connection`).
Ces noms ne servent qu'à documenter cette session — jamais une identité métier.

| # | Scénario | Action | Attendu | Résultat |
|---|---|---|---|---|
| S0-A | Aucun viewer | Send seq=1, viewers vide | Aucun client ne reçoit ; host ne reçoit que s'il est explicitement inclus | **PASS** — `Send seq=1 viewers=[(aucun)] count=0`, aucune ligne `Receive` chez Wiggum, Smithers, ni chez le host lui-même. Aucun repli sur un broadcast global, aucune exception. |
| S0-B | Client A (Wiggum) uniquement | Add Wiggum, Send seq=2 | Wiggum reçoit exactement une fois ; Smithers rien ; host rien sauf s'il est viewer | **PASS** — `ViewerAdded Wiggum Total=1` → `Send seq=2 viewers=[Wiggum] count=1` ; Wiggum `Receive seq=2 totalReceived=1` ; Smithers : aucune ligne. |
| S0-C | Wiggum et Smithers | Add Smithers, Send seq=3 | Les deux reçoivent chacun exactement une fois, même séquence | **PASS** — `Send seq=3 viewers=[Wiggum, Smithers] count=2` ; Wiggum `totalReceived=2`, Smithers `totalReceived=1`, même séquence 3, aucun doublon. |
| S0-D | Retrait de Wiggum | Invalidate Wiggum, retrait, Send seq=4 | Wiggum reçoit l'invalidation, pas seq=4 ; Smithers reçoit seq=4 une fois | **PASS** — ordre confirmé exact : `Invalidate target=Wiggum seq=3` → `ViewerRemoved Total=1` → `Send seq=4 viewers=[Smithers]`. Wiggum : `Receive INVALIDATE seq=3` puis silence. Smithers : `totalReceived=2`. |
| S0-E | Ajout dupliqué | Add Smithers une 2e fois, Send | Pas de doublon ; Smithers reçoit une seule fois | **NON EXÉCUTÉ** — le panneau debug n'affichait pas de bouton « Add » pour une connexion déjà viewer (le bouton « Remove »/« Force Close » remplace « Add » dès qu'une connexion est viewer). Impossible de déclencher un ajout dupliqué via cette UI telle que construite. La garde `Contains` existe dans le code (`AddViewer`) mais n'a pas été exercée en runtime réel. **Ne pas écrire PASS.** |
| S0-F | Retrait idempotent | Remove Wiggum (déjà absent) | Pas de crash/corruption ; Smithers reste viewer | **NON EXÉCUTÉ** — même limite d'UI : le bouton « Remove » n'apparaît que pour une connexion déjà viewer, impossible de cibler une connexion absente. La garde existe dans le code (`RemoveViewer`, retrait sûr même si absent) mais n'a pas été exercée en runtime réel. **Ne pas écrire PASS.** |
| S0-G | Déconnexion d'un viewer | Smithers seul viewer, déconnexion brutale | Pas d'exception ; référence retirée ; aucun envoi vers connexion morte ; count corrigé | **PASS** — `[MultiViewerSpike][DisconnectedCleanup]` puis `[ViewerRemoved] Total=0` apparaissent **à 09:05:02.3384**, 18 secondes **avant** le `Send` suivant (09:05:20) — preuve que le nettoyage vient de `Component.INetworkListener.OnDisconnected`, pas de la purge défensive (qui n'a même pas eu à agir). Une `TcpChannel.IOException` (fermeture brutale du socket côté moteur) apparaît au même instant mais ne mentionne jamais `MultiViewerSpike` — bruit moteur, pas une exception de notre code. |
| S0-H | Late join | Nouvelle connexion (Nelson) rejoint après plusieurs messages, non ajoutée, puis ajoutée explicitement | Nelson ne devient pas viewer automatiquement, ne reçoit aucun historique ; après ajout explicite, seulement les nouveaux messages | **PASS** — Nelson rejoint sans devenir viewer ; `Send seq=7 viewers=[Wiggum]` l'exclut, Wiggum continue de recevoir normalement (`totalReceived=4`). Après `ViewerAdded Nelson`, `Send seq=8 viewers=[Wiggum, Nelson]` : Wiggum `totalReceived=5` (continuité), Nelson `totalReceived=1` (aucun historique de seq=1 à 7). |
| S0-I | Réouverture | Wiggum invalidé/retiré (S0-D) puis ré-ajouté, Send seq=6 | Wiggum reçoit les nouveaux messages ; pas d'état fantôme | **PASS** — `ViewerAdded Wiggum Total=1` → `Send seq=6 viewers=[Wiggum]` ; Wiggum `Receive seq=6 totalReceived=3` (continuité du compteur depuis 2, pas de reset, pas de replay d'un ancien message). |

**Résultat transversal confirmé sur l'ensemble de la session** : aucun non-viewer n'a jamais reçu un
message ; aucune séquence dupliquée ; tous les viewers d'un même envoi ont reçu la même séquence ;
`ViewerCount` a toujours correspondu aux `Connection` réellement présentes ; aucun transfert
d'ownership réseau sur le `GameObject` partagé ; aucune exception RPC dans le code du spike ; aucune
erreur de sérialisation ; aucune mutation d'inventaire ou de vitals déclenchée par le spike.

## 11. Questions tranchées par le runtime

- **Le host reçoit-il ses propres broadcasts filtrés par défaut ?** Non — confirmé par S0-A : le
  host (Jo) n'a reçu aucune ligne `Receive` alors qu'il était l'appelant du broadcast et n'était pas
  dans la liste de viewers. Il faudrait s'ajouter explicitement comme viewer pour se recevoir
  lui-même.
- **`OnDisconnected` se déclenche-t-il de façon fiable et suffisamment tôt ?** Oui — confirmé par
  S0-G, déclenché immédiatement à la déconnexion, 18 secondes avant le prochain envoi. La purge
  défensive avant chaque envoi reste recommandée en complément (filet de sécurité), mais n'est pas
  la source du nettoyage observé.
- **Le Candidat A (broadcast filtré) suffit-il pour `WorldContainerComponent` ?** Oui — confirmé sur
  les 7 scénarios exécutés (S0-A/B/C/D/G/H/I), zéro exception liée au code du spike, zéro fuite vers
  un non-viewer, zéro doublon. Le Candidat B (envoi individuel) n'a pas été nécessaire.

### Questions restées ouvertes (non testées, hors périmètre de ce spike)

- `Rpc.FilterInclude` imbriqués : intersection, remplacement, ou comportement non défini — jamais
  exercé, le design retenu n'imbrique jamais deux filtres.
- `Rpc.FilterExclude` — non testé, non nécessaire au design retenu. Ne jamais le présenter comme
  validé.
- Coût perçu (latence, taille de message) du broadcast filtré à mesure que le nombre de viewers
  augmente — non mesuré, hors périmètre du spike S0.
- Idempotence ajout dupliqué / retrait déjà absent (S0-E/S0-F) — non exercée en runtime réel, garde
  de code uniquement (voir section 10).

Ces résultats ont été reportés dans
[WORLD_CONTAINER_ARCHITECTURE.md](../architecture/WORLD_CONTAINER_ARCHITECTURE.md) (section 7) et
[ADR-0006](../decisions/ADR-0006-WORLD-CONTAINER-VIEWER-TRANSPORT.md).
