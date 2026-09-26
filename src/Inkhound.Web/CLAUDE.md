# Inkhound.Web — Contexte

Backend ASP.NET Core 10 (.NET 10). Sert l'API REST, le Hub SignalR, l'auth JWT, et les fichiers statiques Angular (single-unit).
Dépend de `Inkhound.Core` et `Foundation.Core`.
Doit exposer en API les méthodes public de la class Inkhound.Core\InkhoundManager en les regroupant par ressource.

## Structure

```
Inkhound.Web/
├── Controllers/
│   ├── AuthController.cs         # POST /api/auth/login, GET /api/auth/me
│   ├── UserController.cs         # CRUD /api/users
│   ├── LibraryController.cs      # CRUD /api/libraries
│   ├── VolumeController.cs       # CRUD /api/volumes
│   ├── IssueController.cs        # CRUD /api/issues
│   ├── KavitaController.cs       # /api/kavita (test connexion, scan)
│   ├── FilesystemController.cs   # /api/filesystem (browse dossiers serveur)
│   ├── OptionsController.cs      # /api/options (settings app)
│   ├── SchedulerController.cs    # /api/scheduler — config + Run now (import downloads / rolling refresh / auto search / catalogue Bedetheque)
│   ├── BedethequeCatalogController.cs # /api/bedetheque/catalog — état par lettre + job de refresh du catalogue local
│   ├── ChatbotController.cs      # /api/chatbot — état d'exécution du bot Matrix + start/stop ponctuels
│   ├── DashboardController.cs    # GET /api/dashboard/stats — agrégats + « Most wanted »
│   ├── SystemController.cs       # /api/system/memory — instantané mémoire + purge/compaction manuelle
│   ├── Dtos/DownloadItemDto.cs   # DTO d'une ligne de download (dont CoverUrl : vignette de
│   │                             #   l'issue, à défaut du volume), partagé QBittorrent/Issue/Volume
│   └── JobsController.cs         # GET /api/jobs/{id} — statut d'un job (filet de rattrapage HTTP)
├── Auth/                         # JWT + schemes d'authentification (voir "Auth JWT" ci-dessous)
├── Hubs/AppHub.cs                # Hub SignalR — StateChanged, JobChanged, JobTrace
├── Middleware/ExceptionMiddleware.cs
├── Startup/InkhoundManagerInitializer.cs  # IHostedService — init au démarrage
├── Program.cs
└── data/system/                  # jwt-key.json (volume Docker) + users.json (legacy, sauvegarde inerte)
```

## Conventions controllers

```csharp
// Pattern standard — primary constructor, [ApiController], route préfixée /api/
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class VolumeController(IInkhoundManager manager) : ControllerBase
{
    private record VolumeDto(Guid Id, string Title, string Status);
    private static VolumeDto ToDto(Volume v) => new(v.Id, v.Title, v.Status.ToString());

    [HttpGet]
    public async Task<IActionResult> GetAll() =>
        Ok((await manager.GetVolumesAsync()).Select(ToDto));

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id) { ... }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateVolumeRequest req) { ... }
}
```

**Codes HTTP à retourner :**
| Situation | Code |
|---|---|
| Lecture OK | `Ok(dto)` — 200 |
| Création OK | `CreatedAtAction(...)` — 201 |
| Suppression OK | `NoContent()` — 204 |
| Introuvable | `NotFound()` — 404 |
| Données invalides | `BadRequest(new { message })` — 400 |
| Doublon | `Conflict(new { message })` — 409 |

**Règles controllers :**
- Zéro logique métier dans les actions — délégation totale à `IInkhoundManager` ou aux services
- Jamais exposer un modèle de persistance directement — toujours un DTO
- `try/catch` uniquement pour `KeyNotFoundException`, `InvalidOperationException`, `ArgumentException`
- Pas de `catch (Exception)` — le `ExceptionMiddleware` gère le reste

## ⚠️ Pattern controllers classiques uniquement

Ce projet utilise **`[ApiController]` + `ControllerBase`**, pas les Minimal APIs.
Le skill `aspnet-minimal-api-openapi` est installé mais **ne s'applique pas ici**.
Ne pas suggérer de migrer vers `app.MapGet(...)` ou `IEndpointRouteBuilder`.

## Auth JWT

