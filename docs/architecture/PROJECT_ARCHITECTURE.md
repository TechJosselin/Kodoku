# Architecture générale du projet

**Statut : architecture cible, partiellement implémentée.** `Editor/` reste une fondation vide (`Assembly.cs`). `Code/` contient désormais plusieurs domaines réels — Players (`KodokuPlayerComponent`, `PlayerVitalsComponent`), Items (`ItemDefinition`/`ItemInstance`/`WorldItemComponent`/`LootSpawnPointComponent`) et UI (`GameHud.razor`) — tous validés par test réel host/client pour leur périmètre respectif, voir [../status/CURRENT_STATE.md](../status/CURRENT_STATE.md). Les autres domaines listés ci-dessous (Inventory, Interaction, Persistence, AI, etc.) restent visés, non implémentés. Ce document décrit l'organisation cible des futurs domaines fonctionnels, pas des classes ou une API déjà décidées dans leur totalité.

## Objectif

Donner un vocabulaire commun de domaines fonctionnels avant que le code ne soit écrit, pour que chaque nouvelle fonctionnalité sache où elle vit et de quoi elle a le droit de dépendre.

## Domaines fonctionnels envisagés

| Domaine | Responsabilité envisagée |
|---|---|
| **Core** | Fondations transverses (utilitaires, constantes, bootstrap) — pas de logique de gameplay |
| **Networking** | Primitives de session, ownership, aide autour du modèle réseau s&box — voir [MULTIPLAYER_ARCHITECTURE.md](MULTIPLAYER_ARCHITECTURE.md) |
| **Players** | Pawn réseau, identité, mouvement, présentation locale — voir [PLAYER_ARCHITECTURE.md](PLAYER_ARCHITECTURE.md) |
| **Items** | `ItemDefinition`/`ItemInstance`, définitions de données — voir [ITEM_ARCHITECTURE.md](ITEM_ARCHITECTURE.md) |
| **Inventory** | Conteneurs, équipement, logique d'inventaire (consommateur du domaine Items) |
| **Interaction** | Détection et exécution d'interactions monde ↔ joueur |
| **World** | Scènes, objets statiques/réseau, spawn, zones — voir [SCENE_ARCHITECTURE.md](SCENE_ARCHITECTURE.md) |
| **AI** | Comportement des entités non-joueur |
| **Persistence** | Sauvegarde/chargement d'état durable (hors présentation, hors réseau) |
| **UI** | HUD et menus locaux — voir [UI_ARCHITECTURE.md](UI_ARCHITECTURE.md) |
| **Audio** | Sons et musique, déclenchés par les autres domaines, jamais autorité de gameplay |

Cette liste est un point de départ, pas un contrat figé : un domaine peut être scindé ou fusionné une fois que du code réel existe.

## Principes de dépendance

- Un domaine de gameplay (Players, Items, Inventory, Interaction, World, AI) peut dépendre de Core et Networking.
- UI et Audio **consomment** l'état des autres domaines ; ils ne portent jamais d'autorité de gameplay (voir [UI_ARCHITECTURE.md](UI_ARCHITECTURE.md)).
- Persistence lit/écrit l'état des domaines de gameplay ; elle ne doit pas devenir un point de couplage bidirectionnel (les domaines de gameplay ne doivent pas dépendre de Persistence pour fonctionner en mémoire).
- Aucun domaine runtime ne dépend d'un outil de développement externe au projet.

## Séparation données / logique runtime / présentation

Trois couches à garder distinctes dans chaque domaine concerné :

- **Données** : ce qui décrit un objet indépendamment de toute instance (ex. `ItemDefinition` en tant que ressource `.item`).
- **Logique runtime** : ce qui fait évoluer l'état pendant une partie (ex. un composant qui applique une transaction d'inventaire), soumis aux règles d'autorité réseau.
- **Présentation locale** : ce qui est affiché à un client donné (ex. rendu d'une icône, position de caméra), jamais source de vérité.

## Éléments encore ouverts

- Le découpage exact en assemblies/namespaces C# n'est pas décidé — dépend du premier domaine réellement implémenté.
- Voir [../status/OPEN_QUESTIONS.md](../status/OPEN_QUESTIONS.md) pour les questions transverses non tranchées.
