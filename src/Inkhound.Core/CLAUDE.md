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
│   ├── RegenerateComicInfoJobParameters.cs
│   └── AutoSearchVolumeJobParameters.cs  # job Auto search (VolumeId + MinScore)
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
├── inkhoundManager.cs   # Orchestrateur principal des jobs
├── inkhoundManager.Scheduler.cs   # partial — boucle cron + tâches planifiées
└── inkhoundManager.AutoSearch.cs  # partial — job Auto search (acquisition auto via Prowlarr)
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
CreatedAt, UpdatedAt, DateAdded, LastRefreshedAt (DateTime?), LastAutoSearchAt (DateTime?)
```
- `SourceType` — `"ComicVine"`, `"bedetheque"` ou `"manual"` (valeur libre, non typée — voir `ISourceService.SourceKey` pour la clé canonique de chaque source)
- `AgeRating` — valeur de l'enum `AgeRating` ; écrire dans ComicInfo.xml via `ToKavitaString()` (jamais `.ToString()`)
- `CountOfIssues`/`CountOfDownloadedIssues` — recalculés par `InkhoundManager.RecalculateVolumeStatisticsAsync`, **restreints aux issues `Category == Standard`** : la complétude d'un volume (compteurs, barre de progression, transition vers `COMPLETED`) ignore volontairement les Omnibus/Hors-série/... — voir `Issue.Category` ci-dessous.
  - Section **« Most wanted »** du Dashboard (`GetDashboardStatsAsync` → `DashboardStats.MostWanted`) : issues `MISSING` de catégorie `Standard` dont l'acquisition rapproche leur volume de 100 %. Éligibles = volumes `MONITORED` (les `PAUSED`/`COMPLETED` sont exclus) à qui il manque au plus `MostWantedMaxMissing` (= 2) issues Standard — comptage `GROUP BY` en SQL, puis chargement borné de ces seules issues. Tri par `InkhoundManager.RankMostWanted` (pur, testé par `MostWantedRankerTests`) : moins d'issues manquantes d'abord, puis plus grand `CountOfIssues` (compléter une longue série prime), puis complétude actuelle ; `Take MostWantedRowLimit` (= 6). Lecture pure — pas de job, pas de migration.
  - **Stats par bibliothèque** (`DashboardLibraryStats`) : les compteurs `IssuesCount` /
    `DownloadedIssuesCount` / `DownloadingIssuesCount` / `MissingIssuesCount` sont établis sur les
    issues réelles (`GROUP BY LibraryId, Status`, **toutes catégories**), et non sur
    `Volume.CountOfIssues`/`CountOfDownloadedIssues` qui ne retiennent que les `Standard`. C'est ce
    qui aligne l'encart Libraries sur la carte « Issues » globale, et ce qui permet à la barre du
    Dashboard d'avoir un segment par statut. Deux requêtes groupées, pas un aller-retour par
    bibliothèque (`Volume`/`Issue` n'ayant aucune navigation EF, les jointures sont explicites).
- `DateAdded` — sert uniquement au tri "Recently added" du Dashboard (`GetDashboardStatsAsync`, `OrderByDescending(v => v.DateAdded)`), aucun autre effet visible. **Tout chemin de création d'un `Volume` doit le renseigner** (avec `CreatedAt`/`UpdatedAt`) — un oubli ne casse rien à la compilation ni aux tests fonctionnels courants, il se traduit juste par un dashboard qui semble figé (bug historique corrigé en septembre 2026 sur ComicVine/Bedetheque/sync filesystem, voir `DbStorageService.ApplyPendingMigrationsAsync` pour le backfill des volumes déjà en base).
- `LastRefreshedAt` — `DateTime?`, `null` = jamais synchronisé depuis l'ajout. Estampillé à chaque synchro source réussie (`RunRematchVolumeJobAsync` → `StampVolumeLastRefreshedAsync`, refresh manuel comme job de roulement). Sert au tri du « rolling refresh » du scheduler (voir section Scheduler) et est affiché sur la page Volume (« Last refreshed », `VolumeDto.LastRefreshedAt`) ; distinct de `UpdatedAt` qui bouge aussi au recalcul de stats / à l'édition.
- `LastAutoSearchAt` — `DateTime?`, `null` = jamais traité par la tâche **Auto search** du scheduler (voir section Scheduler). Estampillé **avant** le lancement du job, uniquement par `RunScheduledAutoSearchAsync` (une recherche Prowlarr manuelle ne le touche pas). Sert au tri de rotation de cette tâche ; affiché sur la page Volume (« Last auto search », `VolumeDto.LastAutoSearchAt`).

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
  - Particularité du rematch Bedetheque (`RematchVolumeFromBedethequeAsync`) : contrairement aux autres champs "protégés" par statut, `IssueNumber`/`Category` sont recopiés **sans condition de `Status`** — y compris sur une issue déjà `DOWNLOADED` — pour corriger les valeurs historiquement fausses (issues téléchargées avant l'introduction de `BedethequeAlbumClassifier`, ex. `0`/`Standard` pour un hors-série). `RenameIssueFilesIfNeededAsync` renomme le fichier `.cbz` en conséquence au Refresh (mécanisme générique déjà utilisé pour Title/Year, sans condition de case cochée). Le rematch ComicVine, lui, garde `IssueNumber` figé une fois l'issue téléchargée (`Status == MISSING` requis) — pas concerné par ce bug historique.
  - Mode **"NEW issues only"** du Refresh (`RematchVolumeJobParameters.SyncNewIssuesOnly`, radio de la popup, défaut UI) : la metadata Volume/Serie est synchronisée normalement, mais on ne récupère la page détail (`GetIssueAsync`/`GetAlbumAsync`) **que pour les `SourceId` source encore absents en base**, insérés en `MISSING` via `SyncNew{ComicVine,Bedetheque}IssuesAsync`/`AlbumsAsync`. Les issues déjà connues **ne sont pas touchées** (pas de maj metadata, pas de renumérotation `IssueNumber`/`Category`, pas de suppression d'orphelins). `RecalculateVolumeStatisticsAsync` tourne quand même. Limite assumée : les indices gap-fill des catégories non-Standard peuvent dériver tant qu'un Refresh **"ALL issues"** (`SyncNewIssuesOnly == false`, comportement historique complet) n'a pas été relancé. Le Rematch changement de série (`RematchFromSource`) reste toujours en mode complet.
  - Mode **"NEW only"** de la case *Regenerate ComicInfo.xml* du Refresh (`RematchVolumeJobParameters.RegenerateComicInfoNewOnly`, radio de la popup, défaut UI) : `RegenerateComicInfoForDownloadedIssuesAsync(newOnly: true)` ne (ré)injecte le `ComicInfo.xml` **que dans les CBZ qui n'en contiennent pas déjà un** (`ArchiveService.CbzContainsComicInfo`, lecture seule) — cible les issues sideloadées (torrent, import manuel). Les renommages de fichier sont une étape distincte et inconditionnelle (`RenameIssueFilesIfNeededAsync`, cf. « Renommages au Refresh ») ; un fichier renommé est toujours réinjecté, même en mode "NEW only" (métadonnées à jour). `false` (défaut backend, Rematch inclus) = réécriture dans toutes les issues `DOWNLOADED`, nécessaire quand la metadata du volume a changé.

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
- **Nettoyage des titres scrapés** — `CleanScrapedText` (décodage HTML + fusion de tous les blancs)
  est obligatoire sur tout texte issu d'un `InnerText` : HtmlAgilityPack restitue l'indentation du
  HTML source, d'où des titres comme `"INT2.\n                    L'Intégrale - Tomes 4 à 6"`
  stockés tels quels en base puis recopiés dans les noms de fichiers CBZ. Un simple `.Trim()` ne
  suffit pas (il ne touche pas les blancs internes).
  `CleanAlbumTitle` y ajoute le retrait du préfixe de numérotation : coupe sur le `" . "` littéral
  (codes à espace, `"INT FL . …"`), sinon retire un préfixe accolé — `"1."`, mais aussi `"HS1."` /
  `"INT01."`, que l'ancienne expression ancrée sur `^\d+` laissait passer. Un chiffre reste exigé et
  les lettres de tête sont bornées, pour ne jamais tronquer un titre comme
  `"L'Intégrale - Tomes 4 à 6"`.
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
  → Renommage Kavita : "{VolumeTitle} - [{CategoryCode} - ]{IssueNumber:000} - {IssueTitle} ({IssueYear}).cbz"
  → Génération ComicInfo.xml (données Issue + Volume)
  → Injection ComicInfo.xml dans le CBZ
  → Déplacement vers {Library.RootPath}/{Volume.Title} ({Volume.Year})/
  → Issue.Status = Downloaded
  → Appel Kavita scan
```

