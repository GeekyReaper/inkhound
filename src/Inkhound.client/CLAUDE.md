# Inkhound.client — Contexte

Frontend Angular (latest) avec CoreUI Free. SPA servi par Inkhound.Web en production.
En développement, tourne sur le port 4200 avec proxy vers le backend.

## CoreUI — règles d'utilisation

Le projet utilise **CoreUI Free pour Angular** (`@coreui/angular` v5.x). Chaque page et composant doit
utiliser en priorité les composants CoreUI avant d'écrire du HTML/CSS custom.

### Composants à utiliser systématiquement

| Besoin | Composant CoreUI | Import |
|---|---|---|
| Bouton | `<c-button>` | `ButtonModule` ou `ButtonDirective` |
| Tableau | `<c-table>` | `TableModule` ou `TableDirective` |
| Formulaire / input | `<c-form-control>`, `<c-form-label>`, `<c-input-group>` | `FormModule` |
| Carte / panneau | `<c-card>`, `<c-card-header>`, `<c-card-body>` | `CardModule` |
| Badge | `<c-badge>` | `BadgeModule` |
| Alerte | `<c-alert>` | `AlertModule` |
| Modal | `<c-modal>`, `<c-modal-header>`, `<c-modal-body>` | `ModalModule` |
| Spinner / loading | `<c-spinner>` | `SpinnerModule` |
| Toast / notification | `<c-toast>` | `ToastModule` |
| Progress bar | `<c-progress>`, `<c-progress-bar>` | `ProgressModule` |
| Breadcrumb | `<c-breadcrumb>` | `BreadcrumbModule` |
| Pagination | `<c-pagination>` | `PaginationModule` |
| Dropdown | `<c-dropdown>` | `DropdownModule` |
| Tabs | `<c-tabs>` | `TabsModule` |
| Accordion | `<c-accordion>`, `<c-accordion-item>`, `cAccordionButton`, `cTemplateId` | `AccordionModule` |
| Tooltip | `cTooltip` directive | `TooltipModule` |
| Grid layout | `<c-row>`, `<c-col>` | `GridModule` |
| Sidebar | `<c-sidebar>`, `<c-sidebar-nav>` | `SidebarModule` |
| Header | `<c-header>` | `HeaderModule` |

```typescript
// ✅ Correct — importer uniquement les modules nécessaires
@Component({
  standalone: true,
  imports: [CardModule, ButtonDirective, TableDirective, BadgeModule],
  templateUrl: './volume-list.component.html'
})

// ✅ Exemple template CoreUI
```
```html
<c-card>
  <c-card-header>
    <strong>Volumes</strong>
  </c-card-header>
  <c-card-body>
    <table cTable hover responsive>
      <thead>
        <tr><th>Titre</th><th>Statut</th><th>Actions</th></tr>
      </thead>
      <tbody>
        @for (v of volumes(); track v.id) {
          <tr>
            <td>{{ v.title }}</td>
            <td><c-badge [color]="badgeColor(v.status)">{{ v.status }}</c-badge></td>
            <td>
              <button cButton color="primary" size="sm">Éditer</button>
            </td>
          </tr>
        }
      </tbody>
    </table>
  </c-card-body>
</c-card>
```

### Providers requis dans `app.config.ts`

```typescript
import { importProvidersFrom } from '@angular/core';
import { SidebarModule, DropdownModule } from '@coreui/angular';

export const appConfig: ApplicationConfig = {
  providers: [
    provideAnimationsAsync(),
    importProvidersFrom(SidebarModule, DropdownModule),
    // ...
  ]
};
```

---

## Icônes — règles strictes

### ❌ Interdit absolu
- Ne **jamais** créer d'icônes SVG inline (`<svg>`, `<path>`, etc.)
- Ne **jamais** utiliser d'autres bibliothèques d'icônes (Font Awesome, Material Icons, Heroicons, etc.)
- Ne **jamais** utiliser des emojis comme substitut d'icône

### ✅ Toujours utiliser `@coreui/icons` + directive `cIcon`

**Setup dans `app.component.ts` (une seule fois) :**
```typescript
import { IconSetService } from '@coreui/icons-angular';
import { cilUser, cilSettings, cilTrash, cilPencil, cilPlus, cilSearch } from '@coreui/icons';

@Component({ ... })
export class AppComponent {
  constructor(public iconSet: IconSetService) {
    iconSet.icons = { cilUser, cilSettings, cilTrash, cilPencil, cilPlus, cilSearch };
  }
}
```

**Utilisation dans les composants :**
```typescript
@Component({
  standalone: true,
  imports: [IconDirective],  // ← obligatoire (IconDirective, pas IconModule)
  ...
})
```

```html
<svg cIcon name="cilUser"></svg>
<svg cIcon name="cilTrash" size="lg"></svg>
<svg cIcon name="cilPencil" title="Modifier"></svg>

<!-- Tailles disponibles : sm | lg | xl | 2xl | 3xl ... 9xl -->
<svg cIcon name="cilPlus" size="sm"></svg>

<!-- Avec classes CSS custom -->
<svg cIcon name="cilSearch" customClasses="text-primary"></svg>
```

**Import direct dans un composant (sans passer par IconSetService) :**
```typescript
import { cilUser } from '@coreui/icons';

@Component({ ... })
export class MyComponent {
  readonly cilUser = cilUser;
}
```
```html
<svg cIcon [content]="cilUser"></svg>
```

### Icônes CoreUI disponibles pour Inkhound

| Action | Icône |
|---|---|
| Ajouter | `cilPlus` |
| Modifier | `cilPencil` |
| Supprimer | `cilTrash` |
| Rechercher | `cilSearch` |
| Utilisateur | `cilUser` |
| Paramètres | `cilSettings` |
| Librairie / dossier | `cilFolder` |
| Volume / livre | `cilBook` |
| Télécharger | `cilCloudDownload` |
| Sync / rafraîchir | `cilReload` |
| Statut OK | `cilCheckCircle` |
| Statut erreur | `cilXCircle` |
| Statut en attente | `cilClock` |
| Dashboard | `cilSpeedometer` |
| Liste | `cilList` |
| Filtre | `cilFilter` |
| Info | `cilInfo` |

> La liste complète est disponible sur https://coreui.io/icons/ — chercher le nom `cil*` correspondant.

---

## Structure réelle du projet

