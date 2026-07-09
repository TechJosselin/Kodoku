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