- Clé auto-générée dans `data/system/jwt-key.json` inline dans `Program.cs` (`JwtKeyInitializer.cs` est du code mort, non enregistré — ne pas y toucher)
- Utilisateurs persistés dans la table SQLite `Users` (`Inkhound.Core.Models.User`, CRUD dans `InkhoundManager`) — `data/system/users.json` (ancien `FileUserStore`) n'est plus qu'une sauvegarde legacy inerte, importée une seule fois à la création de la table
- Mots de passe : PBKDF2/SHA-256, 100 000 itérations (`Inkhound.Core.Security.PasswordHasher`)
- **Un seul rôle : `admin`** — pas de notion de rôle multiple, tout principal authentifié a un accès total
- **Mode bootstrap ouvert** : tant qu'aucun utilisateur n'existe en base (`InkhoundManager.HasUsers == false`), le scheme "Smart" (`Program.cs`) route toute requête sans `X-Api-Key` vers `OpenAccessAuthenticationHandler`, qui authentifie systématiquement une identité virtuelle (`login="guest"`, `role="admin"`, non persistée) — l'app entière est alors utilisable sans connexion. Dès qu'un premier utilisateur réel est créé via `POST /api/users`, ce bypass cesse pour toutes les requêtes suivantes. Aucun garde-fou de suppression (auto-suppression, dernier utilisateur) : si `Users` redevient vide, l'app repasse naturellement en mode ouvert.
- Token transmis en header `Authorization: Bearer {token}`
- Pour SignalR : token en query string `?access_token={token}`

## Routes API

