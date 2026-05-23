# Foundation.Core — Contexte

Bibliothèque d'abstractions génériques, sans dépendance vers les autres projets Inkhound.
Tout ce qui est réutilisable indépendamment du domaine métier vit ici.

## Responsabilités

- `BaseService` / `BaseServiceManager` — classes de base pour les services avec cycle de vie
- `RateLimiter` — limitation de débit pour les appels API externes (ComicVine, etc.)
- `Interface/Common.cs` — interfaces génériques partagées
- `Model/Context.cs` — contexte d'exécution générique
- `Model/Definition.cs` — définitions de base
- `Model/State.cs` — modèle d'état générique (utilisé par le système de jobs et SignalR)

## Règles strictes

- **Zéro dépendance** vers `Inkhound.Core`, `Inkhound.Web` ou `Inkhound.client`
- Pas de référence à des entités métier (Volume, Issue, Library) — uniquement des abstractions
- Pas de référence à des services externes (ComicVine, Kavita)
- Tout type ajouté ici doit être générique et réutilisable hors contexte Inkhound

## Conventions C# dans ce projet

- Primary constructors C# 12 pour l'injection de dépendances
- Méthodes async suffixées `Async`
- Champs privés en `_camelCase`
- Interfaces préfixées `I` (`IBaseService`, etc.)
- Namespace : `Foundation.Core` + sous-namespace par dossier (`Foundation.Core.Model`)
- Un fichier = un type

## Quand ajouter quelque chose ici

✅ Un mécanisme de rate limiting générique
✅ Un wrapper de retry générique
✅ Une abstraction de service avec état (Init/Running/Done)
❌ Un modèle `Volume` ou `Issue` — ça va dans `Inkhound.Core/Models`
❌ Un appel à ComicVine — ça va dans `Inkhound.Core/ComicVine`
