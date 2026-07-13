# Fichiers générés — S&box et environnement de développement

Règle stricte, applicable à toute opération Git sur ce dépôt : récupération, développement, commit, merge, pull request, push.

## Ne jamais ajouter, indexer, committer ou pousser

- Fichiers compilés `*_c` : `.scene_c`, `.prefab_c`, `.vmat_c`, `.vmdl_c`, `.vtex_c`, `.item_c`, et tout autre fichier compilé équivalent. Exception déjà présente dans `.gitignore` (`!*.shader_c`) — ne pas la retirer sans vérifier pourquoi elle existe.
- Fichiers auxiliaires `.scene_d`.
- Fichiers projet régénérés par l'éditeur/IDE : `.csproj`, `.slnx`, `Code/Properties/`, `Editor/Properties/`.

## Fichiers source à toujours suivre

`.scene`, `.prefab`, `.vmat`, `.vmdl`, `.razor`, `.scss`, `.cs`, documentation et configuration écrites manuellement.

## Avant chaque `git add` ou commit

1. `git status --short`.
2. Identifier les fichiers générés dans la liste.
3. Ne jamais les ajouter à l'index — préférer un ajout explicite des fichiers source concernés plutôt que `git add .`.
4. Vérifier `git diff --cached --name-only` avant de committer.
5. Si un fichier généré apparaît indexé par erreur, le retirer sans le supprimer localement : `git restore --staged -- <chemin>`.

## Fichiers générés déjà suivis dans l'historique

Si un fichier généré est déjà suivi par Git, ne pas lancer `git rm` ou `git rm --cached` automatiquement. Signaler le cas à l'utilisateur ; le retrait se fait dans un commit de nettoyage séparé et contrôlé, décidé explicitement — voir [git-workflow.md](git-workflow.md).

Cas constaté le 2026-07-13 (`Assets/scenes/Tests/gameplaytest.scene_d`) : résolu par le commit `349869c` (`chore(git): stop tracking generated scene data`). Le fichier n'est plus suivi par Git, n'existe plus localement, et reste couvert par la règle `*.scene_d` du `.gitignore`. Audit du 2026-07-13 : aucun autre fichier généré connu n'est actuellement suivi par Git dans ce dépôt.