### Ordre des pages à l'extraction

Les entrées d'une archive source (CBZ / CBR / TAR) sont triées avec
`NaturalSortComparer` (`CbzQuality/Analysis/`), **jamais** par comparaison de chaînes
brute : beaucoup d'archives utilisent des index non zéro-paddés (`index-10_1.jpg`,
`index-100_1.jpg`, `index-11_1.jpg`), qu'un tri lexicographique désordonne
irrémédiablement — les pages sont ensuite renommées séquentiellement, ce qui fige
l'erreur dans le CBZ produit.

Les pages extraites sont nommées `page_{index:D4}` (`page_0001` …). Le padding sur
4 chiffres est nécessaire car Kavita lit les entrées d'un CBZ dans l'ordre
alphabétique : sur 3 chiffres, `page_1000` passerait avant `page_999` au-delà de
999 pages (intégrales).

## Convention de nommage CBZ

```
Batman - 001.cbz
Batman - 002 - Le joker.cbz
Batman - 003 (2012).cbz
Batman - INT - 001 - Integrale 1.cbz     (issue non Standard)
```

### Code de catégorie dans le nom de fichier

`ArchiveService.GetPath(issue, volume, …)` intercale entre le titre du volume et le numéro un code
court issu de `IssueCategoryExtensions.ToFilenameCode()` (`Models/Issue.cs`) :

