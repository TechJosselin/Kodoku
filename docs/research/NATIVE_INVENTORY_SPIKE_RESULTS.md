# Résultats du spike — adaptateur d'inventaire natif s&box

**Nature de ce document : compte-rendu d'un spike expérimental clos, pas une architecture adoptée.** Reporté fidèlement depuis le rapport original produit sur la branche `spike/native-inventory-adapter` (jamais mergée, jamais commitée) — voir [ADR-0005](../decisions/ADR-0005-CUSTOM-INVENTORY.md) pour la décision d'architecture qui en a résulté et [SBOX_BUILTIN_INVENTORY_EVALUATION.md](SBOX_BUILTIN_INVENTORY_EVALUATION.md) pour l'étude documentaire qui a précédé ce spike. Aucun code expérimental n'est reproduit ici — le code reste sur la branche `spike/native-inventory-adapter` et le worktree associé, non touchés par ce report.

## Question exacte du spike

> `BaseInventoryComponent` et `BaseInventoryItem` peuvent-ils fournir le transport réseau, le pickup, le drop et le transfert tout en laissant `ItemInstance` comme unique source de vérité pour l'identité, la quantité et l'état persistant d'un item ?

## Statut à la clôture

**Spike clos le 12 juillet 2026 par décision explicite de l'utilisateur**, après cinq passages réels à deux ou trois instances (host + clients successifs). Branche `spike/native-inventory-adapter`, créée depuis `experiment/native-inventory`. Ni cette branche ni `experiment/native-inventory` n'ont été mergées par ce spike. Build s&box testée : `buildid` Steam `24152323`.

Architecture prototypée (aucun second identifiant d'instance ajouté, aucune `Quantity` dupliquée côté natif) :

```text
ItemDefinition (water_bottle.item, inchangé)
    ↓
ItemInstance (InstanceId / Definition / Quantity — source de vérité, inchangé)
    ↓
WorldItemComponent (réplication réseau host-authoritative, inchangé)
    ↓
NativeInventoryItemAdapter : BaseInventoryItem (résout WorldItemComponent, ne crée jamais d'ItemInstance)
    ↓
NativeInventoryTestComponent : BaseInventoryComponent (posé sur le joueur, transport réseau natif)
```

## Résultats finaux par test (A à J)

| Test | Description | Résultat |
|---|---|---|
| A | Pickup côté host | ✅ Concluant |
| B | Pickup côté joiner | ✅ Concluant — `InstanceId` identique entre le log du joiner et celui du host |
| C | Pickup simultané par deux joueurs sur le même item | ⚠️ Non concluant tel quel — le contexte testé n'était pas représentatif (un des deux joueurs n'avait déjà plus de slot libre) ; aucune duplication observée, mais un vrai test de contention reste à faire |
| D | Inventaire plein | ✅ Concluant, avec un point de vigilance : `CanPickupWorldItem` peut rapporter `PickupAccepted` alors que l'ajout réel échoue silencieusement plus loin dans le pipeline natif (inventaire déjà plein) — l'item reste au sol, repickable à volonté |
| E | Drop | ✅ Concluant — aucun `Destroyed` observé, cohérent avec un comportement de reparentage plutôt que de recréation |
| F | Nouveau pickup après drop | ✅ Concluant — même `InstanceId` exact avant et après le cycle drop→re-pickup, observé sur plusieurs items distincts |
| G | Transfert entre deux joueurs, les deux sens | ✅ Concluant — confirmé après une fausse alerte initiale : l'item transféré n'est jamais auto-équipé chez le destinataire (confirmé par commentaire XML), donc invisible à l'œil sans UI d'inventaire ; une inspection directe du contenu (`Items`) a confirmé sa présence réelle dans les deux sens, avec le même `InstanceId` |
| H | Late join (inventaire déjà peuplé) | ⚠️ Limite réelle, cause identifiée précisément : seul l'`ActiveItem` d'un inventaire se réplique de façon fiable à un late joiner ; un item rangé dans un autre slot, jamais actif, ne se réplique pas tant qu'il ne le devient pas |
| I | Déconnexion avec un item porté | ✅ Concluant : les items encore dans l'inventaire d'un joueur qui se déconnecte sont détruits avec son pawn — pas de drop automatique, pas d'état orphelin récupérable, pas de persistance |
| J | Plusieurs items simultanés (5 bouteilles) | ⚠️ Partiel — comportement stable pour pickup/drop/re-pickup répétés sur 5 items et jusqu'à 3 clients, mais aucune mesure de coût GameObject réalisée |