```
src/
├── app/
│   ├── core/                    # Singletons, guards, interceptors, modèles globaux
│   │   ├── guards/              # auth.guard.ts
│   │   ├── interceptors/        # auth, auth-error, connection
│   │   ├── models/              # hub.models.ts (EState, JobContext, TraceDefinition, etc.)
│   │   ├── resolvers/           # library-title, volume-title
│   │   ├── pipes/               # SmartDatePipe (`smartDate`) — date+heure compacte : 24 h sans secondes,
│   │   │                        #   année omise si égale à l'année courante ("13 Sep, 14:32" / "13 Sep 2025, 14:32").
│   │   │                        #   À utiliser à la place de date:'medium'/'short' sur tout horodatage métier.
│   │   └── services/            # AuthService, HubService, LibraryService, LibraryViewStateService,
│   │                            # NavigationTrackerService, VolumeService, IssueService, KavitaService,
│   │                            # OptionsService, SchedulerService, BedethequeCatalogService,
│   │                            # FilesystemService, ImageService
│   ├── views/                   # Pages / vues de l'application
│   │   ├── download-card/       # DownloadCardComponent — vignette d'un download (cover + badge statut)
│   │   ├── download-list/       # DownloadListComponent — tableau + actions + modales d'un download,
│   │   │                        #   partagé par la page Downloads et la page Issue (voir plus bas)
│   │   ├── dashboard/           # DashboardComponent (KPI, Libraries, Most wanted, Recently added,
│   │   │                        #   Stalled downloads — section d'alerte rouge listant les downloads
│   │   │                        #   sans seeder, masquée si vide ; les deux sections downloads
│   │   │                        #   s'affichent en grilles d'app-download-card (les Stalled sont
│   │   │                        #   exclus de « Downloads in progress » pour éviter le doublon) —
│   │   │                        #   Active jobs, Downloads — section « Most wanted » : cartes des issues
│   │   │                        #   MISSING proches de compléter leur volume, clic → page détail issue)
│   │   ├── library/             # LibraryShellComponent, LibraryComponent (liste volumes paginée +
│   │   │                        #   filtres côté client : lettre / complétude / source / titre / année / age rating —
│   │   │                        #   filtres + page + scroll persistés par id via LibraryViewStateService).
│   │   │                        #   En-tête : encart pleine largeur en 3 colonnes (Configuration / Volumes /
│   │   │                        #   Issues & activity — stats via GET /api/libraries/{id}/stats, rechargées avec
│   │   │                        #   la liste des volumes ; dernier scan Kavita lu depuis KavitaService), puis la
│   │   │                        #   rangée d'actions (Edit → /library/:id/edit / Synchronize / Refresh / Pause all / Resume all)
│   │   │                        #   entre l'encart et la section Volumes ; job panels pleine largeur sous le titre.
│   │   │   └── library-edit/    # LibraryEditComponent — page /library/:id/edit : Name / Path + sélecteur de
│   │   │                        #   dossier / Kavita library / Kavita path (colonne gauche) + bloc
│   │   │                        #   app-library-indexers (droite). Save → retour /library/:id + loadLibraries()
│   │   │                        #   (sidebar). Seule page d'édition d'une library (plus de formulaire dans /libraries).
│   │   ├── library-indexers/    # LibraryIndexersComponent — sélection des indexers Prowlarr + catégories par
│   │   │                        #   library. Les catégories s'affichent EN PLACE de la liste (titre = nom de
│   │   │                        #   l'indexer, pas de modal empilé — le bloc vit dans le modal Edit de la page
│   │   │                        #   Library) : cocher un indexer ou cliquer « Categories » ouvre la vue, « Back »
│   │   │                        #   revient à la liste ET persiste la sélection (save()).
│   │   ├── prowlarr-search/     # ProwlarrSearchComponent — recherche + tableau des résultats scorés, réutilisé
│   │   │                        #   en mode 'issue' (app-prowlarr-search mode="issue") et 'volume' (colonne
│   │   │                        #   Coverage en plus). Badge de score coloré via scoreColor() (>=70 success,
│   │   │                        #   >=40 warning, sinon danger). hasNoSeed() signale un torrent à 0 seeder
│   │   │                        #   (badge rouge « No seed » + ligne atténuée .row-no-seed) : ces résultats
│   │   │                        #   stalleraient au téléchargement et sont déjà pénalisés de 40 pts côté backend.
│   │   │                        #   `row.banned` (backend) → badge rouge « Banned » sous le score + ligne
│   │   │                        #   atténuée .row-banned : torrent banni pour l'issue (ou une issue du volume
│   │   │                        #   en mode 'volume'), score 0 — levable depuis la page Issue. bannedTitle()
│   │   │                        #   adapte l'infobulle au mode.
│   │   │                        #   Carte mobile (<768px) : mise en page dédiée via `order` + largeurs en %
│   │   │                        #   totalisant 100 % par rangée (score|indexer+seeders, titre,
│   │   │                        #   catégories+format|type, coverage, taille|date, bouton), labels ::before
│   │   │                        #   neutralisés, icônes cilStorage/cilCalendar/cilPeople en rappel. Les
│   │   │                        #   éléments propres à un seul mode portent .col-mobile-only /
│   │   │                        #   .col-desktop-only (+ variantes -inline). NB : les utilitaires Bootstrap
│   │   │                        #   (flex-column, gap-*) sont en !important et ne peuvent pas être surchargés
│   │   │                        #   par la media query — d'où la classe .type-cell — voir le .scss du composant.
│   │   ├── volume/              # VolumeComponent, VolumeAddComponent, VolumeEditComponent, VolumeMatchComponent
│   │   │   └── issue-card/      # IssueCardComponent — mini-carte issue réutilisée par les blocs "Issues"/"Extra".
│   │   │                        #   Input optionnel `downloadStatus` (alimenté par VolumeComponent via
│   │   │                        #   GET /api/volumes/{id}/downloads, un seul appel et seulement s'il existe
│   │   │                        #   une issue DOWNLOADING) : un download `Stalled` affiche un badge rouge
│   │   │                        #   « STALLED » au lieu du bleu « DOWNLOADING ».
│   │   ├── settings/            # SettingsComponent (options par service via OptionsService) +
│   │   │                        #   SchedulerSettingsComponent (planificateur cron, /settings/scheduler —
│   │   │                        #   4 cartes : Import downloads / Rolling refresh / Auto search / Bedetheque catalog,
│   │   │                        #   champs cron via app-cron-editor) +
│   │   │                        #   BedethequeCatalogComponent (/settings/bedetheque — état du catalogue local
│   │   │                        #   Bedetheque : encart stats, « Refresh oldest » N lettres / « Refresh all »
│   │   │                        #   (modal de confirmation) / tableau .table-stack des 27 lettres avec bouton
│   │   │                        #   Refresh par ligne, suivi du job via app-job-panel + PageJobService, état
│   │   │                        #   rechargé à la fin du job — effect() sur hub.jobs())
│   │   ├── cron-editor/         # CronEditorComponent — éditeur cron (CVA, modale, traduction + next runs)
│   │   ├── jobs/                # JobsComponent (historique et suivi des jobs)
│   │   ├── select-path/         # SelectPathComponent — modal réutilisable de navigation filesystem
│   │   ├── language-flag/       # LanguageFlagComponent — drapeau CoreUI (cif-*) depuis le libellé langue
│   │   │                        #   Bedetheque ("Français", "Japonais"…) ; repli texte si non mappé, rien si null.
│   │   │                        #   Utilisé sur les cartes résultats de VolumeAdd/VolumeMatch (badge bas-gauche,
│   │   │                        #   champ `language` de VolumeSearchResult, null pour ComicVine). Les icônes
│   │   │                        #   cif-* utilisées doivent être déclarées dans icons/icon-subset.ts.
│   │   └── pages/               # login, 404, 500
│   ├── layout/                  # DefaultLayoutComponent (sidebar + header)
│   └── icons/                   # logo.ts, signet.ts
├── components/                  # Composants CoreUI surchargés (template CoreUI)
├── scss/                        # Styles globaux
└── assets/
```

