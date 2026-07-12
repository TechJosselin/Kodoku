# Core safety

Règles globales, applicables à toute intervention sur ce dépôt, indépendamment du domaine (code, documentation, assets).

- Pas de suppression massive (fichiers, dossiers, historique Git) sans demande explicite pour cette suppression précise.
- Pas de modification d'une configuration importante (`kodoku.sbproj`, `ProjectSettings/`, `.gitignore`, workflows CI éventuels) sans expliquer ce qui change et pourquoi.
- Pas de nouvelle dépendance (package s&box, Library, paquet externe) sans validation explicite de l'utilisateur.
- Pas de publication (push, création/merge de PR, publication d'un asset ou d'un package) sans demande explicite.
- Pas de contournement d'une erreur de compilation (`#pragma` de suppression, `TreatWarningsAsErrors` désactivé, code commenté pour faire disparaître l'erreur) — corriger la cause, ou signaler le blocage si la cause n'est pas claire.
- Pas de modification en dehors du périmètre explicitement demandé dans le message courant. Si une modification connexe semble nécessaire, la signaler plutôt que l'effectuer silencieusement.
- Pas de fichier généré ou temporaire ajouté au dépôt (`obj/`, `bin/`, `.csproj`/`.sln`/`.slnx` générés par l'éditeur, captures d'écran de debug, logs) — ces artefacts sont déjà couverts par `.gitignore` ; ne pas les forcer avec `git add -f`.
- Vérifier l'état Git (`git status`) avant toute modification importante et après, pour confirmer que seuls les fichiers attendus ont changé.
- Signaler clairement toute incertitude plutôt que de deviner silencieusement — en particulier pour tout ce qui touche au comportement réseau s&box non confirmé (voir [docs/status/OPEN_QUESTIONS.md](../../docs/status/OPEN_QUESTIONS.md)).

Ces règles s'ajoutent à celles, plus spécifiques, de [multiplayer.md](multiplayer.md), [git-workflow.md](git-workflow.md), [documentation.md](documentation.md) et [csharp.md](csharp.md).
