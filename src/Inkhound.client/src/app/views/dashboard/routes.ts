import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    loadComponent: () => import('./dashboard.component').then(m => m.DashboardComponent),
    data: {
      title: $localize`Dashboard`
    }
  },
  {
    path: 'recently-added',
    loadComponent: () => import('./dashboard-list.component').then(m => m.DashboardListComponent),
    data: { title: 'Recently added', kind: 'recently-added' }
  },
  {
    path: 'downloads',
    loadComponent: () => import('./dashboard-list.component').then(m => m.DashboardListComponent),
    data: { title: 'Downloads', kind: 'downloads' }
  },
  {
    path: 'most-wanted',
    loadComponent: () => import('./dashboard-list.component').then(m => m.DashboardListComponent),
    data: { title: 'Most wanted', kind: 'most-wanted' }
  }
];

