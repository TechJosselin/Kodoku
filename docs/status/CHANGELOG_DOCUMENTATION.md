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
