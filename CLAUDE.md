# Inkhound — Contexte global

Pipeline self-hosted de gestion de bibliothèque digitale BD, Comics et Manga.
Automatise le cycle complet : déclaration d'intention → acquisition → normalisation → export vers Kavita.

## Architecture — 4 projets

```
Inkhound.sln
└── src/
    ├── Foundation.Core      # Abstractions génériques réutilisables (BaseService, RateLimiter, modèles d'état)
    ├── Inkhound.Core        # Domaine métier : ComicVine, Kavita, BlobStorage, ArchiveGenerator, modèles
    ├── Inkhound.Web         # ASP.NET Core MVC + SignalR — API REST + Hub + Auth JWT + SPA host
    ├── Inkhound.client      # Angular SPA — frontend (CoreUI)
    └── Inkhound.Console     # Console runner (jobs manuels / debug)
```

**Dépendances entre projets :**
- `Inkhound.Web` → `Inkhound.Core` → `Foundation.Core`
- `Inkhound.client` consomme l'API de `Inkhound.Web`
- `Foundation.Core` n'a aucune dépendance vers les autres projets

## Stack technique

| Couche | Choix |
|---|---|
| Backend | ASP.NET Core 9, C# 12 |
| Frontend | Angular (latest), CoreUI Free |
| Temps réel | SignalR |
| Auth | JWT (clé auto-générée, PBKDF2) |
| Base de données | SQLite (EF Core via DbStorageContext) |
| Métadonnées BD | ComicVine API |
| Lecture | Kavita (instance locale) |
| Déploiement | Docker single-unit |

## Lancement local (sans Docker)

```bash
# Backend (terminal 1) — port configurable via APP_PORT
export APP_PORT=5000
cd src/Inkhound.Web
dotnet watch run

# Frontend (terminal 2)
cd src/Inkhound.client
npm start
# Angular sur http://localhost:4200, proxy vers http://localhost:5000
```

## Variables d'environnement

| Variable | Défaut | Description |
|---|---|---|
| `APP_PORT` | `5000` | Port Kestrel |
| `ASPNETCORE_ENVIRONMENT` | `Production` | `Development` en local |

## Docker

```bash
docker-compose up --build
# Accès sur http://localhost:8080
# Volume persistant : ./src/Inkhound.Web/data/system (jwt.key + users.json)
```

## Skills installés

Les skills suivants sont actifs dans `.claude/skills/` et s'appliquent à tout le projet :

- `angular-component` — conventions de génération de composants Angular
- `angular-best-practices-material` — bonnes pratiques Angular
- `dotnet-best-practices` — conventions .NET / C#

> ⚠️ `aspnet-minimal-api-openapi` est installé mais **ne s'applique pas** — le backend utilise des controllers classiques `[ApiController]`, pas les Minimal APIs. Ignorer les suggestions de ce skill.

## Conventions transversales

- Langue du code : **anglais** (noms de classes, méthodes, variables)
- Commentaires et documentation : **français**
- Pas de secrets dans le code — tout passe par `appsettings.json` ou variables d'environnement
- Un fichier = un type (C#), un composant = un dossier (Angular)

## Documentation

- `docs/architecture.md` — architecture détaillée, patterns SignalR, auth, jobs
- `docs/project.md` — brief produit, modèle de données, flux métier

## Git

- Les commits doivent être faits sur la branche **master**
- Avant chaque commit, mettre à jour les fichiers `CLAUDE.md` à la racine de chaque projet concerné
  par les changements
