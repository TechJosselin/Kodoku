# Migration d'assets depuis Kodoku_Legacy

Politique de transfert d'assets depuis `Kodoku_Legacy` vers le nouveau projet. Complète [LEGACY_REFERENCE_POLICY.md](LEGACY_REFERENCE_POLICY.md), qui couvre la réutilisation d'intention/code ; ce document couvre spécifiquement les fichiers d'assets.

## Règles

- **Importer uniquement des assets explicitement validés** — pas d'import en bloc d'un dossier entier de l'ancien projet.
- **Vérifier les dépendances** d'un asset avant import (un modèle dépend d'un matériau, un prefab dépend de modèles et de scripts, etc.) — un import partiel produit des références cassées.
- **Ne pas importer de logique cachée.** Un prefab ou une ressource `.item` de l'ancien projet peut porter des références vers l'ancien code (composants, chemins) — vérifier qu'aucune dépendance vers l'ancienne architecture ne s'introduit ainsi silencieusement.
- **Ne pas copier des scènes ou des prefabs entiers par défaut** — les scènes et prefabs de Kodoku sont recréés pour la nouvelle architecture (voir [../architecture/SCENE_ARCHITECTURE.md](../architecture/SCENE_ARCHITECTURE.md)).
- **Documenter l'origine de chaque asset importé** (d'où il vient dans `Kodoku_Legacy`, pourquoi il a été conservé) — au minimum dans le commit qui l'introduit.
- **Tester chaque asset dans le nouveau projet** après import (chargement correct, pas de référence manquante) plutôt que de supposer qu'il fonctionne à l'identique.
- **Recréer les ressources de gameplay** (`.item`, définitions de données) plutôt que les copier — voir [../architecture/ITEM_ARCHITECTURE.md](../architecture/ITEM_ARCHITECTURE.md).
- **Conserver temporairement les PNG d'UI actuels** (déjà présents sous `Assets/ui/`, voir [../status/CURRENT_STATE.md](../status/CURRENT_STATE.md)) sans engagement sur la mise en page ou le code UI qui les consommera — voir [../architecture/UI_ARCHITECTURE.md](../architecture/UI_ARCHITECTURE.md).

## État actuel

Le seul transfert effectué à ce jour est celui des PNG d'UI listés dans [../status/CURRENT_STATE.md](../status/CURRENT_STATE.md) (commit `abd02d8`, « Ajout des assets UI conservés »). Aucune autre migration d'asset n'a eu lieu.
