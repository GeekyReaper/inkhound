import { INavData } from '@coreui/angular';

export const navItemsTop: INavData[] = [
  {
    name: 'Dashboard',
    url: '/dashboard',
    iconComponent: { name: 'cil-speedometer' }
  }
];

export const navItemsBottom: INavData[] = [
  {
    name: 'Jobs',
    url: '/jobs',
    iconComponent: { name: 'cil-task' }
  },
  {
    name: 'Downloads',
    url: '/downloads',
    iconComponent: { name: 'cil-cloud-download' }
  },
  {
    title: true,
    name: 'Settings'
  },
  {
    name: 'Modules',
    url: '/settings',
    iconComponent: { name: 'cil-settings' },
    // Sans ça, CoreUI surligne "Modules" comme actif dès que l'URL COMMENCE par /settings
    // (ex: /settings/proxy) — puisque les autres items (QBittorrent, Kavita, ...) sont aussi
    // sous /settings/*, "Modules" restait en surbrillance en même temps qu'eux.
    linkProps: { routerLinkActiveOptions: { exact: true } }
  },
  {
    name: 'Libraries',
    url: '/libraries',
    iconComponent: { name: 'cil-library' }
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
    title: true,
    name: 'Access'
  },
  {
    name: 'Users',
    url: '/users',
    iconComponent: { name: 'cil-user' }
  },
  {
    name: 'API Tokens',
    url: '/settings/api-tokens',
    iconComponent: { name: 'cil-lock-locked' }
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
