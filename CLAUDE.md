# CLAUDE.md

Ce fichier est le point d'entrée principal de Claude Code pour le projet **Kodoku**. Il reste synthétique : c'est un index et un résumé des règles essentielles, pas une documentation exhaustive. Pour le détail, suivre les liens ci-dessous.

## Le projet

**Kodoku** est un jeu s&box (survie/exploration coop) actuellement **reconstruit presque depuis zéro**. Le dépôt contient une fondation de projet quasi vide (`Editor/Assembly.cs` réduit à des `global using`) et certains assets PNG d'interface conservés de l'ancienne version. Trois premiers systèmes existent, tous validés par test réel host/client : `KodokuPlayerComponent` (identification du pawn local/proxy), `PlayerVitalsComponent` (santé/endurance/faim/soif/radiation, host-authoritative) et un HUD local minimal (`Code/UI/Hud/GameHud.razor`). Aucun autre système (items, inventaire, mort/respawn, interaction, sauvegarde) n'existe encore dans le code actuel. Voir [docs/status/CURRENT_STATE.md](docs/status/CURRENT_STATE.md) pour l'état factuel à jour.

L'ancien projet est conservé séparément sous le nom **Kodoku_Legacy**, en lecture seule, comme référence historique uniquement — voir [docs/development/LEGACY_REFERENCE_POLICY.md](docs/development/LEGACY_REFERENCE_POLICY.md). Ne jamais copier directement son code, ses scènes ou ses prefabs.

## Compilation et tests

Il n'y a pas de commande CLI de build/lint/test autonome pour le code du jeu : `Code/kodoku.csproj` référence les DLL du moteur via des chemins relatifs vers l'installation Steam locale de s&box (`../../../../SteamLibrary/steamapps/common/sbox/...`), propres à la machine — un `dotnet build` direct n'est pas fiable. La compilation passe uniquement par l'éditeur s&box (hotload automatique à la sauvegarde d'un fichier) : aucun outil ne permet de vérifier une compilation depuis une session Claude Code, la confirmation se fait dans l'éditeur.

Aucune suite de tests automatisés n'existe pour le code du jeu. Toute fonctionnalité réseau se valide avec **au moins deux instances** (host + client), jamais par un test solo ni par relecture de code — voir [docs/development/TESTING_MULTIPLAYER.md](docs/development/TESTING_MULTIPLAYER.md).

## Règle non négociable : coop/multiplayer-first

Toute fonctionnalité de gameplay est conçue et testée pour le coop **dès sa création**, jamais ajoutée après coup à une version solo. Pour toute fonctionnalité de gameplay, il faut pouvoir répondre à : qui a l'autorité, qui possède le GameObject, quelles données sont synchronisées, lesquelles sont strictement locales, comment fonctionne le late join, ce qui se passe à la déconnexion, comment c'est testé avec au moins deux instances. Voir [.claude/rules/multiplayer.md](.claude/rules/multiplayer.md) et [docs/architecture/MULTIPLAYER_ARCHITECTURE.md](docs/architecture/MULTIPLAYER_ARCHITECTURE.md).

Le fonctionnement en solo ne suffit **jamais** à valider une fonctionnalité de gameplay. Toujours distinguer explicitement : **autorité** (qui décide), **réplication** (ce qui est synchronisé), **présentation locale** (caméra, HUD, inputs, audio listener — jamais répliqués comme état global).

## Règles non négociables (résumé)

- Ne jamais copier directement code/architecture/scène depuis `Kodoku_Legacy` — l'ancien projet sert uniquement à comprendre une intention ou identifier une erreur à ne pas reproduire.
- Pas de commit, push, merge, rebase ou changement de branche sans demande explicite — voir [.claude/rules/git-workflow.md](.claude/rules/git-workflow.md).
- Pas de suppression massive, pas de nouvelle dépendance, pas de contournement d'erreur de compilation sans validation — voir [.claude/rules/core-safety.md](.claude/rules/core-safety.md).
- Ne pas inventer un état du projet ou une fonctionnalité comme "déjà implémentée" — vérifier contre le code réel.

## Documentation

- [docs/README.md](docs/README.md) — index complet de la documentation
- [docs/architecture/](docs/architecture/) — architecture visée (projet, multiplayer, joueur, items, scènes, UI)
- [docs/development/](docs/development/) — workflow, tests multiplayer, migration d'assets, politique legacy
- [docs/decisions/](docs/decisions/) — décisions d'architecture (ADR)
- [docs/status/](docs/status/) — état courant, roadmap, questions ouvertes

Règles détaillées : [.claude/rules/](.claude/rules/) (sécurité, multiplayer, Git, documentation, C#).

## Chemins locaux

Les chemins absolus (dépôt legacy, vault Obsidian) sont propres à cette machine et ne sont **pas** versionnés ici — voir [CLAUDE.local.md](CLAUDE.local.md) (ignoré par Git).
