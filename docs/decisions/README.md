# Architecture Decision Records (ADR)

Ce dossier documente les décisions d'architecture **durables** de Kodoku — pas les détails d'implémentation réversibles, qui vivent dans `docs/architecture/` et peuvent évoluer sans ADR.

## Statuts possibles

- **Acceptée** — décision en vigueur, à respecter.
- **Proposée** — en discussion, pas encore engageante.
- **Remplacée** — supersédée par un ADR ultérieur (le nouvel ADR référence l'ancien).

## Structure d'un ADR

- Statut
- Contexte
- Décision
- Conséquences positives
- Compromis et limites
- Éléments à réévaluer

## Index

| ADR | Titre | Statut |
|---|---|---|
| [ADR-0001](ADR-0001-MULTIPLAYER-FIRST.md) | Multiplayer-first | Acceptée |
| [ADR-0002](ADR-0002-HOST-AUTHORITY.md) | Host authority | Acceptée |
| [ADR-0003](ADR-0003-LOCAL-PRESENTATION.md) | Présentation locale | Acceptée |
| [ADR-0004](ADR-0004-CLEAN-REBUILD.md) | Reconstruction propre | Acceptée |

Quand ajouter un nouvel ADR : une décision d'architecture qui contraint durablement la conception future (ex. « le mouvement est host-authoritative ») justifie un ADR. Une préférence d'implémentation locale à un système ne le justifie pas — voir [../../.claude/rules/documentation.md](../../.claude/rules/documentation.md).
