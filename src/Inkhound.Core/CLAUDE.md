# Inkhound.Core — Contexte

Cœur métier d'Inkhound. Contient tous les services domaine, les modèles, et les intégrations externes.
Dépend de `Foundation.Core`, pas de `Inkhound.Web`.

## Structure

```
Inkhound.Core/
├── Models/              # Entités domaine + paramètres de jobs
│   ├── Library.cs       # Librairie Kavita (dossier racine)
│   ├── Volume.cs        # Série / volume (= COMIC dans le brief)
│   ├── Issue.cs         # Numéro individuel
│   ├── Page.cs          # Page d'une issue
│   ├── AgeRating.cs     # Enum AgeRating + extension ToKavitaString()
│   ├── BlobAccess.cs    # Accès fichier binaire
│   ├── ArchiveJobParameters.cs
│   ├── SynchronizeLibraryJobParameters.cs
│   └── RegenerateComicInfoJobParameters.cs
├── ComicVine/           # Intégration API ComicVine
│   ├── ComicVineService.cs
│   ├── ComicVineModels.cs
│   └── ComicVineOptions.cs
├── Kavita/              # Intégration API Kavita
│   ├── KavitaService.cs
│   ├── KavitaOptions.cs
│   └── Models/
├── DbStorage/           # Persistance SQLite via EF Core
│   ├── DbStorageContext.cs
│   ├── DbStorageService.cs
│   └── DbStorageOption.cs
├── ComicArchiveGenerator/  # Génération CBZ + injection ComicInfo.xml
│   ├── ArchiveService.cs
│   └── ArchiveOption.cs
├── Blob/                # Gestion fichiers binaires (non utilisé)
│   └── BlobService.cs
├── Mapper.cs            # Mapping entre modèles domaine et DTOs
└── inkhoundManager.cs   # Orchestrateur principal des jobs
```

## Modèles domaine

### Library
Représente un dossier racine géré par Kavita.
```
Id, Name, Path, KavitaLibraryId, KavitaPath, CreatedAt, UpdatedAt
```
- `Path` — chemin tel que vu par Inkhound (ex: `Z:\BandeDessinee`)
- `KavitaLibraryId` — identifiant de la library dans Kavita (pour les scans)
- `KavitaPath` — chemin racine tel que vu par Kavita (ex: `/data/BandeDessinee`) ; peut différer de `Path` si les points de montage Docker diffèrent ; utilisé pour construire le chemin exact lors d'un scan ciblé sur le dossier d'un volume

### Volume (= "Comic" dans le brief produit)
Série ou volume, toujours associé à une Library.
```
Id, SourceId, SourceType, LibraryId,
Title, Year, Description, Image (VolumeImage?), Publisher,
Authors (JSON [{Name, Role}]), Genres (JSON [string]),
Status (MONITORED | COMPLETED | PAUSED),
AgeRating (enum AgeRating, stocké en string),
CountOfIssues, CountOfDownloadedIssues,
Issues (JSON [string]?),
CreatedAt, UpdatedAt, DateAdded
```
- `SourceType` — `"comicvine"` ou `"manual"`
- `AgeRating` — valeur de l'enum `AgeRating` ; écrire dans ComicInfo.xml via `ToKavitaString()` (jamais `.ToString()`)

### Issue
Numéro individuel, toujours associé à un Volume.
```
Id, ComicVineId, VolumeId,
IssueNumber, Title, Year, Description,
Image (VolumeImage?), Authors (JSON [{Name, Role}]),
CbzFilename, FileSizeBytes,
DownloadedAt, PublishedAt,
Status (DOWNLOADING | DOWNLOADED | MISSING)
```
- `CbzFilename` — nom du fichier CBZ final (sans chemin) ; `null` si l'issue n'est pas encore téléchargée

### VolumeImage (record partagé Volume + Issue)
```
IconUrl, MediumUrl, ScreenUrl, ScreenLargeUrl, SmallUrl,
SuperUrl, ThumbUrl, TinyUrl, OriginalUrl, ImageTags
```
Toutes les URLs sont nullable — proviennent de ComicVine, peuvent être absentes.

