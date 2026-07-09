# État actuel du projet

**Ce document ne liste que ce qui est réellement vérifié dans le projet à la date indiquée.** Rien ici n'est déduit d'une intention ou d'une roadmap — voir [ROADMAP.md](ROADMAP.md) pour ce qui est prévu mais pas fait.

Date de rédaction : 2026-07-09. Vérifié par inspection directe du dépôt (`git log`, arborescence de fichiers) et de Claude Bridge en lecture seule (`get_project_info`, `get_scene_hierarchy`, `get_compile_errors`).

## Fonctionnel ou présent

- Nouveau projet s&box **Kodoku** créé (`kodoku.sbproj` : type `game`, org `local`, `MaxPlayers: 64`, `TickRate: 50`, `GameNetworkType: Multiplayer`).
- Nouveau dépôt GitHub configuré (`https://github.com/TechJosselin/Kodoku`, remote `origin` confirmé localement).
- Ancien projet conservé séparément comme `Kodoku_Legacy` (dépôt distinct, en lecture seule) — voir [../development/LEGACY_REFERENCE_POLICY.md](../development/LEGACY_REFERENCE_POLICY.md). Note : le remote Git local de `Kodoku_Legacy` pointe encore vers `github.com/TechJosselin/kodoku.git`, pas `kodoku_legacy` — écart non résolu, voir `CLAUDE.local.md`.
- Certains PNG de l'ancienne UI conservés sous `Assets/ui/` (commit `abd02d8`, « Ajout des assets UI conservés ») : icônes d'items (équipement, consommables), icônes de vitals (santé/faim/radiation/stamina/soif), fonds de panneaux d'inventaire, icônes de slots d'équipement. Aucun code (Razor/SCSS) ne les consomme actuellement.
- Claude Bridge (`Libraries/sboxskinsgg.claudebridge/`) installé comme Library de développement, visible dans `kodoku.slnx` et connecté (confirmé via `get_bridge_status`) — outil de développement uniquement, voir [../../.claude/rules/sbox-bridge.md](../../.claude/rules/sbox-bridge.md).
- Fondation documentaire créée par cette mission (`CLAUDE.md`, `CLAUDE.local.md`, `.claude/rules/`, `docs/`).
- Le projet compile sans erreur à la date de rédaction (confirmé via `get_compile_errors`) — attendu, puisqu'il n'y a quasiment pas de code.

## Non implémenté

- Fondation réseau (aucun `NetworkHelper` ni composant réseau dans le projet actuel)
- Spawn joueur
- Mouvement
- Caméra locale
- Système d'items
- Inventaire
- Interaction
- Ennemis
- Combat
- Sauvegarde
- Chargement de zones

`Code/` et `Editor/` ne contiennent chacun que `Assembly.cs` (déclarations `global using` uniquement, aucune classe). `Assets/scenes/` est vide — la scène de démarrage déclarée dans `kodoku.sbproj` (`scenes/minimal.scene`) n'existe pas encore sur disque. `Assets/Data/`, `Assets/Materials/`, `Assets/Models/`, `Assets/Prefabs/`, `Assets/Textures/` sont vides.

## Sur la sécurité technique

**La fondation documentaire créée par cette mission ne constitue pas, à elle seule, un système de sécurité technique.** Elle définit des règles et conventions à destination de Claude Code, mais ne les fait pas respecter mécaniquement.

Devront être configurés dans une **mission séparée**, non couverte par la présente mission documentaire :

- les permissions Claude Code ;
- les hooks `PreToolUse` ;
- les protections GitHub de la branche `main`.

Aucun de ces mécanismes n'a été créé pendant cette mission.

## Vault Obsidian

Recherche s&box existante dans le vault (`D:\Obsidian\Kodoku\01 Docs\`, chemin détaillé dans `CLAUDE.local.md`) : notes officielles sur le réseau/scène/composants s&box (vérifiées contre l'API installée), synthèses (`SBOX_NETWORKING_SUMMARY.md`, `SBOX_LIFECYCLE_SUMMARY.md`, `SBOX_OPEN_QUESTIONS.md`). Les sous-dossiers `01 Docs/architecture/` du vault (`NETWORKING.md`, `PLAYER_LIFECYCLE.md`) existent mais sont vides à ce jour — non utilisés comme source pour ce document.