## Routes applicatives

| Route | Composant | Description |
|---|---|---|
| `/dashboard` | `DashboardComponent` | Tableau de bord |
| `/libraries` | `LibraryManagementComponent` | Gestion CRUD des bibliothèques |
| `/library/:id` | `LibraryComponent` | Détail bibliothèque + liste volumes (paginée 20/page + filtres côté client) |
| `/library/:id/edit` | `LibraryEditComponent` | Édition d'une bibliothèque (Name/Path/Kavita + indexers) |
| `/add-volume?library=<id>` | `VolumeAddComponent` | Page dédiée (entrée de menu « Add Volume » sous la liste des libraries). Ajouter un volume (recherche multi-source ou manuel). `?library=` optionnel : pré-rempli par le bouton « + Add » d'une page Library ; sinon la library est demandée dans le workflow (select dans le modal « Add to Library » en mode recherche, select en tête du formulaire manuel). Après ajout → `/library/<id>`. Plus de route imbriquée sous `/library/:id`. |
| `/library/:id/volume/:volumeId` | `VolumeComponent` | Détail volume + liste issues |
| `/library/:id/volume/:volumeId/edit` | `VolumeEditComponent` | Édition manuelle d'un volume |
| `/library/:id/volume/:volumeId/match` | `VolumeMatchComponent` | Rematch (recherche multi-source) |
| `/settings` | `SettingsComponent` | Options de configuration par service — accordéon CoreUI (`alwaysOpen`, plusieurs panneaux ouverts), état/formulaire par module (`ModuleEntry`), chargement paresseux à la 1re ouverture |
| `/settings/scheduler` | `SchedulerSettingsComponent` | Planificateur cron : import downloads + rolling refresh (N volumes/run, les moins récemment sync) + auto search (N volumes/run, score minimum 0-100 — acquisition automatique via Prowlarr/qBittorrent) + Bedetheque catalog (N lettres/run, les moins récemment chargées) |
| `/settings/chatbot` | `ChatbotSettingsComponent` | Module Chatbot (bot Matrix) : fiche d'état (`GET /api/chatbot/status`, rafraîchie toutes les 10 s), boutons Démarrer/Arrêter, et **console de traces temps réel** avec historique local (voir « Traces par service » plus bas). La configuration se fait depuis `/settings` (accordéon Modules). ⚠️ Deux indicateurs distincts à ne pas confondre : le **badge d'en-tête** vient de `managerState()` et reflète la **santé des dépendances** (homeserver Matrix + modèle vision joignables), tandis que la ligne **« Exécution »** dit si la boucle `/sync` tourne. Un bot volontairement arrêté dont les dépendances répondent affiche donc `OK` |
| `/settings/bedetheque` | `BedethequeCatalogComponent` | Catalogue local des séries Bedetheque : état (`GET /api/bedetheque/catalog`), refresh manuel N lettres / toutes / une lettre (`POST /api/bedetheque/catalog/refresh` → job). Cible du lien affiché par Add Volume / Match quand la recherche Bedetheque renvoie `errorCode = 'CATALOG_NOT_LOADED'` |
| `/settings/system` | `SystemSettingsComponent` | Empreinte mémoire du backend (`GET /api/system/memory`) et purge manuelle (`POST /api/system/memory/compact`). **Aucun rafraîchissement automatique** : la mesure est ponctuelle, un timer entretiendrait l'illusion d'un monitoring continu qu'Inkhound ne fait pas. La page signale explicitement un Server GC actif ou une limite mémoire de conteneur absente, et annonce que la purge ne récupère que le managé (les pics SkiaSharp/PDFium sont en mémoire native) |
| `/jobs` | `JobsComponent` | Historique des jobs |
| `/login` | `LoginComponent` | Authentification |

## Règles absolues

### Composants — standalone uniquement
```typescript
// ✅ Toujours
@Component({ standalone: true, imports: [...], templateUrl: '...' })

// ❌ Jamais
@NgModule({ declarations: [...] })
```

### Réactivité — Signals uniquement pour le state local
```typescript
// ✅ State local
isLoading = signal(false);
error = signal<string | null>(null);

// ✅ Valeur dérivée
isAdmin = computed(() => this.auth.currentUser()?.role === 'admin');

// ❌ Pas de BehaviorSubject pour du state local
private _state = new BehaviorSubject(null);
```

RxJS reste acceptable pour `HttpClient` et les événements SignalR.
Toujours utiliser `takeUntilDestroyed()` pour les souscriptions dans les composants.

### Templates — syntaxe de flux de contrôle moderne
```html
<!-- ✅ -->
@if (isLoading()) { <app-spinner /> }
@for (item of items(); track item.id) { ... }

<!-- ❌ Jamais -->
<div *ngIf="isLoading()">
<li *ngFor="let item of items()">
```

**`track` est obligatoire** sur chaque `@for` — utiliser l'identifiant unique.

### Pas de logique dans les templates
```html
<!-- ❌ -->
<span>{{ user().role === 'admin' ? 'Admin' : 'Guest' }}</span>

<!-- ✅ computed() dans la classe -->
<span>{{ roleLabel() }}</span>
```

### Formulaires — Reactive Forms uniquement
```typescript
form = inject(FormBuilder).group({
  login: ['', [Validators.required]],
  password: ['', [Validators.required, Validators.minLength(6)]]
});
```
Jamais de Template-driven forms.

