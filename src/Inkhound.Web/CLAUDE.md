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
│   └── OptionsController.cs      # /api/options (settings app)
├── Auth/                         # JWT + UserStore fichier
├── Hubs/AppHub.cs                # Hub SignalR — StateChanged, JobChanged, JobTrace
├── Middleware/ExceptionMiddleware.cs
├── Startup/InkhoundManagerInitializer.cs  # IHostedService — init au démarrage
├── Program.cs
└── data/system/                  # jwt.key + users.json (volume Docker)
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

- Clé auto-générée dans `data/system/jwt.key` par `JwtKeyInitializer` (IHostedService)
- Mots de passe : PBKDF2/SHA-256, 100 000 itérations
- Deux rôles : `admin` (accès total) et `guest` (lecture seule profil)
- Token transmis en header `Authorization: Bearer {token}`
- Pour SignalR : token en query string `?access_token={token}`

## Routes API

| Méthode | Route | Rôle | Description |
|---|---|---|---|
| POST | `/api/auth/login` | public | Login, retourne JWT |
| GET | `/api/auth/me` | auth | Profil courant |
| GET/POST/PUT/DELETE | `/api/users` | admin | CRUD utilisateurs |
| GET/POST/PUT/DELETE | `/api/libraries` | admin | CRUD librairies |
| GET/POST/PUT/DELETE | `/api/volumes` | auth | CRUD volumes |
| GET/POST/PUT/DELETE | `/api/issues` | auth | CRUD issues |
| GET/POST | `/api/kavita` | admin | Test + scan Kavita |
| GET | `/api/filesystem` | admin | Browse filesystem |
| GET/PUT | `/api/options` | admin | Paramètres app |

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

La progression est relayée en temps réel vers les clients via SignalR par `InkhoundManagerInitializer` (qui souscrit aux événements `OnJobUpdated` et `OnTrace` du manager et les diffuse via `AppHub`).

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
