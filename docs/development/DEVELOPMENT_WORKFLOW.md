# Workflow de développement

Cycle de travail recommandé pour toute tâche de développement sur Kodoku.

1. **Comprendre la tâche** — périmètre exact demandé, ne pas l'étendre silencieusement (voir [../../.claude/rules/core-safety.md](../../.claude/rules/core-safety.md)).
2. **Consulter la documentation pertinente** — [docs/README.md](../README.md) pour trouver le bon document d'architecture ; le vault Obsidian (`SBOX_DOC_INDEX.md`, chemin dans `CLAUDE.local.md`) pour toute question sur l'API/le comportement s&box.
3. **Définir autorité et responsabilités** avant d'écrire du code, pour toute fonctionnalité de gameplay — répondre aux huit questions de [../../.claude/rules/multiplayer.md](../../.claude/rules/multiplayer.md).
4. **Créer une branche** si demandé, avec le préfixe approprié — voir [../../.claude/rules/git-workflow.md](../../.claude/rules/git-workflow.md). Ne jamais travailler directement sur `main` pour une tâche de développement.
5. **Implémenter un périmètre limité** — pas d'abstraction ou de fonctionnalité anticipée non demandée.
6. **Compiler** — uniquement via l'éditeur s&box (hotload automatique à la sauvegarde). Aucun outil externe ne permet de vérifier une compilation depuis une session Claude Code — voir [../../CLAUDE.md](../../CLAUDE.md).
7. **Tester avec au moins deux instances si le changement concerne le gameplay networké** — voir [TESTING_MULTIPLAYER.md](TESTING_MULTIPLAYER.md). Un test solo seul ne valide jamais une fonctionnalité réseau.
8. **Vérifier les erreurs ou avertissements dans l'éditeur s&box** — aucun outil de lecture de logs externe n'est disponible.
9. **Vérifier Git** (`git status`, fichiers modifiés) avant de considérer la tâche terminée.
10. **Mettre à jour la documentation pertinente** — voir [../../.claude/rules/documentation.md](../../.claude/rules/documentation.md) : le document d'architecture concerné si une décision a été prise, [../status/CURRENT_STATE.md](../status/CURRENT_STATE.md) si l'état vérifiable du projet a changé, un ADR si la décision est durable et architecturale.