### Souscriptions HTTP
```typescript
ngOnInit() {
  this.service.getData()
    .pipe(
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.isLoading.set(false))
    )
    .subscribe({
      next: data => this.items.set(data),
      error: err => this.error.set(err.error?.message ?? 'Erreur inattendue')
    });
}
```

## Modèles TypeScript — domaine Inkhound

Les interfaces métier sont co-localisées avec leur service, pas dans un dossier `models/` central.
Exception : `hub.models.ts` regroupe les types Hub/SignalR (job, état, trace).

```typescript
// ─── volume.service.ts ───────────────────────────────────────────────────────
type VolumeStatus = 'MONITORED' | 'COMPLETED' | 'PAUSED';

type AgeRating = 'Unknown' | 'RatingPending' | 'EarlyChildhood' | 'Everyone' | 'G'
  | 'Everyone10Plus' | 'PG' | 'KidsToAdults' | 'Teen' | 'MA15Plus'
  | 'Mature17Plus' | 'M' | 'R18Plus' | 'AdultsOnly18Plus' | 'X18Plus';

interface VolumeImage {
  iconUrl: string | null; mediumUrl: string | null; screenUrl: string | null;
  screenLargeUrl: string | null; smallUrl: string | null; superUrl: string | null;
  thumbUrl: string | null; tinyUrl: string | null; originalUrl: string | null;
  imageTags: string | null;
}

interface VolumeAuthor { name: string; role: string; }

interface Volume {
  id: string; libraryId: string; sourceId: string; sourceType: string;
  title: string; year: number | null; description: string | null;
  publisher: string | null; status: VolumeStatus; ageRating: AgeRating;
  genres: string[]; authors: VolumeAuthor[]; image: VolumeImage | null;
  countOfIssues: number; countOfDownloadedIssues: number;
  createdAt: string; updatedAt: string; lastRefreshedAt: string | null;
  lastAutoSearchAt: string | null;   // dernier passage de la tâche Auto search du scheduler
}
// ⚠️ countOfIssues / countOfDownloadedIssues ne comptent que les issues de catégorie `Standard` —
// ils mesurent la complétion de la série (barre de progression, statut COMPLETED). Ne JAMAIS s'en
// servir pour décider « ce volume a-t-il des fichiers ? » : un volume dont seuls des hors-séries,
// intégrales ou omnibus sont téléchargés y vaut 0. Pour cette question, filtrer les issues
// chargées sur `status === 'DOWNLOADED'` (cf. `hasDownloadedIssues` de VolumeComponent, qui
// conditionne les cases « Check files » et « Regenerate ComicInfo.xml » de la popup Refresh).

type SourceKey = 'comicvine' | 'bedetheque';

interface VolumeSearchResult {
  sourceId: string; source: SourceKey; title: string; year: number | null;
  countOfIssues: number; description: string | null; publisher: string | null;
  imageUrl: string | null; siteUrl: string | null;
}

// Stats par source d'une recherche multi-source. errorCode = code d'échec exploitable par l'UI :
// SEARCH_ERROR_CATALOG_NOT_LOADED ('CATALOG_NOT_LOADED') → VolumeAdd/VolumeMatch affichent une
// c-alert warning avec routerLink vers /settings/bedetheque (computed catalogNotLoaded()).
interface SourceSearchStats {
  source: SourceKey; resultCount: number; elapsedMs: number;
  success: boolean; errorMessage: string | null; errorCode: string | null;
}

// ─── bedetheque-catalog.service.ts ───────────────────────────────────────────
interface BedethequeCatalogLetterStatus { letter: string; count: number; fetchedAtUtc: string | null; }
interface BedethequeCatalogStatus {
  loaded: boolean; totalSeries: number; oldestFetchUtc: string | null; newestFetchUtc: string | null;
  refreshRunning: boolean; letters: BedethequeCatalogLetterStatus[];   // toujours 27 (0, A-Z)
}
interface RefreshCatalogRequest { letterCount?: number; letters?: string[]; }  // letters prioritaire

interface PageResult<T> {
  items: T[]; pageNumber: number; pageSize: number;
  totalItems: number; totalPages: number; hasNext: boolean; hasPrev: boolean;
}

// Popup "Delete Volume" (page volume) — la case `deleteFilesToo` alimente le query param
// `deleteFiles` et repart TOUJOURS décochée à chaque ouverture (requestDelete()) : l'effacement
// des fichiers doit être un choix explicite. Réponse `{ fileWarning }` (HTTP 200) = le volume A
// ÉTÉ supprimé mais pas son répertoire → la modale reste ouverte sur l'avertissement et n'offre
// plus qu'un bouton "Close" ; toute fermeture passe alors par closeDeleteModal(), qui redirige
// vers la librairie puisque la page du volume n'a plus d'objet.

// Options de la popup "Refresh" (page volume ET page library — interface partagée) — mêmes noms
// que RefreshVolumeRequest / RefreshLibraryRequest côté backend.
interface RefreshVolumeOptions {
  syncFromSource: boolean;
  syncNewIssuesOnly: boolean;   // radio sous "Sync with source" : true = NEW only (défaut UI),
                                // false = re-sync de toutes les issues (historique). setSyncScope().
  checkFiles: boolean;          // case avant "Recalculate statistics" : vérifie présence disque +
                                // à-jour de l'analyse CBZ des issues téléchargées (absent → missing).
                                // Défaut UI coché si issues téléchargées.
  recalculateStatistics: boolean;
  regenerateComicInfo: boolean;
  regenerateComicInfoNewOnly: boolean; // radio sous "Regenerate ComicInfo.xml" : true = ne réinjecter
                                       // que dans les CBZ sans ComicInfo.xml (défaut UI), false = tous.
                                       // setComicInfoScope().
  scanKavita: boolean;
}

// ─── issue.service.ts ────────────────────────────────────────────────────────
// Couple (issue, torrent) banni — créé à la suppression d'un download (case cochée par défaut dans
// la modale de la page Downloads, `deleteDownload(id, removeTorrent, ban)`). Listé par la carte
// « Banned torrents » de la page Issue (bouton « Lift » → deleteBan), et force à 0 le score du
// torrent dans les recherches Prowlarr de l'issue ET de son volume (`banned` sur les résultats).
interface IssueBan {
  id: string; issueId: string; torrentTitle: string; trackerName: string | null;
  downloadUrl: string; torrentHash: string; createdAt: string; reason: string | null;
}
type IssueStatus = 'DOWNLOADING' | 'DOWNLOADED' | 'MISSING';
// Catégorie d'album Bedetheque (BedethequeAlbumClassifier côté backend) — 'Standard' pour
// ComicVine/manuel. Page volume : bloc "Issues" = Standard uniquement, bloc "Extra" = le reste,
// groupé par catégorie (voir volume.component.ts : standardIssues/extraGroups).
type IssueCategory = 'Standard' | 'Special' | 'SpecialEdition' | 'Omnibus' | 'Roman' | 'BestOf';

interface Issue {
  id: string; volumeId: string; sourceId: string; issueNumber: number; category: IssueCategory;
  title: string | null; year: number | null; description: string | null;
  status: IssueStatus; authors: VolumeAuthor[]; image: VolumeImage | null;
  cbzFilename: string | null; publishedAt: string | null;
}

interface SourceIssue {
  sourceId: string; source: SourceKey; name: string | null; issueNumber: string;
  coverDate: string | null; imageUrl: string | null; siteUrl: string | null;
}

// ─── library.service.ts ──────────────────────────────────────────────────────
interface Library {
  id: string; name: string; path: string;
  kavitaLibraryId: number; kavitaPath: string; createdAt: string;
}

// ─── kavita.service.ts ───────────────────────────────────────────────────────
interface KavitaLibrary { id: number; name: string; type: number; lastScanned: string; }

// ─── filesystem.service.ts ───────────────────────────────────────────────────
interface DirectoryDto { name: string; fullPath: string; parent: string | null; createdAt: string; modifiedAt: string; }
interface FileDto { name: string; fullPath: string; extension: string; sizeBytes: number; createdAt: string; modifiedAt: string; }

// ─── auth.service.ts ─────────────────────────────────────────────────────────
interface CurrentUser { id: string; login: string; role: string; }

// ─── hub.models.ts ───────────────────────────────────────────────────────────
type EState = 'NOTINIT' | 'INVALID' | 'OK' | 'WARNING' | 'ERROR';
type EValueType = 'STRING' | 'INT' | 'DOUBLE' | 'BOOL' | 'PASSWORD' | 'TEXT';
type ETraceLevel = 'INFO' | 'DEBUG' | 'WARNING' | 'ERROR' | 'CRITICAL' | 'NONE';
type JobState = 'INITIALIZING' | 'RUNNING' | 'SUCCESS' | 'ERROR';

interface OptionDefinition {
  id: string; name: string; value: string; valueType: EValueType;
  mandatory: boolean; description: string; regexValidator: string;
  defaultValue: string; serviceName: string;
}
interface StateService { state: EState; lastRefresh: string; serviceName: string; infos: string[]; }
interface StateServiceManager { stateServices: StateService[]; date: string; globalState: EState; }
interface Progression { total: number; completed: number; error: number; percentage: number; }
interface JobContext {
  jobId: string; state: JobState; title: string; progress: Progression;
  startDate: string; endDate: string | null; duration: string;
}
interface TraceDefinition {
  message: string[]; date: string; serviceName: string; jobId: string | null; level: ETraceLevel;
}
interface UpdatedData { dataType: string; id: string; updatedAt: string; }
```

