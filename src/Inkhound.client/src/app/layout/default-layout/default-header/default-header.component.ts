import { NgClass, NgTemplateOutlet } from '@angular/common';
import { Component, computed, inject, input } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, NavigationEnd, Router, RouterLink, RouterLinkActive } from '@angular/router';
import { filter, map, startWith } from 'rxjs';

import {
  BreadcrumbRouterComponent,
  ColorModeService,
  ContainerComponent,
  DropdownComponent,
  DropdownDividerDirective,
  DropdownHeaderDirective,
  DropdownItemDirective,
  DropdownMenuDirective,
  DropdownToggleDirective,
  HeaderComponent,
  HeaderNavComponent,
  HeaderTogglerDirective,
  NavItemComponent,
  NavLinkDirective,
  ProgressBarComponent,
  ProgressComponent,
  SidebarToggleDirective
} from '@coreui/angular';

import { IconDirective } from '@coreui/icons-angular';
import { HubService } from '../../../core/services/hub.service';

@Component({
  selector: 'app-default-header',
  templateUrl: './default-header.component.html',
  styleUrl: './default-header.component.scss',
  imports: [
    NgClass, NgTemplateOutlet,
    ContainerComponent, HeaderTogglerDirective, SidebarToggleDirective, IconDirective,
    HeaderNavComponent, NavItemComponent, NavLinkDirective, RouterLink, RouterLinkActive,
    BreadcrumbRouterComponent,
    DropdownComponent, DropdownToggleDirective, DropdownMenuDirective,
    DropdownHeaderDirective, DropdownItemDirective, DropdownDividerDirective,
    ProgressComponent, ProgressBarComponent
  ]
})
export class DefaultHeaderComponent extends HeaderComponent {

  readonly #colorModeService = inject(ColorModeService);
  readonly #hub              = inject(HubService);
  readonly #router           = inject(Router);
  readonly #activatedRoute   = inject(ActivatedRoute);

  readonly colorMode = this.#colorModeService.colorMode;

  readonly breadcrumbs = toSignal(
    this.#router.events.pipe(
      filter(e => e instanceof NavigationEnd),
      startWith(null),
      map(() => this.#buildBreadcrumbs())
    ),
    { initialValue: [] as Array<{ label: string; url: string }> }
  );

  readonly statusBadgeColor = computed<string>(() => {
    const state = this.#hub.managerState();
    if (!state) return 'secondary';
    const getState = (name: string) =>
      state.stateServices.find(s => s.serviceName === name)?.state ?? 'NOTINIT';
    if (['ArchiveService', 'DbStorage'].some(n => getState(n) !== 'OK')) return 'danger';
    if (['ComicVine', 'Kavita'].some(n => getState(n) !== 'OK'))         return 'warning';
    return 'success';
  });

  readonly runningJobs = computed(() =>
    this.#hub.jobs().filter(j => j.state === 'RUNNING' || j.state === 'INITIALIZING')
  );

  readonly hasRunningJobs = computed(() => this.runningJobs().length > 0);

  readonly colorModes = [
    { name: 'light', text: 'Light', icon: 'cilSun' },
    { name: 'dark',  text: 'Dark',  icon: 'cilMoon' },
    { name: 'auto',  text: 'Auto',  icon: 'cilContrast' }
  ];

  readonly icons = computed(() => {
    const currentMode = this.colorMode();
    return this.colorModes.find(mode => mode.name === currentMode)?.icon ?? 'cilSun';
  });

  sidebarId = input('sidebar1');

  constructor() {
    super();
  }

  #buildBreadcrumbs(): Array<{ label: string; url: string }> {
    const items: Array<{ label: string; url: string }> = [];
    let route: ActivatedRoute | null = this.#activatedRoute.root;
    let url = '';
    while (route) {
      const child: ActivatedRoute | undefined = route.children.find(c => c.outlet === 'primary');
      if (!child) break;
      const snapshot = child.snapshot;
      if (!snapshot) { route = child; continue; }
      const segment = snapshot.url?.map(s => s.path).join('/') ?? '';
      if (segment) url += '/' + segment;
      const label = (snapshot.data?.['title'] as string) ?? snapshot.title ?? '';
      if (label) items.push({ label, url: url.endsWith('/') ? url : url + '/' });
      route = child;
    }
    return items;
  }
}
