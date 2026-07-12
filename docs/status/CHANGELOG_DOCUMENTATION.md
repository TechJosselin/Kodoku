# Changelog documentation

Historique des modifications de la documentation du projet (pas du code — voir l'historique Git pour le code).

## 2026-07-09 — Création de la fondation documentaire

Création de l'ensemble de la documentation initiale pour la reconstruction de Kodoku :

- `CLAUDE.md` réécrit comme index/résumé de règles (remplace une version antérieure plus factuelle mais sans structure de règles multiplayer/legacy).
- `CLAUDE.local.md` créé (chemins locaux, ignoré par Git).
- `.claude/rules/` créé : `core-safety.md`, `multiplayer.md`, `sbox-bridge.md`, `git-workflow.md`, `documentation.md`, `csharp.md`.
- `docs/architecture/` créé : `PROJECT_ARCHITECTURE.md`, `MULTIPLAYER_ARCHITECTURE.md`, `PLAYER_ARCHITECTURE.md`, `ITEM_ARCHITECTURE.md`, `SCENE_ARCHITECTURE.md`, `UI_ARCHITECTURE.md`.
- `docs/development/` créé : `DEVELOPMENT_WORKFLOW.md`, `TESTING_MULTIPLAYER.md`, `ASSET_MIGRATION.md`, `LEGACY_REFERENCE_POLICY.md`.
- `docs/decisions/` créé : `README.md`, ADR-0001 à ADR-0004.
- `docs/status/` créé : `CURRENT_STATE.md`, `ROADMAP.md`, `OPEN_QUESTIONS.md`, ce fichier.
- `.gitignore` mis à jour pour ignorer `CLAUDE.local.md` et `.claude/settings.local.json`.

Sources principales utilisées : inspection directe du dépôt Kodoku (git, arborescence, Claude Bridge en lecture seule), vault Obsidian (`SBOX_DOC_INDEX.md`, `SBOX_NETWORKING_SUMMARY.md`, `SBOX_OPEN_QUESTIONS.md`), et lecture ciblée de `Kodoku_Legacy/CLAUDE.md` (sections ownership/caméra) pour identifier des intentions et erreurs architecturales — sans copie de code. Aucun fichier C#, scène, prefab ou asset n'a été modifié pendant cette mission.

## 2026-07-09 — Mise à jour d'état suite au commit `97b27d1`

Mise à jour de `docs/status/CURRENT_STATE.md` et `docs/status/ROADMAP.md` pour refléter l'inspection réelle du commit `97b27d1` (« Ajout de la scène réseau et du prefab joueur minimal ») :

- `CURRENT_STATE.md` : ajout de la scène `GameplayTest.scene` et du prefab `kodoku_player.prefab` en « Fonctionnel ou présent », nouvelle section « En cours / non validé » listant ce qui reste à tester à deux instances, nouvelle section « Caméra — point de vigilance » (usage du `Sandbox.PlayerController` stock, pas encore d'architecture caméra locale Kodoku dédiée), section « Non implémenté » recentrée sur ce qui n'a réellement aucune trace dans le projet.
- `ROADMAP.md` : statuts explicites ajoutés aux étapes 3 (« Laboratoire réseau minimal », `En cours`), 4 (« Pawn joueur minimal », `En cours`) et 5 (« Caméra et présentation locale », `Prototype stock uniquement`).

Aucune fonctionnalité n'a été déclarée terminée ou validée sans preuve de test réel — Claude Bridge était déconnecté au moment de cette mise à jour (éditeur s&box fermé), l'inspection a été faite par lecture directe des fichiers `.scene`/`.prefab` et de l'historique Git. Aucun fichier C#, scène, prefab, asset ou configuration n'a été modifié pendant cette mise à jour.

## 2026-07-11 — Suppression de Claude Bridge et mise à jour de la documentation

Claude Bridge (`Libraries/sboxskinsgg.claudebridge/`) a été désinstallé du projet par l'utilisateur. Documentation mise à jour pour ne plus le référencer comme outil disponible :

- `CLAUDE.md` : section « Compilation et tests » réécrite (plus aucun outil de vérification de compilation externe disponible), retrait de la règle de non-dépendance runtime envers Claude Bridge (devenue sans objet), retrait de la mention dans l'index des règles détaillées.
- `.claude/rules/sbox-bridge.md` supprimé — règle entièrement dédiée à un outil qui n'existe plus dans le dépôt.
- `.claude/rules/csharp.md`, `docs/architecture/PROJECT_ARCHITECTURE.md`, `docs/development/DEVELOPMENT_WORKFLOW.md`, `docs/status/OPEN_QUESTIONS.md` : retrait des mentions de Claude Bridge, reformulation générique (« outil de développement externe »).
- `docs/status/CURRENT_STATE.md` : nouvelle entrée datée reflétant l'ajout de `Code/Players/KodokuPlayerComponent.cs` (premier composant Kodoku, attaché à `kodoku_player.prefab`) et l'absence, désormais, de tout outil de vérification de compilation ou de logs automatisé.

Les entrées précédentes de ce changelog mentionnant Claude Bridge (ci-dessus) sont conservées telles quelles : elles décrivent un état passé réel au moment où elles ont été écrites, pas la situation actuelle.

## 2026-07-12 — Préservation de la recherche sur l'inventaire natif s&box et remise en cohérence

Mission strictement documentaire. Une étude (`experiment/native-inventory`, un commit, jamais mergée) et un spike expérimental (`spike/native-inventory-adapter`, jamais mergé ni commité) avaient été produits séparément pour évaluer le système natif d'inventaire s&box, **avant** que `InventoryContainer` (noyau local personnalisé) ne soit implémenté et mergé dans `main`. Les deux branches expérimentales étaient devenues obsolètes par rapport à `main` (plusieurs de leurs fichiers avaient été modifiés depuis par d'autres missions) — leur contenu utile a donc été reporté sélectivement sur une branche documentaire propre (`docs/native-inventory-research`, créée depuis `main`), pas mergé ni cherry-ické tel quel :

- `docs/research/SBOX_BUILTIN_INVENTORY_EVALUATION.md` créé — import fidèle de l'étude originale (`experiment/native-inventory`, commit `3531a96`), avec une mise à jour datée en tête indiquant que Kodoku n'a pas adopté le système natif et renvoyant vers la décision réelle.
- `docs/research/NATIVE_INVENTORY_SPIKE_RESULTS.md` créé — synthèse fidèle du rapport de clôture du spike (branche `spike/native-inventory-adapter`, jamais commité) : pickup/drop/re-pickup/transfert validés, `InstanceId` stable, aucune duplication, late join incomplet pour les items inactifs (limite réelle non résolue), test de pickup simultané non concluant, test de charge partiel. Aucun code expérimental reproduit — reste sur la branche du spike.
- `docs/decisions/ADR-0005-CUSTOM-INVENTORY.md` créé — décision d'architecture : inventaire personnalisé (`InventoryContainer`/`InventoryPlacement`) retenu plutôt que le système natif, même sous forme hybride, malgré des résultats de spike globalement positifs (la divergence structurelle de fond et la limite du late join ont pesé plus lourd que les résultats positifs de transport réseau).
- `docs/status/OPEN_QUESTIONS.md` : nouvelle section « Décisions résolues » — la question native/hybride/personnalisé y est déplacée et marquée résolue ; nouvelles sous-questions concrètes ajoutées pour ce qui reste réellement ouvert (réplication du futur `PlayerInventoryComponent`, pickup interactif, drop réseau, transfert, stack merge/split, inventaires imbriqués, équipement, persistance).
- `docs/architecture/ITEM_ARCHITECTURE.md` : nouvelle section courte « Décision : inventaire personnalisé, pas le système natif s&box », renvoyant vers l'ADR — le contenu déjà validé (Inventory Core, Tests A à O) n'a pas été modifié.
- `docs/status/ROADMAP.md` : étape 8 complétée d'une note indiquant l'évaluation terminée et la décision actuelle, sans changer son statut.
- `docs/status/CURRENT_STATE.md` : nouvelle entrée datée reflétant que `Code/Items/` (y compris `Inventory/`) est désormais mergé dans `main`, qu'`InventoryContainer` est validé pour son périmètre local (Tests A à O), qu'aucun inventaire joueur networké n'existe encore, et que le système natif a été évalué mais non adopté ; correction de la section « Non implémenté » qui listait encore « Système d'items »/« Inventaire » comme totalement absents (obsolète depuis les jalons Items/Loot/Inventory Core).
- `CLAUDE.md` : le résumé du projet ne dit plus qu'aucun système d'items/inventaire n'existe — corrigé pour refléter l'état réel (Items, `InventoryContainer`, décision ADR-0005), qui avait dérivé de la réalité depuis plusieurs jalons.
- `docs/architecture/PROJECT_ARCHITECTURE.md` : statut introductif corrigé — `Code/` n'est plus décrit comme une fondation vide (obsolète depuis plusieurs jalons), distinction entre domaines partiellement implémentés (Players, Items, UI) et domaines encore visés.
- `docs/README.md` : nouvelle section « Recherche (`research/`) » indexant `LEGACY_MULTIPLAYER_AUDIT.md` (déjà existant, non indexé jusqu'ici), `SBOX_BUILTIN_INVENTORY_EVALUATION.md` et `NATIVE_INVENTORY_SPIKE_RESULTS.md` ; index des ADR complété avec ADR-0005.

**Éléments de `experiment/native-inventory` explicitement ignorés, car obsolètes par rapport à `main`** : la description de `CURRENT_STATE.md`/`PROJECT_ARCHITECTURE.md`/`ROADMAP.md` au moment de cette branche (avant `LootSpawnPointComponent` Tests A à G, avant `InventoryContainer`) n'a pas été recopiée — remplacée par une rédaction reflétant l'état actuel de `main`. Les commentaires C# repérés comme obsolètes par l'étude originale (`ItemDefinition.cs`, mentions de `ItemInstance`/`WorldPrefabOverride` non implémentés) restent non corrigés : ces commentaires eux-mêmes sont désormais obsolètes d'une autre façon (les systèmes qu'ils disaient absents existent), une vérification et correction à part entière du code C# reste hors périmètre d'une mission documentaire.

Aucun fichier C#, Razor, prefab, scène, asset ou configuration n'a été modifié pendant cette mission. `experiment/native-inventory` et `spike/native-inventory-adapter` n'ont pas été mergées — leur contenu a été reporté sélectivement, pas fusionné tel quel.
