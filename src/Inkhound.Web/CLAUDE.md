# Inkhound.Web — Contexte

Backend ASP.NET Core 9. Sert l'API REST, le Hub SignalR, l'auth JWT, et les fichiers statiques Angular (single-unit).
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
| GET/POST/PUT/DELETE | `/api/libraries` | admin | CRUD librairies |
| GET/POST/PUT/DELETE | `/api/volumes` | auth | CRUD volumes |
| GET/POST/PUT/DELETE | `/api/issues` | auth | CRUD issues |
| GET/POST | `/api/kavita` | admin | Test + scan Kavita |
| GET | `/api/filesystem` | admin | Browse filesystem |
| GET/PUT | `/api/options` | admin | Paramètres app |
| GET | `/api/jobs/{id}` | auth | Statut courant d'un job (filet de rattrapage HTTP, voir section Jobs) |

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

`RefreshVolumeRequest` / `RefreshLibraryRequest` portent `SyncNewIssuesOnly` (défaut `false` =
comportement historique « ALL issues ») : radio de la popup Refresh — `true` ne synchronise depuis
la source que les issues/albums encore inconnus (voir `Inkhound.Core/CLAUDE.md`).

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

## Logging

```csharp
// ✅ Toujours passer l'exception en premier paramètre
logger.LogError(ex, "Échec de {Operation} pour {Id}", operation, id);

// ❌ Perd la stack trace
logger.LogError("Erreur : " + ex.Message);
```
