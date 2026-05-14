import { Routes } from '@angular/router';
import { authGuard } from './core/guards/auth.guard';
import { libraryTitleResolver } from './core/resolvers/library-title.resolver';
import { volumeTitleResolver } from './core/resolvers/volume-title.resolver';

export const routes: Routes = [
  {
    path: '',
    redirectTo: 'dashboard',
    pathMatch: 'full'
  },
  {
    path: '',
    loadComponent: () => import('./layout').then(m => m.DefaultLayoutComponent),
    canActivate: [authGuard],
    data: {
      title: 'Home'
    },
    children: [
      {
        path: 'dashboard',
        loadChildren: () => import('./views/dashboard/routes').then((m) => m.routes)
      },
      {
        path: 'theme',
        loadChildren: () => import('./views/theme/routes').then((m) => m.routes)
      },
      {
        path: 'base',
        loadChildren: () => import('./views/base/routes').then((m) => m.routes)
      },
      {
        path: 'buttons',
        loadChildren: () => import('./views/buttons/routes').then((m) => m.routes)
      },
      {
        path: 'forms',
        loadChildren: () => import('./views/forms/routes').then((m) => m.routes)
      },
      {
        path: 'icons',
        loadChildren: () => import('./views/icons/routes').then((m) => m.routes)
      },
      {
        path: 'notifications',
        loadChildren: () => import('./views/notifications/routes').then((m) => m.routes)
      },
      {
        path: 'widgets',
        loadChildren: () => import('./views/widgets/routes').then((m) => m.routes)
      },
      {
        path: 'charts',
        loadChildren: () => import('./views/charts/routes').then((m) => m.routes)
      },
      {
        path: 'pages',
        loadChildren: () => import('./views/pages/routes').then((m) => m.routes)
      },
      {
        path: 'settings',
        loadChildren: () => import('./views/settings/routes').then((m) => m.routes)
      },
      {
        path: 'libraries',
        loadComponent: () => import('./views/library-management/library-management.component').then(m => m.LibraryManagementComponent),
        data: { title: 'Libraries' }
      },
      {
        path: 'library/:id',
        loadComponent: () => import('./views/library/library-shell.component').then(m => m.LibraryShellComponent),
        resolve: { title: libraryTitleResolver },
        children: [
          {
            path: '',
            loadComponent: () => import('./views/library/library.component').then(m => m.LibraryComponent)
          },
          {
            path: 'volume/:volumeId',
            resolve: { title: volumeTitleResolver },
            loadComponent: () => import('./views/volume/volume.component').then(m => m.VolumeComponent)
          },
          {
            path: 'add-volume',
            data: { title: 'Add' },
            loadComponent: () => import('./views/volume/volume-add.component').then(m => m.VolumeAddComponent)
          }
        ]
      }
    ]
  },
  {
    path: '404',
    loadComponent: () => import('./views/pages/page404/page404.component').then(m => m.Page404Component),
    data: {
      title: 'Page 404'
    }
  },
  {
    path: '500',
    loadComponent: () => import('./views/pages/page500/page500.component').then(m => m.Page500Component),
    data: {
      title: 'Page 500'
    }
  },
  {
    path: 'login',
    loadComponent: () => import('./views/pages/login/login.component').then(m => m.LoginComponent),
    data: {
      title: 'Login Page'
    }
  },
  {
    path: 'register',
    loadComponent: () => import('./views/pages/register/register.component').then(m => m.RegisterComponent),
    data: {
      title: 'Register Page'
    }
  },
  { path: '**', redirectTo: 'dashboard' }
];
