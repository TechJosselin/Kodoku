# Architecture UI

**Statut : architecture visée, non implémentée.** Aucun code UI (Razor/SCSS) n'existe dans le projet actuel — seuls des PNG (fonds de panneaux, icônes de slots, icônes de vitals) sont conservés sous `Assets/ui/` depuis l'ancienne version. Voir [../status/CURRENT_STATE.md](../status/CURRENT_STATE.md) et [../development/ASSET_MIGRATION.md](../development/ASSET_MIGRATION.md).

## Principes

- **HUD local** — affichage en jeu (vitals, hotbar, prompts d'interaction) : purement local à chaque client, lit l'état d'autres domaines, n'en décide jamais.
- **Menus locaux** — inventaire, équipement, menus de pause : idem, purement locaux.
- **Données reçues depuis les systèmes runtime** — l'UI consomme l'état exposé par les domaines de gameplay ([PROJECT_ARCHITECTURE.md](PROJECT_ARCHITECTURE.md)), elle ne le stocke pas en double de façon durable.
- **Aucune autorité de gameplay dans l'UI.** Un clic dans un menu déclenche une requête vers le système autoritaire concerné (cohérent avec le modèle host-authoritative, ADR-0002) ; l'UI n'applique jamais elle-même un changement d'état de gameplay.
- **Aucune réplication directe d'un panneau ou d'une caméra.** Un `ScreenPanel`/`WorldPanel` et sa caméra cible sont des concepts locaux à un client — voir le principe de caméra locale dans [PLAYER_ARCHITECTURE.md](PLAYER_ARCHITECTURE.md#principe-central--la-caméra-nest-pas-une-propriété-répliquée-du-pawn).
- **Distinguer état affiché et source de vérité.** Ce qu'un panneau affiche est une projection locale, potentiellement en retard d'un tick sur l'état réseau réel — ne jamais confondre les deux lors du débogage d'une désynchronisation apparente.

## Assets UI conservés (intention de design, pas d'implémentation)

Sous `Assets/ui/` : icônes d'items par catégorie, icônes de vitals (santé/faim/radiation/stamina/soif — suggère un modèle de statistiques de survie), fonds de panneaux d'inventaire, icônes de slots d'équipement (sac à dos, armure, tenue, masque, jambières, chaussures, arme). Ces assets sont conservés **temporairement**, sans engagement sur une réutilisation de l'ancienne mise en page ou de l'ancien code UI — voir [../development/ASSET_MIGRATION.md](../development/ASSET_MIGRATION.md).

## Éléments encore ouverts

- Structure Razor/SCSS cible (aucune décision prise, aucun fichier existant).
- Réutilisation effective des PNG conservés une fois la mise en page UI reconçue.
