# Architecture — Application Web Full-Stack (Single Unit)

> **Stack** : Angular (dernière version) + CoreUI Free + SignalR (Client)  
> **Backend** : ASP.NET Core MVC (dernière version) + SignalR Hub  
> **Déploiement** : Docker single-unit  
> **Développement local** : VS Code, sans Docker, port configurable via variable d'environnement

---

## Table des matières

1. [Vue d'ensemble](#1-vue-densemble)
2. [Structure des répertoires](#2-structure-des-répertoires)
3. [Conventions](#3-conventions)
4. [Frontend — Angular + CoreUI + SignalR](#4-frontend--angular--coreui--signalr)
   - [4.1 Dépendances](#41-dépendances-packagejson)
   - [4.2 Configuration de l'environnement Angular](#42-configuration-de-lenvironnement-angular)
   - [4.3 Service SignalR](#43-service-signalr-signalrservicets)
   - [4.4 Proxy Angular (dev local)](#44-proxy-angular-dev-local)
   - [4.5 Bonnes pratiques Angular](#45-bonnes-pratiques-angular)
5. [Backend — ASP.NET Core MVC + SignalR](#5-backend--aspnet-core-mvc--signalr)
   - [5.1 `Program.cs`](#51-programcs)
   - [5.2 Hub SignalR](#52-hub-signalr-hubsapphubcs)
   - [5.3 Exemple de contrôleur API](#53-exemple-de-contrôleur-api)
   - [5.4 `appsettings.json`](#54-appsettingsjson)
   - [5.5 `appsettings.Development.json`](#55-appsettingsdevelopmentjson)
   - [5.6 Bonnes pratiques .NET](#56-bonnes-pratiques-net)
6. [État global synchronisé — PlatformState](#6-état-global-synchronisé--platformstate)
   - [6.1 Principe](#61-principe--mix-full-snapshot--patches-partiels)
   - [6.2 Backend — types du mécanisme](#62-backend--les-types-du-mécanisme)
   - [6.3 Backend — `PlatformState.cs`](#63-backend--définition-de-létat-platformstatecs)
   - [6.4 Backend — `PlatformStateService.cs`](#64-backend--platformstateservicecs)
   - [6.5 Enregistrement dans `Program.cs`](#65-enregistrement-dans-programcs)
   - [6.6 Frontend — modèles TypeScript](#66-frontend--modèles-typescript-platform-statemodelts)
   - [6.7 Frontend — `PlatformStateService`](#67-frontend--platformstateservice-angular)
   - [6.8 Utilisation dans les composants](#68-utilisation-dans-les-composants)
   - [6.9 Format JSON des messages SignalR](#69-format-json-des-messages-signalr)
7. [Authentification — JWT + UserStore fichier](#7-authentification--jwt--userstore-fichier)
   - [7.1 Principe](#71-principe)
   - [7.2 `Auth/UserRecord.cs`](#72-backend--authuserrecordcs)
   - [7.3 `Auth/IUserStore.cs`](#73-backend--authiuserstorecs)
   - [7.4 `Auth/FileUserStore.cs`](#74-backend--authfileuserstorecs)
   - [7.5 `Auth/JwtKeyInitializer.cs`](#75-backend--authjwtkeyinitializercs)
   - [7.6 `Auth/JwtService.cs`](#76-backend--authjwtservicecs)
   - [7.7 `Controllers/AuthController.cs`](#77-backend--controllersauthcontrollercs)
   - [7.8 `Controllers/UserController.cs`](#78-backend--controllersusercontrollercs)
   - [7.9 `Program.cs` (ajouts auth)](#79-backend--programcs-ajouts-auth)
   - [7.10 `appsettings.json` (auth)](#710-backend--appsettingsjson-auth)
   - [7.11 Tableau des routes API](#711-tableau-des-routes-api)
   - [7.12 Frontend — `auth.model.ts`](#712-frontend--coremodelsauthmodelts)
   - [7.13 Frontend — `auth.service.ts`](#713-frontend--coreservicesauthservicets)
   - [7.14 Frontend — intercepteur auth](#714-frontend--coreinterceptorsauthinterceptorts)
   - [7.15 Frontend — guard auth](#715-frontend--coreguardsauthguardts)
   - [7.16 Frontend — `app.routes.ts`](#716-frontend--approutests)
   - [7.17 Frontend — `app.config.ts`](#717-frontend--appconfigts)
   - [7.18 SignalR — transmission du JWT](#718-signalr--transmission-du-jwt)
   - [7.19 Docker — persistance `/data/system`](#719-docker--persistance-du-répertoire-datasystem)
8. [Système de jobs](#8-système-de-jobs)
   - [8.1 Principe](#81-principe)
   - [8.2 Structure des fichiers](#82-structure-des-fichiers)
   - [8.3 `Jobs/JobStatus.cs`](#83-jobsjobstatuscs)
   - [8.4 `Jobs/JobTrace.cs`](#84-jobsjobtracecs)
   - [8.5 `Jobs/JobContext.cs`](#85-jobsjobcontextcs)
   - [8.6 `Jobs/IJobHandler.cs`](#86-jobsijobhandlercs)
   - [8.7 `Jobs/JobRunner.cs`](#87-jobsjobrunnercs)
   - [8.8 `Controllers/JobController.cs`](#88-controllersjobcontrollercs)
   - [8.9 Exemple d'implémentation](#89-exemple-dimplémentation-dun-service-métier)
   - [8.10 Enregistrement dans `Program.cs`](#810-enregistrement-dans-programcs)
   - [8.11 Tableau des routes](#811-tableau-des-routes)
   - [8.12 Frontend — modèles TypeScript](#812-frontend--modèles-typescript)
   - [8.13 Frontend — `JobService`](#813-frontend--jobservice-angular)
   - [8.14 Frontend — exemple composant](#814-exemple-dutilisation-dans-un-composant)
   - [8.15 Exemples de messages SignalR](#815-exemples-de-messages-signalr)
9. [Communication Frontend ↔ Backend](#9-communication-frontend--backend)
10. [Dockerfile Single Unit](#10-dockerfile-single-unit)
11. [Configuration VS Code](#11-configuration-vs-code)
    - [11.1 `tasks.json`](#111-vscodetasksjson)
    - [11.2 `launch.json`](#112-vscodelaunchjson)
    - [11.3 `settings.json`](#113-vscodesettingsjson)
12. [Variables d'environnement](#12-variables-denvironnement)
13. [Commandes utiles](#13-commandes-utiles)
14. [Extensions VS Code recommandées](#14-extensions-vs-code-recommandées)

---

## 1. Vue d'ensemble

L'application suit un modèle **Single Unit** : le backend ASP.NET Core sert à la fois l'API REST, les WebSockets SignalR, **et** les fichiers statiques compilés du frontend Angular. Un seul conteneur Docker expose un unique port.

```
┌─────────────────────────────────────────────────┐
│                  Docker Container                │
│                                                  │
│  ┌────────────────────────────────────────────┐  │
│  │          ASP.NET Core (Kestrel)            │  │
│  │                                            │  │
│  │  ┌──────────┐  ┌──────────┐  ┌─────────┐  │  │
│  │  │  MVC /   │  │ SignalR  │  │ Static  │  │  │
│  │  │  API     │  │  Hub     │  │ Files   │  │  │
│  │  │ /api/**  │  │ /hub/**  │  │ Angular │  │  │
│  │  └──────────┘  └──────────┘  └─────────┘  │  │
│  └────────────────────────────────────────────┘  │
│                       ▲                          │
│                  PORT (env)                      │
└──────────────────────────────────────────────────┘
         ▲
   Navigateur Client
   Angular SPA + SignalR JS Client
```

---

## 2. Structure des répertoires

```
/
├── src/
│   ├── MyApp.Web/                    # Projet ASP.NET Core
│   │   ├── Controllers/
│   │   │   ├── ApiController.cs
│   │   │   ├── AuthController.cs     # POST /api/auth/login, GET /api/auth/me
│   │   │   └── UserController.cs     # CRUD /api/users
│   │   ├── Auth/
│   │   │   ├── IUserStore.cs         # Interface + DTOs de mutation
│   │   │   ├── FileUserStore.cs      # Implémentation fichier (PBKDF2, thread-safe)
│   │   │   ├── JwtService.cs         # Génération des tokens JWT
│   │   │   ├── JwtKeyInitializer.cs  # IHostedService — génère /data/system/jwt.key au 1er run
│   │   │   └── UserRecord.cs         # Record persisté dans users.json
│   │   ├── Jobs/
│   │   │   ├── JobStatus.cs          # Enums JobStatus et TraceLevel
│   │   │   ├── JobTrace.cs           # Record trace unitaire
│   │   │   ├── JobContext.cs         # État complet du job
│   │   │   ├── IJobHandler.cs        # Interface implémentée par chaque service métier
│   │   │   └── JobRunner.cs          # Singleton — exclusivité, SignalR
│   │   ├── Hubs/
│   │   │   └── AppHub.cs             # Hub SignalR
│   │   ├── State/
│   │   │   ├── TrackedProperty.cs    # Wrapper générique T + timestamp
│   │   │   ├── TrackedValue.cs       # DTO JSON sérialisé
│   │   │   ├── StatePatch.cs         # Enveloppe message SignalR
│   │   │   ├── PlatformState.cs      # Définition de l'état global
│   │   │   └── PlatformStateService.cs # Singleton — source de vérité
│   │   ├── Middleware/
│   │   │   └── ExceptionMiddleware.cs  # Gestion centralisée des exceptions → JSON
│   │   ├── wwwroot/                  # ← Build Angular copié ici
│   │   ├── Program.cs
│   │   ├── appsettings.json
│   │   ├── appsettings.Development.json
│   │   └── MyApp.Web.csproj
│   │
│   └── MyApp.Client/                 # Projet Angular
│       ├── src/
│       │   ├── app/
│       │   │   ├── core/             # Singletons, services globaux, guards, interceptors
│       │   │   │   ├── guards/
│       │   │   │   │   └── auth.guard.ts
│       │   │   │   ├── interceptors/
│       │   │   │   │   ├── auth.interceptor.ts
│       │   │   │   │   └── error.interceptor.ts  # Gestion centralisée 401/403/0
│       │   │   │   ├── models/
│       │   │   │   │   ├── auth.model.ts
│       │   │   │   │   ├── job.model.ts                 # JobContext, JobTrace, JobStatus
│       │   │   │   │   └── platform-state.model.ts
│       │   │   │   └── services/
│       │   │   │       ├── auth.service.ts
│       │   │   │       ├── job.service.ts               # Signal job + traces, start/cancel
│       │   │   │       ├── platform-state.service.ts
│       │   │   │       └── signalr.service.ts
│       │   │   ├── shared/           # Composants, pipes, directives réutilisables
│       │   │   │   ├── components/
│       │   │   │   │   ├── spinner/
│       │   │   │   │   └── alert-banner/
│       │   │   │   ├── directives/
│       │   │   │   └── pipes/
│       │   │   │   ├── features/         # Un dossier par domaine fonctionnel
│       │   │   │   │   ├── auth/
│       │   │   │   │   │   └── login/
│       │   │   │   │   │       ├── login.component.ts
│       │   │   │   │   │       └── login.component.html
│       │   │   │   │   ├── dashboard/
│       │   │   │   │   │   ├── dashboard.component.ts
│       │   │   │   │   │   └── dashboard.component.html
│       │   │   │   │   └── admin/
│       │   │   │   │       ├── users/
│       │   │   │   │       │   ├── user-list/
│       │   │   │   │       │   └── user-form/
│       │   │   │   │       └── settings/
│       │   │   │   ├── app.routes.ts     # Déclaration de toutes les routes
│       │   │   │   ├── app.config.ts     # Providers globaux + interceptors
│       │   │   │   └── app.component.ts  # Root component minimal
│       │   ├── environments/
│       │   │   ├── environment.ts
│       │   │   └── environment.prod.ts
│       │   └── main.ts
│       ├── angular.json
│       └── package.json
│
├── data/                             # Volume persistant (hors image Docker)
│   └── system/
│       ├── jwt.key                   # Généré automatiquement au 1er lancement
│       └── users.json                # Base utilisateurs (PBKDF2) — admin/admin par défaut
│
├── .vscode/
│   ├── launch.json
│   ├── tasks.json
│   └── settings.json
│
├── Dockerfile
├── .dockerignore
└── docker-compose.yml               # Optionnel, pour faciliter le run local Docker
```

## 3. Conventions

- Les commentaires dans le code sont en anglais
- Les commit sont en anglais
- Les fichier readme.md sont en anglais

---

## 4. Frontend — Angular + CoreUI + SignalR

### 4.1 Dépendances (`package.json`)

```json
{
  "dependencies": {
    "@angular/animations": "latest",
    "@angular/common": "latest",
    "@angular/compiler": "latest",
    "@angular/core": "latest",
    "@angular/forms": "latest",
    "@angular/platform-browser": "latest",
    "@angular/platform-browser-dynamic": "latest",
    "@angular/router": "latest",
    "@coreui/angular": "latest",
    "@coreui/coreui": "latest",
    "@coreui/icons": "latest",
    "@coreui/icons-angular": "latest",
    "@microsoft/signalr": "latest",
    "rxjs": "latest",
    "tslib": "latest",
    "zone.js": "latest"
  }
}
```

### 4.2 Configuration de l'environnement Angular

**`src/environments/environment.ts`** (développement local)
```typescript
export const environment = {
  production: false,
  // Le port est injecté au moment du build via le proxy Angular,
  // ou via une variable window.__env définie par le backend.
  apiBaseUrl: 'http://localhost:${PORT}/api',
  signalrHubUrl: 'http://localhost:${PORT}/hub/app'
};
```

**`src/environments/environment.prod.ts`** (production / Docker)
```typescript
export const environment = {
  production: true,
  // En production single-unit, les URLs sont relatives (même origine).
  apiBaseUrl: '/api',
  signalrHubUrl: '/hub/app'
};
```

> **Astuce** : En mode production single-unit, utilisez des chemins relatifs. Le navigateur résoudra automatiquement sur le même host/port que l'application.

### 4.3 Service SignalR (`signalr.service.ts`)

```typescript
import { Injectable, inject } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { environment } from '../../../environments/environment';
import { PlatformStateService } from './platform-state.service';
import { StatePatch } from '../models/platform-state.model';

@Injectable({ providedIn: 'root' })
export class SignalRService {
  private hubConnection!: signalR.HubConnection;
  private platform = inject(PlatformStateService);

  startConnection(): void {
    this.hubConnection = new signalR.HubConnectionBuilder()
      .withUrl(environment.signalrHubUrl)
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.Information)
      .build();

    this.hubConnection
      .start()
      .then(() => console.log('SignalR connected'))
      .catch(err => console.error('SignalR error:', err));

    // Réception de tous les patches (full au démarrage, partial ensuite)
    this.hubConnection.on('StateChanged', (patch: StatePatch) => {
      this.platform.applyPatch(patch);
    });
  }

  stopConnection(): void {
    this.hubConnection?.stop();
  }
}
```

### 4.4 Proxy Angular (dev local)

Créer `proxy.conf.json` à la racine du projet Angular :

```json
{
  "/api": {
    "target": "http://localhost:${APP_PORT}",
    "secure": false,
    "changeOrigin": true
  },
  "/hub": {
    "target": "http://localhost:${APP_PORT}",
    "secure": false,
    "changeOrigin": true,
    "ws": true
  }
}
```

Dans `angular.json`, configurer le proxy pour le serve :
```json
"serve": {
  "builder": "@angular-devkit/build-angular:dev-server",
  "options": {
    "proxyConfig": "proxy.conf.json"
  }
}
```

### 4.5 Bonnes pratiques Angular

Cette section définit les conventions de code et d'organisation à respecter dans tout le projet Angular. Elle s'applique à tous les développeurs et sert de référence lors des revues de code.

---

#### 4.5.1 Organisation des dossiers — architecture layer-based

L'application est structurée en trois couches horizontales, chacune avec un rôle strict.

```
src/app/
│
├── core/                        # Singletons, services globaux, guards, interceptors
│   ├── guards/
│   │   └── auth.guard.ts
│   ├── interceptors/
│   │   └── auth.interceptor.ts
│   ├── models/                  # Interfaces et types partagés globalement
│   │   ├── auth.model.ts
│   │   ├── job.model.ts
│   │   └── platform-state.model.ts
│   └── services/                # Services injectés providedIn:'root'
│       ├── auth.service.ts
│       ├── job.service.ts
│       ├── platform-state.service.ts
│       └── signalr.service.ts
│
├── shared/                      # Composants, pipes, directives réutilisables
│   ├── components/
│   │   ├── spinner/
│   │   │   ├── spinner.component.ts
│   │   │   └── spinner.component.html
│   │   └── alert-banner/
│   │       ├── alert-banner.component.ts
│   │       └── alert-banner.component.html
│   ├── directives/
│   │   └── has-role.directive.ts
│   └── pipes/
│       └── time-ago.pipe.ts
│
├── features/                    # Un dossier par domaine fonctionnel
│   ├── auth/
│   │   └── login/
│   │       ├── login.component.ts
│   │       └── login.component.html
│   ├── dashboard/
│   │   ├── dashboard.component.ts
│   │   └── dashboard.component.html
│   └── admin/
│       ├── users/
│       │   ├── user-list/
│       │   │   ├── user-list.component.ts
│       │   │   └── user-list.component.html
│       │   └── user-form/
│       │       ├── user-form.component.ts
│       │       └── user-form.component.html
│       └── settings/
│           ├── settings.component.ts
│           └── settings.component.html
│
├── app.routes.ts                # Déclaration de toutes les routes
├── app.config.ts                # Configuration globale (providers, interceptors)
└── app.component.ts             # Root component minimal
```

**Règles de dépendances entre couches :**

- `core` → aucune dépendance vers `shared` ou `features`
- `shared` → peut dépendre de `core` (modèles uniquement), jamais de `features`
- `features` → peut dépendre de `core` et `shared`, jamais d'une autre feature

---

#### 4.5.2 Composants — Standalone uniquement

Tous les composants sont standalone. Aucun `NgModule` n'est créé.

```typescript
// ✅ Correct — standalone avec imports explicites
@Component({
  selector: 'app-user-list',
  standalone: true,
  imports: [CommonModule, RouterModule, CIconComponent],
  templateUrl: './user-list.component.html',
})
export class UserListComponent { }

// ❌ Interdit — NgModule
@NgModule({ declarations: [UserListComponent] })
export class UsersModule { }
```

Chaque composant possède son propre dossier. Les fichiers `.ts` et `.html` sont toujours séparés — pas de template inline sauf pour les composants de moins de 5 lignes de template.

```
user-list/
├── user-list.component.ts
├── user-list.component.html
└── user-list.component.scss    # optionnel, si styles spécifiques
```

---

#### 4.5.3 Naming conventions

| Élément | Convention | Exemple |
|---|---|---|
| Fichier composant | `kebab-case.component.ts` | `user-list.component.ts` |
| Fichier service | `kebab-case.service.ts` | `auth.service.ts` |
| Fichier modèle | `kebab-case.model.ts` | `auth.model.ts` |
| Fichier guard | `kebab-case.guard.ts` | `auth.guard.ts` |
| Fichier interceptor | `kebab-case.interceptor.ts` | `auth.interceptor.ts` |
| Fichier pipe | `kebab-case.pipe.ts` | `time-ago.pipe.ts` |
| Classe composant | `PascalCase` + suffixe `Component` | `UserListComponent` |
| Classe service | `PascalCase` + suffixe `Service` | `AuthService` |
| Interface/type | `PascalCase`, pas de préfixe `I` | `LoginRequest`, `CurrentUser` |
| Signal (champ) | `camelCase`, nom du concept | `currentUser`, `state` |
| Signal computed | `camelCase`, nom du résultat | `isAdmin`, `status` |
| Input signal | `camelCase` | `userId`, `readOnly` |
| Sélecteur CSS | `app-` + `kebab-case` | `app-user-list` |
| Variable locale template | `camelCase` court | `user`, `item`, `err` |

---

#### 4.5.4 Signals — règles d'usage

Les Signals Angular sont l'unique mécanisme de réactivité dans ce projet. RxJS est réservé aux flux asynchrones qui ne peuvent pas être exprimés avec des Signals (ex : `HttpClient`, `fromEvent`).

```typescript
// ✅ State local d'un composant → signal()
export class UserFormComponent {
  isSubmitting = signal(false);
  errorMessage = signal<string | null>(null);
}

// ✅ Valeur dérivée → computed() — jamais recalculée inutilement
export class NavbarComponent {
  private auth   = inject(AuthService);
  userLabel      = computed(() => `${this.auth.currentUser()?.login} (${this.auth.currentUser()?.role})`);
  showAdminMenu  = computed(() => this.auth.currentUser()?.role === 'admin');
}

// ✅ Effet de bord réactif → effect() — uniquement pour les side effects
export class AppComponent implements OnInit {
  private auth = inject(AuthService);
  ngOnInit() {
    effect(() => {
      const user = this.auth.currentUser();
      if (!user) console.log('Session terminée');
    });
  }
}

// ❌ Interdit — ne pas exposer le WritableSignal raw en public
export class AuthService {
  private _currentUser = signal<CurrentUser | null>(null);
  readonly currentUser = this._currentUser.asReadonly();   // ✅ lecture seule pour l'extérieur
}

// ❌ Interdit — ne pas utiliser BehaviorSubject quand un signal suffit
private _state = new BehaviorSubject<State | null>(null);   // remplacer par signal()
```

**Quand RxJS est encore acceptable :**

```typescript
// ✅ HttpClient retourne des Observables — toujours utiliser pipe(takeUntilDestroyed())
export class UserService {
  private http          = inject(HttpClient);
  private destroyRef    = inject(DestroyRef);

  getUsers(): Observable<UserDto[]> {
    return this.http.get<UserDto[]>('/api/users');
  }
}

// ✅ Dans un composant — souscrire proprement sans unsubscribe manuel
export class UserListComponent {
  private userService = inject(UserService);
  users = signal<UserDto[]>([]);

  ngOnInit() {
    this.userService.getUsers()
      .pipe(takeUntilDestroyed(inject(DestroyRef)))
      .subscribe(data => this.users.set(data));
  }
}
```

---

#### 4.5.5 Règles sur les templates

**Logique interdite dans les templates.** Toute condition ou calcul non trivial doit être dans un `computed()` dans la classe.

```html
<!-- ❌ Interdit — logique métier dans le template -->
<span>{{ user().role === 'admin' ? 'Administrateur' : 'Invité' }}</span>

<!-- ✅ Correct — computed() dans la classe -->
<span>{{ roleLabel() }}</span>
```

```typescript
roleLabel = computed(() =>
  this.auth.currentUser()?.role === 'admin' ? 'Administrateur' : 'Invité'
);
```

**Utiliser la nouvelle syntaxe de flux de contrôle** (`@if`, `@for`, `@switch`) — jamais `*ngIf` ou `*ngFor`.

```html
<!-- ❌ Interdit -->
<div *ngIf="isAdmin()">...</div>
<li *ngFor="let user of users()">...</li>

<!-- ✅ Correct -->
@if (isAdmin()) {
  <div>...</div>
}
@for (user of users(); track user.id) {
  <li>{{ user.login }}</li>
}
```

**`track` est obligatoire** dans `@for` — toujours utiliser l'identifiant unique de l'objet.

**Accès au `currentUser` dans les templates** — utiliser les `computed()` exposés par `AuthService`, pas l'appel direct au signal imbriqué :

```html
<!-- ❌ Fragile — accès imbriqué dans le template -->
<span>{{ auth.currentUser()?.login }}</span>

<!-- ✅ Correct — computed() dans la classe du composant -->
<span>{{ userLogin() }}</span>
```

---

#### 4.5.6 Gestion des erreurs HTTP

Un intercepteur centralisé gère les erreurs globales. Les composants ne traitent que les erreurs métier spécifiques à leur contexte.

**`core/interceptors/error.interceptor.ts`**

```typescript
import { inject } from '@angular/core';
import { HttpInterceptorFn, HttpErrorResponse } from '@angular/common/http';
import { catchError, throwError } from 'rxjs';
import { Router } from '@angular/router';

export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const router = inject(Router);

  return next(req).pipe(
    catchError((err: HttpErrorResponse) => {
      switch (err.status) {
        case 401: router.navigate(['/login']);     break;  // token expiré
        case 403: router.navigate(['/forbidden']); break;  // rôle insuffisant
        case 0:   console.error('Serveur injoignable'); break;
      }
      return throwError(() => err);
  @if (serverError()) {
    <app-alert-banner type="error" [message]="serverError()!" />
  }

  <button type="submit" [disabled]="form.invalid || isSubmitting()">
    {{ isSubmitting() ? 'Connexion...' : 'Se connecter' }}
  </button>
</form>
```

---

#### 4.5.7 Accès au profil utilisateur dans les composants

Le `currentUser` signal de `AuthService` est accessible dans tout composant. Les propriétés dérivées sont déclarées comme `computed()` dans la classe — jamais calculées dans le template.

```typescript
// Pattern standard d'accès au profil courant
export class NavbarComponent {
  private auth = inject(AuthService);

  // Computed() — réactifs, recalculés uniquement si currentUser() change
  userLogin   = computed(() => this.auth.currentUser()?.login ?? '');
  userRole    = computed(() => this.auth.currentUser()?.role  ?? '');
  isAdmin     = computed(() => this.auth.currentUser()?.role === 'admin');
  isLoggedIn  = computed(() => this.auth.isAuthenticated());

  logout() { this.auth.logout(); }
}
```

```html
@if (isLoggedIn()) {
  <span class="nav-user">{{ userLogin() }}</span>
  <span class="badge">{{ userRole() }}</span>

  @if (isAdmin()) {
    <a routerLink="/admin">Administration</a>
  }

  <button (click)="logout()">Déconnexion</button>
}
```

---

#### 4.5.8 Résumé — checklist de revue de code

Avant tout merge, vérifier :

| # | Règle | Contrôle |
|---|-------|---------|
| 1 | Composant standalone, pas de NgModule | `standalone: true` présent |
| 2 | Pas de logique métier dans le template | Conditions → `computed()` |
| 3 | Nouvelle syntaxe `@if` / `@for` | Pas de `*ngIf` / `*ngFor` |
| 4 | `track` présent sur chaque `@for` | Identifiant unique utilisé |
| 5 | États de chargement gérés | `isLoading`, `error` signals présents |
| 6 | Souscription RxJS propre | `takeUntilDestroyed()` utilisé |
| 7 | Formulaire Reactive Forms | `FormBuilder` + `Validators` |
| 8 | Signal interne privé | `asReadonly()` exposé au public |
| 9 | Naming conventions respectées | Voir tableau section 3.6.3 |



## 5. Backend — ASP.NET Core MVC + SignalR

---

### 5.1 `Program.cs`

```csharp
var builder = WebApplication.CreateBuilder(args);

// ── Port configurable via variable d'environnement ──────────────────────────
var port = Environment.GetEnvironmentVariable("APP_PORT") ?? "5000";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// ── Services ─────────────────────────────────────────────────────────────────
builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();

builder.Services.AddCors(options =>
{
    options.AddPolicy("DevPolicy", policy =>
        policy
            .WithOrigins($"http://localhost:4200")  // Port par défaut Angular dev
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials());
});

var app = builder.Build();

// ── Middleware ────────────────────────────────────────────────────────────────
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
else
{
    app.UseCors("DevPolicy");
    app.UseDeveloperExceptionPage();
}

// Sert les fichiers statiques du build Angular (wwwroot)
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseRouting();
app.UseAuthorization();

// ── Routes ───────────────────────────────────────────────────────────────────
app.MapControllers();
app.MapHub<AppHub>("/hub/app");

// SPA Fallback — toutes les routes inconnues renvoient index.html (Angular Router)
app.MapFallbackToFile("index.html");

app.Run();
```

### 5.2 Hub SignalR (`Hubs/AppHub.cs`)

```csharp
using Microsoft.AspNetCore.SignalR;
using MyApp.Web.State;

namespace MyApp.Web.Hubs;

public class AppHub(PlatformStateService platform) : Hub
{
    public override async Task OnConnectedAsync()
    {
        // Envoie le snapshot complet uniquement au nouveau client
        await Clients.Caller.SendAsync("StateChanged", platform.GetFullPatch());
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await base.OnDisconnectedAsync(exception);
    }
}
```

### 5.3 Exemple de contrôleur API (`Controllers/ApiController.cs`)

```csharp
using Microsoft.AspNetCore.Mvc;

namespace MyApp.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SampleController : ControllerBase
{
    [HttpGet("ping")]
    public IActionResult Ping() => Ok(new { status = "ok", timestamp = DateTime.UtcNow });
}
```

### 5.4 `appsettings.json`

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.AspNetCore.SignalR": "Debug"
    }
  },
  "AllowedHosts": "*"
}
```

### 5.5 `appsettings.Development.json`

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.AspNetCore.SignalR": "Debug"
    }
  }
}
```

### 5.6 Bonnes pratiques .NET

Cette section définit les conventions de code et d'organisation à respecter dans tout le projet ASP.NET Core. Elle sert de référence lors des revues de code.

---

#### 5.6.1 Organisation des dossiers et couches

Le projet backend est organisé par **domaine technique** à la racine, avec un dossier par responsabilité. Chaque dossier = un namespace = une responsabilité claire.

```
MyApp.Web/
│
├── Controllers/          # Points d'entrée HTTP — aucune logique métier
│   ├── AuthController.cs
│   └── UserController.cs
│
├── Hubs/                 # Points d'entrée SignalR
│   └── AppHub.cs
│
├── Auth/                 # Domaine authentification
│   ├── IUserStore.cs
│   ├── FileUserStore.cs
│   ├── JwtService.cs
│   ├── JwtKeyInitializer.cs
│   └── UserRecord.cs
│
├── State/                # Domaine état global temps réel
│   ├── TrackedProperty.cs
│   ├── TrackedValue.cs
│   ├── StatePatch.cs
│   ├── PlatformState.cs
│   └── PlatformStateService.cs
│
├── Middleware/           # Middleware ASP.NET Core personnalisés
│   └── ExceptionMiddleware.cs
│
├── wwwroot/              # Build Angular (généré, ne pas éditer)
├── Program.cs            # Composition root — DI, pipeline, configuration
├── appsettings.json
└── appsettings.Development.json
```

**Règles de dépendances entre couches :**

- `Controllers` → dépend de `Auth`, `State` (via interfaces ou services injectés)
- `Hubs` → dépend de `State`
- `Auth`, `State` → aucune dépendance vers `Controllers` ou `Hubs`
- `Middleware` → dépend uniquement des abstractions ASP.NET Core
- `Program.cs` → seul fichier autorisé à référencer toutes les couches (composition root)

---

#### 5.6.2 Naming conventions

| Élément | Convention | Exemple |
|---|---|---|
| Fichier / Classe | `PascalCase` | `UserController.cs` |
| Interface | `PascalCase` préfixé `I` | `IUserStore` |
| Record | `PascalCase` | `UserRecord`, `StatePatch` |
| Méthode publique | `PascalCase` | `GetAllAsync()`, `ValidateAsync()` |
| Méthode privée | `PascalCase` | `BuildProps()`, `HashPassword()` |
| Champ privé | `_camelCase` | `_filePath`, `_cache`, `_lock` |
| Constante | `PascalCase` ou `UPPER_SNAKE` pour les configs | `KeyFile`, `ConfigKey` |
| Paramètre / variable locale | `camelCase` | `userId`, `request`, `existing` |
| Namespace | `PascalCase` hiérarchique | `MyApp.Web.Auth`, `MyApp.Web.State` |
| Méthode async | Suffixe `Async` | `CreateAsync()`, `UpdateAsync()` |
| DTO de requête | Suffixe `Request` | `CreateUserRequest`, `LoginRequest` |
| DTO de réponse | Suffixe `Response` ou `Dto` | `LoginResponse`, `UserDto` |

**Règle fichier** : un fichier = une classe/interface/record. Le nom du fichier correspond exactement au nom du type qu'il contient.

---

#### 5.6.3 Injection de dépendances — primary constructors (C# 12)

Toutes les dépendances sont injectées via **primary constructor**. Pas de constructeur classique, pas de champs privés pour les dépendances injectées.

```csharp
// ✅ Correct — primary constructor C# 12
public class JwtService(IConfiguration config)
{
    private readonly string _secret = config["Auth:JwtSecret"]
        ?? throw new InvalidOperationException("Auth:JwtSecret manquant");
}

public class UserController(IUserStore users) : ControllerBase { }

public class AppHub(PlatformStateService platform) : Hub { }

// ❌ Interdit — constructeur classique
public class JwtService
{
    private readonly IConfiguration _config;
    public JwtService(IConfiguration config) { _config = config; }
}
```

**Durées de vie dans `Program.cs`** :

```csharp
// Singleton  — une seule instance pour toute la durée de l'app
builder.Services.AddSingleton<IUserStore, FileUserStore>();
builder.Services.AddSingleton<JwtService>();
builder.Services.AddSingleton<PlatformStateService>();

// Scoped     — une instance par requête HTTP (défaut pour les services métier)
builder.Services.AddScoped<IMonService, MonService>();

// Transient  — une nouvelle instance à chaque injection (léger, sans état)
builder.Services.AddTransient<IMonHelper, MonHelper>();
```

**Règle** : toujours enregistrer une interface plutôt que la classe concrète dès qu'une abstraction existe (`IUserStore` et non `FileUserStore`). Cela facilite les tests et les futures migrations.

---

#### 5.6.4 Règles sur les controllers

**Structure d'un controller :**

```csharp
[ApiController]
[Route("api/[controller]")]      // convention kebab-case automatique sur le segment
[Authorize]                      // protection par défaut sur tout le controller
public class ResourceController(IResourceService service) : ControllerBase
{
    // ── DTOs internes (si simples) ou dans un fichier dédié ──────────────────
    private record ResourceDto(string Id, string Name);
    private static ResourceDto ToDto(Resource r) => new(r.Id, r.Name);

    // ── Actions — une action = une responsabilité ────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetAll() { ... }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id) { ... }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateResourceRequest req) { ... }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateResourceRequest req) { ... }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id) { ... }
}
```

**Codes HTTP à retourner :**

| Situation | Méthode |
|---|---|
| Lecture réussie | `Ok(dto)` — 200 |
| Création réussie | `CreatedAtAction(nameof(GetById), new { id }, dto)` — 201 |
| Mise à jour réussie | `Ok(dto)` — 200 |
| Suppression réussie | `NoContent()` — 204 |
| Ressource introuvable | `NotFound()` — 404 |
| Données invalides | `BadRequest(new { message })` — 400 |
| Conflit (doublon) | `Conflict(new { message })` — 409 |
| Accès refusé (rôle) | `Forbid()` — 403 |
| Non authentifié | `Unauthorized(new { message })` — 401 |

**Règles DTOs :**

```csharp
// ✅ Ne jamais exposer directement un record de persistance dans la réponse
public async Task<IActionResult> GetAll()
    => Ok((await users.GetAllAsync()).Select(ToDto));  // ← transformation explicite

// ❌ Interdit — expose le hash du mot de passe
public async Task<IActionResult> GetAll()
    => Ok(await users.GetAllAsync());                  // ← UserRecord contient PasswordHash
```

**Règles sur les routes :**

```csharp
// ✅ Routes explicites — toujours préfixées /api/
[Route("api/auth")]           // préfixe fixe
[Route("api/[controller]")]   // préfixe automatique : api/users, api/resource...

// ✅ Verbes HTTP corrects
[HttpGet]         // lecture sans effet de bord
[HttpPost]        // création
[HttpPut("{id}")] // remplacement complet
[HttpPatch("{id}")] // modification partielle
[HttpDelete("{id}")] // suppression

// ❌ Interdit — verbes incorrects ou routes ambiguës
[HttpGet("delete/{id}")]  // action destructive via GET
[HttpPost("getAll")]      // lecture via POST
```

---

#### 5.6.5 Gestion des erreurs et exceptions globales

Un middleware centralisé intercepte toutes les exceptions non gérées et retourne une réponse JSON cohérente. Les controllers ne contiennent des `try/catch` que pour les erreurs **métier prévisibles** (`KeyNotFoundException`, `InvalidOperationException`, `ArgumentException`).

**`Middleware/ExceptionMiddleware.cs`**

```csharp
namespace MyApp.Web.Middleware;

public class ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        try
        {
            await next(ctx);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception non gérée : {Message}", ex.Message);
            await WriteErrorAsync(ctx, ex);
        }
    }

    private static async Task WriteErrorAsync(HttpContext ctx, Exception ex)
    {
        ctx.Response.ContentType = "application/json";

        // Mapping exception → code HTTP
        ctx.Response.StatusCode = ex switch
        {
            KeyNotFoundException        => StatusCodes.Status404NotFound,
            InvalidOperationException   => StatusCodes.Status409Conflict,
            ArgumentException           => StatusCodes.Status400BadRequest,
            UnauthorizedAccessException => StatusCodes.Status401Unauthorized,
            _                           => StatusCodes.Status500InternalServerError
        };

        var payload = new
        {
            status  = ctx.Response.StatusCode,
            message = ctx.Response.StatusCode == 500
                          ? "Une erreur interne est survenue."  // ne pas exposer les détails en prod
                          : ex.Message
        };

        await ctx.Response.WriteAsJsonAsync(payload);
    }
}
```

**Enregistrement dans `Program.cs`** — toujours en premier dans le pipeline :

```csharp
app.UseMiddleware<ExceptionMiddleware>();  // ← avant tout autre middleware
app.UseAuthentication();
app.UseAuthorization();
```

**Pattern dans les controllers** — avec le middleware global, les controllers restent concis :

```csharp
// ✅ Le middleware catch les KeyNotFoundException automatiquement
[HttpGet("{id}")]
public async Task<IActionResult> GetById(string id)
{
    var user = await users.GetByIdAsync(id);
    return user is null ? NotFound() : Ok(ToDto(user));
}

// ✅ try/catch uniquement pour distinguer des codes HTTP différents
[HttpPost]
public async Task<IActionResult> Create([FromBody] CreateUserRequest request)
{
    try   { return CreatedAtAction(..., await users.CreateAsync(request)); }
    catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    catch (ArgumentException ex)         { return BadRequest(new { message = ex.Message }); }
    // KeyNotFoundException → laissée au middleware → 404 automatique
}

// ❌ Interdit — catch trop large qui masque les erreurs
catch (Exception ex) { return StatusCode(500, ex.Message); }
```

**Logging — niveaux à respecter :**

```csharp
// Informations de flux normaux
logger.LogInformation("Utilisateur {Login} connecté", user.Login);

// Anomalie récupérée (ne bloque pas le service)
logger.LogWarning("Tentative de login échouée pour {Login}", login);

// Erreur qui impacte une fonctionnalité
logger.LogError(ex, "Échec de la lecture du fichier {Path}", _filePath);

// ❌ Interdit — logger une exception sans le paramètre ex
logger.LogError("Erreur : " + ex.Message);   // perd la stack trace
```

---

#### 5.6.6 Résumé — checklist de revue de code

Avant tout merge, vérifier :

| # | Règle | Contrôle |
|---|-------|---------|
| 1 | Primary constructor pour les dépendances | Pas de champ `private readonly` pour les injectés |
| 2 | Interface enregistrée dans le DI | `AddSingleton<IStore, FileStore>()` et non `<FileStore>()` |
| 3 | Durée de vie DI correcte | Singleton sans état mutable non protégé |
| 4 | Controller sans logique métier | Délégation au service, pas de calcul dans l'action |
| 5 | DTO de réponse distinct du record de persistance | Jamais `UserRecord` exposé directement |
| 6 | Code HTTP sémantiquement correct | 201 création, 204 suppression, 409 conflit |
| 7 | Route préfixée `/api/` et verbe HTTP correct | Pas de GET pour une action destructive |
| 8 | `try/catch` uniquement pour erreurs métier prévisibles | Pas de `catch (Exception)` dans les controllers |
| 9 | Méthode async suffixée `Async` | `GetAllAsync()`, jamais `GetAll()` si asynchrone |
| 10 | Logging avec paramètre exception | `LogError(ex, "...")` jamais `LogError("" + ex.Message)` |
| 11 | Namespace correspond au dossier | `MyApp.Web.Auth` pour les fichiers dans `Auth/` |
| 12 | Un fichier = un type | Pas de plusieurs classes dans le même `.cs` |

---

## 6. État global synchronisé — PlatformState

### 6.1 Principe : mix full snapshot + patches partiels

Au lieu de choisir entre tout envoyer ou n'envoyer que des deltas, l'approche retenue combine les deux :

- **Première connexion** → snapshot complet (`patchType: "full"`) envoyé uniquement au client qui se connecte via `Clients.Caller`.
- **Chaque mutation** → patch partiel (`patchType: "partial"`) diffusé à tous les clients via `Clients.All`, contenant uniquement les propriétés explicitement ciblées.
- **Chaque propriété** porte son propre timestamp de dernière modification, quel que soit son type (`string`, `int`, `bool`, classe complexe, liste...).

```
Connexion initiale :  client ←──── StatePatch { type:"full",    Props: TOUTES }
Mutation partielle :  clients ←─── StatePatch { type:"partial", Props: ["Status", "Metrics"] }
```

### 6.2 Backend — les types du mécanisme

**`State/TrackedProperty.cs`**

```csharp
namespace MyApp.Web.State;

/// <summary>
/// Encapsule une valeur de n'importe quel type T avec son horodatage.
/// Fonctionne aussi bien pour des primitifs (string, int, bool)
/// que pour des classes complexes ou des collections.
/// </summary>
public class TrackedProperty<T>
{
    public T        Value     { get; private set; }
    public DateTime UpdatedAt { get; private set; } = DateTime.UtcNow;

    public TrackedProperty(T initialValue) => Value = initialValue;

    /// <summary>
    /// Remplace l'instance entière et met à jour le timestamp.
    /// À utiliser pour les primitifs et pour les objets complexes
    /// quand on veut reconstruire l'objet complet.
    /// </summary>
    public void Set(T newValue)
    {
        Value     = newValue;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Mute une ou plusieurs propriétés internes de l'instance existante
    /// et met à jour le timestamp. Évite de reconstruire l'objet entier.
    /// À utiliser pour les types complexes (classes, listes).
    /// </summary>
    /// <example>
    /// // Un seul champ
    /// s.Metrics.Mutate(m => m.MemoryUsage = 72.4);
    ///
    /// // Plusieurs champs en une opération
    /// s.Metrics.Mutate(m => { m.MemoryUsage = 72.4; m.CpuUsage = 55.1; });
    /// </example>
    public void Mutate(Action<T> mutator)
    {
        mutator(Value);
        UpdatedAt = DateTime.UtcNow;
    }

    public TrackedValue ToDto() => new(Value, UpdatedAt);
}
```

**`State/TrackedValue.cs`**

```csharp
namespace MyApp.Web.State;

/// <summary>DTO JSON envoyé dans chaque StatePatch.</summary>
public record TrackedValue(object? Value, DateTime UpdatedAt);
```

**`State/StatePatch.cs`**

```csharp
namespace MyApp.Web.State;

/// <summary>Enveloppe du message SignalR "StateChanged".</summary>
public record StatePatch(
    string PatchType,                        // "full" | "partial"
    Dictionary<string, TrackedValue> Props,  // propriétés concernées
    DateTime ServerTime
);
```

### 6.3 Backend — définition de l'état (`PlatformState.cs`)

Déclarez ici toutes les propriétés à synchroniser. Le type `T` est libre : primitif, classe complexe, liste, record, etc.

```csharp
namespace MyApp.Web.State;

public class PlatformState
{
    // ── Primitifs ────────────────────────────────────────────────────────────
    public TrackedProperty<string> Status          = new("idle");
    public TrackedProperty<bool>   MaintenanceMode = new(false);
    public TrackedProperty<int>    ConnectedUsers  = new(0);

    // ── Classe complexe trackée en bloc (un seul timestamp pour l'objet) ────
    public TrackedProperty<ServerMetrics>  Metrics    = new(new ServerMetrics());
    public TrackedProperty<DeploymentInfo> LastDeploy = new(new DeploymentInfo());

    // ── Collection trackée en bloc ───────────────────────────────────────────
    public TrackedProperty<List<Alert>>    Alerts     = new([]);
}

// Exemples de types complexes — aucune contrainte particulière
public class ServerMetrics
{
    public double CpuUsage    { get; set; }
    public double MemoryUsage { get; set; }
    public int    RequestRate { get; set; }
}

public class DeploymentInfo
{
    public string  Version   { get; set; } = "0.0.0";
    public string  CommitSha { get; set; } = string.Empty;
    public DateTime DeployedAt { get; set; } = DateTime.UtcNow;
    public bool    IsHealthy { get; set; } = true;
}

public class Alert
{
    public string  Id       { get; set; } = Guid.NewGuid().ToString();
    public string  Level    { get; set; } = "info";   // info | warning | error
    public string  Message  { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

### 6.4 Backend — `PlatformStateService.cs`

```csharp
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.AspNetCore.SignalR;
using MyApp.Web.Hubs;

namespace MyApp.Web.State;

/// <summary>
/// Singleton — source de vérité de l'état global de la plateforme.
/// Expose des méthodes pour muter l'état et synchroniser une ou plusieurs
/// propriétés ciblées vers tous les clients SignalR connectés.
/// </summary>
public class PlatformStateService(IHubContext<AppHub> hub)
{
    private readonly PlatformState _state = new();
    private readonly SemaphoreSlim _lock  = new(1, 1);

    // ── Lecture ──────────────────────────────────────────────────────────────

    public PlatformState GetState() => _state;

    /// <summary>Construit le snapshot complet pour une première connexion.</summary>
    public StatePatch GetFullPatch() => new(
        PatchType:  "full",
        Props:      BuildProps(GetAllPropertyNames()),
        ServerTime: DateTime.UtcNow
    );

    // ── Synchronisation ciblée ───────────────────────────────────────────────

    /// <summary>
    /// Diffuse une ou plusieurs propriétés à la racine de PlatformState
    /// vers tous les clients connectés, sans muter l'état.
    /// </summary>
    /// <example>
    /// await platform.SyncAsync(s => s.Status);
    /// await platform.SyncAsync(s => s.Status, s => s.MaintenanceMode);
    /// </example>
    public async Task SyncAsync(
        params Expression<Func<PlatformState, object?>>[] selectors)
    {
        await _lock.WaitAsync();
        try
        {
            var patch = BuildPatch("partial", selectors);
            await hub.Clients.All.SendAsync("StateChanged", patch);
        }
        finally { _lock.Release(); }
    }

    /// <summary>
    /// Mute l'état via l'action fournie, puis diffuse uniquement
    /// les propriétés ciblées vers tous les clients.
    /// </summary>
    /// <example>
    /// // Primitif — remplacement complet
    /// await platform.UpdateAndSyncAsync(
    ///     s => s.Status.Set("processing"),
    ///     s => s.Status
    /// );
    ///
    /// // Objet complexe — remplacement complet de l'instance
    /// await platform.UpdateAndSyncAsync(
    ///     s => s.LastDeploy.Set(new DeploymentInfo {
    ///         Version = "1.4.2", CommitSha = "abc123", IsHealthy = true
    ///     }),
    ///     s => s.LastDeploy
    /// );
    ///
    /// // Objet complexe — mutation d'un seul champ interne
    /// await platform.UpdateAndSyncAsync(
    ///     s => s.Metrics.Mutate(m => m.MemoryUsage = 72.4),
    ///     s => s.Metrics
    /// );
    ///
    /// // Objet complexe — mutation de plusieurs champs internes
    /// await platform.UpdateAndSyncAsync(
    ///     s => s.Metrics.Mutate(m => { m.MemoryUsage = 72.4; m.CpuUsage = 55.1; }),
    ///     s => s.Metrics
    /// );
    ///
    /// // Mutation multi-propriétés, patch groupé en un seul message SignalR
    /// await platform.UpdateAndSyncAsync(
    ///     s => { s.Metrics.Mutate(m => m.MemoryUsage = 72.4); s.Status.Set("warning"); },
    ///     s => s.Metrics, s => s.Status
    /// );
    ///
    /// // Collection — remplacement de la liste entière
    /// await platform.UpdateAndSyncAsync(
    ///     s => s.Alerts.Set(newAlerts),
    ///     s => s.Alerts
    /// );
    /// </example>
    public async Task UpdateAndSyncAsync(
        Action<PlatformState> mutate,
        params Expression<Func<PlatformState, object?>>[] selectors)
    {
        await _lock.WaitAsync();
        try
        {
            mutate(_state);
            var patch = BuildPatch("partial", selectors);
            await hub.Clients.All.SendAsync("StateChanged", patch);
        }
        finally { _lock.Release(); }
    }

    // ── Helpers privés ───────────────────────────────────────────────────────

    private StatePatch BuildPatch(
        string type,
        IEnumerable<Expression<Func<PlatformState, object?>>> selectors)
    {
        var names = selectors.Select(GetPropertyName).ToList();
        return new StatePatch(type, BuildProps(names), DateTime.UtcNow);
    }

    private Dictionary<string, TrackedValue> BuildProps(IEnumerable<string> names)
    {
        var fields = typeof(PlatformState)
            .GetFields(BindingFlags.Public | BindingFlags.Instance);

        return names.ToDictionary(
            name => name,
            name =>
            {
                var field   = fields.First(f => f.Name == name);
                var tracked = field.GetValue(_state)!;
                var toDto   = tracked.GetType().GetMethod("ToDto")!;
                return (TrackedValue)toDto.Invoke(tracked, null)!;
            }
        );
    }

    private static List<string> GetAllPropertyNames() =>
        typeof(PlatformState)
            .GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(f => f.FieldType.IsGenericType &&
                        f.FieldType.GetGenericTypeDefinition() == typeof(TrackedProperty<>))
            .Select(f => f.Name)
            .ToList();

    private static string GetPropertyName(
        Expression<Func<PlatformState, object?>> expr) =>
        expr.Body switch
        {
            MemberExpression m => m.Member.Name,
            UnaryExpression { Operand: MemberExpression m } => m.Member.Name,
            _ => throw new ArgumentException($"Expression non supportée : {expr}")
        };
}
```

### 6.5 Enregistrement dans `Program.cs`

```csharp
// Ajouter après AddSignalR()
builder.Services.AddSingleton<PlatformStateService>();
```

### 6.6 Frontend — modèles TypeScript (`platform-state.model.ts`)

Les interfaces TypeScript sont le miroir exact de `PlatformState`. `TrackedValue<T>` accepte lui aussi n'importe quel type : primitif, interface complexe ou tableau.

```typescript
// core/models/platform-state.model.ts

export interface TrackedValue<T = unknown> {
  value:     T;
  updatedAt: string;   // ISO 8601
}

export interface StatePatch {
  patchType:  'full' | 'partial';
  props:      Partial<PlatformStateProps>;
  serverTime: string;
}

// ── Types métier ─────────────────────────────────────────────────────────────

export interface ServerMetrics {
  cpuUsage:    number;
  memoryUsage: number;
  requestRate: number;
}

export interface DeploymentInfo {
  version:    string;
  commitSha:  string;
  deployedAt: string;
  isHealthy:  boolean;
}

export interface Alert {
  id:        string;
  level:     'info' | 'warning' | 'error';
  message:   string;
  createdAt: string;
}

// ── État global — miroir de PlatformState.cs ─────────────────────────────────

export interface PlatformStateProps {
  Status:          TrackedValue<string>;
  MaintenanceMode: TrackedValue<boolean>;
  ConnectedUsers:  TrackedValue<number>;
  Metrics:         TrackedValue<ServerMetrics>;    // objet complexe tracké en bloc
  LastDeploy:      TrackedValue<DeploymentInfo>;   // objet complexe tracké en bloc
  Alerts:          TrackedValue<Alert[]>;          // collection trackée en bloc
}
```

### 6.7 Frontend — `PlatformStateService` Angular

```typescript
// core/services/platform-state.service.ts
import { Injectable, computed, signal } from '@angular/core';
import { StatePatch, PlatformStateProps } from '../models/platform-state.model';

@Injectable({ providedIn: 'root' })
export class PlatformStateService {

  // Signal global — unique source de vérité côté Angular
  readonly state = signal<PlatformStateProps | null>(null);

  // ── Sélecteurs dérivés (computed) ─────────────────────────────────────────
  // Recalculés automatiquement uniquement quand state() change.
  // Accessibles dans tout composant via inject(PlatformStateService).

  readonly status          = computed(() => this.state()?.Status);
  readonly maintenanceMode = computed(() => this.state()?.MaintenanceMode);
  readonly connectedUsers  = computed(() => this.state()?.ConnectedUsers);
  readonly metrics         = computed(() => this.state()?.Metrics);
  readonly lastDeploy      = computed(() => this.state()?.LastDeploy);
  readonly alerts          = computed(() => this.state()?.Alerts);

  // ── Application d'un patch ────────────────────────────────────────────────

  applyPatch(patch: StatePatch): void {
    if (patch.patchType === 'full') {
      // Remplacement complet — première connexion
      this.state.set(patch.props as PlatformStateProps);
    } else {
      // Merge partiel — seules les propriétés reçues sont mises à jour
      this.state.update(current => ({
        ...current!,
        ...patch.props
      }));
    }
  }
}
```

### 6.8 Utilisation dans les composants

```typescript
// N'importe quel composant — accès direct via inject()
@Component({
  template: `
    <!-- Accès à un primitif -->
    <span [class]="status()?.value">{{ status()?.value }}</span>
    <small>Mis à jour : {{ status()?.updatedAt | date:'HH:mm:ss' }}</small>

    <!-- Accès à un objet complexe -->
    @if (metrics(); as m) {
      <div>CPU : {{ m.value.cpuUsage | number:'1.0-1' }} %</div>
      <div>Mémoire : {{ m.value.memoryUsage | number:'1.0-1' }} %</div>
      <small>Métriques du {{ m.updatedAt | date:'HH:mm:ss' }}</small>
    }

    <!-- Accès à une collection -->
    @for (alert of alerts()?.value; track alert.id) {
      <div [class]="'alert-' + alert.level">{{ alert.message }}</div>
    }

    <!-- Mode maintenance -->
    @if (maintenanceMode()?.value) {
      <div class="banner">Maintenance en cours</div>
    }
  `
})
export class DashboardComponent {
  private platform    = inject(PlatformStateService);

  status          = this.platform.status;
  metrics         = this.platform.metrics;
  alerts          = this.platform.alerts;
  maintenanceMode = this.platform.maintenanceMode;
}
```

### 6.9 Format JSON des messages SignalR

```jsonc
// Patch "full" — envoyé uniquement au client qui se connecte
{
  "patchType": "full",
  "serverTime": "2026-03-23T14:30:00Z",
  "props": {
    "Status":          { "value": "idle",     "updatedAt": "2026-03-23T14:00:00Z" },
    "MaintenanceMode": { "value": false,       "updatedAt": "2026-03-23T09:00:00Z" },
    "ConnectedUsers":  { "value": 4,           "updatedAt": "2026-03-23T14:29:58Z" },
    "Metrics": {
      "value": { "cpuUsage": 42.1, "memoryUsage": 61.3, "requestRate": 210 },
      "updatedAt": "2026-03-23T14:29:55Z"
    },
    "LastDeploy": {
      "value": { "version": "1.4.1", "commitSha": "abc123", "isHealthy": true, "deployedAt": "2026-03-23T08:00:00Z" },
      "updatedAt": "2026-03-23T08:00:00Z"
    },
    "Alerts": {
      "value": [{ "id": "x1", "level": "warning", "message": "Latence élevée", "createdAt": "2026-03-23T14:25:00Z" }],
      "updatedAt": "2026-03-23T14:25:00Z"
    }
  }
}

// Patch "partial" — seul Metrics est diffusé après une mise à jour CPU
{
  "patchType": "partial",
  "serverTime": "2026-03-23T14:32:01Z",
  "props": {
    "Metrics": {
      "value": { "cpuUsage": 72.4, "memoryUsage": 58.1, "requestRate": 340 },
      "updatedAt": "2026-03-23T14:32:01Z"
    }
  }
}
```

---

## 7. Authentification — JWT + UserStore fichier

### 7.1 Principe

- Stockage local dans `/data/system/users.json` — aucune base de données externe. Initialisé automatiquement avec un compte `admin / admin` au premier lancement.
- Mots de passe hashés en **PBKDF2/SHA-256** (100 000 itérations) via l'API cryptographique .NET intégrée.
- **Clé JWT générée automatiquement** au premier lancement dans `/data/system/jwt.key` (256 bits aléatoires). Le répertoire `/data/system` doit être monté en volume Docker pour persister entre les redémarrages.
- **`IUserStore`** découple les contrôleurs de l'implémentation fichier. Une future version base de données remplace uniquement `FileUserStore` dans le DI.
- Deux rôles : `admin` (accès total) et `guest` (lecture seule de son propre profil).

### 7.2 Backend — `Auth/UserRecord.cs`

```csharp
namespace MyApp.Web.Auth;

public record UserRecord(
    string Id,
    string Login,
    string PasswordHash,   // format : "iterations:salt_b64:hash_b64"
    string Role            // "admin" | "guest"
);
```

### 7.3 Backend — `Auth/IUserStore.cs`

```csharp
namespace MyApp.Web.Auth;

public interface IUserStore
{
    // ── Lecture ──────────────────────────────────────────────────────────────
    Task<IReadOnlyList<UserRecord>> GetAllAsync();
    Task<UserRecord?>               GetByIdAsync(string id);
    Task<UserRecord?>               GetByLoginAsync(string login);

    // ── CRUD ─────────────────────────────────────────────────────────────────
    Task<UserRecord>  CreateAsync(CreateUserRequest request);
    Task<UserRecord>  UpdateAsync(string id, UpdateUserRequest request);
    Task              DeleteAsync(string id);

    // ── Auth ─────────────────────────────────────────────────────────────────
    Task<UserRecord?> ValidateAsync(string login, string password);
}

public record CreateUserRequest(string Login, string Password, string Role);
public record UpdateUserRequest(string? Login = null, string? Password = null, string? Role = null);
```

### 7.4 Backend — `Auth/FileUserStore.cs`

```csharp
using System.Security.Cryptography;
using System.Text.Json;

namespace MyApp.Web.Auth;

/// <summary>
/// Implémentation fichier de IUserStore.
/// Thread-safe via ReaderWriterLockSlim.
/// Écriture atomique via fichier temporaire (.tmp → rename).
/// </summary>
public class FileUserStore : IUserStore
{
    private readonly string               _filePath;
    private readonly ReaderWriterLockSlim _lock = new();
    private          List<UserRecord>     _cache;

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented               = true
    };

    public FileUserStore(IConfiguration config)
    {
        _filePath = config["Auth:UsersFile"] ?? Path.Combine("data", "system", "users.json");
        _cache    = Load();
    }

    // ── Lecture ──────────────────────────────────────────────────────────────

    public Task<IReadOnlyList<UserRecord>> GetAllAsync()
    {
        _lock.EnterReadLock();
        try   { return Task.FromResult<IReadOnlyList<UserRecord>>(_cache.AsReadOnly()); }
        finally { _lock.ExitReadLock(); }
    }

    public Task<UserRecord?> GetByIdAsync(string id)
    {
        _lock.EnterReadLock();
        try   { return Task.FromResult(_cache.FirstOrDefault(u => u.Id == id)); }
        finally { _lock.ExitReadLock(); }
    }

    public Task<UserRecord?> GetByLoginAsync(string login)
    {
        _lock.EnterReadLock();
        try
        {
            return Task.FromResult(
                _cache.FirstOrDefault(u =>
                    u.Login.Equals(login, StringComparison.OrdinalIgnoreCase)));
        }
        finally { _lock.ExitReadLock(); }
    }

    // ── CRUD ─────────────────────────────────────────────────────────────────

    public async Task<UserRecord> CreateAsync(CreateUserRequest request)
    {
        if (!IsValidRole(request.Role))
            throw new ArgumentException($"Rôle invalide : '{request.Role}'. Valeurs acceptées : admin, guest.");

        _lock.EnterWriteLock();
        try
        {
            if (_cache.Any(u => u.Login.Equals(request.Login, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Le login '{request.Login}' existe déjà.");

            var record = new UserRecord(
                Id:           Guid.NewGuid().ToString(),
                Login:        request.Login,
                PasswordHash: HashPassword(request.Password),
                Role:         request.Role.ToLower()
            );
            _cache.Add(record);
            await PersistAsync();
            return record;
        }
        finally { _lock.ExitWriteLock(); }
    }

    public async Task<UserRecord> UpdateAsync(string id, UpdateUserRequest request)
    {
        if (request.Role is not null && !IsValidRole(request.Role))
            throw new ArgumentException($"Rôle invalide : '{request.Role}'.");

        _lock.EnterWriteLock();
        try
        {
            var idx = _cache.FindIndex(u => u.Id == id);
            if (idx < 0) throw new KeyNotFoundException($"Utilisateur '{id}' introuvable.");

            if (request.Login is not null &&
                _cache.Any(u => u.Id != id &&
                    u.Login.Equals(request.Login, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Le login '{request.Login}' est déjà utilisé.");

            var existing = _cache[idx];
            var updated  = existing with
            {
                Login:        request.Login    ?? existing.Login,
                PasswordHash: request.Password != null
                                  ? HashPassword(request.Password)
                                  : existing.PasswordHash,
                Role:         request.Role?.ToLower() ?? existing.Role
            };
            _cache[idx] = updated;
            await PersistAsync();
            return updated;
        }
        finally { _lock.ExitWriteLock(); }
    }

    public async Task DeleteAsync(string id)
    {
        _lock.EnterWriteLock();
        try
        {
            if (_cache.RemoveAll(u => u.Id == id) == 0)
                throw new KeyNotFoundException($"Utilisateur '{id}' introuvable.");
            await PersistAsync();
        }
        finally { _lock.ExitWriteLock(); }
    }

    // ── Auth ─────────────────────────────────────────────────────────────────

    public Task<UserRecord?> ValidateAsync(string login, string password)
    {
        _lock.EnterReadLock();
        try
        {
            var user = _cache.FirstOrDefault(u =>
                u.Login.Equals(login, StringComparison.OrdinalIgnoreCase));
            if (user is null) return Task.FromResult<UserRecord?>(null);
            return Task.FromResult(VerifyPassword(password, user.PasswordHash) ? user : null);
        }
        finally { _lock.ExitReadLock(); }
    }

    // ── Persistance ──────────────────────────────────────────────────────────

    private List<UserRecord> Load()
    {
        if (!File.Exists(_filePath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);

            // Initialisation avec le compte admin par défaut
            var seed = new List<UserRecord>
            {
                new UserRecord(
                    Id:           Guid.NewGuid().ToString(),
                    Login:        "admin",
                    PasswordHash: HashPassword("admin"),
                    Role:         "admin"
                )
            };
            File.WriteAllText(_filePath,
                JsonSerializer.Serialize(seed, _json));
            return seed;
        }
        return JsonSerializer.Deserialize<List<UserRecord>>(
            File.ReadAllText(_filePath), _json) ?? [];
    }

    private async Task PersistAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var tmp = _filePath + ".tmp";
        await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(_cache, _json));
        File.Move(tmp, _filePath, overwrite: true);  // écriture atomique
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static bool IsValidRole(string role) =>
        role.Equals("admin", StringComparison.OrdinalIgnoreCase) ||
        role.Equals("guest", StringComparison.OrdinalIgnoreCase);

    public static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, 100_000, HashAlgorithmName.SHA256, 32);
        return $"100000:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    private static bool VerifyPassword(string password, string stored)
    {
        var parts = stored.Split(':');
        if (parts.Length != 3) return false;
        var iterations = int.Parse(parts[0]);
        var salt       = Convert.FromBase64String(parts[1]);
        var expected   = Convert.FromBase64String(parts[2]);
        var actual     = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, iterations, HashAlgorithmName.SHA256, 32);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
```

### 7.5 Backend — `Auth/JwtKeyInitializer.cs`

```csharp
namespace MyApp.Web.Auth;

/// <summary>
/// IHostedService exécuté au démarrage.
/// Génère /data/system/jwt.key (256 bits) s'il n'existe pas,
/// puis injecte la clé dans IConfiguration pour JwtService.
/// </summary>
public class JwtKeyInitializer(
    IConfiguration             config,
    ILogger<JwtKeyInitializer> logger) : IHostedService
{
    private const string KeyFile   = "data/system/jwt.key";
    private const string ConfigKey = "Auth:JwtSecret";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.Combine("data", "system"));

        string secret;
        if (File.Exists(KeyFile))
        {
            secret = File.ReadAllText(KeyFile).Trim();
            logger.LogInformation("Clé JWT chargée depuis {KeyFile}", KeyFile);
        }
        else
        {
            secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            File.WriteAllText(KeyFile, secret);
            File.SetAttributes(KeyFile, FileAttributes.ReadOnly);
            logger.LogInformation("Nouvelle clé JWT générée dans {KeyFile}", KeyFile);
        }

        // Injecte en mémoire — priorité sur appsettings.json
        ((IConfigurationRoot)config).AddInMemoryCollection(
            new Dictionary<string, string?> { [ConfigKey] = secret });

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
```

### 7.6 Backend — `Auth/JwtService.cs`

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;

namespace MyApp.Web.Auth;

public class JwtService(IConfiguration config)
{
    private readonly string _secret  = config["Auth:JwtSecret"]
        ?? throw new InvalidOperationException("Auth:JwtSecret manquant — JwtKeyInitializer doit s'exécuter en premier.");
    private readonly int    _minutes = int.Parse(config["Auth:JwtExpiryMinutes"] ?? "480");

    public (string Token, DateTime ExpiresAt) Generate(UserRecord user)
    {
        var key       = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
        var creds     = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiresAt = DateTime.UtcNow.AddMinutes(_minutes);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub,  user.Id),
            new Claim(JwtRegisteredClaimNames.Name, user.Login),
            new Claim(ClaimTypes.Role,              user.Role),
            new Claim(JwtRegisteredClaimNames.Jti,  Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer:             "MyApp",
            audience:           "MyApp",
            claims:             claims,
            expires:            expiresAt,
            signingCredentials: creds
        );

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
```

### 7.7 Backend — `Controllers/AuthController.cs`

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyApp.Web.Auth;

namespace MyApp.Web.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(IUserStore users, JwtService jwt) : ControllerBase
{
    public record LoginRequest(string Login, string Password);
    public record LoginResponse(string Token, DateTime ExpiresAt, string Role);

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        var user = await users.ValidateAsync(req.Login, req.Password);
        if (user is null)
            return Unauthorized(new { message = "Identifiants incorrects" });

        var (token, expiresAt) = jwt.Generate(user);
        return Ok(new LoginResponse(token, expiresAt, user.Role));
    }

    [HttpGet("me")]
    [Authorize]
    public IActionResult Me() => Ok(new
    {
        Id    = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value,
        Login = User.Identity?.Name,
        Role  = User.FindFirst(ClaimTypes.Role)?.Value
    });
}
```

### 7.8 Backend — `Controllers/UserController.cs`

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyApp.Web.Auth;

namespace MyApp.Web.Controllers;

[ApiController]
[Route("api/users")]
[Authorize]
public class UserController(IUserStore users) : ControllerBase
{
    private record UserDto(string Id, string Login, string Role);
    private static UserDto ToDto(UserRecord u) => new(u.Id, u.Login, u.Role);

    // GET /api/users — admin uniquement
    [HttpGet]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> GetAll()
        => Ok((await users.GetAllAsync()).Select(ToDto));

    // GET /api/users/{id} — admin : tous / guest : soi-même uniquement
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        if (!User.IsInRole("admin") && CallerId() != id) return Forbid();
        var user = await users.GetByIdAsync(id);
        return user is null ? NotFound() : Ok(ToDto(user));
    }

    // POST /api/users — admin uniquement
    [HttpPost]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest request)
    {
        try
        {
            var created = await users.CreateAsync(request);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, ToDto(created));
        }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
        catch (ArgumentException ex)         { return BadRequest(new { message = ex.Message }); }
    }

    // PUT /api/users/{id} — admin ou soi-même (sans pouvoir changer son rôle)
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateUserRequest request)
    {
        var isAdmin = User.IsInRole("admin");
        if (!isAdmin && CallerId() != id)    return Forbid();
        if (!isAdmin && request.Role != null) return Forbid();

        try
        {
            return Ok(ToDto(await users.UpdateAsync(id, request)));
        }
        catch (KeyNotFoundException)         { return NotFound(); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
        catch (ArgumentException ex)         { return BadRequest(new { message = ex.Message }); }
    }

    // DELETE /api/users/{id} — admin uniquement, pas de self-delete
    [HttpDelete("{id}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Delete(string id)
    {
        if (CallerId() == id)
            return BadRequest(new { message = "Impossible de supprimer son propre compte." });
        try   { await users.DeleteAsync(id); return NoContent(); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    private string? CallerId() =>
        User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
}
```

### 7.9 Backend — `Program.cs` (ajouts auth)

```csharp
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using MyApp.Web.Auth;

// ── Auth — ordre important : Initializer avant JwtService ────────────────────
builder.Services.AddHostedService<JwtKeyInitializer>();   // génère /data/system/jwt.key
builder.Services.AddSingleton<IUserStore, FileUserStore>(); // interface → implémentation
builder.Services.AddSingleton<JwtService>();

// ── JWT Bearer ────────────────────────────────────────────────────────────────
// La clé sera disponible dans IConfiguration après le démarrage de JwtKeyInitializer.
// On la résout lazily via un lambda pour éviter la race condition au démarrage.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer              = "MyApp",
            ValidAudience            = "MyApp",
            IssuerSigningKeyResolver = (_, _, _, _) =>
            {
                var secret = builder.Configuration["Auth:JwtSecret"] ?? string.Empty;
                return [new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret))];
            },
            ClockSkew = TimeSpan.Zero
        };

        // Token SignalR transmis via query string
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var token = ctx.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(token) &&
                    ctx.HttpContext.Request.Path.StartsWithSegments("/hub"))
                    ctx.Token = token;
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// ── Dans le pipeline (après UseRouting) ───────────────────────────────────────
app.UseAuthentication();   // toujours avant UseAuthorization
app.UseAuthorization();
```

### 7.10 Backend — `appsettings.json` (auth)

```json
{
  "Auth": {
    "UsersFile":        "data/system/users.json",
    "JwtExpiryMinutes": "480"
  }
}
```

> `Auth:JwtSecret` n'apparaît **plus** dans `appsettings.json` — il est généré et injecté en mémoire par `JwtKeyInitializer`.

### 7.11 Tableau des routes API

| Méthode | Route | Rôle requis | Description |
|---------|-------|-------------|-------------|
| `POST` | `/api/auth/login` | Aucun | Authentification, retourne le JWT |
| `GET` | `/api/auth/me` | Tout authentifié | Profil du token courant |
| `GET` | `/api/users` | `admin` | Liste tous les utilisateurs |
| `GET` | `/api/users/{id}` | `admin` ou soi-même | Détail d'un utilisateur |
| `POST` | `/api/users` | `admin` | Crée un utilisateur |
| `PUT` | `/api/users/{id}` | `admin` ou soi-même* | Modifie login/password (guest : sans rôle) |
| `DELETE` | `/api/users/{id}` | `admin` | Supprime (interdit sur soi-même) |

### 7.12 Frontend — `core/models/auth.model.ts`

```typescript
export interface LoginRequest  { login: string; password: string; }
export interface LoginResponse { token: string; expiresAt: string; role: 'admin' | 'guest'; }
export interface CurrentUser   { id: string; login: string; role: 'admin' | 'guest'; expiresAt: Date; }
export interface UserDto       { id: string; login: string; role: 'admin' | 'guest'; }
```

### 7.13 Frontend — `core/services/auth.service.ts`

```typescript
import { Injectable, signal, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { Observable, tap, map } from 'rxjs';
import { LoginRequest, LoginResponse, CurrentUser } from '../models/auth.model';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private http        = inject(HttpClient);
  private router      = inject(Router);
  private readonly LS = 'auth_token';

  // Signal global — accessible dans tous les composants via inject(AuthService).currentUser
  readonly currentUser = signal<CurrentUser | null>(this.loadFromStorage());

  login(req: LoginRequest): Observable<void> {
    return this.http.post<LoginResponse>('/api/auth/login', req).pipe(
      tap(res => {
        localStorage.setItem(this.LS, res.token);
        this.currentUser.set({
          id:        this.decodePayload(res.token).sub,
          login:     this.decodePayload(res.token).name,
          role:      res.role,
          expiresAt: new Date(res.expiresAt)
        });
      }),
      map(() => void 0)
    );
  }

  logout(): void {
    localStorage.removeItem(this.LS);
    this.currentUser.set(null);
    this.router.navigate(['/login']);
  }

  getToken(): string | null { return localStorage.getItem(this.LS); }

  isAuthenticated(): boolean {
    const u = this.currentUser();
    return u !== null && u.expiresAt > new Date();
  }

  isAdmin(): boolean { return this.currentUser()?.role === 'admin'; }

  private loadFromStorage(): CurrentUser | null {
    const token = localStorage.getItem(this.LS);
    if (!token) return null;
    try {
      const p         = this.decodePayload(token);
      const expiresAt = new Date(p.exp * 1000);
      if (expiresAt <= new Date()) { localStorage.removeItem(this.LS); return null; }
      return { id: p.sub, login: p.name, role: p.role, expiresAt };
    } catch { return null; }
  }

  private decodePayload(token: string): any {
    return JSON.parse(atob(token.split('.')[1]));
  }
}
```

### 7.14 Frontend — `core/interceptors/auth.interceptor.ts`

```typescript
import { inject } from '@angular/core';
import { HttpInterceptorFn } from '@angular/common/http';
import { catchError, throwError } from 'rxjs';
import { AuthService } from '../services/auth.service';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth  = inject(AuthService);
  const token = auth.getToken();

  const authReq = token
    ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
    : req;

  return next(authReq).pipe(
    catchError(err => {
      if (err.status === 401) auth.logout();  // token expiré ou invalide
      return throwError(() => err);
    })
  );
};
```

### 7.15 Frontend — `core/guards/auth.guard.ts`

```typescript
import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../services/auth.service';

export const authGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  if (auth.isAuthenticated()) return true;
  return inject(Router).createUrlTree(['/login']);
};

export const adminGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  if (auth.isAdmin()) return true;
  return inject(Router).createUrlTree(['/forbidden']);
};
```

### 7.16 Frontend — `app.routes.ts`

```typescript
import { Routes } from '@angular/router';
import { authGuard, adminGuard } from './core/guards/auth.guard';

export const routes: Routes = [
  { path: 'login',     component: LoginComponent },
  { path: 'forbidden', component: ForbiddenComponent },
  {
    path: '',
    canActivate: [authGuard],
    children: [
      { path: 'dashboard', component: DashboardComponent },
      { path: 'profile',   component: ProfileComponent },
      {
        path: 'admin',
        canActivate: [adminGuard],
        children: [
          { path: 'settings', component: AdminSettingsComponent },
          { path: 'users',    component: AdminUsersComponent },
        ]
      }
    ]
  },
  { path: '**', redirectTo: '' }
];
```

### 7.17 Frontend — `app.config.ts`

```typescript
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { authInterceptor }  from './core/interceptors/auth.interceptor';
import { errorInterceptor } from './core/interceptors/error.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideRouter(routes),
    provideHttpClient(
      withInterceptors([authInterceptor, errorInterceptor])  // auth avant error
    )
  ]
};
```

### 7.18 SignalR — transmission du JWT

```typescript
// Dans SignalRService — le token est passé en query string pour le Hub
this.hubConnection = new signalR.HubConnectionBuilder()
  .withUrl(environment.signalrHubUrl, {
    accessTokenFactory: () => inject(AuthService).getToken() ?? ''
  })
  .withAutomaticReconnect()
  .build();
```

### 7.19 Docker — persistance du répertoire `/data/system`

```yaml
# docker-compose.yml
services:
  app:
    build: .
    ports:
      - "${APP_PORT:-8080}:${APP_PORT:-8080}"
    environment:
      - APP_PORT=${APP_PORT:-8080}
      - ASPNETCORE_ENVIRONMENT=Production
    volumes:
      - ./data/system:/app/data/system   # ← persiste jwt.key et users.json entre les redémarrages
```

> Au premier `docker-compose up`, `/data/system/jwt.key` est généré et `users.json` est initialisé avec `admin / admin`. Les relancer ne régénère pas la clé ni ne réinitialise les utilisateurs.

---

## 8. Système de jobs

### 8.1 Principe

- **Un seul job actif à la fois**, tous types confondus. Exclusivité garantie par `SemaphoreSlim(1,1)`.
- Chaque service métier implémente `IJobHandler` : `ValidateAsync()` vérifie les paramètres et définit le titre, `RunAsync()` exécute le traitement.
- Le controller orchestre en 4 étapes : résolution du handler → validation → lecture du login JWT → démarrage.
- Le résultat est un `Dictionary<string, string>` initialisé vide dès le départ, alimenté progressivement via `SetResult()`. Chaque appel à `BroadcastJobAsync()` pousse l'état courant (progression + résultat partiel) à tous les clients via SignalR.
- Les traces sont diffusées en temps réel via un événement SignalR dédié `JobTrace`.

### 8.2 Structure des fichiers

```
Jobs/
├── JobStatus.cs          # Enums JobStatus et TraceLevel
├── JobTrace.cs           # Record trace unitaire
├── JobContext.cs         # État complet du job (title, progress, result, traces...)
├── IJobHandler.cs        # Interface implémentée par chaque service métier
└── JobRunner.cs          # Singleton — orchestration, exclusivité, SignalR
```

### 8.3 `Jobs/JobStatus.cs`

```csharp
namespace MyApp.Web.Jobs;

public enum JobStatus  { Init, Running, Failed, Succeeded, Canceled }
public enum TraceLevel { Info, Error, Success, Cancel, Timeout }
```

### 8.4 `Jobs/JobTrace.cs`

```csharp
namespace MyApp.Web.Jobs;

public record JobTrace(DateTime Date, TraceLevel Level, string Message);
```

### 8.5 `Jobs/JobContext.cs`

```csharp
namespace MyApp.Web.Jobs;

public class JobContext
{
    public string     Id          { get; init; } = Guid.NewGuid().ToString();
    public string     JobType     { get; init; } = string.Empty;
    public string     Title       { get; internal set; } = string.Empty;
    public string     RequestedBy { get; init; } = string.Empty;          // login JWT
    public Dictionary<string, string> Parameters { get; init; } = [];
    public JobStatus  Status      { get; private set; } = JobStatus.Init;
    public DateTime   StartedAt   { get; private set; }
    public DateTime?  EndedAt     { get; private set; }
    public int        Progress    { get; private set; }                    // 0-100

    // Initialisé vide — alimenté progressivement via SetResult()
    public Dictionary<string, string> Result { get; private set; } = [];

    public List<JobTrace> Traces  { get; } = [];

    internal void MarkRunning()
    {
        Status    = JobStatus.Running;
        StartedAt = DateTime.UtcNow;
    }

    internal void MarkSucceeded()
    {
        Status   = JobStatus.Succeeded;
        EndedAt  = DateTime.UtcNow;
        Progress = 100;
    }

    internal void MarkFailed()   { Status = JobStatus.Failed;   EndedAt = DateTime.UtcNow; }
    internal void MarkCanceled() { Status = JobStatus.Canceled; EndedAt = DateTime.UtcNow; }

    internal void SetProgress(int percent)
        => Progress = Math.Clamp(percent, 0, 100);

    /// <summary>
    /// Ajoute ou met à jour une entrée dans le résultat.
    /// Appeler BroadcastJobAsync() après pour pousser la mise à jour au front.
    /// </summary>
    internal void SetResult(string key, string value)
        => Result[key] = value;

    internal void AddTrace(TraceLevel level, string message)
        => Traces.Add(new JobTrace(DateTime.UtcNow, level, message));
}
```

### 8.6 `Jobs/IJobHandler.cs`

```csharp
namespace MyApp.Web.Jobs;

/// <summary>
/// Interface implémentée par chaque service métier.
/// Sépare la validation (synchrone, avant démarrage) du traitement (asynchrone).
/// </summary>
public interface IJobHandler
{
    /// <summary>
    /// Vérifie les paramètres et construit le titre du job.
    /// Appelé par le controller AVANT de démarrer — pas encore de JobContext.
    /// </summary>
    /// <returns>
    /// (true, null, titre) si valide.
    /// (false, "message d'erreur", "") sinon.
    /// </returns>
    Task<(bool IsValid, string? Error, string Title)> ValidateAsync(
        Dictionary<string, string> parameters);

    /// <summary>
    /// Exécute le traitement. Appelé uniquement si ValidateAsync a retourné true.
    /// Utilise ctx.SetResult() + jobRunner.BroadcastJobAsync() pour envoyer
    /// les résultats partiels au fur et à mesure.
    /// </summary>
    Task RunAsync(JobContext ctx, CancellationToken ct);
}
```

### 8.7 `Jobs/JobRunner.cs`

```csharp
using Microsoft.AspNetCore.SignalR;
using MyApp.Web.Hubs;

namespace MyApp.Web.Jobs;

/// <summary>
/// Singleton — un seul job actif à la fois, tous types confondus.
/// Exclusivité garantie par SemaphoreSlim(1,1).
/// </summary>
public class JobRunner(
    IHubContext<AppHub> hub,
    ILogger<JobRunner>  logger)
{
    private readonly SemaphoreSlim            _gate = new(1, 1);
    private          JobContext?              _current;
    private          CancellationTokenSource? _cts;

    public JobContext? Current   => _current;
    public bool        IsRunning => _current?.Status == JobStatus.Running;

    // ── Démarrage ────────────────────────────────────────────────────────────

    public async Task<bool> StartAsync(
        string                     jobType,
        string                     title,
        string                     requestedBy,
        Dictionary<string, string> parameters,
        Func<JobContext, CancellationToken, Task> work)
    {
        // Tentative non-bloquante — rejet immédiat si un job tourne déjà
        if (!await _gate.WaitAsync(0))
            return false;

        _cts     = new CancellationTokenSource();
        _current = new JobContext
        {
            JobType     = jobType,
            Title       = title,
            RequestedBy = requestedBy,
            Parameters  = parameters
        };
        _current.MarkRunning();

        await BroadcastJobAsync();   // diffuse l'état RUNNING initial

        _ = Task.Run(async () =>
        {
            try
            {
                await work(_current, _cts.Token);
                _current.MarkSucceeded();
                await TraceAsync(TraceLevel.Success, "Job terminé avec succès.");
            }
            catch (OperationCanceledException)
            {
                _current.MarkCanceled();
                await TraceAsync(TraceLevel.Cancel, "Job annulé.");
            }
            catch (Exception ex)
            {
                _current.MarkFailed();
                await TraceAsync(TraceLevel.Error, $"Erreur : {ex.Message}");
                logger.LogError(ex, "Job {JobType} échoué", _current.JobType);
            }
            finally
            {
                await BroadcastJobAsync();   // diffuse l'état final
                _cts?.Dispose();
                _cts = null;
                _gate.Release();             // libère le verrou
            }
        }, CancellationToken.None);

        return true;
    }

    // ── Annulation ───────────────────────────────────────────────────────────

    public Task CancelAsync()
    {
        _cts?.Cancel();
        return Task.CompletedTask;
    }

    // ── Notifications SignalR ─────────────────────────────────────────────────

    /// <summary>
    /// Diffuse le contexte complet du job à tous les clients.
    /// À appeler depuis RunAsync après chaque SetProgress() ou SetResult().
    /// </summary>
    public Task BroadcastJobAsync()
        => hub.Clients.All.SendAsync("JobChanged", _current);

    /// <summary>
    /// Enregistre une trace dans le contexte et la diffuse immédiatement.
    /// </summary>
    public async Task TraceAsync(TraceLevel level, string message)
    {
        _current?.AddTrace(level, message);
        await hub.Clients.All.SendAsync("JobTrace",
            new JobTrace(DateTime.UtcNow, level, message));
    }
}
```

### 8.8 `Controllers/JobController.cs`

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyApp.Web.Jobs;

namespace MyApp.Web.Controllers;

[ApiController]
[Route("api/job")]
[Authorize]
public class JobController(
    JobRunner     jobRunner,
    ImportService importService,
    ExportService exportService,
    SyncService   syncService
) : ControllerBase
{
    // ── Résolution du handler par jobType ────────────────────────────────────

    private IJobHandler? ResolveHandler(string jobType) => jobType switch
    {
        "Import" => importService,
        "Export" => exportService,
        "Sync"   => syncService,
        _        => null
    };

    // ── GET /api/job ─ état courant ───────────────────────────────────────────

    [HttpGet]
    public IActionResult GetCurrent()
    {
        if (jobRunner.Current is null) return NoContent();
        return Ok(jobRunner.Current);
    }

    // ── POST /api/job/start ───────────────────────────────────────────────────

    [HttpPost("start")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Start([FromBody] StartJobRequest request)
    {
        // 1. Résolution du handler
        var handler = ResolveHandler(request.JobType);
        if (handler is null)
            return NotFound(new { message = $"Type de job inconnu : '{request.JobType}'." });

        // 2. Validation des paramètres — le handler définit aussi le titre
        var (isValid, error, title) = await handler.ValidateAsync(request.Parameters);
        if (!isValid)
            return BadRequest(new { message = error });

        // 3. Récupération du login depuis le token JWT
        var requestedBy = User.FindFirstValue(ClaimTypes.Name)
                       ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                       ?? "unknown";

        // 4. Démarrage — rejeté si un job tourne déjà
        var started = await jobRunner.StartAsync(
            jobType:     request.JobType,
            title:       title,
            requestedBy: requestedBy,
            parameters:  request.Parameters,
            work:        handler.RunAsync
        );

        if (!started)
            return Conflict(new
            {
                message = "Un job est déjà en cours d'exécution.",
                current = new
                {
                    jobRunner.Current?.JobType,
                    jobRunner.Current?.Title,
                    jobRunner.Current?.Status,
                    jobRunner.Current?.RequestedBy
                }
            });

        return Accepted(new
        {
            message = "Job démarré.",
            jobId   = jobRunner.Current?.Id,
            title
        });
    }

    // ── POST /api/job/cancel ──────────────────────────────────────────────────

    [HttpPost("cancel")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Cancel()
    {
        if (!jobRunner.IsRunning)
            return BadRequest(new { message = "Aucun job en cours à annuler." });

        await jobRunner.CancelAsync();
        return Accepted(new { message = "Annulation demandée." });
    }
}

public record StartJobRequest(
    string                     JobType,
    Dictionary<string, string> Parameters
);
```

### 8.9 Exemple d'implémentation d'un service métier

```csharp
public class ImportService(JobRunner jobRunner) : IJobHandler
{
    // ── Validation + titre ────────────────────────────────────────────────────

    public Task<(bool IsValid, string? Error, string Title)> ValidateAsync(
        Dictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue("source", out var source) || string.IsNullOrWhiteSpace(source))
            return Task.FromResult((false, "Le paramètre 'source' est requis.", string.Empty));

        if (!parameters.TryGetValue("mode", out var mode) || mode is not ("full" or "delta"))
            return Task.FromResult((false, "Le paramètre 'mode' doit être 'full' ou 'delta'.", string.Empty));

        var title = $"Import {mode} depuis {source}";
        return Task.FromResult((true, (string?)null, title));
    }

    // ── Traitement ────────────────────────────────────────────────────────────

    public async Task RunAsync(JobContext ctx, CancellationToken ct)
    {
        var source = ctx.Parameters["source"];
        var mode   = ctx.Parameters["mode"];

        // Résultat initial visible immédiatement côté front
        ctx.SetResult("source", source);
        ctx.SetResult("mode",   mode);
        ctx.SetResult("status", "en cours");
        ctx.SetProgress(10);
        await jobRunner.TraceAsync(TraceLevel.Info, $"Démarrage import {mode} depuis {source}...");
        await jobRunner.BroadcastJobAsync();

        await Task.Delay(1000, ct);
        ct.ThrowIfCancellationRequested();

        ctx.SetResult("recordsProcessed", "600");   // résultat partiel
        ctx.SetProgress(60);
        await jobRunner.TraceAsync(TraceLevel.Info, "600 enregistrements traités...");
        await jobRunner.BroadcastJobAsync();

        await Task.Delay(1000, ct);
        ct.ThrowIfCancellationRequested();

        // Résultat final — upsert des clés existantes
        ctx.SetResult("recordsProcessed", "1200");
        ctx.SetResult("status",           "terminé");
        ctx.SetResult("duration",         "2.1s");
        ctx.SetResult("errors",           "0");
        ctx.SetProgress(100);
        await jobRunner.TraceAsync(TraceLevel.Info, "Import terminé — 1200 enregistrements.");
        await jobRunner.BroadcastJobAsync();
        // MarkSucceeded() appelé automatiquement par JobRunner après RunAsync
    }
}
```

### 8.10 Enregistrement dans `Program.cs`

```csharp
// Un seul JobRunner singleton pour tout le système
builder.Services.AddSingleton<JobRunner>();

// Les services métier en Scoped (accès possible à des services Scoped comme DbContext)
builder.Services.AddScoped<ImportService>();
builder.Services.AddScoped<ExportService>();
builder.Services.AddScoped<SyncService>();
```

### 8.11 Tableau des routes

| Méthode | Route | Rôle | Description |
|---|---|---|---|
| `GET` | `/api/job` | Tout authentifié | État courant du job |
| `POST` | `/api/job/start` | `admin` | Démarre un job |
| `POST` | `/api/job/cancel` | `admin` | Annule le job en cours |

### 8.12 Frontend — modèles TypeScript

```typescript
// core/models/job.model.ts

export type JobStatus  = 'Init' | 'Running' | 'Failed' | 'Succeeded' | 'Canceled';
export type TraceLevel = 'Info' | 'Error' | 'Success' | 'Cancel' | 'Timeout';

export interface JobTrace {
  date:    string;
  level:   TraceLevel;
  message: string;
}

export interface JobContext {
  id:          string;
  jobType:     string;
  title:       string;
  requestedBy: string;
  parameters:  Record<string, string>;
  status:      JobStatus;
  startedAt:   string;
  endedAt:     string | null;
  progress:    number;                    // 0-100
  result:      Record<string, string>;    // toujours présent, jamais null
  traces:      JobTrace[];
}
```

### 8.13 Frontend — `JobService` Angular

```typescript
// core/services/job.service.ts
@Injectable({ providedIn: 'root' })
export class JobService {
  private signalR = inject(SignalRService);
  private http    = inject(HttpClient);
  private destroyRef = inject(DestroyRef);

  // Signals globaux accessibles dans tous les composants
  readonly job    = signal<JobContext | null>(null);
  readonly traces = signal<JobTrace[]>([]);

  // Computed utiles
  readonly isRunning  = computed(() => this.job()?.status === 'Running');
  readonly progress   = computed(() => this.job()?.progress ?? 0);
  readonly result     = computed(() => Object.entries(this.job()?.result ?? {})
                          .map(([key, value]) => ({ key, value })));

  constructor() {
    // Écoute les événements SignalR
    this.signalR.on<JobContext>('JobChanged')
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(job => this.job.set(job));

    this.signalR.on<JobTrace>('JobTrace')
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(trace => this.traces.update(list => [...list, trace]));
  }

  start(jobType: string, parameters: Record<string, string>): Observable<void> {
    return this.http.post<void>('/api/job/start', { jobType, parameters });
  }

  cancel(): Observable<void> {
    return this.http.post<void>('/api/job/cancel', {});
  }

  clearTraces(): void {
    this.traces.set([]);
  }
}
```

### 8.14 Exemple d'utilisation dans un composant

```typescript
@Component({
  standalone: true,
  template: `
    @if (job.isRunning()) {
      <div class="progress-bar" [style.width.%]="job.progress()"></div>
      <button (click)="cancel()">Annuler</button>
    } @else {
      <button (click)="startImport()">Lancer l'import</button>
    }

    @if (job.job(); as ctx) {
      <p>{{ ctx.title }} — demandé par {{ ctx.requestedBy }}</p>
      <p>Statut : {{ ctx.status }}</p>

      <!-- Résultat partiel visible en temps réel -->
      @for (entry of job.result(); track entry.key) {
        <tr><td>{{ entry.key }}</td><td>{{ entry.value }}</td></tr>
      }
    }

    <!-- Traces en temps réel -->
    @for (trace of job.traces(); track trace.date) {
      <div [class]="'trace-' + trace.level.toLowerCase()">
        {{ trace.date | date:'HH:mm:ss' }} — {{ trace.message }}
      </div>
    }
  `
})
export class ImportComponent {
  job = inject(JobService);

  startImport(): void {
    this.job.clearTraces();
    this.job.start('Import', { source: 'erp-prod', mode: 'delta' })
      .subscribe({ error: err => console.error(err) });
  }

  cancel(): void {
    this.job.cancel().subscribe();
  }
}
```

### 8.15 Exemples de messages SignalR

```jsonc
// JobChanged — contexte complet diffusé à chaque BroadcastJobAsync()
{
  "id":          "a3f1c2d4-...",
  "jobType":     "Import",
  "title":       "Import delta depuis erp-prod",
  "requestedBy": "admin",
  "parameters":  { "source": "erp-prod", "mode": "delta" },
  "status":      "Running",
  "startedAt":   "2026-03-24T10:15:00Z",
  "endedAt":     null,
  "progress":    60,
  "result": {
    "source":           "erp-prod",
    "mode":             "delta",
    "status":           "en cours",
    "recordsProcessed": "600"
  },
  "traces": [
    { "date": "2026-03-24T10:15:00Z", "level": "Info",    "message": "Démarrage import delta depuis erp-prod..." },
    { "date": "2026-03-24T10:15:01Z", "level": "Info",    "message": "600 enregistrements traités..." }
  ]
}

// JobTrace — trace unitaire diffusée à chaque TraceAsync()
{ "date": "2026-03-24T10:15:02Z", "level": "Success", "message": "Job terminé avec succès." }
```

---

## 9. Communication Frontend ↔ Backend

```
┌──────────────────────────────────────────────────────────────┐
│  Navigateur (Angular SPA)                                    │
│                                                              │
│  ┌─────────────┐    HTTP REST     ┌─────────────────────┐   │
│  │  Components │ ──/api/**──────► │  MVC Controllers    │   │
│  └─────────────┘                  └─────────────────────┘   │
│                                                              │
│  ┌─────────────────┐  WebSocket   ┌─────────────────────┐   │
│  │ SignalRService  │ ◄──/hub/app─►│  AppHub (SignalR)   │   │
│  └─────────────────┘              └─────────────────────┘   │
└──────────────────────────────────────────────────────────────┘
```

| Route         | Protocole      | Usage                              |
|---------------|----------------|------------------------------------|
| `/api/**`     | HTTP/HTTPS     | Appels REST (CRUD, données, auth)  |
| `/hub/app`    | WebSocket/SSE  | Temps réel via SignalR             |
| `/**`         | HTTP           | SPA Angular (fallback index.html)  |
| `/` (static)  | HTTP           | Assets Angular (wwwroot)           |

---

## 10. Dockerfile Single Unit

```dockerfile
# ─────────────────────────────────────────────────────────────
# ÉTAPE 1 — Build Angular
# ─────────────────────────────────────────────────────────────
FROM node:22-alpine AS angular-build

WORKDIR /angular

# Copie des fichiers de dépendances en premier (cache Docker optimal)
COPY src/MyApp.Client/package*.json ./
RUN npm ci

# Copie du source Angular et build de production
COPY src/MyApp.Client/ ./
RUN npm run build -- --configuration production

# ─────────────────────────────────────────────────────────────
# ÉTAPE 2 — Build .NET
# ─────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS dotnet-build

WORKDIR /app

# Restauration des dépendances NuGet
COPY src/MyApp.Web/*.csproj ./
RUN dotnet restore

# Copie du source backend
COPY src/MyApp.Web/ ./

# Copie du build Angular dans wwwroot
COPY --from=angular-build /angular/dist/my-app/browser ./wwwroot/

# Publication Release
RUN dotnet publish -c Release -o /publish

# ─────────────────────────────────────────────────────────────
# ÉTAPE 3 — Image finale (runtime only)
# ─────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final

WORKDIR /app

COPY --from=dotnet-build /publish ./

# Port par défaut exposé (surchargeable via APP_PORT)
ENV APP_PORT=8080
EXPOSE ${APP_PORT}

ENTRYPOINT ["dotnet", "MyApp.Web.dll"]
```

### `.dockerignore`

```
**/node_modules
**/dist
**/bin
**/obj
**/.git
**/.vscode
**/wwwroot
```

### `docker-compose.yml` (optionnel)

```yaml
version: '3.9'
services:
  app:
    build: .
    ports:
      - "${APP_PORT:-8080}:${APP_PORT:-8080}"
    environment:
      - APP_PORT=${APP_PORT:-8080}
      - ASPNETCORE_ENVIRONMENT=Production
    volumes:
      - ./data/system:/app/data/system   # persiste jwt.key et users.json entre les redémarrages
```

---

## 11. Configuration VS Code

### 11.1 `.vscode/tasks.json`

```json
{
  "version": "2.0.0",
  "tasks": [
    {
      "label": "build-angular",
      "type": "shell",
      "command": "npm run build -- --watch",
      "options": {
        "cwd": "${workspaceFolder}/src/MyApp.Client"
      },
      "group": "build",
      "presentation": {
        "group": "dev",
        "panel": "dedicated",
        "reveal": "silent"
      },
      "isBackground": true,
      "problemMatcher": {
        "owner": "typescript",
        "pattern": "$tsc",
        "background": {
          "activeOnStart": true,
          "beginsPattern": "Building...",
          "endsPattern": "Watching for file changes"
        }
      }
    },
    {
      "label": "build-dotnet",
      "command": "dotnet",
      "type": "process",
      "args": [
        "build",
        "${workspaceFolder}/src/MyApp.Web/MyApp.Web.csproj",
        "/property:GenerateFullPaths=true",
        "/consoleloggerparameters:NoSummary"
      ],
      "group": "build",
      "presentation": {
        "group": "dev",
        "panel": "dedicated",
        "reveal": "silent"
      },
      "problemMatcher": "$msCompile"
    },
    {
      "label": "watch-dotnet",
      "command": "dotnet",
      "type": "process",
      "args": [
        "watch",
        "--project",
        "${workspaceFolder}/src/MyApp.Web/MyApp.Web.csproj"
      ],
      "group": "build",
      "presentation": {
        "group": "dev",
        "panel": "dedicated",
        "reveal": "always"
      },
      "isBackground": true,
      "problemMatcher": "$msCompile"
    }
  ]
}
```

### 11.2 `.vscode/launch.json`

```json
{
  "version": "0.2.0",
  "configurations": [
    {
      "name": "🚀 Debug Full Stack (Backend + Angular Proxy)",
      "type": "coreclr",
      "request": "launch",
      "preLaunchTask": "build-dotnet",
      "program": "${workspaceFolder}/src/MyApp.Web/bin/Debug/net9.0/MyApp.Web.dll",
      "args": [],
      "cwd": "${workspaceFolder}/src/MyApp.Web",
      "stopAtEntry": false,
      "serverReadyAction": {
        "action": "openExternally",
        "pattern": "\\bNow listening on:\\s+(https?://\\S+)"
      },
      "env": {
        "ASPNETCORE_ENVIRONMENT": "Development",
        "APP_PORT": "${env:APP_PORT}",
        "DOTNET_WATCH_RESTART_ON_RUDE_EDIT": "true"
      },
      "sourceFileMap": {
        "/Views": "${workspaceFolder}/src/MyApp.Web/Views"
      }
    },
    {
      "name": "🅰️ Debug Angular (ng serve)",
      "type": "chrome",
      "request": "launch",
      "preLaunchTask": "build-angular",
      "url": "http://localhost:4200",
      "webRoot": "${workspaceFolder}/src/MyApp.Client/src",
      "sourceMapPathOverrides": {
        "webpack:/*": "${webRoot}/*",
        "/./*": "${webRoot}/*",
        "/src/*": "${webRoot}/*",
        "/*": "*",
        "/./~/*": "${workspaceFolder}/node_modules/*"
      }
    }
  ],
  "compounds": [
    {
      "name": "⚡ Full Stack Debug",
      "configurations": [
        "🚀 Debug Full Stack (Backend + Angular Proxy)",
        "🅰️ Debug Angular (ng serve)"
      ],
      "stopAll": true
    }
  ]
}
```

### 11.3 `.vscode/settings.json`

```json
{
  "editor.formatOnSave": true,
  "editor.defaultFormatter": "esbenp.prettier-vscode",
  "[csharp]": {
    "editor.defaultFormatter": "ms-dotnettools.csharp"
  },
  "dotnet.defaultSolution": "src/MyApp.Web/MyApp.Web.csproj",
  "typescript.tsdk": "src/MyApp.Client/node_modules/typescript/lib",
  "terminal.integrated.env.windows": {
    "APP_PORT": "5000"
  },
  "terminal.integrated.env.linux": {
    "APP_PORT": "5000"
  },
  "terminal.integrated.env.osx": {
    "APP_PORT": "5000"
  }
}
```

---

## 12. Variables d'environnement

| Variable               | Défaut | Usage                                                    |
|------------------------|--------|----------------------------------------------------------|
| `APP_PORT`             | `5000` | Port d'écoute du serveur Kestrel (dev local et Docker)  |
| `ASPNETCORE_ENVIRONMENT` | `Production` | Mode de l'application (`Development` en local) |

### Définir `APP_PORT` en local

**Linux / macOS (terminal) :**
```bash
export APP_PORT=5001
dotnet run --project src/MyApp.Web
```

**Windows (PowerShell) :**
```powershell
$env:APP_PORT = "5001"
dotnet run --project src/MyApp.Web
```

**VS Code — `.env` (avec l'extension DotENV) :**  
Créer un fichier `.env` à la racine (**ne pas commiter**) :
```env
APP_PORT=5000
ASPNETCORE_ENVIRONMENT=Development
```

Référencer dans `launch.json` via `"envFile": "${workspaceFolder}/.env"` dans la configuration de debug.

### Utilisation dans Angular (proxy dev)

Le proxy Angular (`proxy.conf.json`) redirige `/api` et `/hub` vers `http://localhost:${APP_PORT}`. Il faut remplacer `${APP_PORT}` dynamiquement ou utiliser un script de génération :

```bash
# Script npm dans package.json
"start": "APP_PORT=${APP_PORT:-5000} envsubst < proxy.conf.template.json > proxy.conf.json && ng serve --proxy-config proxy.conf.json"
```

Ou plus simplement, définir le port fixe dans `proxy.conf.json` pour le dev local et le documenter dans le README.

---

## 13. Commandes utiles

### Développement local (sans Docker)

```bash
# 1. Démarrer le backend (terminal 1)
export APP_PORT=5000
cd src/MyApp.Web
dotnet watch run

# 2. Démarrer le frontend Angular avec proxy (terminal 2)
cd src/MyApp.Client
npm start
# → Angular disponible sur http://localhost:4200
# → Proxy redirige /api et /hub vers http://localhost:5000
```

### Build & Run Docker

```bash
# Build de l'image
docker build -t myapp:latest .

# Run avec port par défaut (8080)
docker run -p 8080:8080 myapp:latest

# Run avec port personnalisé
docker run -e APP_PORT=9090 -p 9090:9090 myapp:latest

# Via docker-compose
APP_PORT=8080 docker-compose up --build
```

### Build Angular seul (pour vérification)

```bash
cd src/MyApp.Client
npm run build -- --configuration production
# Output dans ../MyApp.Web/wwwroot
```

---

## 14. Extensions VS Code recommandées

```json
// .vscode/extensions.json
{
  "recommendations": [
    "ms-dotnettools.csharp",
    "ms-dotnettools.csdevkit",
    "angular.ng-template",
    "dbaeumer.vscode-eslint",
    "esbenp.prettier-vscode",
    "ms-vscode.vscode-typescript-next",
    "mikestead.dotenv",
    "eamodio.gitlens"
  ]
}
```

---