## Statuts

**Volume.Status** (`VolumeStatus`)
- `MONITORED` — Inkhound cherche activement les issues en status MISSING
- `COMPLETED` — toutes les issues sont en status DOWNLOADED, aucune recherche
- `PAUSED` — suspendu manuellement, aucune recherche en cours.

**Issue.Status** (`IssueStatus`)
- `MISSING` — connue via ComicVine, introuvable localement
- `DOWNLOADING` — acquisition en cours
- `DOWNLOADED` — fichier traité et présent dans la librairie Kavita

## Intégrations externes

### ComicVine
- Recherche de volumes : `GET /api/volumes/?filter=name:{query}`
- Issues d'un volume : `GET /api/issues/?filter=volume:{comicvineId}`
- Auth : API key en query param `?api_key={key}`
- Options dans `ComicVineOptions` (injectées via `IConfiguration`)
- Le `RateLimiter` de `Foundation.Core` est obligatoire sur tous les appels

### Kavita
- Déclenchement scan : `POST /api/libraries/scan`
- Auth : API key Kavita
- Options dans `KavitaOptions`

## Pipeline de traitement d'une Issue

```
Fichier brut (CBR/CBZ/ZIP/dossier)
  → Normalisation CBZ (ArchiveService)
  → Renommage Kavita : "{VolumeTitle} - {IssueNumber:000} - {IssueTitle} ({IssueYear}).cbz"
  → Génération ComicInfo.xml (données Issue + Volume)
  → Injection ComicInfo.xml dans le CBZ
  → Déplacement vers {Library.RootPath}/{Volume.Title} ({Volume.Year})/
  → Issue.Status = Downloaded
  → Appel Kavita scan
```

## Convention de nommage CBZ

```
Batman - 001.cbz
Batman - 002 - Le joker.cbz
Batman - 003 (2012).cbz
```

## Mapping ComicInfo.xml

| Champ modèle | Balise XML |
|---|---|
| `Volume.Title` | `<Series>` |
| `Issue.IssueNumber` | `<Number>` |
| `Issue.Title` | `<Title>` |
| `Issue.PublishedAt.Year` | `<Year>` |
| `Issue.PublishedAt.Month` | `<Month>` |
| `Volume.Publisher` | `<Publisher>` |
| `Volume.Authors` (role=writer) | `<Writer>` |
| `Volume.Authors` (role=penciller) | `<Penciller>` |
| `Volume.Genres` | `<Genre>` |
| `Issue.Description` | `<Summary>` |
| count(issues du volume) | `<Count>` |
| `Issue.ComicVineId` | `<Web>` |
| `Volume.AgeRating` (via `ToKavitaString()`) | `<AgeRating>` |

## Base de données SQLite

La persistance repose sur EF Core avec SQLite. Il n'y a **pas de migrations EF Core formelles** : le schéma est créé au premier démarrage via `EnsureCreated()`, puis évolué à chaud via des scripts idempotents.

### Règle : toute modification de schéma passe par `ApplyPendingMigrationsAsync`

**Fichier :** `src/Inkhound.Core/DbStorage/DbStorageService.cs`, méthode `ApplyPendingMigrationsAsync`.

Cette méthode est appelée automatiquement au démarrage, après `EnsureCreated()`. Elle vérifie si chaque colonne/table ajoutée existe déjà avant d'exécuter l'ALTER, ce qui la rend sûre à rejouer à chaque boot.

### Pattern obligatoire pour ajouter une colonne

```csharp
// 1. Vérifier si la colonne existe
var hasMyColumn = await db.Database
    .SqlQueryRaw<string>("SELECT name FROM pragma_table_info('TableName') WHERE name='MyColumn'")
    .AnyAsync();

// 2. L'ajouter seulement si absente
if (!hasMyColumn)
    await db.Database.ExecuteSqlRawAsync(
        "ALTER TABLE TableName ADD COLUMN MyColumn TEXT NOT NULL DEFAULT 'valeur'");
```