## Services clés

| Service | Signals exposés | Méthodes principales |
|---|---|---|
| `AuthService` | `currentUser`, `isAuthenticated` | `login()`, `logout()`, `getToken()` |
| `HubService` | `managerState`, `currentJob`, `lastTrace`, `lastDataUpdated`, `jobs`, `jobTraces`, `serviceTraces` | `ensureConnected()`, `disconnect()`, `clearServiceTraces(name)` |
| `ChatbotService` | — | `getStatus()`, `start()`, `stop()` — `/api/chatbot`, page `/settings/chatbot` |
| `LibraryService` | `libraries` | `loadLibraries()`, `getAll()`, `create()`, `update()`, `delete()`, `sync()`, `refresh()`, `patchVolumesStatus(id, 'PAUSED' \| 'MONITORED')` (boutons « Pause all » / « Resume all » de la page Library, affichés selon `monitoredCount()` / `pausedCount()`) |
| `VolumeService` | — | `getById()`, `getByLibrary()`, `search()`, `addFromSource()`, `addManually()`, `update()`, `rematchFromSource()`, `regenerateComicInfo()`, `patchAgeRating()`, `patchStatus(id, 'MONITORED' \| 'PAUSED')` (bouton Pause/Resume de la page Volume, masqué si `COMPLETED`), `delete(id, deleteFiles?)`, `importFromDirectory()` |
| `IssueService` | — | `getByVolume()`, `getBySourceVolume()`, `getDownloads(issueId)` (carte « Download » de la page Issue), `getBans(issueId)` / `deleteBan(banId)` (carte « Banned torrents ») |
| `QBittorrentService` | — | `getDownloads(statuses, page, pageSize)`, `getVolumeDownloads(volumeId)`, `getStalledDownloads(limit)`, `deleteDownload(id, removeTorrent, ban)`, `processDownload()`, `updateDownloadHash()` |
| `KavitaService` | `libraries`, `loading` | `loadLibraries()`, `scanLibrary()` |
| `OptionsService` | — | `getServices()`, `getOptions()`, `updateOptions()` |
| `SchedulerService` | — | `get()`, `update(req)`, `runNow(key)` — config `/api/scheduler` (planificateur cron, page `/settings/scheduler`) ; `SchedulerTaskKey = 'ProcessDownloads' \| 'RollingRefresh' \| 'AutoSearch' \| 'BedethequeCatalog'` |
| `BedethequeCatalogService` | — | `getStatus()`, `refresh(req)` → `{ jobId }` (409 si un refresh tourne déjà) — page `/settings/bedetheque` |
| `SystemService` | — | `getMemory()`, `compactMemory()` — `/api/system/memory`, page `/settings/system` |
| `FilesystemService` | — | `getDirectories()`, `getFiles()` |
| `JobsService` | — | `getStatus(jobId)` — `GET /api/jobs/{id}`, filet de rattrapage HTTP utilisé par `HubService` |
| `PageJobService` | — | `register()`, `clear()`, `activeJobId()`, `trackedEntries()` — association pageKey↔jobId (sessionStorage) |
| `LibraryViewStateService` | — | `get(libraryId)`, `patch(libraryId, partial)` — état de la vue liste Library (filtres + page + `scrollY`) par id, fusion + `sessionStorage` |
| `NavigationTrackerService` | — | `lastTrigger`, `isBackForward`, `previousUrl`, `isReturnInto(baseUrl)` — suit la dernière navigation router (déclencheur + URL quittée) ; instancié tôt par `AppComponent` |

