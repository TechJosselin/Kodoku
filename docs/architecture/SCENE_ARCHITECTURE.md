# Architecture des scènes

**Statut : architecture visée, non implémentée.** `Assets/scenes/` est actuellement vide ; la scène de démarrage référencée par `kodoku.sbproj` (`scenes/minimal.scene`) n'existe pas encore sur disque — voir [../status/CURRENT_STATE.md](../status/CURRENT_STATE.md).

## Principes envisagés

- **Scènes neuves.** Les scènes de Kodoku sont recréées pour la nouvelle architecture, pas reprises depuis `Kodoku_Legacy` — voir [../development/LEGACY_REFERENCE_POLICY.md](../development/LEGACY_REFERENCE_POLICY.md).
- **Séparer explicitement, dans la hiérarchie de scène** : objets statiques (jamais networkés, identiques pour tout le monde), objets réseau (spawnés/possédés, soumis aux règles de [MULTIPLAYER_ARCHITECTURE.md](MULTIPLAYER_ARCHITECTURE.md)), et objets locaux (existent uniquement côté client, ex. caméra locale — voir [PLAYER_ARCHITECTURE.md](PLAYER_ARCHITECTURE.md)).
- **Hiérarchie locale clairement identifiée** — un client doit pouvoir distinguer d'un coup d'œil ce qui est purement à lui (convention de nommage/racine dédiée à définir au moment de l'implémentation).
- **Points de spawn** définis explicitement dans la scène, pas déduits implicitement d'une position d'objet arbitraire.
- **Objets interactifs** suivent le modèle requête/réponse host-authoritative décrit dans [MULTIPLAYER_ARCHITECTURE.md](MULTIPLAYER_ARCHITECTURE.md#objets-du-monde-et-interactions).
- **Chargement de zones** — stratégie non encore décidée (une seule scène vs. plusieurs zones chargées/déchargées) ; voir [../status/OPEN_QUESTIONS.md](../status/OPEN_QUESTIONS.md).
- **Responsabilités du host** — tout ce qui décide de l'état initial d'une zone (spawn des entités, génération) est une responsabilité host, cohérent avec ADR-0002.
- **Pas de référence fragile vers un objet qui peut ne pas exister chez un client donné.** Une référence sérialisée (`[Property] GameObject`/`Component`) vers un objet scene-local depuis un composant qui peut s'exécuter sur un client sans cette portion de scène est un risque documenté (voir `docs/references/SBOX_OPEN_QUESTIONS.md` du vault et [../../.claude/rules/csharp.md](../../.claude/rules/csharp.md)) — préférer une résolution explicite à la conception plutôt qu'une référence d'éditeur non vérifiée.

## Éléments encore ouverts

- Nombre et granularité des scènes/zones.
- Mécanisme de transition entre zones (voir [../status/OPEN_QUESTIONS.md](../status/OPEN_QUESTIONS.md)).
- Contenu exact de la scène de démarrage (`scenes/minimal.scene`, référencée par `kodoku.sbproj` mais pas encore créée).
