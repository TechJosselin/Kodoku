# ADR-0006 — Broadcast filtré pour les viewers de conteneur du monde

**Statut** : Acceptée

## Contexte

[WORLD_CONTAINER_ARCHITECTURE.md](../architecture/WORLD_CONTAINER_ARCHITECTURE.md) identifiait le
transport réseau multi-viewer comme le risque technique principal du futur `WorldContainerComponent` :
aucun mécanisme réseau multi-destinataire ciblé n'était utilisé ni testé dans ce projet. Tous les
push réseau existants sont soit `[Rpc.Owner]` (un seul destinataire fixe, le propriétaire du
`GameObject`), soit `[Rpc.Host]` (un seul destinataire fixe, le host), soit
`[Sync(SyncFlags.FromHost)]` (broadcast implicite à tous les clients). Un conteneur du monde partagé
(caisse, coffre) doit au contraire diffuser son contenu à un ensemble **dynamique et variable** de
joueurs qui le consultent — un besoin structurellement différent des trois cas déjà validés.

`Rpc.FilterInclude`/`FilterExclude` étaient identifiées par audit statique (XML doc + réflexion sur
`Sandbox.Engine.dll`) comme des API disponibles, mais leur comportement exact n'avait jamais été
vérifié par un test réel dans ce projet — aucun exemple d'usage trouvé nulle part dans le SDK
installé. Un spike technique isolé (Spike S0, branche `spike/world-container-multiviewer-rpc`) a été
exécuté pour lever ce risque avant toute écriture de code de conteneur — voir
[docs/research/WORLD_CONTAINER_MULTIVIEWER_SPIKE.md](../research/WORLD_CONTAINER_MULTIVIEWER_SPIKE.md)
pour le détail complet (logs, séquences, verdicts scénario par scénario).

### Options étudiées

- **A. Broadcast filtré** — une RPC `[Rpc.Broadcast]` enveloppée dans
  `using ( Rpc.FilterInclude( viewers ) ) { ... }`, un seul appel host-side par envoi, filtré à
  l'ensemble courant des viewers autorisés.
- **B. Envoi individuel en boucle** — une RPC par viewer, filtre à un seul élément à chaque
  itération. Fonctionnellement équivalent à l'option A pour le résultat final, mais N appels réseau
  au lieu d'un seul.
- **C. Réplication globale** (`[Sync(SyncFlags.FromHost)]` du contenu complet, visible par tous les
  clients en permanence).
- **D. Ownership temporaire** (réassigner `GameObject.Network.Owner` à chaque viewer successivement
  pour réutiliser `[Rpc.Owner]`).

## Décision

Kodoku retient l'**option A : broadcast filtré**, `Rpc.FilterInclude` (une collection de
`Connection`, ou une seule `Connection` pour un message ciblé) combiné à `[Rpc.Broadcast]`. Le futur
`WorldContainerComponent` héberge une collection host-only `_viewers` (jamais `[Sync]`, jamais
reconstruite depuis un cache client), sans `Network Owner` joueur sur le `GameObject` conteneur
lui-même — l'autorité de mutation reste déterminée par `Networking.IsHost`, jamais par ownership.
Les snapshots ne sont diffusés qu'aux viewers courants ; l'invalidation ciblée d'un viewer précis
précède toujours son retrait de la collection ; le nettoyage à la déconnexion s'appuie sur
`Component.INetworkListener.OnDisconnected(Connection)`.

## Preuves runtime du Spike S0

Exécuté en runtime réel avec un host et deux clients distants (2026-07-19). Verdicts (détail complet
dans le document de recherche cité en contexte) :

| Scénario | Verdict | Preuve |
|---|---|---|
| S0-A — aucun viewer | PASS | Collection vide sûre : aucun envoi à personne, pas de repli sur un broadcast global, host non destinataire de son propre appel. |
| S0-B — un viewer | PASS | Cible exactement un viewer, une seule fois. |
| S0-C — deux viewers | PASS | Cible plusieurs viewers simultanément, même séquence, aucun doublon. |
| S0-D — invalidation puis retrait | PASS | Ordre confirmé : notification ciblée → retrait de la collection → exclusion des envois futurs. |
| S0-G — déconnexion d'un viewer | PASS | `OnDisconnected` déclenché immédiatement (18s avant l'envoi suivant), sans exception ; source réelle du nettoyage, pas la purge défensive. |
| S0-H — late join | PASS | Un nouveau venu ne devient jamais viewer automatiquement, ne reçoit aucun historique après ajout explicite. |
| S0-I — réouverture | PASS | Un viewer ré-ajouté après invalidation reçoit à nouveau les messages, sans état fantôme. |
| S0-E — ajout dupliqué | **Non exécuté** | Limite de l'outil debug utilisé (pas de bouton pour forcer l'action invalide) — garde de code existante (`Contains`), non exercée en runtime. |
| S0-F — retrait idempotent | **Non exécuté** | Même limite — garde de code existante, non exercée en runtime. |

## Conséquences positives

- Un seul mécanisme de transport, un seul appel réseau par envoi quel que soit le nombre de viewers.
- Aucune modification requise sur `PlayerInventoryComponent`/`WorldItemPickupComponent`/
  `PlayerItemDropComponent`/`PlayerItemUseComponent` — le patron RPC-transport/méthode-métier déjà
  établi par ces composants est directement réutilisable pour `WorldContainerComponent`.
- Nettoyage à la déconnexion couvert par un mécanisme confirmé (`OnDisconnected`), pas seulement une
  purge défensive supposée suffisante.
- Le contenu d'un conteneur reste privé aux viewers actuels — jamais un `[Sync(FromHost)]` public à
  tout le monde en permanence.

## Compromis et limites

- L'idempotence d'ajout/retrait de viewer (S0-E/S0-F) reste une garantie de **code**, pas de
  **runtime** — simple vérification de collection C# côté host, sans composante réseau, donc risque
  réel jugé faible, mais à ne jamais présenter comme validé par test réel tant qu'un outil de test
  déterministe ou une UI de test dédiée ne l'aura pas exercée.
- `Rpc.FilterExclude` n'a pas été testé et n'est pas nécessaire au design retenu — ne jamais le
  présenter comme validé dans une future révision de la documentation.
- Le coût réseau (latence, taille de message) du broadcast filtré à mesure que le nombre de viewers
  augmente n'a pas été mesuré — hors périmètre du spike, à surveiller si un conteneur attire un
  nombre de viewers significatif en pratique.
- Ce spike valide le **transport générique**, pas le futur `WorldContainerComponent` lui-même —
  aucune logique de conteneur, d'inventaire ou d'item n'a été exercée par ce spike.

## Éléments à réévaluer

- Si un besoin futur exige un traitement différencié par viewer (option B, envoi individuel),
  reconsidérer à ce moment — non nécessaire pour la V1 des conteneurs du monde.
- Confirmer l'idempotence S0-E/S0-F par un outil de test déterministe si un incident réel la met en
  cause un jour (voir [OPEN_QUESTIONS.md](../status/OPEN_QUESTIONS.md)).

## Références

- [docs/research/WORLD_CONTAINER_MULTIVIEWER_SPIKE.md](../research/WORLD_CONTAINER_MULTIVIEWER_SPIKE.md) — rapport complet du spike (API auditée, logs, verdicts).
- [docs/architecture/WORLD_CONTAINER_ARCHITECTURE.md](../architecture/WORLD_CONTAINER_ARCHITECTURE.md) — section 7, modèle réseau multi-viewer.