### HubService — événements SignalR reçus

| Événement | Signal mis à jour | Description |
|---|---|---|
| `ManagerStateChanged` | `managerState` | Changement d'état d'un service |
| `ManagerHealthcheck` | `managerState` | Healthcheck périodique |
| `ManagerJobChanged` | `currentJob`, `jobs` | Mise à jour d'un job |
| `ManagerTrace` | `lastTrace`, `jobTraces`, `serviceTraces` | Log de trace — bufferisé par job, et par service pour les modules suivis (voir « Traces par service ») |
| `ManagerDataUpdated` | `lastDataUpdated` | Entité modifiée côté serveur (Volume, Issue, Library…) |

> `lastDataUpdated.dataType` se termine par `'Volume'`, `'Issue'` ou `'Library'` — utiliser `.endsWith()` pour filtrer.

> ⚠️ Les jobs n'émettent pas tous un `ManagerDataUpdated` **par entité modifiée** : le rematch/refresh
> (`RunRematchVolumeJobAsync`) n'en diffuse un que pour le `Volume` (via `RecalculateVolumeStatistics`),
> jamais par issue. Une page qui doit refléter des changements d'issues après un job doit donc
> **recharger explicitement ses données dans l'`effect()` de fin de job** (état `SUCCESS`), sans se
> reposer uniquement sur les souscriptions `lastDataUpdated` — cf. `VolumeComponent` (recharge
> volume + `loadIssues()` à la complétion du job Refresh/Rematch/Import).

### HubService — résynchronisation après coupure (mobile background)

`ManagerJobChanged` est un push fire-and-forget côté serveur (pas de buffer) : un job qui se
termine pendant que le client est déconnecté (app mobile en arrière-plan, WebSocket coupé) ne sera
jamais retransmis. `HubService` compense via un filet de rattrapage HTTP :

- `onreconnected` (SignalR) et le listener `visibilitychange` (retour au premier plan) déclenchent
  tous deux `resyncTrackedJobs()`.
- `resyncTrackedJobs()` interroge `JobsService.getStatus(jobId)` pour chaque job suivi par
  `PageJobService.trackedEntries()` ainsi que tout job connu encore `INITIALIZING`/`RUNNING`, et
  applique le résultat via `applyJobUpdate()` — le **même** point d'écriture que le handler
  `ManagerJobChanged` temps réel. Les pages métier (`effect()`/`computed()` sur `hub.jobs()`) n'ont
  donc rien à changer pour bénéficier de la resync.
- Un `404` (job expiré côté serveur, au-delà de `JobRetention`) libère la page via
  `pageJobs.clear()` plutôt que de la laisser bloquée indéfiniment.

### HubService — traces par service (console du module Chatbot)

`_jobTraces` est indexé **par jobId** : il ne capte rien d'un module qui trace en continu hors de
tout job — c'est le cas du Chatbot, dont la boucle `/sync` est permanente et dont les traces
arrivent avec `jobId === null`. `HubService` tient donc un **second buffer indexé par
`serviceName`**, exposé par le signal `serviceTraces` :

- `TRACKED_SERVICES` (aujourd'hui `{'Chatbot'}`) filtre ce qui est bufferisé — inutile de garder
  les traces de tous les services.
- Le dispatch se fait **après** le bloc `if (trace.jobId)` et **pas** dans un `else` : une trace
  d'un service suivi doit rejoindre sa console même si elle est par ailleurs rattachée à un job.
- **Historisation 100 % client** : `localStorage`, clé `inkhound.serviceTraces.<service>`, écriture
  **débouncée à 1 s** (sérialiser 500 entrées à chaque message serait visible pendant une commande
  bavarde). Double borne **500 entrées et ~256 Ko** — le nombre d'entrées ne dit rien de leur poids,
  une trace pouvant porter plusieurs lignes ; au-delà on retire par moitié depuis le début.
  `QuotaExceededError` et stockage inaccessible (navigation privée, site data bloqué) sont
  attrapés : purge silencieuse, jamais d'erreur vers l'UI.
- Réhydratation dans le **constructeur** du service, pas dans `connect()` : l'historique doit être
  lisible même déconnecté.
- `disconnect()` ne vide **pas** ces traces (contrairement à `_jobTraces`) — c'est tout l'intérêt de
  l'historique. `clearServiceTraces(name)` est le seul effacement, branché sur le bouton « Vider ».

Rien n'est persisté côté serveur : cet historique est local à ce navigateur, et la page le dit.

### Composant réutilisable : TraceConsoleComponent

`app-trace-console` (`views/trace-console/`) — console monospace purement présentationnelle : rendu
des `TraceDefinition[]`, coloration par niveau (`traceLevelClass`) et autoscroll. Extraite de
`job-console-modal`, qui la consomme désormais, pour être partagée avec la page Chatbot : deux
sources de traces différentes, un seul rendu.

```html
<app-trace-console [traces]="traces()" height="60vh" emptyText="Aucune trace pour l'instant." />
```

### LibraryViewStateService — persistance de la vue Library

`LibraryComponent` (`path: ''` sous `library/:id`) est **détruit** quand on ouvre un volume ou
quitte vers `/add-volume`, mais **réutilisé** quand seul `:id` change (library A → library B). Ses
filtres/pagination (signaux locaux) repartaient donc à zéro. `LibraryViewStateService`
(`sessionStorage`, clé = id de library, même pattern que `PageJobService`) mémorise
`{ search, letter, completeness, source, year, ageRating, page, scrollY }` par library ;
`patch()` fusionne (permet de sauver séparément les filtres/page et le `scrollY`).

- **Sauvegarde réactive** : un `effect()` écrit `patch(lib.id, { …filtres, page })` à chaque
  changement — couvre le cas « réutilisation du composant » où `onDestroy` ne se déclenche pas.
  Garde `viewStateReady` (mis à `false` dans un `tap()` sur `route.params`, `true` après chargement
  de la library) pour ne pas écraser l'état restauré avec les défauts. `scrollY` : sauvé par le
  listener `fromEvent(window,'scroll')` (`auditTime`) + un commit final dans `onDestroy`.
- **Restauration filtres + page** : `restoreViewState()` dans le `subscribe` de `route.params`,
  écriture **directe** sur les signaux (jamais via les setters → pas de scroll parasite).
  Applique `EMPTY_LIBRARY_VIEW_STATE` s'il n'y a pas d'état mémorisé (sinon les filtres de la
  library précédente resteraient collés).
