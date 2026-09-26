import { ChangeDetectionStrategy, Component, computed, DestroyRef, effect, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DatePipe } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import {
  AlertComponent,
  ButtonDirective,
  ButtonGroupComponent,
  ColComponent,
  FormSelectDirective,
  NavComponent,
  NavItemComponent,
  NavLinkDirective,
  PageItemComponent,
  PageLinkDirective,
  PaginationComponent,
  RowComponent,
  SpinnerComponent
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import {
  NEWS_CATEGORIES,
  NewsCategory,
  NewsItem,
  newsItemLabel,
  NewsLibraryLink,
  NewsReleases,
  NewsService,
  NewsTopSales
} from '../../core/services/news.service';
import { HubService } from '../../core/services/hub.service';
import { PageJobService } from '../../core/services/page-job.service';
import { libraryPageKey } from '../../core/services/library.service';
import { JobPanelComponent } from '../job-panel/job-panel.component';
import { ImageLightboxComponent, LightboxImage } from '../image-lightbox/image-lightbox.component';
import { AddedVolume, VolumeAddDialogComponent } from '../volume/volume-add-dialog/volume-add-dialog.component';
import { NewsCardComponent } from './news-card/news-card.component';

type NewsTab = 'top-sales' | 'releases';

// Module News : deux onglets alimentés par la base (historisée par le job News) —
// « Top Sales » (classement hebdomadaire, sélecteur de semaine) et « New Releases » (filtre
// BD / Manga / Comics, mois, pagination). « Add » lance le workflow d'ajout classique
// (VolumeAddDialogComponent) après résolution de la série si l'item n'est pas encore enrichi.
@Component({
  selector: 'app-news',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RowComponent, ColComponent, AlertComponent, ButtonDirective, ButtonGroupComponent, SpinnerComponent,
    NavComponent, NavItemComponent, NavLinkDirective, FormSelectDirective,
    PaginationComponent, PageItemComponent, PageLinkDirective, IconDirective, RouterLink, DatePipe,
    JobPanelComponent, ImageLightboxComponent, VolumeAddDialogComponent, NewsCardComponent
  ],
  templateUrl: './news.component.html'
})
export class NewsComponent {
  private readonly route    = inject(ActivatedRoute);
  private readonly router   = inject(Router);
  private readonly news     = inject(NewsService);
  private readonly hub      = inject(HubService);
  private readonly pageJobs = inject(PageJobService);
  readonly #destroyRef      = inject(DestroyRef);

  private readonly pageKey = '/news';
  readonly pageSize = 24;
  readonly categories = NEWS_CATEGORIES;

  readonly tab = signal<NewsTab>(this.route.snapshot.queryParamMap.get('tab') === 'releases' ? 'releases' : 'top-sales');

  // ── Top Sales ─────────────────────────────────────────────────────────────
  readonly topSales        = signal<NewsTopSales | null>(null);
  readonly topSalesLoading = signal(false);

  // ── New Releases ──────────────────────────────────────────────────────────
  readonly releases        = signal<NewsReleases | null>(null);
  readonly releasesLoading = signal(false);
  readonly category        = signal<NewsCategory | null>(null);
  readonly month           = signal<string | null>(null);
  readonly page            = signal(1);

  readonly error = signal<string | null>(null);

  readonly visiblePages = computed(() => {
    const total   = this.releases()?.page.totalPages ?? 0;
    const current = this.releases()?.page.pageNumber ?? 1;
    if (total <= 7) return Array.from({ length: total }, (_, i) => i + 1);
    const start = Math.max(1, Math.min(current - 3, total - 6));
    const end   = Math.min(total, start + 6);
    return Array.from({ length: end - start + 1 }, (_, i) => start + i);
  });

  // ── Refresh job ───────────────────────────────────────────────────────────
  readonly activeJobId = this.pageJobs.activeJobId(this.pageKey);
  private readonly currentJob = computed(() => {
    const id = this.activeJobId();
    return id ? this.hub.jobs().find(j => j.jobId === id) ?? null : null;
  });
  private readonly handledJobIds = new Set<string>();

  // ── Add workflow ──────────────────────────────────────────────────────────
  readonly addTarget   = signal<NewsItem | null>(null);
  readonly addSeriesId = signal<string | null>(null);
  readonly resolving   = signal<string | null>(null);   // albumId en cours de résolution

  // ── Lightbox ──────────────────────────────────────────────────────────────
  readonly lightboxImages = signal<LightboxImage[]>([]);
  readonly lightboxOpen   = signal(false);

  constructor() {
    this.loadCurrentTab();

    effect(() => {
      const job = this.currentJob();
      if (!job || this.handledJobIds.has(job.jobId)) return;
      if (job.state === 'SUCCESS' || job.state === 'ERROR') {
        this.handledJobIds.add(job.jobId);
        this.pageJobs.clear(this.pageKey);
        this.topSales.set(null);
        this.releases.set(null);
        this.loadCurrentTab();
      }
    });
  }

