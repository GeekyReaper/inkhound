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

**Installation des packages :**
```bash
npm install @coreui/icons @coreui/icons-angular
```

**Setup dans `app.component.ts` (une seule fois) :**
```typescript
import { IconSetService } from '@coreui/icons-angular';
import { cilUser, cilSettings, cilTrash, cilPencil, cilPlus, cilSearch } from '@coreui/icons';

@Component({ ... })
export class AppComponent {
  constructor(public iconSet: IconSetService) {
    // Enregistrer toutes les icônes utilisées dans l'app
    iconSet.icons = { cilUser, cilSettings, cilTrash, cilPencil, cilPlus, cilSearch };
  }
}
```

**Utilisation dans les composants :**
```typescript
// Importer IconModule dans chaque composant qui utilise des icônes
@Component({
  standalone: true,
  imports: [IconModule],  // ← obligatoire
  ...
})
```

```html
<!-- Par nom (recommandé — icône enregistrée dans IconSetService) -->
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

Préférer ces icônes pour les actions courantes du projet :

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

## Structure

```
src/
├── app/
│   ├── core/                    # Singletons, guards, interceptors, modèles globaux
│   │   ├── guards/
│   │   ├── interceptors/
│   │   ├── models/              # Interfaces TypeScript (auth, job, state, domaine)
│   │   └── services/            # Services providedIn:'root'
│   ├── shared/                  # Composants, pipes, directives réutilisables
│   │   ├── components/
│   │   ├── directives/
│   │   └── pipes/
│   └── features/                # Un dossier par domaine fonctionnel
│       ├── auth/login/
│       ├── dashboard/
│       ├── library/
│       ├── volume/
│       ├── issue/
│       └── admin/
├── components/                  # Composants CoreUI surchargés
├── scss/                        # Styles globaux
└── assets/
```

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

```typescript
// Statuts Volume (miroir de VolumeStatus C#)
type VolumeStatus = 'MONITORED' | 'COMPLETED' | 'PAUSED';

// Statuts Issue (miroir de IssueStatus C#)
type IssueStatus = 'DOWNLOADING' | 'DOWNLOADED' | 'MISSING';

// Types partagés
interface VolumeAuthor { name: string; role: string; }
interface VolumeImage {
  iconUrl: string | null; mediumUrl: string | null; screenUrl: string | null;
  screenLargeUrl: string | null; smallUrl: string | null; superUrl: string | null;
  thumbUrl: string | null; tinyUrl: string | null; originalUrl: string | null;
  imageTags: string | null;
}

// Entités principales (miroir de Inkhound.Core/Models)
interface Library {
  id: string; name: string; path: string; kavitaLibraryId: number; createdAt: string;
}
interface Volume {
  id: string; sourceId: string; sourceType: string; libraryId: string;
  title: string; year: number | null; description: string | null;
  image: VolumeImage | null; publisher: string | null;
  authors: VolumeAuthor[]; genres: string[];
  status: VolumeStatus; countOfIssues: number; countOfDownloadedIssues: number;
  issues: string[] | null; createdAt: string; updatedAt: string; dateAdded: string;
}
interface Issue {
  id: string; comicVineId: string; volumeId: string; issueNumber: number;
  title: string | null; year: number | null; description: string | null;
  image: VolumeImage | null; authors: VolumeAuthor[];
  filePath: string | null; cbzFilename: string | null; fileSizeBytes: number;
  downloadedAt: string; publishedAt: string | null; status: IssueStatus;
}
```

## Services clés

- `AuthService` — signal `currentUser`, `login()`, `logout()`, `isAdmin()`
- `SignalRService` — connexion Hub, méthode `on<T>(eventName)`
- `PlatformStateService` — signal `state`, `applyPatch()`
- `JobService` — signals `job`, `traces`, `isRunning`, `start()`, `cancel()`

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
  imports: [CardModule, TableDirective, BadgeModule, SpinnerModule, AlertModule, ButtonDirective, IconModule],
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