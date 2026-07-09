# Claude Bridge

Claude Bridge (`Libraries/sboxskinsgg.claudebridge/`, exposé via le serveur MCP `plugin:sbox-claude:sbox`) est un **outil de développement uniquement**. Kodoku ne doit jamais en dépendre au runtime : l'absence de Claude Bridge ne doit jamais empêcher le projet de compiler ou de fonctionner. C'est une Library tierce, pas une brique de l'architecture du jeu — voir [docs/architecture/PROJECT_ARCHITECTURE.md](../../docs/architecture/PROJECT_ARCHITECTURE.md).

## Autorisé par défaut (lecture / diagnostic)

- Inspection de la scène (`get_scene_hierarchy`, `get_selected_objects`, `find_objects`)
- Lecture de composants et de propriétés (`get_property`, `get_all_properties`, `describe_type`)
- Lecture des logs (`read_log`, `get_compile_errors`)
- Vérification des erreurs de compilation
- Captures d'écran (`take_screenshot`, `screenshot_from`, `screenshot_orbit`)
- Diagnostic (`get_bridge_status`, `get_project_info`, `get_network_status`, `networking_lint`, `sandbox_lint`)
- Consultation de la documentation et de l'API (`search_docs`, `search_types`, `get_doc_page`, `get_method_signature`)

## Toute modification via Claude Bridge (scène, composants, assets, projet)

Une action de modification (create/set/delete/instantiate/etc.) doit systématiquement :

1. avoir été explicitement demandée dans le **message courant** — une autorisation donnée dans un message précédent ne vaut pas pour la suite ;
2. être effectuée sur une branche autre que `main` ;
3. rester limitée au périmètre demandé ;
4. être suivie d'une vérification de compilation (`get_compile_errors`) ;
5. être suivie d'une vérification Git (`git status`) pour confirmer l'étendue réelle du changement.

## Interdit par défaut

- Exécution libre de C# (`execute_csharp`) sans demande explicite et scope précis
- Commandes console destructrices (`console_run` avec des commandes de suppression/reset)
- Suppressions (`delete_gameobject`, `delete_script`) sans confirmation explicite
- Installation d'assets (`install_asset`) sans validation
- Modification de la configuration projet (`set_project_config`)
- Publication (rien dans ce dépôt ne doit être publié via le bridge)
- Modification interne de Claude Bridge lui-même (son code sous `Libraries/sboxskinsgg.claudebridge/`) sans demande explicite dédiée à cet outil
- Toute utilisation de Claude Bridge référencée depuis le code runtime de Kodoku (`Code/`, `Editor/`)

## Missions documentaires

Pendant une mission exclusivement documentaire, Claude Bridge ne doit être utilisé qu'en lecture (inspection, logs, hiérarchie, vérification d'informations) — jamais pour modifier la scène ou le projet, même en dehors de `main`.
