import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DatePipe, DecimalPipe } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { switchMap, tap } from 'rxjs/operators';
import {
  AlertComponent,
  BadgeComponent,
  ButtonDirective,
  CardBodyComponent,
  CardComponent,
  CardHeaderComponent,
  ColComponent,
  RowComponent,
  SpinnerComponent
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { NewsAlbumDetail, NewsImage, NewsService, newsItemLabel } from '../../../core/services/news.service';
import { PageJobService } from '../../../core/services/page-job.service';
import { libraryPageKey } from '../../../core/services/library.service';
import { ImageLightboxComponent, LightboxImage } from '../../image-lightbox/image-lightbox.component';
import { AddedVolume, VolumeAddDialogComponent } from '../../volume/volume-add-dialog/volume-add-dialog.component';

// Page détail d'un album News : grande couverture et visuels (planche d'extrait, verso) en
// visionneuse, fiche de l'album (auteurs, éditeur, dépôt légal, note…), fiche de la série avec la
// liste de ses albums (chacun ouvrant sa propre page détail), et « Add » / « Open » selon que la
// série est déjà suivie. Données live côté backend (enrichissement + caches 24 h de la source).
@Component({
  selector: 'app-news-album',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RowComponent, ColComponent, CardComponent, CardHeaderComponent, CardBodyComponent, BadgeComponent,
    ButtonDirective, SpinnerComponent, AlertComponent, IconDirective, RouterLink, DatePipe, DecimalPipe,
    ImageLightboxComponent, VolumeAddDialogComponent
  ],
  templateUrl: './news-album.component.html'
})
export class NewsAlbumComponent {
  private readonly route    = inject(ActivatedRoute);
  private readonly news     = inject(NewsService);
  private readonly pageJobs = inject(PageJobService);
  readonly #destroyRef      = inject(DestroyRef);

  readonly detail  = signal<NewsAlbumDetail | null>(null);
  readonly loading = signal(true);
  readonly error   = signal<string | null>(null);

  readonly addOpen      = signal(false);
  readonly lightboxOpen = signal(false);
  readonly lightboxStart = signal(0);

  readonly label = computed(() => {
    const a = this.detail()?.album;
    return a ? newsItemLabel({ seriesTitle: a.seriesTitle, albumNumber: a.albumNumber, albumTitle: a.albumTitle }) : '';
  });

  // Visuels : couverture d'abord (repli sur la miniature connue si la page n'en expose pas), puis planches et verso.
  readonly images = computed<NewsImage[]>(() => {
    const a = this.detail()?.album;
    if (!a) return [];
    const imgs = [...a.images];
    if (!imgs.some(i => i.kind === 'Cover') && (a.coverLargeUrl || a.coverUrl))
      imgs.unshift({ kind: 'Cover', url: a.coverLargeUrl ?? a.coverUrl!, thumbUrl: a.coverUrl ?? a.coverLargeUrl! });
    return imgs;
  });

  readonly cover = computed(() => this.images().find(i => i.kind === 'Cover') ?? null);
  readonly previews = computed(() => this.images().filter(i => i.kind !== 'Cover'));

  readonly lightboxImages = computed<LightboxImage[]>(() =>
    this.images().map(i => ({ url: i.url, thumbUrl: i.thumbUrl, caption: `${this.label()} — ${this.kindLabel(i.kind)}` })));

  readonly libraryLink = computed<unknown[] | null>(() => {
    const lib = this.detail()?.library;
    return lib ? ['/library', lib.libraryId, 'volume', lib.volumeId] : null;
  });

  constructor() {
    this.route.paramMap
      .pipe(
        tap(() => { this.loading.set(true); this.error.set(null); this.detail.set(null); }),
        switchMap(params => this.news.getAlbum(params.get('albumId') ?? '')),
        takeUntilDestroyed(this.#destroyRef)
      )
      .subscribe({
        next:  d => { this.detail.set(d); this.loading.set(false); },
        error: err => {
          this.error.set(err?.error?.message ?? 'Failed to load this album.');
          this.loading.set(false);
        }
      });
  }

  kindLabel(kind: NewsImage['kind']): string {
    switch (kind) {
      case 'Cover': return 'Cover';
      case 'Plate': return 'Preview page';
      default:      return 'Back cover';
    }
  }

  openImage(image: NewsImage): void {
    this.lightboxStart.set(Math.max(0, this.images().indexOf(image)));
    this.lightboxOpen.set(true);
  }

  onVolumeAdded(added: AddedVolume): void {
    this.addOpen.set(false);
    this.pageJobs.register(libraryPageKey(added.libraryId), added.jobId);
    this.detail.update(d => d ? { ...d, library: { libraryId: added.libraryId, volumeId: added.id } } : d);
  }
}