- **Restauration scroll** : uniquement quand on « revient » sur la page —
  `navTracker.isReturnInto('/library/{id}')` = retour depuis une sous-page (`/library/{id}/volume/…`
  — via le bouton *Back* de la page volume, le breadcrumb, ou le back navigateur) **ou**
  back/forward navigateur. Le retour depuis `/add-volume` (page dédiée, hors `/library/:id`)
  n'est **pas** un « retour » : liste rechargée en haut, ce qui convient après un ajout. **Pas** lors d'une arrivée depuis la sidebar / un
  autre écran. Le bouton *Back* de `VolumeComponent` fait `router.navigate()` → navigation
  `imperative`, d'où la détection par URL quittée (`NavigationTrackerService.previousUrl`) plutôt
  que par `navigationTrigger` seul. Un `effect()` applique `window.scrollTo` (double
  `requestAnimationFrame` + relance à 300 ms) une fois `volumesLoading()` retombé — passe **après**
  le scroll-to-top asynchrone du `RouterScroller` (`scrollPositionRestoration: 'top'`) et
  l'animation de view transition.
- **Remontée en tête de liste** : ancre `#volumesTop` (+ `scroll-margin-top` pour le header sticky,
  `library.component.scss`) ; `scrollToVolumesTop()` appelé par `goToPage()` et les setters de
  filtre discrets (`resetPaging()`), **pas** par `onSearch()`.

## Composant réutilisable : SelectPathComponent

`app-select-path` — modal de navigation du filesystem serveur.

```typescript
// Inputs
mode        = input<'file' | 'directory'>('directory');
initialPath = input<string>('');
visible     = model<boolean>(false);  // two-way binding

// Output
pathSelected = output<string>();  // chemin sélectionné, ou '' si annulé
```

```html
<app-select-path
  mode="directory"
  [initialPath]="library().path"
  [(visible)]="importVisible"
  (pathSelected)="onImportSelected($event)" />
```

`mode="file"` (émet le chemin complet du fichier sur *confirm*, `''` sur *cancel*) — utilisé par la
page Issue (bouton « Import » → `POST /api/issues/{id}/import { filePath }`).

## Composant réutilisable : DownloadCardComponent

`app-download-card` (`views/download-card/`) — vignette d'un téléchargement : couverture de l'issue
(`DownloadItem.coverUrl`, à défaut celle du volume), badge `#n` en haut-gauche, **badge de statut**
en haut-droite (rouge pour `Stalled`, sinon `downloadStatusColor`), puis titre du volume et début du
nom du torrent (tronqués, complets en infobulle). Cliquable vers la page de l'issue quand
`volumeId`/`libraryId` sont connus.

```html
<c-col xs="6" sm="4" md="3" xl="2"><app-download-card [item]="item" /></c-col>
```

Utilisé par les deux sections du Dashboard (« Stalled downloads » et « Downloads in progress »),
sur le même gabarit que les cartes « Most wanted » pour que la page se lise d'un coup d'œil.

## Composant réutilisable : DownloadListComponent

`app-download-list` (`views/download-list/`) — tableau `.table-stack` des téléchargements et
**toutes** leurs actions : process, correction de hash (modale « Fix download »), suppression
(modale avec les cases « remove torrent » et « ban »). Utilisé par la page Downloads **et** la carte
« Download » de la page Issue : une seule implémentation du rendu, de la carte mobile (6 lignes via
`order`, SCSS du composant) et du flux de suppression.

```html
<app-download-list [items]="items()" (changed)="reload()" />               <!-- page Downloads -->
<app-download-list [items]="downloads()" [showVolume]="false"              <!-- page Issue -->
                   (changed)="reloadDownloads()" />
```
- `showVolume` (défaut `true`) masque la colonne « Volume / Issue » quand la page porte déjà ce contexte.
- `changed` est émis après toute action : le parent recharge sa liste (la page Downloads bumpe son
  `reloadTick`, la page Issue son `downloadsTick`).

Formatage et couleurs dans `core/util/download-format.ts` (`downloadStatusColor`, `formatSpeed`,
`formatEta`, `formatSize`, `formatAdded`, `canProcess`) — fonctions pures partagées avec le
Dashboard, qui en avait sa propre copie divergente.

> Les downloads ne passent **pas** par SignalR (aucun `ManagerDataUpdated` de type download) :
> chaque page qui les affiche poll toutes les 10 s (`merge(interval(10_000), toObservable(tick))`).

## Composant réutilisable : CronEditorComponent

`app-cron-editor` (`views/cron-editor/`) — éditeur d'expression cron 5 champs façon crontab.guru,
**`ControlValueAccessor`** : se branche directement sur un `formControlName`. Sur la page, il
n'affiche qu'un résumé compact (input en lecture seule + traduction en langage naturel + bouton
« Edit ») ; l'édition se fait dans une **modale** (`size="lg"`) sur un brouillon, appliqué au
`FormControl` uniquement sur « Apply » (désactivé si invalide).

```html
<label cLabel for="autoSearchCron">Cron expression</label>
<app-cron-editor inputId="autoSearchCron" formControlName="autoSearchCron"
                 title="Auto search — schedule" placeholder="0 4 * * *" [nextCount]="5" />
```

Contenu de la modale : input + dropdown « Presets » (`CRON_PRESETS`), traduction en anglais
(`cronstrue`, `use24HourTimeFormat`, non verbose), 5 puces (minute / hour / day (month) / month /
day (week)) dont celle sous le curseur est surlignée et pilote le panneau d'aide (plage, alias
`JAN-DEC` / `SUN-SAT`, légende `* , - /`), et les `nextCount` prochaines exécutions (`cron-parser`,
**fuseau du navigateur** — le backend Cronos tourne en heure serveur, mention affichée).

Logique pure dans `cron.utils.ts` (testée par `cron.utils.spec.ts`) : `describeCron`, `cronError`
(strict 5 champs — les secondes sont refusées, comme côté Cronos), `nextOccurrences`, `splitFields`,
`fieldAtCursor`, `CRON_FIELDS` / `CRON_OPERATORS` / `CRON_PRESETS`, et le validateur Reactive Forms
`cronValidator` (vide = valide ; sinon `{ cron: message }`) utilisé par `SchedulerSettingsComponent`
à la place de l'ancienne regex.

Dépendances npm : `cronstrue`, `cron-parser`.

## Composant réutilisable : FileIssueMatcherComponent

`app-file-issue-matcher` (`views/file-issue-matcher/`) — tableau générique d'appariement
**fichiers ↔ issues d'un volume** : auto-appariement par numéro détecté (issues `MISSING` de
catégorie `Standard` uniquement — les hors-séries/omnibus ne s'assignent qu'à la main),
`<select>` manuel par ligne (toutes les issues, `DOWNLOADING` désactivées sauf
`allowDownloadingIssues`, une issue prise ailleurs disparaît des autres listes), coché ⟺ une issue
est assignée.

