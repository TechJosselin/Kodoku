# ADR-0004 — Clean rebuild

**Statut** : Acceptée

## Contexte

L'ancienne version du projet avait accumulé, sur sa durée de vie, des correctifs empiriques enchevêtrés (en particulier autour du réseau et de la caméra) difficiles à isoler proprement, ainsi qu'une architecture d'items/inventaire non pensée coop dès le départ (voir [ADR-0001](ADR-0001-MULTIPLAYER-FIRST.md)). Poursuivre son développement en l'état aurait signifié continuer à composer avec ces couches historiques plutôt que de corriger les causes racines.

## Décision

Kodoku est **reconstruit dans un nouveau projet et un nouveau dépôt** (`https://github.com/TechJosselin/Kodoku`). L'ancien projet est conservé sous `Kodoku_Legacy` (`https://github.com/TechJosselin/kodoku_legacy`) **uniquement comme archive et référence historique**, jamais comme base de code active ni comme dépendance — voir [../development/LEGACY_REFERENCE_POLICY.md](../development/LEGACY_REFERENCE_POLICY.md).

## Conséquences positives

- Permet d'appliquer [ADR-0001](ADR-0001-MULTIPLAYER-FIRST.md), [ADR-0002](ADR-0002-HOST-AUTHORITY.md) et [ADR-0003](ADR-0003-LOCAL-PRESENTATION.md) dès la première ligne de code, sans code hérité à concilier.
- Les leçons de l'ancienne version (erreurs identifiées, pas code) sont capturées explicitement dans la documentation plutôt que réimplicitement présentes dans une base de code historique.

## Compromis et limites

- Perte temporaire de toute fonctionnalité déjà construite sur l'ancienne version — le projet reconstruit repart d'un état fonctionnellement vide (voir [../status/CURRENT_STATE.md](../status/CURRENT_STATE.md)).
- Risque de reperdre du temps sur des problèmes déjà résolus une fois si l'ancienne version n'est pas consultée à bon escient — d'où la procédure explicite dans [../development/LEGACY_REFERENCE_POLICY.md](../development/LEGACY_REFERENCE_POLICY.md).

## Éléments à réévaluer

Aucun à ce jour — décision fondatrice déjà exécutée (les deux dépôts existent séparément).