  selectTab(tab: NewsTab): void {
    if (this.tab() === tab) return;
    this.tab.set(tab);
    this.router.navigate([], { relativeTo: this.route, queryParams: { tab }, replaceUrl: true });
    this.loadCurrentTab();
  }

  private loadCurrentTab(): void {
    if (this.tab() === 'top-sales') {
      if (!this.topSales()) this.loadTopSales(null);
    } else if (!this.releases()) {
      this.loadReleases();
    }
  }

  loadTopSales(period: string | null): void {
    this.topSalesLoading.set(true);
    this.error.set(null);
    this.news.getTopSales(period)
      .pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        next:  res => { this.topSales.set(res); this.topSalesLoading.set(false); },
        error: err => { this.error.set(err?.error?.message ?? 'Failed to load top sales.'); this.topSalesLoading.set(false); }
      });
  }

  loadReleases(): void {
    this.releasesLoading.set(true);
    this.error.set(null);
    this.news.getReleases(this.category(), this.month(), this.page(), this.pageSize)
      .pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        next:  res => { this.releases.set(res); this.releasesLoading.set(false); },
        error: err => { this.error.set(err?.error?.message ?? 'Failed to load new releases.'); this.releasesLoading.set(false); }
      });
  }

  setCategory(category: NewsCategory | null): void {
    this.category.set(category);
    this.page.set(1);
    this.loadReleases();
  }

  setMonth(month: string): void {
    this.month.set(month || null);
    this.page.set(1);
    this.loadReleases();
  }

  goToPage(page: number): void {
    const total = this.releases()?.page.totalPages ?? 1;
    if (page < 1 || page > total || page === this.page()) return;
    this.page.set(page);
    this.loadReleases();
  }

  refresh(): void {
    if (this.activeJobId()) return;
    this.error.set(null);
    this.news.refresh(true)
      .pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        next:  res => this.pageJobs.register(this.pageKey, res.jobId),
        error: err => this.error.set(err?.error?.message ?? 'Failed to start the refresh.')
      });
  }

  // "2026-10" → date du 1er du mois, pour le DatePipe.
  monthDate(month: string): string {
    return `${month}-01`;
  }

  // ── Lightbox ──────────────────────────────────────────────────────────────
  openCover(item: NewsItem): void {
    const url = item.coverLargeUrl ?? item.coverUrl;
    if (!url) return;
    this.lightboxImages.set([{ url, thumbUrl: item.coverUrl, caption: newsItemLabel(item) }]);
    this.lightboxOpen.set(true);
  }

  // ── Add ───────────────────────────────────────────────────────────────────
  onAdd(item: NewsItem): void {
    if (item.seriesId) {
      this.openAddDialog(item, item.seriesId);
      return;
    }

    // Item pas encore enrichi : on résout sa série à la demande (une requête source).
    this.resolving.set(item.albumId);
    this.news.resolveSeries(item.albumId)
      .pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        next: res => {
          this.resolving.set(null);
          this.patchItems(item.albumId, res.seriesId, res.library);
          if (!res.library) this.openAddDialog(item, res.seriesId);
        },
        error: err => {
          this.resolving.set(null);
          this.error.set(err?.error?.message ?? 'Failed to resolve the series of this album.');
        }
      });
  }

  private openAddDialog(item: NewsItem, seriesId: string): void {
    this.addSeriesId.set(seriesId);
    this.addTarget.set(item);
  }

  closeAddDialog(): void {
    this.addTarget.set(null);
    this.addSeriesId.set(null);
  }

  onVolumeAdded(added: AddedVolume): void {
    const item = this.addTarget();
    const seriesId = this.addSeriesId();
    this.closeAddDialog();
    // Le peuplement des issues continue en tâche de fond — suivi sur la page Library.
    this.pageJobs.register(libraryPageKey(added.libraryId), added.jobId);
    if (item && seriesId)
      this.patchItems(item.albumId, seriesId, { libraryId: added.libraryId, volumeId: added.id });
  }

  // Met à jour localement l'item (et tous ceux de la même série) après résolution / ajout.
  private patchItems(albumId: string, seriesId: string, library: NewsLibraryLink | null): void {
    const patch = (i: NewsItem): NewsItem =>
      i.albumId === albumId || i.seriesId === seriesId
        ? { ...i, seriesId, library: library ?? i.library }
        : i;
    this.topSales.update(t => t ? { ...t, items: t.items.map(patch) } : t);
    this.releases.update(r => r ? { ...r, page: { ...r.page, items: r.page.items.map(patch) } } : r);
  }

  addTitle(): string | null {
    const item = this.addTarget();
    return item ? item.seriesTitle : null;
  }
}
