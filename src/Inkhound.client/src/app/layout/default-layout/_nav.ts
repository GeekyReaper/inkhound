import { INavData } from '@coreui/angular';

export const navItemsTop: INavData[] = [
  {
    name: 'Dashboard',
    url: '/dashboard',
    iconComponent: { name: 'cil-speedometer' }
  },
  {
    title: true,
    name: 'Library'
  }
];

export const navItemsBottom: INavData[] = [
  {
    title: true,
    name: 'Inkhound'
  },
  {
    name: 'Jobs',
    url: '/jobs',
    iconComponent: { name: 'cil-task' }
  },
  {
    name: 'Settings',
    url: '/settings',
    iconComponent: { name: 'cil-settings' },
    // Sans ça, CoreUI surligne "Settings" comme actif dès que l'URL COMMENCE par /settings
    // (ex: /settings/proxy) — puisque les autres items (Indexers, QBittorrent, ...) sont aussi
    // sous /settings/*, "Settings" restait en surbrillance en même temps qu'eux.
    linkProps: { routerLinkActiveOptions: { exact: true } }
  },
  {
    name: 'Indexers',
    url: '/settings/indexers',
    iconComponent: { name: 'cil-list' }
  },
  {
    name: 'QBittorrent',
    url: '/settings/qbittorrent',
    iconComponent: { name: 'cil-settings' }
  },
  {
    name: 'Kavita',
    url: '/settings/kavita',
    iconComponent: { name: 'cil-settings' }
  },
  {
    name: 'Proxy',
    url: '/settings/proxy',
    iconComponent: { name: 'cil-globe-alt' }
  },
  {
    name: 'API Tokens',
    url: '/settings/api-tokens',
    iconComponent: { name: 'cil-lock-locked' }
  },
  {
    name: 'Libraries',
    url: '/libraries',
    iconComponent: { name: 'cil-library' }
  },
  {
    name: 'Users',
    url: '/users',
    iconComponent: { name: 'cil-user' }
  },
  {
    name: 'Downloads',
    url: '/downloads',
    iconComponent: { name: 'cil-cloud-download' }
  },
  {
    title: true,
    name: 'Links',
    class: 'mt-auto'
  },
  {
    name: 'Docs',
    url: 'https://coreui.io/angular/docs/',
    iconComponent: { name: 'cil-description' },
    attributes: { target: '_blank' }
  }
];
