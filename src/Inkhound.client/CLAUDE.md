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
│   │   └── services/            # AuthService, HubService, LibraryService, VolumeService,
│   │                            # IssueService, KavitaService, OptionsService, FilesystemService, ImageService
│   ├── views/                   # Pages / vues de l'application
│   │   ├── dashboard/           # DashboardComponent
│   │   ├── library/             # LibraryShellComponent, LibraryComponent (liste volumes paginée +
│   │   │                        #   filtres côté client : lettre / complétude / source / titre / année / age rating)
│   │   ├── library-management/  # LibraryManagementComponent (CRUD bibliothèques)
│   │   ├── volume/              # VolumeComponent, VolumeAddComponent, VolumeEditComponent, VolumeMatchComponent
│   │   │   └── issue-card/      # IssueCardComponent — mini-carte issue réutilisée par les blocs "Issues"/"Extra"
│   │   ├── settings/            # SettingsComponent (options par service via OptionsService)
│   │   ├── jobs/                # JobsComponent (historique et suivi des jobs)
│   │   ├── select-path/         # SelectPathComponent — modal réutilisable de navigation filesystem
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
| `/library/:id/add-volume` | `VolumeAddComponent` | Ajouter un volume (recherche multi-source ou manuel) |
| `/library/:id/volume/:volumeId` | `VolumeComponent` | Détail volume + liste issues |
| `/library/:id/volume/:volumeId/edit` | `VolumeEditComponent` | Édition manuelle d'un volume |
| `/library/:id/volume/:volumeId/match` | `VolumeMatchComponent` | Rematch (recherche multi-source) |
| `/settings` | `SettingsComponent` | Options de configuration par service |
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
  createdAt: string; updatedAt: string;
}

type SourceKey = 'comicvine' | 'bedetheque';

interface VolumeSearchResult {
  sourceId: string; source: SourceKey; title: string; year: number | null;
  countOfIssues: number; description: string | null; publisher: string | null;
  imageUrl: string | null; siteUrl: string | null;
}

interface PageResult<T> {
  items: T[]; pageNumber: number; pageSize: number;
  totalItems: number; totalPages: number; hasNext: boolean; hasPrev: boolean;
}

// ─── issue.service.ts ────────────────────────────────────────────────────────
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
| `HubService` | `managerState`, `currentJob`, `lastTrace`, `lastDataUpdated`, `jobs`, `jobTraces` | `ensureConnected()`, `disconnect()` |
| `LibraryService` | `libraries` | `loadLibraries()`, `getAll()`, `create()`, `update()`, `delete()`, `sync()` |
| `VolumeService` | — | `getById()`, `getByLibrary()`, `search()`, `addFromSource()`, `addManually()`, `update()`, `rematchFromSource()`, `regenerateComicInfo()`, `patchAgeRating()`, `delete()`, `importFromDirectory()` |
| `IssueService` | — | `getByVolume()`, `getBySourceVolume()` |
| `KavitaService` | `libraries`, `loading` | `loadLibraries()`, `scanLibrary()` |
| `OptionsService` | — | `getServices()`, `getOptions()`, `updateOptions()` |
| `FilesystemService` | — | `getDirectories()`, `getFiles()` |
| `JobsService` | — | `getStatus(jobId)` — `GET /api/jobs/{id}`, filet de rattrapage HTTP utilisé par `HubService` |
| `PageJobService` | — | `register()`, `clear()`, `activeJobId()`, `trackedEntries()` — association pageKey↔jobId (sessionStorage) |

### HubService — événements SignalR reçus

| Événement | Signal mis à jour | Description |
|---|---|---|
| `ManagerStateChanged` | `managerState` | Changement d'état d'un service |
| `ManagerHealthcheck` | `managerState` | Healthcheck périodique |
| `ManagerJobChanged` | `currentJob`, `jobs` | Mise à jour d'un job |
| `ManagerTrace` | `lastTrace`, `jobTraces` | Log de trace (par job) |
| `ManagerDataUpdated` | `lastDataUpdated` | Entité modifiée côté serveur (Volume, Issue, Library…) |

> `lastDataUpdated.dataType` se termine par `'Volume'`, `'Issue'` ou `'Library'` — utiliser `.endsWith()` pour filtrer.

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

## Composant réutilisable : FileIssueMatcherComponent

`app-file-issue-matcher` (`views/file-issue-matcher/`) — tableau générique d'appariement
**fichiers ↔ issues d'un volume** : auto-appariement par numéro détecté (issues `MISSING`),
`<select>` manuel par ligne (toutes les issues, `DOWNLOADING` désactivées, une issue prise ailleurs
disparaît des autres listes), coché ⟺ une issue est assignée.

```typescript
// Inputs
files  = input.required<MatchableFile[]>();  // { name; size; detectedIssueNumber: number | null }
issues = input.required<Issue[]>();
// Sélection courante — lue par le parent via viewChild(FileIssueMatcherComponent).selection()
selection = computed<{ fileIndex: number; issueId: string }[]>();  // fileIndex = position dans files()
```

Purement présentationnel (aucun appel réseau). Utilisé par :
- `ProwlarrSearchComponent` — revue des fichiers d'un PACK torrent avant `apply-selection`.
- `VolumeComponent` — revue de l'import d'un dossier (`GET .../import/scan` → matcher →
  `POST .../import { fileIssueMap }`).

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

Utilisé par `DownloadsComponent` (colonne Torrent) et `ProwlarrSearchComponent` (colonne Title).

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