## Piste de contournement testée pour le Test H

Un appel host-only à `GameObject.Network.Refresh()` (« Send a complete refresh snapshot of this networked object to other clients », confirmé par commentaire XML) a été testé sur le pawn détenant l'item inactif, après la connexion du late joiner. **Résultat négatif dans la fenêtre d'observation** (~8 secondes après l'appel pertinent) : l'item inactif n'est pas réapparu chez le late joiner. **Non totalement exclu** — la fenêtre d'observation était courte comparée aux délais habituels des autres événements réseau du spike, et aucune vérification positive alternative (relire `Items` du point de vue du late joiner) n'a été obtenue. Un test plus rigoureux (cible unique sans ambiguïté, observation d'au moins 30 secondes) reste à faire avant d'exclure définitivement cette piste.

## Découvertes transverses, valables au-delà de ce spike

- **Les hooks natifs peuvent rapporter un succès sans garantir un résultat visible/complet** : `PickupAccepted` sans ajout réel à l'inventaire (Test D) ; item transféré mais jamais auto-équipé chez le destinataire (Test G). Toujours vérifier l'état réel (`Items`/contenu), pas seulement la valeur de retour d'un callback.
- **Confirmation empirique d'une lacune moteur déjà documentée** comme question ouverte du projet — voir [OPEN_QUESTIONS.md](../status/OPEN_QUESTIONS.md) : les valeurs `[Sync]` ne sont pas garanties disponibles avant qu'un hook (`OnEquipped`) ne se déclenche côté late joiner. Un hook s'est déclenché avec `InstanceId`/`ItemId`/`Quantity` encore vides, la restauration réelle arrivant une fraction de seconde après dans le même burst d'événements.
- **Poser un prefab réseau directement dans un fichier de scène ne le networke pas** — un `NetworkSpawn()` explicite host-only est nécessaire, confirmé en pratique après une première tentative ratée qui avait généré des `InstanceId` disjoints entre host et joiner pour ce qui devait être les mêmes objets.

## Verdict du spike

Penchait vers « architecture hybride potentiellement viable » : pickup, drop, ré-acquisition et transfert (Tests A, B, D, E, F, G) se sont montrés solides et reproductibles sur plusieurs passages, à deux et trois instances, avec un `InstanceId` parfaitement stable et aucune recréation de GameObject observée en dehors des cas de déconnexion. La limite réelle restante (Test H, items inactifs) avait une cause précisément identifiée, mais la piste de contournement la plus simple ne s'est pas confirmée dans le test effectué — une resynchronisation explicite côté Kodoku (RPC dédié ou état `[Sync]` supplémentaire) aurait probablement été nécessaire pour la lever.

## Non fait, explicitement laissé de côté par l'arrêt du spike

- Confirmation définitive (positive ou négative) de la piste `Network.Refresh()`.
- Conception et test d'une resynchronisation explicite côté Kodoku pour le late join.
- Test C (pickup simultané) rejoué dans un contexte représentatif.
- Tout test avec un item empilable (`MaxStack > 1`) ou un inventaire imbriqué.
- Mesure de coût (nombre de GameObjects) au-delà de 5 items.

## Décision qui a suivi ce spike

Le spike n'a pris **aucune décision d'architecture** — c'était hors de son périmètre. La décision effective, prise séparément après ce spike, est documentée dans [ADR-0005](../decisions/ADR-0005-CUSTOM-INVENTORY.md) : Kodoku retient un inventaire personnalisé (`InventoryContainer`/`InventoryPlacement`, noyau local déjà validé Tests A à O) plutôt que d'adopter le système natif, même sous forme hybride — les résultats globalement positifs de ce spike n'ont pas suffi à compenser la divergence structurelle de fond (`BaseInventoryItem` comme GameObject vivant en permanence vs. `ItemInstance` comme donnée pure) au regard de la grille spatiale, du stacking et de la persistance visés par Kodoku.

## Origine de ce document

Ce rapport reporte fidèlement le contenu du document original produit pendant le spike (`docs/research/NATIVE_INVENTORY_SPIKE_RESULTS.md` sur la branche/worktree `spike/native-inventory-adapter`, jamais commité). Le rapport original contient un niveau de détail supplémentaire (narration pas-à-pas des cinq passages, y compris un défaut de setup initial et sa correction, extraits de logs bruts, signatures d'API exactes consultées) — conservé sur cette branche expérimentale pour une consultation future si nécessaire, non dupliqué ici pour rester un document de synthèse.