| Catégorie | Code |
|---|---|
| `Standard` | *(aucun — segment omis)* |
| `Special` | `HS` |
| `SpecialEdition` | `SP` |
| `Omnibus` | `INT` |
| `Roman` | `ROM` |
| `BestOf` | `BO` |

Deux raisons : `IssueNumber` n'est unique qu'**au sein d'une catégorie** (l'intégrale `INT1` et le
tome 1 portent tous deux `IssueNumber = 1`), et le tri alphabétique du dossier garde la série
principale groupée au lieu d'y intercaler hors-séries et intégrales. `Standard` n'a volontairement
pas de code : un chiffre trie avant une lettre, donc les tomes classiques restent en tête, et les
fichiers déjà nommés ne bougent pas.

### Normalisation des titres (`ArchiveService.NormalizeTitle`)

Diacritiques supprimés, seuls les alphanumériques et `-` conservés, **tout le reste devient un
espace — mais jamais deux d'affilée**. Sans cette fusion, la ponctuation produit autant d'espaces
qu'elle compte de caractères (`"Boing ! Boing !"` → `"Boing   Boing"`), et un titre scrapé
contenant l'indentation HTML de la page source donne un nom de fichier béant.

Un `Volume` n'a pas de champ `Path` : son dossier est **toujours recalculé** depuis `Title`/`Year`.
Toute évolution de `NormalizeTitle` change donc le nom attendu des dossiers déjà sur disque —
d'où `ArchiveService.FindExistingVolumeDirectory(volume, library)`, qui retourne le dossier
réellement présent : le chemin calculé s'il existe, sinon un dossier frère qui n'en diffère que par
les blancs. C'est lui qui alimente `oldFolderPath`.

### Renommages au Refresh

Deux étapes **inconditionnelles** de `RunRematchVolumeJobAsync`, dans cet ordre imposé — elles ne
dépendent d'aucune case de la popup Refresh, et s'exécutent **après le sync source** (les
métadonnées doivent être à jour) mais **avant "Check files"** :

1. **Le dossier de la série** — `RenameVolumeDirectoryIfNeededAsync`. `oldFolderPath` est capturé
   *avant* le sync source, qui écrase `Title`/`Year`.
2. **Les fichiers CBZ** — `RenameIssueFilesIfNeededAsync` : toute issue `DOWNLOADED` dont
   `CbzFilename` diffère du nom calculé est renommée, que le titre ait changé à la source ou que la
   normalisation ait évolué. Retourne les ids renommés, passés ensuite à
   `RegenerateComicInfoForDownloadedIssuesAsync` pour forcer la réécriture de leur `ComicInfo.xml`
   même en mode "NEW only".

L'ordre est critique : "Check files" sonde les fichiers sous le dossier recalculé et repasserait des
issues en `MISSING` si dossier ou fichiers portaient encore leur ancien nom.

