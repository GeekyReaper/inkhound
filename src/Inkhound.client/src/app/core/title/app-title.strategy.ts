import { Injectable, inject } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { RouterStateSnapshot, TitleStrategy } from '@angular/router';

/** Titre fixe de l'onglet : les `title` des routes (template CoreUI) ne doivent pas le remplacer. */
export const APP_TITLE = 'Inkhound';

@Injectable({ providedIn: 'root' })
export class AppTitleStrategy extends TitleStrategy {
  private readonly title = inject(Title);

  override updateTitle(_snapshot: RouterStateSnapshot): void {
    this.title.setTitle(APP_TITLE);
  }
}