| Méthode | Route | Auth | Description |
|---|---|---|---|
| POST | `/api/auth/login` | public | Login, retourne JWT |
| GET | `/api/auth/me` | auth | Profil courant (fonctionne aussi en mode bootstrap ouvert) |
| GET/POST/PUT/DELETE | `/api/users` | auth | CRUD utilisateurs (rôle unique, pas de restriction supplémentaire) |
| GET/POST/PUT/DELETE | `/api/libraries` | admin | CRUD librairies — `DELETE ?deleteFiles=true` supprime aussi les répertoires des volumes sur disque (204, ou 200 `{ fileWarning }` si un répertoire n'a pas pu l'être) ; la suppression en base cascade volumes/issues/downloads/indexers (`InkhoundManager.DeleteLibraryAsync`) |
| GET | `/api/libraries/{id}/stats` | admin | Stats de l'encart de la page Library (volumes par statut/source, issues par statut, taille téléchargée, dernières dates d'activité) — `InkhoundManager.GetLibraryStatsAsync` |
| GET/POST/PUT/DELETE | `/api/volumes` | auth | CRUD volumes |
| POST / GET | `/api/volumes/search`, `/api/volumes/search/{jobId}` | admin | Recherche multi-source en job (`202 { jobId }`, puis résultat). Chaque `stats[]` porte `errorCode` (nullable) — `"CATALOG_NOT_LOADED"` = le catalogue local Bedetheque est vide, l'UI affiche un lien vers `/settings/bedetheque` |
| GET/POST/PUT/DELETE | `/api/issues` | auth | CRUD issues |
| GET/POST | `/api/kavita` | admin | Test + scan Kavita |
| GET | `/api/filesystem` | admin | Browse filesystem |
| GET/PUT | `/api/options` | admin | Paramètres app |
| GET/PUT | `/api/scheduler` | admin | Config du planificateur (4 tâches cron : import downloads / rolling refresh / auto search / catalogue Bedetheque — `AutoSearchMinScore` validé 0-100 même tâche désactivée) |
| POST | `/api/scheduler/run/{key}` | admin | Déclenche immédiatement une tâche (`ProcessDownloads` / `RollingRefresh` / `AutoSearch` / `BedethequeCatalog`) |
| GET | `/api/bedetheque/catalog` | admin | État du catalogue local Bedetheque : `loaded`, `totalSeries`, `oldestFetchUtc`/`newestFetchUtc`, `refreshRunning`, `letters[27]` (`letter`, `count`, `fetchedAtUtc`) |
| POST | `/api/bedetheque/catalog/refresh` | admin | Lance le job de refresh → `202 { jobId }`. Body `{ letterCount?, letters? }` (`letters` prioritaire, sinon rotation des `letterCount` plus anciennes, sinon les 27). `409` si un refresh est déjà en cours. |
| GET | `/api/qbittorrent/downloads` | admin | Liste paginée des downloads (`?status=` répétable, `page`, `pageSize`). ⚠️ Le statut de chaque ligne est **réévalué depuis qBittorrent et persisté** à chaque lecture — c'est le seul endroit où `IssueDownload.Status` est rafraîchi |
| GET | `/api/qbittorrent/downloads/stalled` | admin | Downloads bloqués (`?limit=`, défaut 5) — enrichis **puis** filtrés sur `Stalled` (section d'alerte du Dashboard) |
| GET | `/api/issues/{id}/downloads` | admin | Downloads d'une issue (carte de sa page détail) |
| GET | `/api/volumes/{id}/downloads` | admin | Downloads de toutes les issues d'un volume, en un appel (badges de la liste des issues) |
| GET | `/api/issues/{id}/bans` | admin | Torrents bannis pour cette issue (carte « Banned torrents » de la page Issue) |
| DELETE | `/api/issues/bans/{banId}` | admin | Lève un ban (204, 404 si inconnu) — le torrent est de nouveau scoré normalement |
| DELETE | `/api/qbittorrent/downloads/{id}` | admin | Supprime un download. `?removeTorrent=` (défaut false) retire aussi le torrent de qBittorrent ; `?ban=` (**défaut true**) mémorise le couple (issue, torrent) dans `IssueTorrentBans`. Réponse `{ torrentRemoved, deletedCount, banCount }` |
| GET | `/api/chatbot/status` | admin | État d'exécution du module Chatbot (connexion Matrix, compteurs, usage du modèle vision). Tout est en mémoire — les compteurs repartent de zéro à chaque redémarrage |
| POST | `/api/chatbot/start`, `/api/chatbot/stop` | admin | Démarre/arrête la boucle de synchronisation, effet immédiat. Ne modifie **pas** l'option `StartAtStartup`, qui ne pilote que le lancement automatique au démarrage de l'application. La configuration passe par `/api/options` comme pour tout module |
| GET | `/api/jobs/{id}` | auth | Statut courant d'un job (filet de rattrapage HTTP, voir section Jobs) |
| GET | `/api/dashboard/stats` | auth | Agrégats du Dashboard : KPI globaux, stats par library, volumes récents, et `mostWanted` (issues `MISSING` proches de compléter leur volume — voir `Inkhound.Core/CLAUDE.md`) |
| GET | `/api/system/memory` | admin | Instantané mémoire (working set, tas managé, fragmentation, budget GC, compteurs de collectes, `caches[]`). Lecture pure, aucune collecte déclenchée |
| POST | `/api/system/memory/compact` | admin | Vide tous les caches applicatifs puis force une collecte compactante (LOH inclus). **Bloquant ~1 s** ; ne récupère que le managé. Retourne `{ before, after, cacheEntriesRemoved, bytesFreed, durationSeconds }` |

## Jobs — exposition via les controllers

Les méthodes `LaunchJobXxx` d'`InkhoundManager` sont des opérations longues (voir `Inkhound.Core/CLAUDE.md`). Le controller les déclenche en **fire-and-forget** et retourne immédiatement `202 Accepted`.

```csharp
[HttpPost("...")]
public IActionResult StartXxx(Guid id)
{
    _ = manager.LaunchJobXxx(new XxxJobParameters { EntityId = id });
    return Accepted(new { message = "Job started." });
}
```

Quand le front doit suivre la progression sur la page (JobPanel), le `LaunchJobXxx` retourne le
`JobContext` et le controller renvoie `Accepted(new { jobId = job.JobId })` — cf. rematch / refresh /
regenerate-comic-info / analyze / **import dossier** (`POST /api/volumes/{id}/import`, précédé de
`GET /api/volumes/{id}/import/scan` pour la popup de revue fichiers ↔ issues) / **import fichier
issue** (`POST /api/issues/{id}/import { filePath }`, bouton « Import » de la page Issue).

`DELETE /api/volumes/{id}` accepte le query param **`deleteFiles`** (défaut `false`, case à cocher
de la popup de confirmation) : `true` supprime aussi récursivement le répertoire du volume et tous
ses fichiers. Trois réponses possibles — `404` volume inconnu, `204` suppression complète, et
`200 { fileWarning }` quand le volume a bien été supprimé en base mais que son répertoire n'a pas
pu l'être (verrou, droits, ou refus du garde-fou de confinement). Le front doit traiter ce `200`
comme un avertissement, pas comme un échec : la suppression a bien eu lieu.

`RefreshVolumeRequest` / `RefreshLibraryRequest` portent des cases/radios de la popup Refresh
(défaut `false` = comportement historique) : `SyncNewIssuesOnly` (`true` = ne synchroniser depuis
la source que les issues/albums encore inconnus), `RegenerateComicInfoNewOnly` (`true` = ne
réinjecter le `ComicInfo.xml` que dans les CBZ qui n'en ont pas) et `CheckFiles` (`true` = étape
avant le recalc de stats : vérifie présence disque + fraîcheur d'analyse CBZ de chaque issue
`DOWNLOADED`, fichier absent → `MISSING`). Voir `Inkhound.Core/CLAUDE.md`.

Hors job : `PATCH /api/volumes/{id}/status { status: "MONITORED" | "PAUSED" }` (bouton Pause /
Resume de la page Volume) → `200 VolumeDto`, `400` valeur hors MONITORED/PAUSED, `404`, `409` si le
volume est `COMPLETED`. Version en masse : `PATCH /api/libraries/{id}/volumes/status { status }`
(« Pause all » / « Resume all » de la page Library) → `200 { updated }`, `COMPLETED` jamais touchés.

Hors job : `DELETE /api/issues/{id}/file` (bouton « Delete file » de la page Issue) supprime le CBZ
de la librairie, remet l'issue à `MISSING` et purge l'analyse + le suivi de download associés
(`DeleteIssueFileAsync` → `NoContent` ou `BadRequest { message }`).

La progression est relayée en temps réel vers les clients via SignalR par `InkhoundManagerInitializer` (qui souscrit aux événements `OnJobUpdated` et `OnTrace` du manager et les diffuse via `AppHub`).

### Filet de rattrapage HTTP (`GET /api/jobs/{id}`)

Le broadcast SignalR est fire-and-forget (`Clients.All`, pas de buffer) : un client déconnecté au
moment d'un `ManagerJobChanged` (ex: app mobile mise en arrière-plan) le manque définitivement.
`JobsController.GetStatus(jobId)` interroge `manager.TryGetJob(jobId)` (cache `_recentJobs` dans
`BaseServiceManager`, voir `Foundation.Core/CLAUDE.md`) pour renvoyer l'état réel d'un job, y
compris peu après sa complétion (fenêtre `JobRetention`, 15 min par défaut). Le frontend
(`HubService.resyncTrackedJobs`) l'appelle à la reconnexion SignalR et au retour au premier plan
de la page. 404 si le job n'a jamais existé ou si sa fenêtre de rétention est dépassée — les deux
cas sont indistinguables (pas de registre permanent).

## SignalR Hub (`/hub/app`)

Événements émis par le serveur :
- `StateChanged` — patch d'état global (full au connect, partial ensuite)
- `ManagerJobChanged` — contexte complet du job en cours (état, progression)
- `ManagerTrace` — trace unitaire en temps réel (niveau, message, jobId)
- `ManagerDataUpdated` — notification de modification d'une entité (type + id)

## Gestion des erreurs

`ExceptionMiddleware` intercepte toutes les exceptions non gérées :
```
KeyNotFoundException        → 404
InvalidOperationException   → 409
ArgumentException           → 400
UnauthorizedAccessException → 401
Exception                   → 500 (message générique en prod)
```

Toujours enregistrer `app.UseMiddleware<ExceptionMiddleware>()` **en premier** dans le pipeline.

## Empreinte mémoire — ce que fait `Program.cs`

Voir le `CLAUDE.md` racine pour l'ensemble des réglages. Trois points propres à ce projet :

- **`ImageProcessingSetup.Configure()` est appelé en tête de `Program.cs`**, avant la construction du
  builder : le plafond du pool mémoire d'ImageSharp doit être posé avant tout décodage d'image du
  process. Ne pas déplacer cet appel plus bas.
- **`<ServerGarbageCollection>false</ServerGarbageCollection>` dans `Inkhound.Web.csproj`** — le SDK
  Web active le Server GC par défaut. ⚠️ La propriété s'appelle `ServerGarbageCollection`, **pas**
  `ServerGarbageCollector` : le mauvais nom compile sans broncher et ne fait rien. Vérifier
  `System.GC.Server` dans le `runtimeconfig.json` généré après toute modification.
- **Swagger est derrière `app.Environment.IsDevelopment()`** — le document OpenAPI était matérialisé
  en mémoire en production sans aucun consommateur.

## Logging

```csharp
// ✅ Toujours passer l'exception en premier paramètre
logger.LogError(ex, "Échec de {Operation} pour {Id}", operation, id);

// ❌ Perd la stack trace
logger.LogError("Erreur : " + ex.Message);
```