> ⚠️ Ces renommages ont été rattachés à `RegenerateComicInfoForDownloadedIssuesAsync` par le passé,
> donc soumis à la case *Regenerate ComicInfo.xml*. Ne pas y revenir : un changement de
> normalisation laissait alors des fichiers au nom périmé indéfiniment, sans aucune case pour les
> rattraper (constaté en septembre 2026).

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
  appariement auto par numéro — **réservé aux issues `Category == Standard`** (même règle côté
  torrent dans `GrabPackSelectiveAsync` : seul un override manuel peut cibler un hors-série/omnibus).
  Retourne le `JobContext` (le controller renvoie le `jobId`).
- `LaunchJobImportIssueFile(ImportIssueFileJobParameters parameters)` — import d'un fichier local
  unique comme CBZ d'une issue précise (bouton « Import » de la page Issue). Retourne le `JobContext`.

Le cœur « fichier → CBZ normalisé → dossier du volume → issue DOWNLOADED + stats » est factorisé
dans `ImportArchiveFileForIssueAsync(...)` (privé), partagé par `RunImportDirectoryJobAsync` et
`RunImportIssueFileJobAsync` ; il s'appuie sur `ImportArchiveAsync` (pipeline pur, sans job).

Les paramètres sont des classes dédiées dans `Models/`, implémentant `IJobParameters` (Foundation.Core) avec une méthode `IsValid()`.

Certaines méthodes `LaunchJobXxx` retournent le `JobContext` (setup synchrone + `_ = RunXxxJobAsync(job, …)`
en fire-and-forget) pour que le controller expose le `jobId` immédiatement — cf. `LaunchJobRematchVolume`,
`LaunchJobImportDirectory`.

`LaunchJobRefreshVolume` / `LaunchJobsRefreshLibrary` prennent les booléens `syncNewIssuesOnly`,
`regenerateComicInfoNewOnly` et `checkFiles` (défaut `false` = comportement historique ; le Rematch
changement de série ne les passe jamais) — cases/radios de la popup Refresh, voir
`RematchVolumeJobParameters` et la section Issue.

Étape **« Check files »** (`RematchVolumeJobParameters.CheckFiles`) dans `RunRematchVolumeJobAsync`,
exécutée **avant le recalc de stats** (donc avant `RegenerateComicInfo`) via `CheckVolumeFilesAsync`
— pour chaque issue `DOWNLOADED` avec un `CbzFilename` : fichier absent du disque → `MISSING`
(`ClearIssueDownloadState` + purge des `IssueDownload`) ; fichier présent jamais analysé → analyse
CBZ ; fichier présent déjà analysé mais SHA-256 courant ≠ `Issue.AnalysisFileHash` → ré-analyse. Si
elle repasse ≥1 issue en `MISSING`, le job force un `RecalculateVolumeStatisticsAsync` même après un
sync. Helpers factorisés : `ClearIssueDownloadState(Issue)` (partagé avec `DeleteIssueFileAsync`) et
`AnalyzeIssueFileAsync(...)` (partagé avec `RunAnalyzeIssueJobAsync` — écrit les champs `Analysis*`
+ `AnalyzedAt`, ne sauve pas).

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
- `UpdateVolumeStatusAsync(id, MONITORED | PAUSED)` — bascule manuelle Pause/Resume d'un volume
  **incomplet** (bouton de la page Volume). `InvalidOperationException` (→ 409) si le volume est
  `COMPLETED`. Repasse ensuite par `RecalculateVolumeStatisticsAsync`, qui ne touche jamais un
  `PAUSED` et tranche seul MONITORED/COMPLETED pour les autres — un volume complété pendant sa
  pause redevient donc `COMPLETED` (et non `MONITORED`) au Resume. Les tâches Auto search / Most
  wanted n'éligibilisent que les `MONITORED`.
- Appel unique à une API externe sans boucle
- `DeleteIssueFileAsync` — supprime le CBZ de la librairie, remet l'issue à `MISSING`, purge les
  résultats d'analyse + les lignes `IssueDownload` de l'issue (torrent qBittorrent non touché),
  recalcule les stats du volume, déclenche un scan Kavita. Un seul `File.Delete` + un appel Kavita.
- `DeleteVolumeAsync(id, deleteFiles)` — voir ci-dessous.

### `DeleteVolumeAsync(Guid id, bool deleteFiles = false)`

Retourne `(bool Found, string? FileWarning)`. Supprime le volume, ses issues et leurs lignes
`IssueDownload`.

