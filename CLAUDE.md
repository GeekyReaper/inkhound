# Inkhound — Contexte global

Pipeline self-hosted de gestion de bibliothèque digitale BD, Comics et Manga.
Automatise le cycle complet : déclaration d'intention → acquisition → normalisation → export vers Kavita.

## Architecture — 4 projets

```
Inkhound.sln
└── src/
    ├── Foundation.Core      # Abstractions génériques réutilisables (BaseService, RateLimiter, modèles d'état,
    │                        #   socle Chatbot : bot Matrix + analyse d'image par LLM) — zéro PackageReference
    ├── Inkhound.Core        # Domaine métier : ComicVine, Kavita, BlobStorage, ArchiveGenerator, Chatbot, modèles
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
| Backend | ASP.NET Core 10 (.NET 10), C# 14 |
| Frontend | Angular (latest), CoreUI Free |
| Temps réel | SignalR |
| Auth | JWT (clé auto-générée, PBKDF2) |
| Base de données | SQLite (EF Core via DbStorageContext) |
| Métadonnées BD | ComicVine API |
| Chatbot | Matrix/Synapse (client HTTP maison) — démarrage auto ou manuel |
| Analyse d'image | LLM cloud Anthropic / Google (client HTTP maison, clés en options) |
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
| `DOTNET_GCConserveMemory` | `5` (Docker) | Priorité à la compaction sur le débit d'allocation — voir « Empreinte mémoire » |

## Docker

```bash
docker-compose up --build
# Accès sur http://localhost:8080
# Volume persistant : ./data (base SQLite + data/system + data/images)
```

## Empreinte mémoire

Le RSS du conteneur montait à ~1,5 Go sans jamais redescendre. Trois réglages structurels, à ne pas
défaire :

| Où | Réglage | Pourquoi |
|---|---|---|
| `Inkhound.Web.csproj` | `<ServerGarbageCollection>false</ServerGarbageCollection>` | Le SDK Web active le **Server GC** par défaut : un tas par cœur logique, collectes tardives. Inkhound est mono-utilisateur, le Workstation GC suffit et divise le RSS. ⚠️ La propriété s'appelle `ServerGarbageCollection`, **pas** `ServerGarbageCollector` — ce dernier nom compile sans erreur et ne fait rien (vérifier `System.GC.Server` dans le `runtimeconfig.json` généré). |
| `docker-compose.yml` | `mem_limit: 1g` | Sans limite cgroup, le GC prend la RAM de l'hôte comme budget et n'a aucune raison de compacter. Avec une limite, le runtime pose un `GCHeapHardLimit` à ~75 % de celle-ci. |
| `ImageProcessingSetup.Configure()` (appelé en tête de `Program.cs`) | Plafond de 128 Mo sur le `MemoryAllocator` d'ImageSharp | Son pool ne rend jamais ses blocs à l'OS : une analyse CBZ d'intégrale fixait durablement plusieurs centaines de Mo. |

La page **Settings > System** (`GET /api/system/memory`, `POST /api/system/memory/compact`) affiche
le working set, le tas managé, la fragmentation et le contenu des caches, et permet une purge
manuelle. Elle signale explicitement un Server GC actif ou une limite conteneur absente.

⚠️ SkiaSharp et PDFium allouent en mémoire **native**, invisible du GC : aucun `GC.Collect` ne la
récupère et elle ne déclenche jamais de collecte. C'est pourquoi la conversion d'images est
sérialisée (`ArchiveService.ImageDecodeGate`) et pourquoi chaque bitmap de page doit être disposé.

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
