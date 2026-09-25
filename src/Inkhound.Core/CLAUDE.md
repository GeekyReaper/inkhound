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
│   ├── IssueTorrentBan.cs  # Couple (Issue, Torrent) banni — voir section IssueTorrentBan
│   ├── Page.cs          # Page d'une issue
│   ├── AgeRating.cs     # Enum AgeRating + extension ToKavitaString()
│   ├── BlobAccess.cs    # Accès fichier binaire
│   ├── User.cs          # Compte utilisateur (auth) — un seul rôle "admin", pas de propriété Role
│   ├── ArchiveJobParameters.cs
│   ├── SynchronizeLibraryJobParameters.cs
│   ├── RegenerateComicInfoJobParameters.cs
│   ├── AutoSearchVolumeJobParameters.cs  # job Auto search (VolumeId + MinScore)
│   └── RefreshBedethequeCatalogJobParameters.cs  # job refresh catalogue Bedetheque (LetterCount / Letters)
├── Security/            # PasswordHasher.cs — PBKDF2/SHA-256, 100 000 itérations
├── ComicVine/           # Intégration API ComicVine
│   ├── ComicVineSourceService.cs
│   ├── ComicVineModels.cs
│   └── ComicVineOptions.cs
├── Bedetheque/          # Intégration bedetheque.com (scraping HTML — Serie = Volume, Album = Issue)
│   ├── BedethequeSourceService.cs
│   ├── BedethequeModels.cs
│   ├── BedethequeOptions.cs
│   ├── BedethequeBlockedException.cs
│   └── Catalog/         # Catalogue local des séries (recherche floue hors-ligne — voir section Bedetheque)
│       ├── BedethequeCatalogEntry.cs        # entité EF (table BedethequeCatalogSeries)
│       ├── BedethequeCatalogIndex.cs        # index mémoire + Search() (scoring par paliers)
│       ├── BedethequeTitleNormalizer.cs     # normalisation/tokens/synonymes/Levenshtein (pur, testé)
│       ├── BedethequeCatalogStatus.cs / BedethequeCatalogLetterStatus.cs
│       └── BedethequeCatalogNotLoadedException.cs
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
├── Chatbot/             # Module Chatbot — commandes BD du bot Matrix (voir section dédiée)
│   ├── ChatbotService.cs            # : BaseChatbotService<ChatbotOptions> (Foundation.Core)
│   ├── ChatbotOptions.cs            # socle d'options + réglages BD
│   ├── IInkhoundChatbotGateway.cs / InkhoundChatbotGateway.cs  # appels in-process au manager
│   ├── ChatbotGatewayException.cs
│   ├── Services/        # BdSeriesLookupService, BdLookupOutcome, InkhoundLibraryService
│   └── Features/        # !bd-scan, !bd-search, !ocr-bd + flows de présentation et d'ajout
├── Blob/                # Gestion fichiers binaires (non utilisé)
│   └── BlobService.cs
├── Mapper.cs            # Mapping entre modèles domaine et DTOs
├── inkhoundManager.cs   # Orchestrateur principal des jobs
├── inkhoundManager.Scheduler.cs   # partial — boucle cron + tâches planifiées
├── inkhoundManager.AutoSearch.cs  # partial — job Auto search (acquisition auto via Prowlarr)
└── inkhoundManager.BedethequeCatalog.cs  # partial — chargement/état/job de refresh du catalogue Bedetheque
```

## Modèles domaine

### Library
Représente un dossier racine géré par Kavita.
```
Id, Name, Path, KavitaLibraryId, KavitaPath, CreatedAt, UpdatedAt
```
- `Path` — chemin tel que vu par Inkhound (ex: `Z:\BandeDessinee`)
- `KavitaLibraryId` — identifiant de la library dans Kavita (pour les scans)
- Suppression (`DeleteLibraryAsync(id, deleteFiles)`) : cascade en base (volumes, issues, downloads, `SelectedIndexers`) ; avec `deleteFiles`, chaque répertoire de volume est supprimé via `DeleteVolumeDirectory` (garde-fou `TryGetVolumeDirectory`) — jamais `Library.Path` lui-même. Échecs disque cumulés dans `FileWarning`, scan Kavita complet ensuite si liée.
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
  - **Stats d'une library** (`GetLibraryStatsAsync` → `LibraryStats`, `GET /api/libraries/{id}/stats`,
    encart de la page Library) : même logique de comptage scopée à une library — volumes par
    `VolumeStatus`, `VolumesBySource` (clé = `SourceType` en minuscules), issues par `Status`
    (toutes catégories), `TotalDownloadedBytes` (cast `long` avant `SUM`), et `Max` de
    `DateAdded` / `LastRefreshedAt` / `LastAutoSearchAt` (null si aucun volume). `null` si la
    library n'existe pas. Lecture pure.
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

### Téléchargements (`IssueDownload`) — statut et vues ciblées

`IssueDownload.Status` (`Downloading | Stalled | Paused | Finished | Syncing | Done | Error |
Unknown | NotFound`) n'est **recalculé que par `EnrichDownloadsAsync`** (mapping de l'état
qBittorrent via `MapQBittorrentState`, `"stalleddl"` → `Stalled`), qui le **persiste** au passage.
Toute lecture de downloads passe donc par elle — et une fois `Finished`/`Syncing`/`Done` (statuts
« possédés » par Inkhound), le polling ne l'écrase plus.

⚠️ **Conséquence** : un `WHERE Status = 'Stalled'` en SQL renvoie l'état du **dernier**
enrichissement. `GetStalledDownloadsAsync(limit)` (section d'alerte du Dashboard) charge donc les
lignes *actives* (`ActiveDownloadStatuses`), les enrichit, **puis** filtre sur `Stalled` — ne jamais
inverser cet ordre.

Vues ciblées, toutes bâties sur `EnrichDownloadsAsync(ctx, …)` avec le contexte de l'appelant :
| Méthode | Usage |
|---|---|
| `GetIssueDownloadsAsync(issueId)` | carte « Download » de la page Issue |
| `GetVolumeDownloadsAsync(volumeId)` | badges de la liste des issues — **un seul** appel qBittorrent pour tout le volume (jointure sur `Issues`, `IssueDownload` n'ayant pas de `VolumeId`) |
| `GetStalledDownloadsAsync(limit)` | section « Stalled downloads » du Dashboard |

### IssueTorrentBan
Couple (Issue, Torrent) banni — table `IssueTorrentBans`.
```
Id, IssueId, TorrentHash, TorrentTitle, DownloadUrl, TrackerName, CreatedAt, Reason
```
Créé par `DeleteDownloadAsync(..., ban: true)` (défaut : case cochée dans la modale de la page
Downloads) — **un ban par ligne `IssueDownload` supprimée**, donc aussi pour les jumelles d'un PACK
partageant le hash ; les doublons (même issue + même URL/hash/titre) ne sont pas recréés. Les autres
chemins de purge (`DeleteVolumeAsync`, `DeleteLibraryAsync`, `CheckVolumeFilesAsync`,
`DeleteIssueFileAsync`) **ne bannissent pas** ; volume et library purgent en revanche les bans de
leurs issues en cascade.

**Effet au scoring** (`Scoring/TorrentBanIndex.cs`, testé) : l'appelant charge l'index du périmètre
voulu — `LoadBanIndexForIssueAsync(issueId)` pour une recherche par issue,
`LoadBanIndexForVolumeAsync(volumeId)` (jointure `IssueTorrentBans` × `Issues`) pour une recherche
par volume, où un ban posé sur **n'importe quelle** issue écarte le torrent. `IsBanned(result)`
rapproche par **`DownloadUrl`**, puis par `Guid` (certains indexers y placent l'URL ou le hash),
puis par **titre normalisé** (`TextSimilarity.Normalize`) — une clé vide ne matche jamais.
⚠️ `ProwlarrSearchResult` n'expose **pas** d'`InfoHash` (il n'existe qu'après l'ajout à
qBittorrent) : `TorrentHash` sert à la traçabilité et au repli par `Guid`, pas au matching direct.

`ScoringTorrent.ApplyBan(score, banned)` force le score à **0** et le record scoré porte
`Banned = true` ; comme `ApplyNoSeederPenalty`, il s'applique **en tout dernier** (voir l'avertissement
de la section « Malus aucun seeder »). Les 4 points de scoring passent l'index : les deux jobs de
recherche manuelle et les phases A/B de l'auto search — dont `AutoSearchRun.IsEligible` exclut aussi
explicitement `banned`, pour qu'un `MinScore` abaissé à 0 ne rouvre pas la porte à un torrent écarté
à la main.

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
- **Recherche de séries = catalogue local, zéro requête réseau** (`Bedetheque/Catalog/`, port de
  la recherche par catalogue de `bdguest-scrapper`). Les 27 pages d'index alphabétique du site
  (`/bandes_dessinees_{0,A..Z}.html`, ~77 000 séries) sont scrapées lettre par lettre
  (`FetchCatalogLetterAsync` → `ParseCatalogPage`, `internal static` testé) et persistées dans la
  table `BedethequeCatalogSeries` (`Id` = id série, `Title` au format du site « Titre (Les) »,
  `Language`/`Origin` déduits du drapeau, `Letter`, `FetchedAtUtc`). `InkhoundManager.LoadBedethequeCatalogAsync`
  (fin d'`AutomaticLoadServices` + fin de chaque refresh) charge la table dans l'index mémoire
  `BedethequeCatalogIndex` de `BedethequeSourceService` (`LoadCatalog` / `IsCatalogLoaded` / `CatalogCount`).
  - `ISourceService.SearchVolumesByNameAsync` interroge uniquement cet index : **catalogue vide →
    `BedethequeCatalogNotLoadedException`**, que `SearchVolumesAsync` traduit en
    `SourceSearchStats.ErrorCode = "CATALOG_NOT_LOADED"` (l'UI affiche un lien vers `/settings/bedetheque`).
    Le service reste `OK` (son état ne dépend pas du catalogue). Les `CatalogMaxResults` (défaut 5)
    meilleures séries sont ensuite **toutes enrichies** via leur page série (`GetOrFetchSerieAsync(requireComplete:false)`,
    cache 24 h, `MaxParallelRequests` en parallèle) → cover (premier album / og:image), année, nombre
    de tomes, éditeur, description. **Seuls les résultats enrichis remontent** : une série dont la
    page n'a pas pu être lue est écartée (trace WARNING). `CatalogMaxResults` plafonne donc aussi les
    requêtes réseau par recherche.
  - Rapprochement (`BedethequeTitleNormalizer` + `BedethequeCatalogIndex.Search`) : titre et requête
    passent par le même pipeline — `ReorderParentheticalPrefix` (titre seulement, « X (Les) » → « Les X »),
    `StripLeadingArticle`, minuscules, `&`→`et`, `$`→`s`, points supprimés, ligatures/accents retirés
    (table `BaseChar` de repli, indépendante d'ICU), ponctuation → espace, puis `CanonicalizeTokens`
    (`and`→`et`, nombres en lettres FR/EN → chiffres sauf `un/une/one`). Score : **1000** identique,
    **900−écart** (≤ 99) requête = mot entier du titre, **800−écart** (≤ 200) sous-chaîne, **1-500**
    flou par tokens (meilleure similarité Levenshtein par token de requête, rejet si < 0,5 ;
    `round(500 × (0,7 × similarité moyenne + 0,3 × couverture du titre))`). Options `CatalogMinScore`
    (défaut 250) et `CatalogMaxResults` (défaut 5) dans `BedethequeOptions` ; `SearchLanguageFilter`
    = filtre exact sur `Language`. Le score 0-100 affiché reste `SearchScoring.ScoreTitleMatch`.
  - **Refresh** (`inkhoundManager.BedethequeCatalog.cs`) : `LaunchJobRefreshBedethequeCatalog(RefreshBedethequeCatalogJobParameters)`
    → `JobContext` (`Letters` explicites, sinon `LetterCount` lettres par rotation « jamais chargée
    d'abord, puis `FetchedAtUtc` asc », sinon les 27). Un seul refresh à la fois (`_catalogRefreshRunning`,
    `InvalidOperationException` → 409). Par lettre : fetch, page vide → WARNING + contenu précédent
    conservé, sinon `ReplaceCatalogLetterAsync` (transaction : delete par `Letter` + delete des ids
    réinsérés, puis `AddRange`). `BedethequeBlockedException` arrête la boucle. ⚠️ `StartJob` est
    appelé directement dans `LaunchJob…`/`RunScheduled…` (jamais dans un helper `async` awaité) :
    le job courant est un `AsyncLocal` qui ne remonte pas vers l'appelant.
  - `GetBedethequeCatalogStatusAsync()` → `BedethequeCatalogStatus` (loaded, total, oldest/newest,
    refreshRunning, 27 `BedethequeCatalogLetterStatus`).
- Détail d'une série + liste des albums : `GET /serie-{id}-BD-x.html` — `GetSerieAsync` met en
  cache mémoire 24h (`_serieCache`). `GetSerieAsync(id, ct, forceRefresh: true)` ignore ce cache
  et le repeuple : le flux Refresh/Rematch (`RematchVolumeFromBedethequeAsync`) le passe pour
  qu'un tome ajouté récemment sur la source soit vu tout de suite ; recherche/enrichissement
  gardent `forceRefresh: false`.
- Détail d'un album (auteurs, EAN, ...) : `GET /BD-x-Tome-1-x-{id}.html`
- **Auteurs du Volume** : la page Serie ne porte aucun auteur (`BdSerie` n'a pas d'`Auteurs`,
  `Mapper.Map(BdSerie)` laisse `Authors = []`) — contrairement à ComicVine (`cvVolume.People`).
  `Volume.Authors` est donc reconstruit par `MergeIssueAuthorsIntoVolume` (union dédoublonnée par
  nom, premier rôle non vide) depuis les auteurs de tous les albums, dans les trois flux : Add
  (`RunAddBedethequeIssuesJobAsync`), Refresh complet (`RematchVolumeFromBedethequeAsync`) et
  Refresh "NEW only" (`SyncNewBedethequeAlbumsAsync`). Côté ComicVine, le bloc `allIssueAuthors`
  ne fait que compléter les rôles vides de `cvVolume.People`, il n'ajoute pas d'auteurs.
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

**Tri par pertinence** (`Scoring/SearchScoring.ScoreTitleMatch`, tests dans
`Inkhound.Core.Tests/Scoring/SearchScoringTests.cs`) — la pagination est appliquée **par source
avant** la fusion, puis l'ensemble est retrié par score :
- `NormalizeTitle` = `TextSimilarity.Normalize` (accents/casse/ponctuation) + retrait d'un article
  en **tête et/ou en queue** (`le/la/les/l/un/une/des/the/a/an`), jamais au milieu. Sans ça la
  forme Bedetheque « Trois fantômes de Tesla (Les) » tombait en repli Levenshtein (~42) face à
  « Les trois fantômes de Tesla » de ComicVine (100) pour la même requête.
- Une source qui lève une exception est ignorée avec `SourceSearchStats(Success=false, ErrorMessage)` ;
  `ErrorCode` (nullable) qualifie les échecs que l'UI traite spécifiquement — aujourd'hui
  `SourceSearchStats.CatalogNotLoaded` (`"CATALOG_NOT_LOADED"`) pour `BedethequeCatalogNotLoadedException`.
- Bonus langue `+10` uniquement si `SourceVolume.Language` == `ISourceService.PreferredLanguage`
  de la première source qui en déclare une — Bedetheque expose `LanguageFilterLabel(SearchLanguageFilter)`
  (`null` en `All` → aucun bonus), ComicVine `null` (pas de métadonnée langue, ni bonus ni pénalité).
  Plus de « Français » codé en dur.
- Bonus « série complète » `min(5, CountOfIssues/10)` — plafonné pour rester sous le bonus langue.

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

**Import sur une issue `DOWNLOADING`** : `ImportArchiveFileForIssueAsync` mémorise le statut avant
bascule et, si l'issue était en cours de téléchargement, supprime **ses seules** lignes
`IssueDownload` après la conversion réussie (le fichier importé rend le suivi caduc). Contrairement
à `DeleteDownloadAsync` : qBittorrent n'est pas touché (torrent et fichiers conservés), les lignes
jumelles des autres issues du même torrent (PACK) restent, et **aucun ban n'est créé** — le torrent
n'a pas démérité, il n'est simplement plus nécessaire pour cette issue.

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
- `UpdateLibraryVolumesStatusAsync(libraryId, PAUSED | MONITORED)` — version en masse pour une
  library (boutons « Pause all » / « Resume all » de la page Library) : `PAUSED` bascule tous les
  `MONITORED`, `MONITORED` tous les `PAUSED` (+ recalc de stats par volume repris) ; `COMPLETED`
  jamais touchés. `ExecuteUpdate` unique, **un seul** `OnDataUpdated(Library)` (pas un par volume —
  la page Library recharge sa liste à la réponse). Retourne le nombre modifié, `null` si library
  inconnue.
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
merge de `AutomaticLoadServices`). Quatre tâches indépendantes, chacune `Enabled` + expression
**cron 5 champs** (parsing via le package **`Cronos`**, heure serveur `TimeZoneInfo.Local`) :

| Tâche (clé) | Options | Action |
|---|---|---|
| `ProcessDownloads` | `ProcessDownloadsEnabled`, `ProcessDownloadsCron` | `LaunchJobProcessDownloads(new())` |
| `RollingRefresh` | `RollingRefreshEnabled`, `RollingRefreshCron`, `RollingRefreshBatchSize` (int, défaut 10) | `RunScheduledRollingRefreshAsync` — voir ci-dessous |
| `AutoSearch` | `AutoSearchEnabled`, `AutoSearchCron` (défaut `0 4 * * *`), `AutoSearchBatchSize` (int, défaut 5), `AutoSearchMinScore` (int 0-100, défaut 70) | `RunScheduledAutoSearchAsync` — voir « Auto search » ci-dessous |
| `BedethequeCatalog` | `BedethequeCatalogEnabled`, `BedethequeCatalogCron` (défaut `0 2 * * *`), `BedethequeCatalogLetterCount` (int, défaut 3) | `RunScheduledBedethequeCatalogAsync` — job de refresh du catalogue local sur les N lettres les moins récemment chargées, **awaité** (la garde `_schedulerBusy` couvre toute l'exécution) ; ignoré (trace WARNING) si un refresh manuel tourne déjà. Voir section Bedetheque. |

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
   `DownloadUrl` non vide, **non banni** (voir `IssueTorrentBan`), `Score >= MinScore`, URL absente
   de `IssueDownloads.DownloadUrl` (déjà suivi) et non rejetée dans ce job. Parcours par score
   décroissant. Les torrents **sans seeder**
   sont de fait écartés au `MinScore` par défaut (70) grâce au malus `NoSeederPenalty` — voir
   « Malus aucun seeder » ci-dessous.
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

**Malus « aucun seeder »** (`Scoring/ScoringTorrent.cs`) — un torrent à `Seeders == 0` ne se
télécharge jamais et finit en `DownloadStatus.Stalled`. `ApplyNoSeederPenalty(score, result)`
retranche `NoSeederPenalty` (**40 points**, planché à 0) du score **final**, et est appelé par
`ScoringTorrent.ScoringIndexerResult` **et** `ScoringVolumePack.ScoringIndexerResult`.

⚠️ L'ordre est critique : le malus s'applique **après** `ApplyTypeAdjustment`, car sur le chemin
`SINGLE "#n"` confirmé cette méthode **écrase** le `baseScore` (et donc la composante
`ScoreSeeders`) par la formule fixe `70 + ScoreSizeBonusForSingle + reassurance`. Intégré en
amont, le malus serait purement perdu — un torrent mort pouvait ainsi atteindre 100.

Le malus ne vise que `IsTorrent(result)` (l'usenet n'a pas de seeders et conserve son bonus plat
de 7) et uniquement `Seeders == 0` : les seeders faibles restent gérés par la gradation
`ScoreSeeders` (`min(10, Seeders / 3)`). Un `SINGLE` parfait sans seeder tombe ainsi de 100 à 60,
sous le `AutoSearchMinScore` par défaut — mais **pas** sous un seuil abaissé à 50 ou moins.
Tests : `Inkhound.Core.Tests/Scoring/ScoringTorrentTests.cs` et `ScoringVolumePackTests.cs`.

Côté UI (`prowlarr-search.component`), `hasNoSeed()` signale ces résultats : badge rouge
« No seed » dans la colonne Seeders et ligne atténuée via `.row-no-seed`.

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
tâche + `RollingRefreshBatchSize`, `AutoSearchBatchSize`, `AutoSearchMinScore`, `BedethequeCatalogLetterCount`). `RunSchedulerTaskNow(key)` → déclenchement manuel (bouton
« Run now »), `ArgumentException` si clé inconnue. Exposés par `SchedulerController` (`Inkhound.Web`).

---

## Module Chatbot (`Chatbot/`)

Bot **Matrix** (pas Discord) exposant `!bd-scan`, `!bd-search` et `!ocr-bd`, porté depuis le projet
`chatbot`. Toute la mécanique de chat et l'analyse d'image vivent dans `Foundation.Core/Chatbot`
(voir son CLAUDE.md) ; ici ne restent que les commandes métier.

### Enregistrement — l'ordre est critique

```csharp
// inkhoundManager.cs, dans AutomaticLoadServices(), AVANT la boucle de chargement des options
GetService<ChatbotService, ChatbotOptions>().Attach(new InkhoundChatbotGateway(this));
```

`BaseServiceManager.GetService<T,K>()` contraint `T : ..., new()` : impossible d'injecter le manager
par constructeur, d'où `Attach`. Et c'est la boucle `LoadOptions` qui construit les commandes puis
démarre la boucle Matrix — attacher après laisserait le bot démarrer sans accès au domaine.

### `IInkhoundChatbotGateway` — la frontière métier

Remplace l'`IInkhoundClient` HTTP du bot d'origine : plus d'appel REST, plus de header `X-Api-Key`,
et surtout plus de **polling du job de recherche** (`POST /api/volumes/search` puis attente jusqu'à
60 s) — `SearchVolumesAsync(name, page, pageSize, job: null, ct)` accepte un `job` optionnel et
fonctionne en agrégateur silencieux. Les DTO du bot ont été abandonnés au profit des types du
domaine (`SourceVolume`, `SourceIssue`, `Library`, `Volume`, `AgeRating`), ce qui supprime une
couche de mapping entière.

> **Contrat d'erreur** : la passerelle enveloppe chaque appel et ne laisse remonter que
> `ChatbotGatewayException` — le domaine lève sinon des `InvalidOperationException` (source
> inconnue) et des `FormatException` (`AddVolumeFromSourceAsync` fait un `int.Parse(sourceId)`).
> `OperationCanceledException` n'est **jamais** avalée : l'arrêt du bot doit interrompre la commande
> en cours. Testé par `InkhoundChatbotGatewayTests`.

### Parcours d'une commande

`!bd-scan` = OCR de la couverture (prompt `BdCoverAnalysis.Prompt`, JSON à clés imposées) puis même
parcours que `!bd-search` : `BdSeriesLookupService` (recherche → filtrage par numéro/titre d'album
sur les 8 premiers candidats → résolution, avec repli sur le 1er résultat) puis `BdSeriesResultFlow`
(couvertures ré-uploadées sur le homeserver, fiche série, liste d'albums, menu des autres résultats)
et `BdAddToLibraryFlow` (librairie → classification d'âge → confirmation → ajout).

Le téléchargement des couvertures exige un **User-Agent** (`CoverUserAgent`) : Bédéthèque renvoie
403 sans. C'est le seul appel du bot qui peut passer par le proxy (`UseProxyForCovers`).

`BdAgeRatings` a été rebranché sur l'enum `AgeRating` du domaine ; le bot d'origine envoyait des
chaînes, dont `"AdultsOnly"` — qui ne correspond à aucun membre (le vrai est `AdultsOnly18Plus`).

### Pas de table, pas de migration

Les options vont dans la table `Options` existante (service `"Chatbot"`), les traces ne sont pas
persistées, le cache vision et les menus en attente vivent en mémoire. **Aucune migration à ajouter
dans `ApplyPendingMigrationsAsync`** — ne pas céder à la tentation d'historiser les échanges du bot
en base.

---

## Conventions C#

- Primary constructors C# 12
- Options injectées via `IOptions<T>` (ex : `IOptions<ComicVineOptions>`)
- Services enregistrés en `Singleton` ou `Scoped` selon qu'ils ont un état
- Namespace : `Inkhound.Core` + sous-namespace par dossier
- Mapper centralisé dans `Mapper.cs` — pas de mapping inline dans les services