### Points importants

- **Ne jamais supprimer `EnsureCreated()`** — il crée le schéma initial pour les nouvelles installations.
- **Ne jamais utiliser `Database.Migrate()`** — incompatible avec `EnsureCreated()`.
- Les colonnes ajoutées via `ALTER TABLE` doivent avoir une valeur `DEFAULT` pour ne pas casser les lignes existantes.
- Les enums stockés en `string` (via `HasConversion<string>()` dans `DbStorageContext`) : la valeur `DEFAULT` du SQL doit correspondre à un nom de membre de l'enum valide (ex: `DEFAULT 'Unknown'`).
- Pour ajouter une **table entière**, utiliser `CREATE TABLE IF NOT EXISTS`.

---

## Jobs dans InkhoundManager

### Règle fondamentale

**Toute opération longue doit être un job.** Est considérée comme longue : toute opération qui traite plusieurs entités en boucle, effectue plusieurs appels successifs à des API externes (Kavita, ComicVine), ou manipule des fichiers sur le disque.

Un job diffuse sa progression et ses traces en temps réel via les événements `OnJobUpdated` et `OnTrace` de `BaseServiceManager` (relayés ensuite par la couche d'exposition).

### Nomenclature

Les méthodes qui créent un job suivent la convention :

```
LaunchJob{NomDeLOpération}(NomDeLOpérationJobParameters parameters)
```

Exemples existants :
- `LaunchJobSynchronizeLibrary(SynchronizeLibraryJobParameters parameters)`
- `LaunchJobArchiveIssue(ArchiveJobParameters parameters)`
- `LaunchJobRegenerateComicInfo(RegenerateComicInfoJobParameters parameters)`

Les paramètres sont des classes dédiées dans `Models/`, implémentant `IJobParameters` (Foundation.Core) avec une méthode `IsValid()`.

### Structure obligatoire d'un LaunchJob

```csharp
public async Task LaunchJobXxx(XxxJobParameters parameters)
{
    // Optionnel : charger une entité AVANT StartJob pour avoir un titre lisible
    var entity = await GetDb().Entities.FindAsync(parameters.EntityId);
    var jobTitle = entity is not null ? $"Xxx — {entity.Name}" : $"Xxx — {parameters.EntityId}";

    var job = StartJob(jobTitle, parameters);   // valide les paramètres via IsValid()
    job.SetState(JobState.RUNNING);
    try
    {
        // 1. Charger les données nécessaires
        // 2. Déclarer le total : job.CallbackHandler.UpdateTotal(count)
        // 3. Traiter en boucle avec progression :
        //    JobSendTrace($"[Xxx] Traitement de {item.Name}");
        //    await DoSomethingAsync(item);
        //    job.Progress.Increment(true);           // true = succès, false = erreur
        //    job.CallbackHandler.Callback(job.Progress);
        // 4. Émettre OnDataUpdated si des entités ont changé
        EndJob(true);
    }
    catch (Exception ex)
    {
        JobSendTrace($"[Xxx] Erreur inattendue : {ex.Message}", ETraceLevel.ERROR);
        EndJob(false);
    }
}
```

### Ce qui N'est PAS un job

Les opérations simples et rapides restent des méthodes `async Task<T>` classiques sans job :
- Lecture / écriture d'une seule entité en base
- Patch d'un champ (ex: `UpdateVolumeAgeRatingAsync`)
- Appel unique à une API externe sans boucle

---

## Conventions C#

- Primary constructors C# 12
- Options injectées via `IOptions<T>` (ex : `IOptions<ComicVineOptions>`)
- Services enregistrés en `Singleton` ou `Scoped` selon qu'ils ont un état
- Namespace : `Inkhound.Core` + sous-namespace par dossier
- Mapper centralisé dans `Mapper.cs` — pas de mapping inline dans les services