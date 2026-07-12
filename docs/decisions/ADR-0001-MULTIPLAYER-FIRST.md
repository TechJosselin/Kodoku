# ADR-0001 — Multiplayer-first

**Statut** : Acceptée

## Contexte

Kodoku est reconstruit presque depuis zéro. Sur la version précédente du projet, le support coop avait été ajouté après coup à une base conçue pour le solo, ce qui a généré des bugs difficiles à diagnostiquer (ownership, caméra, timing de spawn — voir [../development/LEGACY_REFERENCE_POLICY.md](../development/LEGACY_REFERENCE_POLICY.md)) parce que des hypothèses implicites de solo s'étaient déjà propagées dans le code avant d'être remises en question.

## Décision

Toute fonctionnalité de gameplay est conçue et testée pour le coop **dès sa création**, jamais ajoutée après coup à une implémentation solo. Le fonctionnement en solo ne suffit jamais à valider une fonctionnalité de gameplay — voir [../../.claude/rules/multiplayer.md](../../.claude/rules/multiplayer.md).

## Conséquences positives

- Les questions d'autorité, de réplication et de présentation locale sont posées au moment de la conception, pas découvertes en production.
- Réduit le risque de reproduire les classes de bugs observées sur l'ancienne version.

## Compromis et limites

- Coût de conception plus élevé dès les premières fonctionnalités, même simples, comparé à un prototype solo rapide.
- Nécessite un test à au moins deux instances pour valider quoi que ce soit de gameplay — voir [../development/TESTING_MULTIPLAYER.md](../development/TESTING_MULTIPLAYER.md), ce qui ralentit l'itération par rapport à un test solo seul.

## Éléments à réévaluer

Aucun à ce jour — principe fondateur de la reconstruction, non remis en question tant que le projet reste un jeu coop.
