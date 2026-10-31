import { Component, computed, DestroyRef, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  AlertComponent,
  ButtonCloseDirective,
  ButtonDirective,
  CardBodyComponent,
  CardComponent,
  CardFooterComponent,
  ColComponent,
  ModalBodyComponent,
  ModalComponent,
  ModalHeaderComponent,
  ModalTitleDirective,
  PageItemComponent,
  PageLinkDirective,
  PaginationComponent,
  RowComponent,
  SpinnerComponent
} from '@coreui/angular';
import { NgClass, SlicePipe } from '@angular/common';
import { IconDirective } from '@coreui/icons-angular';
import {
  PageResult,
  SEARCH_ERROR_CATALOG_NOT_LOADED,
  SourceSearchStats,
  VolumeSearchResult,
  VolumeService
} from '../../../core/services/volume.service';
import { SourceIssue, IssueService } from '../../../core/services/issue.service';
import { HubService } from '../../../core/services/hub.service';
import { PageJobService } from '../../../core/services/page-job.service';
import { AuthService } from '../../../core/services/auth.service';
import { Library, libraryPageKey, LibraryService } from '../../../core/services/library.service';
import { JobPanelComponent } from '../../job-panel/job-panel.component';
import { LanguageFlagComponent } from '../../language-flag/language-flag.component';
import { AddedVolume, VolumeAddDialogComponent } from '../volume-add-dialog/volume-add-dialog.component';

// Section « New request » de la page Requests : recherche multi-source (ComicVine + Bedetheque) puis
// modale library + age rating. Un admin ajoute le volume tout de suite (demande auto-approuvée), un
// Guest dépose une demande en attente — la différence est gérée par VolumeAddDialogComponent.
@Component({
  selector: 'app-volume-search',
  templateUrl: './volume-search.component.html',
  styleUrl: './volume-search.component.scss',
  imports: [
    RowComponent, ColComponent,
    CardComponent, CardFooterComponent, CardBodyComponent,
    ModalComponent, ModalHeaderComponent, ModalBodyComponent, ModalTitleDirective, ButtonCloseDirective,
    SpinnerComponent, AlertComponent, ButtonDirective,
    PaginationComponent, PageItemComponent, PageLinkDirective,
    FormsModule, NgClass, IconDirective, SlicePipe,
    JobPanelComponent, LanguageFlagComponent, RouterLink, VolumeAddDialogComponent
  ]
})
export class VolumeSearchComponent {
  private route          = inject(ActivatedRoute);
  private router         = inject(Router);
  private volumeService  = inject(VolumeService);
  private issueService   = inject(IssueService);
  private hub            = inject(HubService);
  private pageJobs       = inject(PageJobService);
  private libraryService = inject(LibraryService);
  readonly isAdmin       = inject(AuthService).isAdmin;
  readonly #destroyRef   = inject(DestroyRef);

  // Library cible : pré-remplie via ?library=<id> (bouton "+ Add" d'une page Library), sinon
  // choisie par l'utilisateur dans la modale. Un id inconnu des libraries chargées est traité
  // comme non pré-rempli.
  readonly libraries  = this.libraryService.libraries;
  readonly presetLibrary = computed<Library | null>(() => {
    const id = this.route.snapshot.queryParamMap.get('library');
    return id ? this.libraries().find(l => l.id === id) ?? null : null;
  });
  readonly hasLibraries = computed(() => this.libraries().length > 0);

  readonly issuesPageSize = 12;

  // Clé de page pour PageJobService — inclut le query string (?library=...), donc stable pour
  // toute la durée de vie de ce composant.
  private readonly pageKey = this.router.url;

  // ── Search state (multi-source : ComicVine + Bedetheque, via un Job) ─────
  query            = signal('');
  results          = signal<PageResult<VolumeSearchResult> | null>(null);
  loading          = signal(false);
  error            = signal<string | null>(null);
  selected         = signal<VolumeSearchResult | null>(null);
  adding           = signal(false);
  ageRatingModalVisible = signal(false);
  issuesModalVolume = signal<VolumeSearchResult | null>(null);
  issuesPage        = signal<PageResult<SourceIssue> | null>(null);
  issuesLoading     = signal(false);
  issuesError       = signal<string | null>(null);

  stats           = signal<SourceSearchStats[] | null>(null);
  // Le catalogue local Bedetheque est vide : alerte avec lien vers /settings/bedetheque.
  readonly catalogNotLoaded = computed(() =>
    this.stats()?.some(s => s.errorCode === SEARCH_ERROR_CATALOG_NOT_LOADED) ?? false);
  private handledJobIds = new Set<string>();

  // Job actif pour cette page (persisté en session via PageJobService, 1 seul job actif à la
  // fois) — signal créé une seule fois, source de vérité pour le composant et pour le panel.
  readonly activeJobId = this.pageJobs.activeJobId(this.pageKey);

