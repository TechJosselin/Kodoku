# Multiplayer-first

Kodoku est reconstruit avec une architecture **coop/multiplayer-first**. Toute fonctionnalité de gameplay est conçue et testée pour le coop dès sa création, jamais retrofit sur une version solo existante. Le fonctionnement solo (un seul client, `Networking.IsActive == false`) ne suffit **jamais** à valider une fonctionnalité — il ne teste qu'un cas dégénéré, pas la logique réseau réelle.

## Ce que Claude doit pouvoir préciser pour toute fonctionnalité de gameplay

1. **Autorité** — qui décide de l'état (host, owner, ou logique purement locale) ?
2. **Ownership** — le GameObject concerné a-t-il un propriétaire (`OwnerConnection`), et lequel ?
3. **Données synchronisées** — quelles données sont répliquées (`[Sync]`, RPC, snapshot custom) ?
4. **Données strictement locales** — quelles données ne doivent jamais quitter le client (caméra, HUD, inputs bruts) ?
5. **Late join** — que reçoit un joueur qui rejoint après que l'état existe déjà ?
6. **Déconnexion** — que devient l'état/les objets possédés par une connexion qui part ?
7. **Test** — comment cette fonctionnalité est-elle vérifiée avec au moins deux instances (host + client) ?
8. **Source de vérité** — en cas de désaccord entre deux copies locales de la même donnée, laquelle fait foi ?

Si l'une de ces réponses n'est pas encore tranchée pour une fonctionnalité donnée, le dire explicitement plutôt que de deviner — voir [docs/status/OPEN_QUESTIONS.md](../../docs/status/OPEN_QUESTIONS.md) et le principe d'architecture dans [docs/architecture/MULTIPLAYER_ARCHITECTURE.md](../../docs/architecture/MULTIPLAYER_ARCHITECTURE.md).

## Principes établis

- **Le host contrôle les états de gameplay importants** (santé, inventaire, progression, tout ce qui doit résister à un client malveillant ou désynchronisé) — voir ADR-0002.
- **La présentation locale n'est jamais un état global répliqué.** Caméra, HUD, menus, inputs et audio listener sont strictement locaux à chaque client — voir ADR-0003 et [docs/architecture/PLAYER_ARCHITECTURE.md](../../docs/architecture/PLAYER_ARCHITECTURE.md).
- **Un GameObject visuel n'est pas automatiquement la source de vérité.** Le pion réseau (pawn), sa présentation (modèle, animation) et son état de gameplay (santé, inventaire) sont des responsabilités distinctes — ne pas driver une décision d'autorité depuis un composant purement visuel.
- **Un RPC ponctuel ne remplace pas un état durable synchronisé.** Un RPC est un événement (non snapshotté, jamais reçu par un late joiner) ; un `[Sync]`/état networké est une donnée persistante. Ne pas utiliser un RPC pour porter une information qu'un late joiner doit connaître.
- Un round-trip RPC (requête → réponse) doit rester une transaction unique et complète en un aller-retour ; ne pas construire un enchaînement où le traitement de la réponse déclenche un second RPC indépendant. Ce point est une contrainte empirique observée sur l'ancien projet (non documentée officiellement) — voir [docs/status/OPEN_QUESTIONS.md](../../docs/status/OPEN_QUESTIONS.md) et `docs/references/SBOX_NETWORKING_SUMMARY.md` dans le vault.

## Ce qui n'est pas encore figé

Les détails d'implémentation qui n'ont pas encore été décidés (stratégie exacte d'autorité du mouvement, format de snapshot custom, gestion précise de la reconnexion) ne doivent pas être imposés prématurément. Les proposer comme recommandation ou hypothèse, pas comme règle acquise — voir [docs/status/OPEN_QUESTIONS.md](../../docs/status/OPEN_QUESTIONS.md).
