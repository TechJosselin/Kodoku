# Maintenance de la documentation

- Ne pas dupliquer une information dans plusieurs fichiers. Si une information existe déjà (ex. état d'autorité réseau dans `MULTIPLAYER_ARCHITECTURE.md`), y renvoyer par lien plutôt que la recopier.
- Mettre à jour les documents concernés lorsqu'une décision change, au moment où elle change — ne pas laisser la documentation dériver du code réel.
- Distinguer explicitement dans chaque document : une **décision validée**, une **recommandation** (pas encore tranchée), une **idée** ou une **question ouverte**. Ne jamais présenter une recommandation comme une vérité acquise.
- Utiliser les ADR (`docs/decisions/`) pour les décisions architecturales durables — pas pour des détails d'implémentation réversibles.
- Maintenir [docs/status/CURRENT_STATE.md](../../docs/status/CURRENT_STATE.md) strictement factuel : ne déclarer une fonctionnalité "présente" ou "fonctionnelle" que si elle est vérifiable dans le projet actuel (code, scène, asset réellement présents).
- Ne jamais déclarer une fonctionnalité terminée sans validation réelle (compilation + test, et pour le gameplay réseau, test à au moins deux instances — voir [multiplayer.md](multiplayer.md)).
- Documenter les limites connues et la dette technique plutôt que de les passer sous silence.
- Utiliser des liens relatifs entre documents (`../architecture/...`, pas de chemin absolu).
- Ne jamais mettre de chemin absolu local (`E:\...`, `D:\...`) dans un document versionné — ces chemins vont dans `CLAUDE.local.md`, qui est ignoré par Git.
- Ne pas modifier automatiquement l'ensemble de la documentation pour un changement mineur : mettre à jour uniquement les documents réellement concernés par le changement.
