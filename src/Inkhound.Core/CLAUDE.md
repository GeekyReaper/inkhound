# Inkhound.Core — Contexte

Cœur métier d'Inkhound. Contient tous les services domaine, les modèles, et les intégrations externes.
Dépend de `Foundation.Core`, pas de `Inkhound.Web`.

## Structure

```
Inkhound.Core/
├── Models/              # Entités domaine
│   ├── Library.cs       # Librairie Kavita (dossier racine)
│   ├── Volume.cs        # Série / volume (= COMIC dans le brief)
│   ├── Issue.cs         # Numéro individuel
│   ├── Page.cs          # Page d'une issue
│   ├── BlobAccess.cs    # Accès fichier binaire
│   ├── ArchiveJobParameters.cs
│   └── SynchronizeLibraryJobParameters.cs
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
Représente un dossier racine Kavita.
```
Id, Name, Path, KavitaLibraryId, CreatedAt
```

### Volume (= "Comic" dans le brief produit)
Série ou volume, toujours associé à une Library.
```
Id, SourceId, SourceType, LibraryId, Title, Year, Description,
Image (VolumeImage?), Publisher,
Authors (JSON [{Name, Role}]), Genres (JSON [string]),
Status (MONITORED | COMPLETED | PAUSED),
CountOfIssues, CountOfDownloadedIssues,
Issues (JSON [string]?), CreatedAt, UpdatedAt, DateAdded
```

### Issue
Numéro individuel, toujours associé à un Volume.
```
Id, ComicVineId, VolumeId, IssueNumber, Title, Year,
Description, Image (VolumeImage?),
Authors (JSON [{Name, Role}]),
FilePath, CbzFilename, FileSizeBytes,
DownloadedAt, PublishedAt,
Status (DOWNLOADING | DOWNLOADED | MISSING)
```

### VolumeImage (record partagé Volume + Issue)
```
IconUrl, MediumUrl, ScreenUrl, ScreenLargeUrl, SmallUrl,
SuperUrl, ThumbUrl, TinyUrl, OriginalUrl, ImageTags
```

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

## Conventions C#

- Primary constructors C# 12
- Options injectées via `IOptions<T>` (ex : `IOptions<ComicVineOptions>`)
- Services enregistrés en `Singleton` ou `Scoped` selon qu'ils ont un état
- Namespace : `Inkhound.Core` + sous-namespace par dossier
- Mapper centralisé dans `Mapper.cs` — pas de mapping inline dans les services