# Roadmap

Étapes de reconstruction, sans dates artificielles. L'ordre reflète les dépendances techniques, pas un engagement de calendrier. Voir [CURRENT_STATE.md](CURRENT_STATE.md) pour ce qui est fait à date.

## 1. Fondation du projet

- **Objectif** : projet s&box vide, compilable, dépôt configuré.
- **Dépendances** : aucune.
- **Critères de validation** : `kodoku.sbproj` valide, le projet compile, dépôt GitHub configuré.
- **Hors périmètre** : tout code de gameplay.
- **Statut** : fait — voir [CURRENT_STATE.md](CURRENT_STATE.md).

## 2. Documentation et règles

- **Objectif** : fondation documentaire (`CLAUDE.md`, règles, architecture visée, ADR) avant d'écrire du code de gameplay.
- **Dépendances** : étape 1.
- **Critères de validation** : documents créés, cohérents, sans information inventée.
- **Hors périmètre** : configuration de sécurité technique (permissions, hooks, protections de branche — voir [CURRENT_STATE.md](CURRENT_STATE.md)).
- **Statut** : fait (cette mission).

## 3. Laboratoire réseau minimal

- **Objectif** : une session réseau minimale fonctionnelle (host + un client rejoint), sans gameplay, pour valider le modèle réseau s&box dans ce projet.
- **Dépendances** : étape 1.
- **Critères de validation** : deux instances se connectent, se voient dans les logs/`get_network_status`, sans code de gameplay.
- **Hors périmètre** : pawn, mouvement, UI.

## 4. Pawn joueur minimal

- **Objectif** : un pawn networké spawné par joueur, ownership correcte.
- **Dépendances** : étape 3.
- **Critères de validation** : testé à deux instances (voir [../development/TESTING_MULTIPLAYER.md](../development/TESTING_MULTIPLAYER.md)) — ownership correcte, pas de doublon.
- **Hors périmètre** : mouvement, caméra, HUD.

## 5. Caméra et présentation locale

- **Objectif** : caméra strictement locale par client, jamais répliquée — voir [../architecture/PLAYER_ARCHITECTURE.md](../architecture/PLAYER_ARCHITECTURE.md) et ADR-0003.
- **Dépendances** : étape 4.
- **Critères de validation** : testé à deux instances — chaque client garde sa propre vue, pas de vol/gel de caméra.
- **Hors périmètre** : HUD de gameplay, inputs de gameplay au-delà du regard/déplacement de base.

## 6. Première interaction complète

- **Objectif** : un cycle complet interaction monde → requête host → réponse, comme cas d'école du modèle d'autorité — voir [../architecture/MULTIPLAYER_ARCHITECTURE.md](../architecture/MULTIPLAYER_ARCHITECTURE.md).
- **Dépendances** : étape 4.
- **Critères de validation** : testé à deux instances, y compris interaction simultanée par deux joueurs.
- **Hors périmètre** : items réels (peut utiliser un placeholder).

## 7. Nouveau système d'items

- **Objectif** : `ItemDefinition`/`ItemInstance` — voir [../architecture/ITEM_ARCHITECTURE.md](../architecture/ITEM_ARCHITECTURE.md).
- **Dépendances** : étape 2 (architecture définie).
- **Critères de validation** : au moins une définition d'item chargée et instanciée, testée en réseau.
- **Hors périmètre** : inventaire, équipement.

## 8. Inventaire et équipement

- **Objectif** : conteneurs, emplacements d'équipement, consommation d'objets.
- **Dépendances** : étapes 6, 7.
- **Critères de validation** : transfert d'item entre deux joueurs testé à deux instances, sans désynchronisation.
- **Hors périmètre** : UI définitive (peut utiliser un placeholder minimal).

## 9. Objets du monde

- **Objectif** : items ramassables, conteneurs de butin dans la scène — voir [../architecture/SCENE_ARCHITECTURE.md](../architecture/SCENE_ARCHITECTURE.md).
- **Dépendances** : étape 8.
- **Critères de validation** : ramassage/dépôt testé à deux instances, pas de duplication d'objet.
- **Hors périmètre** : génération procédurale de contenu.

## 10. IA et combat

- **Objectif** : entités non-joueur, logique de combat de base.
- **Dépendances** : étape 8 (pour les drops éventuels).
- **Critères de validation** : comportement IA cohérent pour tous les clients, testé à deux instances.
- **Hors périmètre** : IA avancée (pathfinding complexe, comportements sociaux).

## 11. Scènes, zones et extraction

- **Objectif** : plusieurs zones, transition entre elles — voir [../architecture/SCENE_ARCHITECTURE.md](../architecture/SCENE_ARCHITECTURE.md) et [../status/OPEN_QUESTIONS.md](OPEN_QUESTIONS.md).
- **Dépendances** : étape 9.
- **Critères de validation** : transition de zone testée à deux instances, sans perte d'état inattendue.
- **Hors périmètre** : contenu final des zones.

## 12. Persistance et reconnexion

- **Objectif** : sauvegarde/chargement d'état, reconnexion après déconnexion.
- **Dépendances** : étapes 7, 8, 11.
- **Critères de validation** : reconnexion testée à deux instances sans duplication ni perte d'état.
- **Hors périmètre** : optimisation de performance de sauvegarde.

## 13. Production de contenu

- **Objectif** : contenu de jeu réel (items, zones, IA) au-delà des cas de test des étapes précédentes.
- **Dépendances** : toutes les étapes précédentes.
- **Critères de validation** : à définir au moment venu.
- **Hors périmètre** : rien — c'est la phase de contenu.
