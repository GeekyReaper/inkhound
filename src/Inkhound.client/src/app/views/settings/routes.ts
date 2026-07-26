import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    loadComponent: () => import('./modules.component').then(m => m.ModulesComponent),
    data: { title: 'Modules' }
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
  },
  {
    path: 'proxy',
    loadComponent: () => import('./proxy.component').then(m => m.ProxySettingsComponent),
    data: { title: 'Proxy' }
  },
  {
    path: 'api-tokens',
    loadComponent: () => import('./api-tokens.component').then(m => m.ApiTokensComponent),
    data: { title: 'API Tokens' }
  }
];