  private readonly currentJob = computed(() => {
    const jobId = this.activeJobId();
    return jobId ? this.hub.jobs().find(j => j.jobId === jobId) ?? null : null;
  });

  constructor() {
    // Les libraries sont normalement déjà chargées par DefaultLayoutComponent ; repli si on
    // arrive directement sur cette URL avant que le layout ait répondu.
    if (this.libraries().length === 0) {
      this.libraryService.loadLibraries().pipe(takeUntilDestroyed(this.#destroyRef)).subscribe();
    }

    effect(() => {
      const job = this.currentJob();
      if (!job || this.handledJobIds.has(job.jobId)) return;

      if (job.state === 'SUCCESS') {
        this.handledJobIds.add(job.jobId);
        this.fetchSearchResult(job.jobId);
        this.pageJobs.clear(this.pageKey);
      } else if (job.state === 'ERROR') {
        this.handledJobIds.add(job.jobId);
        this.loading.set(false);
        this.error.set('Search failed — see the console for details.');
        this.pageJobs.clear(this.pageKey);
      }
    });
  }

  // ── ComicVine pagination ──────────────────────────────────────────────────
  visiblePages = computed(() => {
    const total   = this.results()?.totalPages ?? 0;
    const current = this.results()?.pageNumber ?? 1;
    if (total <= 7) return Array.from({ length: total }, (_, i) => i + 1);
    const start = Math.max(1, Math.min(current - 3, total - 6));
    const end   = Math.min(total, start + 6);
    return Array.from({ length: end - start + 1 }, (_, i) => start + i);
  });

  issuesVisiblePages = computed(() => {
    const total   = this.issuesPage()?.totalPages ?? 0;
    const current = this.issuesPage()?.pageNumber ?? 1;
    if (total <= 7) return Array.from({ length: total }, (_, i) => i + 1);
    const start = Math.max(1, Math.min(current - 3, total - 6));
    const end   = Math.min(total, start + 6);
    return Array.from({ length: end - start + 1 }, (_, i) => start + i);
  });

  // ── Search methods ────────────────────────────────────────────────────────
  // Le bouton "Search" lance un Job backend (recherche multi-source, peut prendre plusieurs
  // secondes) et suit sa progression en direct via HubService/SignalR. Le résultat final est
  // récupéré une fois le job terminé (cf. l'effect() du constructeur).
  search(page = 1): void {
    const name = this.query().trim();
    if (!name || this.activeJobId()) return;

    this.loading.set(true);
    this.error.set(null);
    this.selected.set(null);
    this.stats.set(null);

    this.volumeService.startSearchJob(name, page)
      .pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        next:  res => this.pageJobs.register(this.pageKey, res.jobId),
        error: err => { this.error.set(err?.error?.message ?? 'Search failed.'); this.loading.set(false); }
      });
  }

  private fetchSearchResult(jobId: string): void {
    this.volumeService.getSearchJobResult(jobId)
      .pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        next:  res => { this.results.set(res.page); this.stats.set(res.stats); this.loading.set(false); },
        error: err => { this.error.set(err?.error?.message ?? 'Failed to load results.'); this.loading.set(false); }
      });
  }

  sourceLabel(source: string): string {
    return source === 'comicvine' ? 'ComicVine' : 'Bedetheque';
  }

  showIssues(volume: VolumeSearchResult, event: Event): void {
    event.stopPropagation();
    this.issuesPage.set(null);
    this.issuesError.set(null);
    this.issuesModalVolume.set(volume);
    this.loadIssues(volume, 1);
  }

  loadIssues(volume: VolumeSearchResult, page: number): void {
    this.issuesLoading.set(true);
    this.issuesError.set(null);

    this.issueService.getBySourceVolume(volume.source, volume.sourceId, page, this.issuesPageSize)
      .pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        next:  res => { this.issuesPage.set(res); this.issuesLoading.set(false); },
        error: err => { this.issuesError.set(err?.error?.message ?? 'Failed to load issues.'); this.issuesLoading.set(false); }
      });
  }

  closeIssuesModal(): void {
    this.issuesModalVolume.set(null);
    this.issuesPage.set(null);
    this.issuesError.set(null);
  }

  onAdd(): void {
    if (!this.selected()) return;
    this.ageRatingModalVisible.set(true);
  }

  cancelAddModal(): void {
    this.ageRatingModalVisible.set(false);
  }

  // Volume créé par la modale : le peuplement des issues continue en tâche de fond — on associe
  // son job à la page Library qu'on rejoint, pour y afficher sa progression (cf. LibraryComponent).
  onVolumeAdded(added: AddedVolume): void {
    this.ageRatingModalVisible.set(false);
    if (added.jobId) this.pageJobs.register(libraryPageKey(added.libraryId), added.jobId);
    this.router.navigate(['/library', added.libraryId]);
  }
}
