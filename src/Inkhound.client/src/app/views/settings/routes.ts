import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    loadComponent: () => import('./settings.component').then(m => m.SettingsComponent),
    data: { title: 'Settings' }
  },
  {
    path: 'indexers',
    loadComponent: () => import('./indexers.component').then(m => m.IndexersComponent),
    data: { title: 'Indexers' }
  }
];