Avec `deleteFiles: true`, le répertoire du volume dans la librairie est supprimé **récursivement**
(fichiers étrangers au pipeline compris) **avant** la suppression en base — le chemin se calcule
depuis le volume et la librairie, qui n'existent plus après. Un échec disque n'annule pas la
suppression en base : il est remonté dans `FileWarning`, jamais levé.

⚠️ **Garde-fou de confinement (`DeleteVolumeDirectory`)** — `ArchiveService.GetPath` normalise le
titre en ne conservant que les alphanumériques : un titre sans aucun caractère retenu (ex. `"???"`)
et sans année produit un nom de dossier vide, donc un chemin **égal à la racine de la librairie**.
Un `Directory.Delete(recursive: true)` y effacerait toute la librairie. La suppression est donc
refusée (avec un `FileWarning`) si le nom de dossier est vide ou si le chemin résolu n'est pas un
sous-dossier **strict** de `Library.Path`. Toute évolution du calcul de chemin doit conserver cette
vérification.

Le scan Kavita post-suppression utilise `ScanKavitaLibraryAsync(library.KavitaLibraryId)` et **non**
`TriggerKavitaScanAsync(volumeId)` : ce dernier relit le volume en base (disparu) et scanne un
dossier qui vient d'être supprimé.

---

## Scheduler — jobs récurrents

`SchedulerService` / `SchedulerOptions` (`Models/SchedulerOptions.cs`) — service à options
(persisté dans la table `Options`, service `"Scheduler"`, aucune migration d'options : créé par le
merge de `AutomaticLoadServices`). Trois tâches indépendantes, chacune `Enabled` + expression
**cron 5 champs** (parsing via le package **`Cronos`**, heure serveur `TimeZoneInfo.Local`) :

| Tâche (clé) | Options | Action |
|---|---|---|
| `ProcessDownloads` | `ProcessDownloadsEnabled`, `ProcessDownloadsCron` | `LaunchJobProcessDownloads(new())` |
| `RollingRefresh` | `RollingRefreshEnabled`, `RollingRefreshCron`, `RollingRefreshBatchSize` (int, défaut 10) | `RunScheduledRollingRefreshAsync` — voir ci-dessous |
| `AutoSearch` | `AutoSearchEnabled`, `AutoSearchCron` (défaut `0 4 * * *`), `AutoSearchBatchSize` (int, défaut 5), `AutoSearchMinScore` (int 0-100, défaut 70) | `RunScheduledAutoSearchAsync` — voir « Auto search » ci-dessous |