```typescript
// Inputs
files  = input.required<MatchableFile[]>();  // { name; size; detectedIssueNumber: number | null }
issues = input.required<Issue[]>();
allowDownloadingIssues = input(false);   // true → une issue DOWNLOADING peut être assignée à la main
// Sélection courante — lue par le parent via viewChild(FileIssueMatcherComponent).selection()
selection = computed<{ fileIndex: number; issueId: string }[]>();  // fileIndex = position dans files()
```

Purement présentationnel (aucun appel réseau). Utilisé par :
- `ProwlarrSearchComponent` — revue des fichiers d'un PACK torrent avant `apply-selection`.
- `VolumeComponent` — revue de l'import d'un dossier (`GET .../import/scan` → matcher →
  `POST .../import { fileIssueMap }`). Seul consommateur à passer `allowDownloadingIssues`
  (`true`) : importer un fichier local sur une issue en cours de téléchargement est légitime, le
  backend abandonne alors le suivi du download (sans toucher au torrent ni créer de ban). Côté
  Prowlarr l'option reste à `false` — relancer un grab sur une issue déjà `DOWNLOADING` n'a pas de
  sens.

## Pattern SCSS réutilisable : tableau responsive `.table-stack`

Défini dans `src/scss/_tables.scss` (importé via `_custom.scss`). À utiliser sur tout `<table
cTable>` ayant une colonne de texte libre (titre, nom de fichier...) à côté de colonnes à largeur
fixe — évite qu'une colonne `.text-break` sans largeur ne soit écrasée par l'auto-layout HTML au
profit des colonnes voisines, et bascule en cartes empilées sous 768px (pas de `@media` dans le
composant lui-même, tout est en CSS pur, zéro logique TypeScript).

```html
<table cTable [hover]="true" class="table-stack">
  <thead>
    <tr>
      <th class="col-identity">Titre</th>   <!-- colonne texte libre : largeur plancher desktop -->
      <th style="width: 90px;">Taille</th>
      <th style="width: 60px;"></th>
    </tr>
  </thead>
  <tbody>
    <tr>
      <td class="col-identity">{{ item.title }}</td>              <!-- pleine largeur en carte mobile -->
      <td data-label="Taille">{{ item.size }}</td>                <!-- puce compacte "Taille: ..." en carte -->
      <td class="col-full col-actions"><button cButton>...</button></td> <!-- pied de carte, aligné à droite -->
    </tr>
  </tbody>
</table>
```

- `.col-identity` — colonne texte libre (largeur plancher en desktop/tablette, pleine largeur +
  gras + sans label en carte mobile).
- `.col-full` — cellule à contenu riche qui doit garder sa propre ligne en carte mobile (barre de
  progression, cellule déjà auto-descriptive, cellule d'actions).
- `.col-actions` — à combiner avec `.col-full` sur la cellule de boutons, pour les aligner à droite.
- `[data-label="..."]` — sur les cellules scalaires simples, affichées en puce compacte préfixée du
  label une fois le `<thead>` masqué en mobile.

Utilisé par `DownloadsComponent` (colonne Torrent), `ProwlarrSearchComponent` (colonne Title) et
`JobsComponent` (colonne Title). `JobsComponent` et `DownloadsComponent` complètent le socle carte
par un SCSS local qui réordonne les cellules via `order` en lignes visuelles (avec séparateurs
`border-top` pour Downloads) et masque les icônes date-heure au-dessus de 768px :
- `jobs.component.scss` — 4 lignes : titre / statut + date + durée / progression / bouton console.
- `downloads.component.scss` — 6 lignes : titre `Vol | #n — issue` / torrent / statut + Speed·ETA·Size /
  progression / date / actions.

Le parent retraduit `selection()` (indexé sur la position dans `files()`) vers la clé attendue par
son endpoint (index de fichier qBittorrent / nom de fichier).

## Environnements

- **Dev** : `apiBaseUrl = ''` (proxy Angular vers `http://localhost:5000`)
- **Prod** : chemins relatifs `/api` et `/hub/app` (same-origin single-unit)

## Proxy dev (`proxy.conf.json`)

```json
{
  "/api":  { "target": "http://localhost:5000", "secure": false, "changeOrigin": true },
  "/hub":  { "target": "http://localhost:5000", "secure": false, "ws": true }
}
```

## Naming conventions

| Élément | Convention | Exemple |
|---|---|---|
| Fichier composant | `kebab-case.component.ts` | `volume-list.component.ts` |
| Classe | `PascalCase` + suffixe | `VolumeListComponent` |
| Signal (champ) | `camelCase` | `volumes`, `isLoading` |
| Interface/type | `PascalCase`, sans préfixe `I` | `Volume`, `LoginRequest` |
| Sélecteur CSS | `app-` + kebab | `app-volume-list` |

## Pattern standard composant avec chargement HTTP

```typescript
@Component({
  standalone: true,
  imports: [CardModule, TableDirective, BadgeModule, SpinnerModule, AlertModule, ButtonDirective, IconDirective],
})
export class VolumeListComponent {
  private service    = inject(VolumeService);
  private destroyRef = inject(DestroyRef);

  volumes   = signal<Volume[]>([]);
  isLoading = signal(false);
  error     = signal<string | null>(null);

  ngOnInit() { this.load(); }

  load(): void {
    this.isLoading.set(true);
    this.error.set(null);
    this.service.getAll()
      .pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.isLoading.set(false)))
      .subscribe({ next: data => this.volumes.set(data), error: err => this.error.set(err.error?.message ?? 'Erreur') });
  }
}
```

```html
@if (isLoading()) {
  <c-spinner />
}
@else if (error()) {
  <c-alert color="danger">{{ error() }}</c-alert>
}
@else {
  <c-card>
    <c-card-body>
      <table cTable hover responsive>
        @for (v of volumes(); track v.id) {
          <tr>
            <td>{{ v.title }}</td>
            <td><c-badge [color]="badgeColor(v.status)">{{ v.status }}</c-badge></td>
            <td>
              <button cButton color="primary" size="sm">
                <svg cIcon name="cilPencil" size="sm"></svg>
              </button>
              <button cButton color="danger" size="sm">
                <svg cIcon name="cilTrash" size="sm"></svg>
              </button>
            </td>
          </tr>
        }
      </table>
    </c-card-body>
  </c-card>
}
```
