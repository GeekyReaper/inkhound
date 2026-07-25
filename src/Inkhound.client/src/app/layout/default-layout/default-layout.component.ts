import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink, RouterOutlet } from '@angular/router';
import { NgScrollbar } from 'ngx-scrollbar';

import { INavData } from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import {
  ContainerComponent,
  ShadowOnScrollDirective,
  SidebarBrandComponent,
  SidebarComponent,
  SidebarFooterComponent,
  SidebarHeaderComponent,
  SidebarNavComponent,
  SidebarToggleDirective,
  SidebarTogglerDirective
} from '@coreui/angular';

import { DefaultFooterComponent, DefaultHeaderComponent } from './';
import { navItemsTop, navItemsBottom } from './_nav';
import { LibraryService } from '../../core/services/library.service';
import { ApiTokenService } from '../../core/services/api-token.service';
import { VersionService } from '../../core/services/version.service';

function isOverflown(element: HTMLElement) {
  return (
    element.scrollHeight > element.clientHeight ||
    element.scrollWidth > element.clientWidth
  );
}

@Component({
  selector: 'app-dashboard',
  templateUrl: './default-layout.component.html',
  styleUrls: ['./default-layout.component.scss'],
  imports: [
    SidebarComponent,
    SidebarHeaderComponent,
    SidebarBrandComponent,
    SidebarNavComponent,
    SidebarFooterComponent,
    SidebarToggleDirective,
    SidebarTogglerDirective,
    ContainerComponent,
    DefaultFooterComponent,
    DefaultHeaderComponent,
    IconDirective,
    NgScrollbar,
    RouterOutlet,
    RouterLink,
    ShadowOnScrollDirective
  ]
})
export class DefaultLayoutComponent {
  private libraryService = inject(LibraryService);
  private apiTokenService = inject(ApiTokenService);
  private versionService = inject(VersionService);

  apiTokensEnabled = signal(false);
  version = signal('');

  constructor() {
    this.libraryService.loadLibraries().subscribe();
    this.apiTokenService.isEnabled().subscribe(enabled => this.apiTokensEnabled.set(enabled));
    this.versionService.getVersion().subscribe(res => this.version.set(res.version));
  }

  navItems = computed<INavData[]>(() => [
    ...navItemsTop,
    ...this.libraryService.libraries().map(lib => ({
      name: lib.name,
      url: `/library/${lib.id}`,
      iconComponent: { name: 'cil-library' }
    })),
    ...navItemsBottom.filter(item => item.name !== 'API Tokens' || this.apiTokensEnabled())
  ]);
}