**Rolling refresh** (`RunScheduledRollingRefreshAsync`) : au lieu de rafraîchir tout le catalogue
d'un coup (charge source trop forte), chaque exécution prend les **N volumes les moins récemment
synchronisés** (`Volumes.Where(v => v.SourceType != "manual").OrderBy(v => v.LastRefreshedAt)` — les
`NULL` = jamais synchronisés d'abord), estampille leur `LastRefreshedAt = now` (commit **avant**
lancement → rotation garantie même en cas d'échec / redémarrage), puis lance un job
`LaunchJobRefreshVolume(…, syncNewIssuesOnly: true, regenerateComicInfoNewOnly: true)` par volume.
`Volume.LastRefreshedAt` (colonne nullable, migration `AddColumnIfMissingAsync`) est aussi
estampillé par **`RunRematchVolumeJobAsync`** après toute synchro source réussie (`StampVolumeLastRefreshedAsync`,
contexte neuf, un seul champ) — un refresh manuel repousse donc le volume en fin de file.

**Auto search** (`RunScheduledAutoSearchAsync` + partial `inkhoundManager.AutoSearch.cs`) :
acquisition automatique des issues **`Category == Standard`** encore `MISSING`. Chaque exécution
prend les **N volumes `MONITORED`** ayant ≥1 issue Standard MISSING, les moins récemment traités
d'abord (`OrderBy(LastAutoSearchAt)`, NULL en premier), estampille `LastAutoSearchAt = now`
**avant** lancement, puis lance **séquentiellement** (`await`, pas fire-and-forget — on ne martèle
pas Prowlarr/qBittorrent) un job `LaunchJobAutoSearchVolume(AutoSearchVolumeJobParameters
{ VolumeId, MinScore })` par volume ("Auto search — {titre}"). Run refusé (trace ERROR, aucun
estampillage) si Prowlarr ou qBittorrent n'est pas `OK`.

Algorithme par volume (`RunAutoSearchVolumeJobAsync`) :
1. **Phase A — volume** : cascade `BuildSearchQueries(volume)` → `ScoringVolumePack.ScoreAndSort`.
2. **Phase B — par issue restante** : cascade `BuildSearchQueries(volume, issue)` →
   `ScoringTorrent.ScoreAndSort` ; on s'arrête dès que l'issue est acquise.
3. Candidat **éligible** = `Protocol == "torrent"` (usenet/NZB ignoré : non suivi par qBittorrent),
   `DownloadUrl` non vide, `Score >= MinScore`, URL absente de `IssueDownloads.DownloadUrl` (déjà
   suivi) et non rejetée dans ce job. Parcours par score décroissant.
4. **SINGLE `#n`** avec `n` manquant → `GrabToQBittorrentAsync` pour cette issue. `SINGLE/?` et
   `UNKNOWN` ne sont jamais grabés à l'aveugle.
5. **PACK** → `GrabPackSelectiveAsync` (ajout en pause + fichiers ; si métadonnées absentes, polling
   `GetPackFetchStatusAsync` ≤ 30 s), puis `MatchPackFilesToMissingIssues(files, missing)` (pure,
   `internal static`, testée par `PackFileMatcherTests` : fichiers `IsArchiveFile` +
   `TorrentTypeAnalyzer.ExtractIssueNumber`, un fichier par issue). **Aucun fichier utile →
   `AbortPackSelectionAsync` (torrent + fichiers supprimés) et candidat suivant.** Sinon
   `ApplyPackSelectionAsync(volumeId, indices appariés)` — les issues déjà grabées en SINGLE sont
   `DOWNLOADING`, donc jamais ré-appariées.
6. Les exceptions par candidat sont absorbées (trace WARNING) ; le job continue.

Le cache `queryCache` de `SearchProwlarrCascadeAsync` (partagé par les cascades A et B d'un même
job) évite de réinterroger Prowlarr pour une requête commune (ex. `"{titre}"` seul). Les jobs de
recherche manuels (`RunSearchMissingIssueJobAsync`, `RunSearchMissingVolumeJobAsync`) reposent sur
les mêmes helpers `ResolveIndexersAsync` / `SearchProwlarrCascadeAsync`.

`SchedulerOptions.IsValid` ne contrôle cron + `RollingRefreshBatchSize >= 1` (resp.
`AutoSearchBatchSize >= 1`, `AutoSearchMinScore` ∈ [0,100]) que si la tâche est activée (sinon
service `INVALID` pour une valeur inerte).

**Boucle** — `inkhoundManager.Scheduler.cs` (partial de `InkhoundManager`). `StartScheduler()` est
appelé en fin de `AutomaticLoadServices()` (idempotent) et lance un `PeriodicTimer` 60 s. À chaque
tick, `EvaluateScheduledTask` compare `CronExpression.GetNextOccurrence(lastCheckUtc, TimeZoneInfo.Local)`
à `nowUtc` : si une occurrence tombe dans la fenêtre `(lastCheckUtc, nowUtc]`, `FireScheduledTask`
lance l'action en `Task.Run` détaché avec garde de ré-entrance (`_schedulerBusy`). Pas de rattrapage
au démarrage (`lastCheckUtc` initialisé à `DateTime.UtcNow`). `_schedulerLastRun` (dernier
déclenchement) est **en mémoire** — repart à vide après redémarrage.

`GetSchedulerStatus()` → `SchedulerStatus` (enabled / cron / lastRunUtc / nextRunUtc / running par
tâche + `RollingRefreshBatchSize`, `AutoSearchBatchSize`, `AutoSearchMinScore`). `RunSchedulerTaskNow(key)` → déclenchement manuel (bouton
« Run now »), `ArgumentException` si clé inconnue. Exposés par `SchedulerController` (`Inkhound.Web`).

---

## Conventions C#

- Primary constructors C# 12
- Options injectées via `IOptions<T>` (ex : `IOptions<ComicVineOptions>`)
- Services enregistrés en `Singleton` ou `Scoped` selon qu'ils ont un état
- Namespace : `Inkhound.Core` + sous-namespace par dossier
- Mapper centralisé dans `Mapper.cs` — pas de mapping inline dans les services