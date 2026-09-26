import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { Router } from '@angular/router';
import {
  ButtonCloseDirective,
  ButtonDirective,
  ModalBodyComponent,
  ModalComponent,
  ModalFooterComponent,
  ModalHeaderComponent,
  ModalTitleDirective
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';

/** Image affichable en grand : `url` = grand format, `thumbUrl` = repli si le grand format échoue. */
export interface LightboxImage {
  url: string;
  thumbUrl?: string | null;
  caption?: string | null;
}

// Visionneuse plein écran (couvertures, planches, versos) : grand format avec repli automatique
// sur la miniature si l'image haute résolution est introuvable, navigation ‹ › (et flèches du
// clavier) quand plusieurs images sont fournies. `link` (optionnel) ajoute un bouton de navigation
// (ex. « Details » vers la page d'un album) : la modale est fermée avant de naviguer.
@Component({
  selector: 'app-image-lightbox',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ModalComponent, ModalHeaderComponent, ModalBodyComponent, ModalFooterComponent, ModalTitleDirective, ButtonCloseDirective, ButtonDirective, IconDirective],
  templateUrl: './image-lightbox.component.html',
  host: { '(document:keydown)': 'onKeydown($event)' }
})
export class ImageLightboxComponent {
  readonly images     = input<LightboxImage[]>([]);
  readonly startIndex = input(0);
  readonly visible    = input(false);
  readonly closed     = output<void>();
  /** Route du bouton d'action (ex. détail de l'album) — aucun bouton si null. */
  readonly link       = input<unknown[] | null>(null);
  readonly linkLabel  = input('Details');

  private readonly router = inject(Router);

  readonly index    = signal(0);
  // Grand format en échec pour l'image courante → on affiche la miniature.
  readonly fallback = signal(false);

  readonly current = computed<LightboxImage | null>(() => this.images()[this.index()] ?? null);
  readonly src     = computed(() => {
    const img = this.current();
    if (!img) return null;
    return this.fallback() && img.thumbUrl ? img.thumbUrl : img.url;
  });

  constructor() {
    effect(() => {
      if (!this.visible()) return;
      this.index.set(Math.min(Math.max(0, this.startIndex()), Math.max(0, this.images().length - 1)));
      this.fallback.set(false);
    });
  }

  move(delta: number): void {
    const count = this.images().length;
    if (count < 2) return;
    this.index.set((this.index() + delta + count) % count);
    this.fallback.set(false);
  }

  onError(): void {
    const img = this.current();
    if (!this.fallback() && img?.thumbUrl && img.thumbUrl !== img.url) this.fallback.set(true);
  }

  followLink(): void {
    const link = this.link();
    if (!link) return;
    this.closed.emit();
    this.router.navigate(link);
  }

  onKeydown(event: KeyboardEvent): void {
    if (!this.visible()) return;
    if (event.key === 'ArrowLeft') this.move(-1);
    else if (event.key === 'ArrowRight') this.move(1);
  }
}
