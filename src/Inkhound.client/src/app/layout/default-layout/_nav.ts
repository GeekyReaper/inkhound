import { INavData } from '@coreui/angular';

export const navItemsTop: INavData[] = [
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
    iconComponent: { name: 'cil-settings' }
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
    name: 'Libraries',
    url: '/libraries',
    iconComponent: { name: 'cil-library' }
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
