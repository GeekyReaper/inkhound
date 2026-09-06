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
│   ├── User.cs          # Compte utilisateur (auth) — un seul rôle "admin", pas de propriété Role
│   ├── ArchiveJobParameters.cs
│   ├── SynchronizeLibraryJobParameters.cs
│   └── RegenerateComicInfoJobParameters.cs
├── Security/            # PasswordHasher.cs — PBKDF2/SHA-256, 100 000 itérations
├── ComicVine/           # Intégration API ComicVine
│   ├── ComicVineSourceService.cs
│   ├── ComicVineModels.cs
│   └── ComicVineOptions.cs
├── Bedetheque/          # Intégration bedetheque.com (scraping HTML — Serie = Volume, Album = Issue)
│   ├── BedethequeSourceService.cs
│   ├── BedethequeModels.cs
│   ├── BedethequeOptions.cs
│   └── BedethequeBlockedException.cs
├── Sources/             # Abstraction multi-source (ISourceService, SourceVolume/SourceIssue)
│   ├── ISourceService.cs
│   └── SourceModels.cs
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
- `SourceType` — `"ComicVine"`, `"bedetheque"` ou `"manual"` (valeur libre, non typée — voir `ISourceService.SourceKey` pour la clé canonique de chaque source)
- `AgeRating` — valeur de l'enum `AgeRating` ; écrire dans ComicInfo.xml via `ToKavitaString()` (jamais `.ToString()`)
- `CountOfIssues`/`CountOfDownloadedIssues` — recalculés par `InkhoundManager.RecalculateVolumeStatisticsAsync`, **restreints aux issues `Category == Standard`** : la complétude d'un volume (compteurs, barre de progression, transition vers `COMPLETED`) ignore volontairement les Omnibus/Hors-série/... — voir `Issue.Category` ci-dessous.
- `DateAdded` — sert uniquement au tri "Recently added" du Dashboard (`GetDashboardStatsAsync`, `OrderByDescending(v => v.DateAdded)`), aucun autre effet visible. **Tout chemin de création d'un `Volume` doit le renseigner** (avec `CreatedAt`/`UpdatedAt`) — un oubli ne casse rien à la compilation ni aux tests fonctionnels courants, il se traduit juste par un dashboard qui semble figé (bug historique corrigé en septembre 2026 sur ComicVine/Bedetheque/sync filesystem, voir `DbStorageService.ApplyPendingMigrationsAsync` pour le backfill des volumes déjà en base).

