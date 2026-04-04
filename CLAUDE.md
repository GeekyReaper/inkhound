# Inkhound

## Contexte du projet

**Inkhound** est une application web self-hosted de gestion de bibliothèque digitale pour BD, Comics et Manga. Elle automatise l'ensemble du pipeline : de la découverte d'un titre jusqu'à une arborescence lisible par Kavita.

Le projet complet est décrit dans @docs/project.md.  
L'architecture technique est définie dans @docs/architecture.md.

## Instructions pour Claude

### Conventions de code
- Les commentaires dans le code sont en anglais
- Les commits sont en anglais
- Angular : composants standalone uniquement, pas de NgModule
- Angular : privilégier les Signals plutôt que RxJS/BehaviorSubject quand c'est possible
- Toujours cibler la dernière version d'Angular et d'ASP.NET Core

### Terminologie — noms exacts à respecter
- Utiliser `Volume`, `Issue`, `Library` — jamais `Comic`, `Chapter`, `Book` ou autre synonyme
- Les statuts `MONITORED`, `COMPLETED`, `FREEZE` (Volume) et `SEEKING`, `DOWNLOADING`, `DOWNLOADED` (Issue) sont figés

### Règles de travail
- Ne jamais modifier la définition des entités `VOLUME`, `ISSUE` ou `LIBRARY` sans confirmation explicite
- Ne jamais modifier la convention de nommage des CBZ (section 9.3 de project.md) sans confirmation
- Demander confirmation avant de créer de nouveaux fichiers
- Le code métier Inkhound va dans une library .NET séparée du projet backend

### Editeur
- VS Code uniquement, veiller au bon fonctionnement des fichiers launch.json et tasks.json
