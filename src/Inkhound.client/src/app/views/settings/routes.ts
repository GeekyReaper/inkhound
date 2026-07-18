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
  },
  {
    path: 'qbittorrent',
    loadComponent: () => import('./qbittorrent.component').then(m => m.QBittorrentSettingsComponent),
    data: { title: 'QBittorrent' }
  },
  {
    path: 'kavita',
    loadComponent: () => import('./kavita.component').then(m => m.KavitaSettingsComponent),
    data: { title: 'Kavita' }
  }
];
