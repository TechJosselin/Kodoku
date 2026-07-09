# Git workflow

- `main` doit toujours rester compilable. Ne jamais y pousser un état connu pour être cassé.
- Le développement se fait sur des branches dédiées, jamais directement sur `main`.
- Préfixes de branches recommandés :
  - `feature/` — nouvelle fonctionnalité
  - `fix/` — correction de bug
  - `refactor/` — réorganisation sans changement de comportement
  - `docs/` — documentation uniquement
  - `chore/` — maintenance, outillage, configuration
- Ne jamais effectuer de `commit`, `push`, `merge`, `rebase` ou changement de branche sans demande explicite de l'utilisateur pour cette action précise.
- Vérifier `git status` avant toute intervention, pour connaître l'état de départ réel du dépôt (changements en cours, fichiers non suivis).
- Afficher les fichiers modifiés après le travail, pour que l'utilisateur puisse valider l'étendue réelle du changement avant tout commit.
- Ne jamais inclure de fichiers générés (`obj/`, `bin/`, `.csproj`/`.sln`/`.slnx` générés par l'éditeur, `.sbox/`) — déjà couverts par `.gitignore`, ne pas les ajouter avec `-f`.
- Un commit doit avoir un périmètre cohérent : ne pas mélanger une fonctionnalité de gameplay, un changement de documentation et une modification de configuration dans le même commit sans raison.
- Les modifications réseau importantes (autorité, ownership, `[Sync]`, RPC) doivent être isolées dans des commits/branches identifiables et testables indépendamment — voir [multiplayer.md](multiplayer.md) et [../../docs/development/TESTING_MULTIPLAYER.md](../../docs/development/TESTING_MULTIPLAYER.md).
