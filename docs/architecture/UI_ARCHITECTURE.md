# Architecture UI

**Statut : architecture visée, partiellement implémentée.** Un premier HUD local existe, `Code/UI/Hud/GameHud.razor` (affichage brut des vitals du pawn local, sans design final) — **terminé pour sa version minimale, validé par test réel host/client** (chaque instance n'affiche que ses propres vitals, mise à jour temps réel) le 2026-07-11, voir [../status/CURRENT_STATE.md](../status/CURRENT_STATE.md). Aucun menu, aucune interaction UI. Les PNG (fonds de panneaux, icônes de slots, icônes de vitals) conservés sous `Assets/ui/` depuis l'ancienne version ne sont pas encore consommés par ce HUD. Voir aussi [../development/ASSET_MIGRATION.md](../development/ASSET_MIGRATION.md).

**Point technique confirmé par test réel** : un `PanelComponent` Razor s&box ne reconstruit son contenu que si sa méthode `BuildHash()` retourne une valeur différente de la précédente — sans la surcharger avec les valeurs pertinentes (et l'identité de ce qu'il observe, pas seulement ses valeurs), un panneau peut rester figé sur son premier rendu indéfiniment. À reproduire pour tout futur panneau Razor qui affiche un état qui change au cours du temps.

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

- Structure Razor/SCSS cible pour les menus (inventaire, pause) — le HUD est le seul précédent à ce jour, pas encore de convention établie au-delà.
- Réutilisation effective des PNG conservés une fois la mise en page UI reconçue.
