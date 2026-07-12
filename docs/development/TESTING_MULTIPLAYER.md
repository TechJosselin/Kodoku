# Tests multiplayer

Checklist de vérification pour toute fonctionnalité de gameplay networké. Voir [../../.claude/rules/multiplayer.md](../../.claude/rules/multiplayer.md) et [../architecture/MULTIPLAYER_ARCHITECTURE.md](../architecture/MULTIPLAYER_ARCHITECTURE.md) pour les principes sous-jacents. Méthode de test officielle confirmée par l'API s&box : rejoindre via une nouvelle instance depuis l'icône de statut réseau (`docs/official/sbox/networking/10-testing-multiplayer.md` du vault) — c'est la méthode à utiliser pour tout ce qui suit.

## Tests systématiques (à chaque fonctionnalité de gameplay networké)

- Fonctionne côté **host**.
- Fonctionne côté **client** (non-host).
- **Deux joueurs visibles** simultanément, sans doublon ni objet fantôme.
- **Ownership** correcte : chaque pawn/objet est contrôlé par la bonne connexion.
- **Caméra indépendante** : chaque client garde sa propre vue, aucun vol/gel de caméra entre clients.
- **HUD indépendant** : chaque client voit son propre état (santé, inventaire), jamais celui d'un autre.
- **Late join** : un joueur qui rejoint après le début de partie reçoit un état cohérent.
- **Déconnexion** : la connexion qui part ne laisse pas d'état incohérent chez les autres.
- **Reconnexion** : si applicable à la fonctionnalité, revenir après déconnexion ne duplique pas l'état.
- **Absence de doublons** d'objets réseau après une séquence connexion/déconnexion/reconnexion.
- **Absence d'erreurs de références** (`Unknown GameObject`, résolution de `ComponentReference`/`GameObjectReference` en échec) dans les logs.
- **Synchronisation d'état** : la valeur affichée converge vers la même donnée sur tous les clients (pas de dérive persistante).
- **Interaction simultanée** : deux joueurs qui interagissent avec le même objet en même temps ne produisent pas un état incohérent (ex. double attribution d'un même item).
- **Respawn**, si applicable : un joueur qui meurt puis respawn revient dans un état propre.
- **Nettoyage des objets réseau** : aucun objet networké orphelin ne persiste après la fin d'une session/partie.

## Tests spécifiques aux fonctionnalités

À définir au cas par cas selon ce que fait la fonctionnalité (ex. pour l'inventaire : transfert d'item entre deux inventaires appartenant à deux joueurs différents). Documenter ces cas dans la tâche ou le document d'architecture concerné, pas ici.

## Critères pour considérer une fonctionnalité terminée

Une fonctionnalité de gameplay networké n'est **terminée** que si :

1. elle compile sans erreur ;
2. tous les tests systématiques pertinents ci-dessus passent, testés avec au moins deux instances réelles (pas seulement lus/déduits du code) ;
3. les logs ne montrent aucune erreur/avertissement inattendu pendant le test ;
4. la documentation concernée (matrice d'autorité, état courant) est à jour.

Ne jamais déclarer une fonctionnalité réseau « terminée » sur la seule base d'un test solo ou d'une relecture de code — voir [../../.claude/rules/documentation.md](../../.claude/rules/documentation.md).
