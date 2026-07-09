# Architecture des items

**Statut : architecture visée, non implémentée.** Aucun système d'items n'existe dans le code actuel de Kodoku, et aucune ressource `.item` n'est présente dans `Assets/Data/` (dossier vide) — voir [../status/CURRENT_STATE.md](../status/CURRENT_STATE.md). Seuls des PNG d'icônes de l'ancienne UI ont été conservés (voir [../development/ASSET_MIGRATION.md](../development/ASSET_MIGRATION.md)).

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

## Éléments encore ouverts

- Format exact des futures ressources `.item` — voir [../status/OPEN_QUESTIONS.md](../status/OPEN_QUESTIONS.md).
- Modèle d'autorité réseau pour la création/destruction d'`ItemInstance` (probable host-authoritative par cohérence avec ADR-0002, à confirmer au moment de la conception).
- Portée de la réplication des conteneurs (contenu d'un conteneur networké en temps réel, ou seulement son existence).
