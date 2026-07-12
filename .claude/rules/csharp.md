---
paths:
  - "Code/**/*.cs"
  - "Editor/**/*.cs"
---

# C# — Kodoku

S'applique uniquement au code source du nouveau projet Kodoku (`Code/`, assembly jeu, et `Editor/`, assembly éditeur — confirmés comme les deux dossiers de code du projet lors de l'inspection initiale du dépôt). Ne s'applique **pas** à `Libraries/`, aux fichiers générés (`**/obj/**`, `**/bin/**`), à `Kodoku_Legacy`, ni au vault Obsidian.

Le dossier `Code/` ne contient encore que `Assembly.cs` et un premier composant (`Code/Players/KodokuPlayerComponent.cs`) — les règles ci-dessous s'appliquent à mesure que du code y est ajouté, elles ne décrivent pas encore un ensemble large.

## Attendu

- **Namespaces organisés par domaine** (ex. réseau, joueur, items, UI), pas par type technique.
- **Responsabilités limitées par composant** — un composant fait une chose ; s'il commence à orchestrer plusieurs domaines (réseau + inventaire + UI), c'est un signal pour le découper.
- **Éviter les managers globaux sans justification.** Un singleton/manager statique doit se justifier par un besoin réel (ex. point d'entrée réseau unique côté host), pas être le réflexe par défaut.
- **Éviter les dépendances circulaires** entre domaines.
- **Éviter les références directes fragiles entre objets réseau.** Une référence `[Property] GameObject`/`Component` vers un objet qui peut ne pas exister chez tous les clients (late join, ordre de spawn) est un risque connu — voir `Unknown GameObject` dans [docs/status/OPEN_QUESTIONS.md](../../docs/status/OPEN_QUESTIONS.md). Préférer une résolution explicite (recherche par id stable, ancêtre) à une référence sérialisée fragile quand l'objet visé peut ne pas être présent partout au même moment.
- **Privilégier des identifiants stables pour les données persistantes** (items, conteneurs) plutôt que des identifiants dérivés de l'instance (ex. `GetHashCode()`, GUID de scène) qui ne survivent pas à une reconstruction réseau ou une sauvegarde.
- **Préciser l'autorité réseau pour tout composant de gameplay** : qui écrit l'état, qui le lit, ce qui est répliqué — voir [.claude/rules/multiplayer.md](multiplayer.md).
- **Ne pas ajouter de couche d'abstraction sans besoin concret.** Pas d'interface ou de système générique pour un seul cas d'usage actuel.
- **Code lisible et testable** — pas de règle de style arbitraire au-delà de ce que le projet a déjà adopté.
- **Ne jamais référencer un outil de développement externe depuis le runtime** — le code de `Code/`/`Editor/` doit compiler et fonctionner indépendamment de tout outillage tiers.

Ces règles ne sont pas des conventions arbitraires : elles reflètent des erreurs constatées sur l'ancienne version du projet (référencement fragile, gestion d'autorité peu claire) — voir [docs/development/LEGACY_REFERENCE_POLICY.md](../../docs/development/LEGACY_REFERENCE_POLICY.md). Elles seront affinées à mesure que du code réel existe dans `Code/`.