### Issue
Numéro individuel, toujours associé à un Volume.
```
Id, SourceId, VolumeId,
IssueNumber, Category (enum IssueCategory, stocké en string), Title, Year, Description,
Image (VolumeImage?), Authors (JSON [{Name, Role}]),
CbzFilename, FileSizeBytes,
DownloadedAt, PublishedAt,
Status (DOWNLOADING | DOWNLOADED | MISSING)
```
- `SourceId` — identifiant de l'issue dans sa source d'origine (ComicVine ou Bedetheque) ; la source elle-même se déduit du `SourceType` du `Volume` parent
- `CbzFilename` — nom du fichier CBZ final (sans chemin) ; `null` si l'issue n'est pas encore téléchargée
- `Category` — `Standard | Special | SpecialEdition | Omnibus | Roman | BestOf`, dérivée par `BedethequeAlbumClassifier` (voir section Bedetheque) ; toujours `Standard` pour ComicVine/manuel. `IssueNumber` est résolu conjointement (`Idx`, gap-filled par catégorie) — le couple `(Category, IssueNumber)` sert de repli de correspondance au rematch, `SourceId` restant toujours prioritaire.
  - Particularité du rematch Bedetheque (`RematchVolumeFromBedethequeAsync`) : contrairement aux autres champs "protégés" par statut, `IssueNumber`/`Category` sont recopiés **sans condition de `Status`** — y compris sur une issue déjà `DOWNLOADED` — pour corriger les valeurs historiquement fausses (issues téléchargées avant l'introduction de `BedethequeAlbumClassifier`, ex. `0`/`Standard` pour un hors-série). Si l'option "Regenerate ComicInfo" est cochée au Refresh, `RegenerateComicInfoForDownloadedIssuesAsync` renomme le fichier `.cbz` en conséquence (mécanisme générique déjà utilisé pour Title/Year). Le rematch ComicVine, lui, garde `IssueNumber` figé une fois l'issue téléchargée (`Status == MISSING` requis) — pas concerné par ce bug historique.
  - Mode **"NEW issues only"** du Refresh (`RematchVolumeJobParameters.SyncNewIssuesOnly`, radio de la popup, défaut UI) : la metadata Volume/Serie est synchronisée normalement, mais on ne récupère la page détail (`GetIssueAsync`/`GetAlbumAsync`) **que pour les `SourceId` source encore absents en base**, insérés en `MISSING` via `SyncNew{ComicVine,Bedetheque}IssuesAsync`/`AlbumsAsync`. Les issues déjà connues **ne sont pas touchées** (pas de maj metadata, pas de renumérotation `IssueNumber`/`Category`, pas de suppression d'orphelins). `RecalculateVolumeStatisticsAsync` tourne quand même. Limite assumée : les indices gap-fill des catégories non-Standard peuvent dériver tant qu'un Refresh **"ALL issues"** (`SyncNewIssuesOnly == false`, comportement historique complet) n'a pas été relancé. Le Rematch changement de série (`RematchFromSource`) reste toujours en mode complet.

### VolumeImage (record partagé Volume + Issue)
```
IconUrl, MediumUrl, ScreenUrl, ScreenLargeUrl, SmallUrl,
SuperUrl, ThumbUrl, TinyUrl, OriginalUrl, ImageTags
```
Toutes les URLs sont nullable — proviennent de ComicVine, peuvent être absentes.

## Statuts

**Volume.Status** (`VolumeStatus`)
- `MONITORED` — Inkhound cherche activement les issues en status MISSING
- `COMPLETED` — toutes les issues `Category == Standard` sont en status DOWNLOADED (les extras n'entrent pas en compte), aucune recherche
- `PAUSED` — suspendu manuellement, aucune recherche en cours.

**Issue.Status** (`IssueStatus`)
- `MISSING` — connue via ComicVine, introuvable localement
- `DOWNLOADING` — acquisition en cours
- `DOWNLOADED` — fichier traité et présent dans la librairie Kavita

**Issue.Category** (`IssueCategory`) — voir `BedethequeAlbumClassifier` dans la section Bedetheque
- `Standard` — tome classique (défaut ; seule valeur possible pour ComicVine/manuel)
- `Special` — hors-série (préfixe `HS*`)
- `SpecialEdition` — édition spéciale non classée ailleurs (repli par défaut) ; exception : une
  série à album unique tombée dans ce repli est ramenée à `Standard`/`1` (voir `NormalizeSingleAlbumSeries`)
- `Omnibus` — intégrale (préfixe `INT*`, ou titre contenant `" / "` / `"Tomes N à M"` / `"intégrale"`)
- `Roman` — roman/novélisation (préfixe `ROMAN*`)
- `BestOf` — compilation "Best Of" (préfixe `BO*`)

## Intégrations externes

### ComicVine
- Recherche de volumes : `GET /api/volumes/?filter=name:{query}`
- Issues d'un volume : `GET /api/issues/?filter=volume:{comicvineId}`
- Auth : API key en query param `?api_key={key}`
- Options dans `ComicVineOptions` (injectées via `IConfiguration`)
- Le `RateLimiter` de `Foundation.Core` est obligatoire sur tous les appels

### Bedetheque
- Site scrapé (pas d'API publique) — Serie = Volume, Album = Issue
- Recherche de séries : formulaire `/search/albums` (token CSRF + dédup par nom de série,
  puis résolution de l'ID réel via la page du premier album trouvé)
- Détail d'une série + liste des albums : `GET /serie-{id}-BD-x.html` — `GetSerieAsync` met en
  cache mémoire 24h (`_serieCache`). `GetSerieAsync(id, ct, forceRefresh: true)` ignore ce cache
  et le repeuple : le flux Refresh/Rematch (`RematchVolumeFromBedethequeAsync`) le passe pour
  qu'un tome ajouté récemment sur la source soit vu tout de suite ; recherche/enrichissement
  gardent `forceRefresh: false`.
- Détail d'un album (auteurs, EAN, ...) : `GET /BD-x-Tome-1-x-{id}.html`
- Pas d'authentification ; `CookieContainer` partagé + headers façon navigateur requis
  (le site bloque les requêtes qui ressemblent à du scraping automatisé)
- Options dans `BedethequeOptions` ; `RateLimiter` obligatoire, comme pour ComicVine
- Catégorisation des albums (`BedethequeAlbumClassifier`, port de `ClassifyAlbum` du projet
  `bdguest-scrapper`) : le préfixe de numérotation brut de chaque album (ex. `"1"`, `"HS1"`,
  `"INT FL"`) est extrait sur la page liste (`ParseAlbumList`, span `itemprop="name"` de la forme
  `"<préfixe> . <titre>"`) et classé en `Standard`/`Special`/`SpecialEdition`/`Omnibus`/`Roman`/
  `BestOf`. Les albums sans chiffre exploitable dans leur préfixe (ex. plusieurs `"INT FL"`) se
  voient attribuer un rang par `ResolveMissingIndices`, trié par année puis Id au sein de leur
  catégorie. `GetAllAlbumsForSerieAsync` reporte ce résultat (page liste, fiable pour toutes les
  catégories) sur chaque `BdAlbum` (page détail) avant mapping vers `Issue`.
  - **Règle one-shot** (`NormalizeSingleAlbumSeries`, appelée par `ParseAlbumList` juste après
    `Classify`) : une série réduite à **un seul album** tombé dans le repli `SpecialEdition`
    (préfixe absent/non reconnu) est normalisée en `(Standard, 1)` — aucune ambiguïté à lever
    quand il n'y a qu'un tome, et sans ça le volume affiche `CountOfIssues = 0`
    (`RecalculateVolumeStatisticsAsync` ne compte que les `Standard`). Les one-shots explicitement
    catalogués (`HS*`, `INT*`, `ROMAN*`, `BO*`, titre `"intégrale"` / `" / "`) ne sont pas touchés.

### Recherche multi-source
`InkhoundManager.SearchVolumesAsync` interroge en parallèle tous les `ISourceService`
enregistrés (`Services.Values.OfType<ISourceService>()`) et fusionne leurs résultats en un
seul `Page<SourceVolume>`, chaque entrée indiquant sa source (`SourceVolume.Source`). Les
flux "Ajouter à la bibliothèque"/"Rematch" restent branchés par source sur des modèles natifs
riches (`CvVolume`/`CvIssue` pour ComicVine, `BdSerie`/`BdAlbum` pour Bedetheque) via les
dispatchers `AddVolumeFromSourceAsync`/`RematchVolumeFromSourceAsync` — le DTO
`SourceVolume`/`SourceIssue` sert uniquement à l'affichage des résultats de recherche, pas à
la persistance (il n'a pas d'auteurs/genres).

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

Généré par `ArchiveService.BuildComicInfoDocument(Volume, Issue)`. Les auteurs sont regroupés par
`AuthorRole` (`src/Inkhound.Core/Models/AuthorRole.cs`), qui reconnaît à la fois le vocabulaire
anglais de ComicVine et le français scrapé sur Bedetheque ("Scénario", "Dessin", "Encrage",
"Couleurs", "Lettrage", "Couverture", "Traduction") — un rôle non reconnu est simplement ignoré.

| Champ modèle | Balise XML | Conversion |
|---|---|---|
| `Issue.Title ?? Volume.Title` | `<Title>` | — |
| `Volume.Title` | `<Series>` | — |
| `Issue.IssueNumber` | `<Number>` | — |
| `Issue.Category` (via `ToKavitaFormat()`) | `<Format>` | `Standard` → tag omis ; sinon mot-clé reconnu par Kavita (`Special`, `Omnibus`, `Compendium`, ...) pour router l'issue vers l'onglet "Specials" de la série, hors de la liste numérotée — voir `IssueCategoryExtensions` (`Models/Issue.cs`) |
| `Issue.Year ?? Volume.Year` | `<Year>` | — |
| `Issue.PublishedAt.Month` | `<Month>` | — |
| `Issue.Publisher ?? Volume.Publisher` | `<Publisher>` | éditeur album prioritaire |
| `Issue.Description ?? Volume.Description` | `<Summary>` | — |
| `Volume.Genres` + `Issue.Genre` | `<Genre>` | fusionnés, dédupliqués |
| `Volume.AgeRating` (via `ToKavitaString()`) | `<AgeRating>` | — |
| `Issue.Ean` | `<GTIN>` | — |
| `Volume.Language` | `<LanguageISO>` | nom complet FR → code ISO 639-1 (`LanguageToIso`) |
| `Issue.Collection` | `<Imprint>` | — |
| `Volume.Website` | `<Web>` | — |
| `Issue.AnalysisPageCount ?? Issue.OfficialPageCount` | `<PageCount>` | mesuré sur le CBZ prioritaire sur l'annoncé |
| `Issue.CommunityRating` | `<CommunityRating>` | échelle /10 → /5 (`valeur / 2`) |
| `Volume.Origin`, `Volume.PublicationStatus`, `Issue.LegalDepositDate`, `Issue.CommunityRatingCount` | `<Notes>` | concaténés en texte libre (aucun tag standard équivalent) |
| Auteurs (role=Writer) | `<Writer>` | — |
| Auteurs (role=Penciller) | `<Penciller>` | — |
| Auteurs (role=Artist) | `<Artist>` | — |
| Auteurs (role=Inker) | `<Inker>` | — |
| Auteurs (role=Colorist) | `<Colorist>` | — |
| Auteurs (role=Letterer) | `<Letterer>` | — |
| Auteurs (role=CoverArtist) | `<CoverArtist>` | — |
| Auteurs (role=Editor) | `<Editor>` | — |
| Auteurs (role=Translator) | `<Translator>` | — |

`Issue.Category` est exporté via `<Format>` (voir table ci-dessus) — Kavita n'a pas de notion de
"catégorie d'album" propre, mais son vocabulaire `Format` permet de router les non-Standard vers
l'onglet "Specials" de la série. La catégorie reste également affichée côté Angular indépendamment
(page volume, blocs "Issues"/"Extra") — les deux mécanismes coexistent, l'un pour Kavita, l'autre
pour l'UI Inkhound.

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
- `LaunchJobImportDirectory(ImportDirectoryJobParameters parameters)` — import des archives d'un
  dossier vers un volume ; `FileIssueMap` (nom de fichier → IssueId, issu de la popup de revue) ou
  appariement auto par numéro. Retourne le `JobContext` (le controller renvoie le `jobId`).
- `LaunchJobImportIssueFile(ImportIssueFileJobParameters parameters)` — import d'un fichier local
  unique comme CBZ d'une issue précise (bouton « Import » de la page Issue). Retourne le `JobContext`.

Le cœur « fichier → CBZ normalisé → dossier du volume → issue DOWNLOADED + stats » est factorisé
dans `ImportArchiveFileForIssueAsync(...)` (privé), partagé par `RunImportDirectoryJobAsync` et
`RunImportIssueFileJobAsync` ; il s'appuie sur `ImportArchiveAsync` (pipeline pur, sans job).

Les paramètres sont des classes dédiées dans `Models/`, implémentant `IJobParameters` (Foundation.Core) avec une méthode `IsValid()`.

Certaines méthodes `LaunchJobXxx` retournent le `JobContext` (setup synchrone + `_ = RunXxxJobAsync(job, …)`
en fire-and-forget) pour que le controller expose le `jobId` immédiatement — cf. `LaunchJobRematchVolume`,
`LaunchJobImportDirectory`.

`LaunchJobRefreshVolume` / `LaunchJobsRefreshLibrary` prennent un booléen `syncNewIssuesOnly`
(défaut `false` = comportement historique "ALL issues" ; le Rematch changement de série ne le
passe jamais) — voir `RematchVolumeJobParameters.SyncNewIssuesOnly` et la section Bedetheque.

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
- `DeleteIssueFileAsync` — supprime le CBZ de la librairie, remet l'issue à `MISSING`, purge les
  résultats d'analyse + les lignes `IssueDownload` de l'issue (torrent qBittorrent non touché),
  recalcule les stats du volume, déclenche un scan Kavita. Un seul `File.Delete` + un appel Kavita.

---

## Conventions C#

- Primary constructors C# 12
- Options injectées via `IOptions<T>` (ex : `IOptions<ComicVineOptions>`)
- Services enregistrés en `Singleton` ou `Scoped` selon qu'ils ont un état
- Namespace : `Inkhound.Core` + sous-namespace par dossier
- Mapper centralisé dans `Mapper.cs` — pas de mapping inline dans les services