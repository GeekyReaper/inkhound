import { Component, computed, DestroyRef, effect, ElementRef, inject, signal, viewChild } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { auditTime, filter, fromEvent, switchMap, tap } from 'rxjs';
import {
  AlertComponent,
  BadgeComponent,
  ButtonCloseDirective,
  ButtonDirective,
  CardBodyComponent,
  CardComponent,
  ColComponent,
  ContainerComponent,
  FormCheckComponent,
  FormCheckInputDirective,
  FormCheckLabelDirective,
  FormControlDirective,
  FormSelectDirective,
  ModalBodyComponent,
  ModalComponent,
  ModalFooterComponent,
  ModalHeaderComponent,
  ModalTitleDirective,
  PageItemComponent,
  PageLinkDirective,
  PaginationComponent,
  ProgressBarComponent,
  ProgressComponent,
  RowComponent,
  SpinnerComponent,
  TooltipDirective
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { Library, LibraryService, libraryPageKey } from '../../core/services/library.service';
import { EMPTY_LIBRARY_VIEW_STATE, LibraryViewStateService } from '../../core/services/library-view-state.service';
import { NavigationTrackerService } from '../../core/services/navigation-tracker.service';
import { KavitaService } from '../../core/services/kavita.service';
import { AGE_RATINGS, AgeRating, RefreshVolumeOptions, Volume, VolumeService, VolumeStatus } from '../../core/services/volume.service';
import { HubService } from '../../core/services/hub.service';
import { PageJobService } from '../../core/services/page-job.service';
import { JobPanelComponent } from '../job-panel/job-panel.component';
import { JobContext, UpdatedData } from '../../core/models/hub.models';

@Component({
  selector: 'app-library',
  templateUrl: './library.component.html',
  styleUrl: './library.component.scss',
  imports: [
    ContainerComponent, RowComponent, ColComponent,
    CardComponent, CardBodyComponent,
    SpinnerComponent, AlertComponent, ButtonDirective, DatePipe, RouterLink,
    BadgeComponent, ProgressComponent, ProgressBarComponent, TooltipDirective,
    ModalComponent, ModalHeaderComponent, ModalBodyComponent, ModalFooterComponent,
    ModalTitleDirective, ButtonCloseDirective,
    FormCheckComponent, FormCheckInputDirective, FormCheckLabelDirective,
    FormControlDirective, FormSelectDirective,
    PaginationComponent, PageItemComponent, PageLinkDirective,
    JobPanelComponent, IconDirective
  ]
})
export class LibraryComponent {
  private route          = inject(ActivatedRoute);
  private libraryService = inject(LibraryService);
  private viewState      = inject(LibraryViewStateService);
  private navTracker     = inject(NavigationTrackerService);
  private kavitaService  = inject(KavitaService);
  private volumeService  = inject(VolumeService);
  private hub            = inject(HubService);
  private pageJobs       = inject(PageJobService);
  readonly #destroyRef   = inject(DestroyRef);

  // Ancre « début de la liste des volumes » (en-tête « Volumes (n) ») — cible de remontée du
  // viewport lors d'un changement de page ou de filtre.
  private readonly volumesTop = viewChild<ElementRef<HTMLElement>>('volumesTop');

  // ─── Persistance de la vue (filtres + page + scroll) — cf. LibraryViewStateService ───────────
  // Position de scroll à restaurer une fois la liste rendue — « latch » impératif consommé une
  // seule fois par l'effect ci-dessous (volontairement pas un signal : seul volumesLoading doit
  // déclencher la tentative de restauration).
  private pendingScrollY: number | null = null;
  // Dernière position de scroll connue, pistée en continu — filet de sécurité si `window.scrollY`
  // a déjà été remis à 0 par le retrait du DOM au moment de la destruction (view transitions).
  private lastScrollY = 0;
  // Empêche la sauvegarde réactive d'écraser l'état restauré tant que la library n'est pas chargée.
  private viewStateReady = false;

  library        = signal<Library | null>(null);
  loading        = signal(true);
  error          = signal<string | null>(null);
  syncing        = signal(false);
  syncDone       = signal<string | null>(null);
  volumes        = signal<Volume[]>([]);
  volumesLoading = signal(false);

  // ─── Filtres + pagination (côté client — même approche que jobs.component) ─────
  // La liste complète des volumes reste chargée (loadVolumes) ; filtres et découpage
  // se font en mémoire via des computed. Toute la barre de filtres (facettes incluses)
  // est dérivée de volumes(), donc la popup Refresh continue de lire la liste entière.
  private static readonly SOURCE_LABELS: Record<string, string> = {
    comicvine: 'ComicVine', bedetheque: 'Bédéthèque', manual: 'Manual'
  };

  // "ComicVine" / "bedetheque" / "manual" côté backend — normalisé, casse ignorée.
  private static sourceKey(v: Volume): string {
    const s = (v.sourceType ?? '').toLowerCase();
    return s === 'comicvine' || s === 'bedetheque' ? s : 'manual';
  }

  // Minuscule + suppression des diacritiques (recherche titre insensible aux accents).
  private static norm(s: string): string {
    return s.trim().toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '');
  }

  // Initiale normalisée : "Astérix" → "A" ; tout ce qui n'est pas A–Z → "#".
  private static initial(title: string): string {
    const c = LibraryComponent.norm(title).charAt(0).toUpperCase();
    return c >= 'A' && c <= 'Z' ? c : '#';
  }

  readonly ALPHABET = [...'ABCDEFGHIJKLMNOPQRSTUVWXYZ', '#'];
  readonly pageSize = 20;

  currentPage     = signal(1);
  search          = signal('');
  letter          = signal<string | null>(null);
  completeness    = signal<'all' | 'complete' | 'incomplete'>('all');
  sourceFilter    = signal<string | null>(null);
  yearFilter      = signal<number | null>(null);
  ageRatingFilter = signal<AgeRating | null>(null);

  readonly filteredVolumes = computed(() => {
    const q            = LibraryComponent.norm(this.search());
    const letter       = this.letter();
    const completeness = this.completeness();
    const source       = this.sourceFilter();
    const year         = this.yearFilter();
    const rating       = this.ageRatingFilter();

    return this.volumes().filter(v => {
      if (q && !LibraryComponent.norm(v.title).includes(q)) return false;
      if (letter && LibraryComponent.initial(v.title) !== letter) return false;
      if (completeness === 'complete'   && v.status !== 'COMPLETED') return false;
      if (completeness === 'incomplete' && v.status === 'COMPLETED') return false;
      if (source && LibraryComponent.sourceKey(v) !== source) return false;
      if (year !== null && v.year !== year) return false;
      if (rating && v.ageRating !== rating) return false;
      return true;
    });
  });

  readonly totalPages  = computed(() => Math.max(1, Math.ceil(this.filteredVolumes().length / this.pageSize)));
  // Garde-fou : si la liste rétrécit (SignalR) alors qu'on est sur une page haute.
  readonly clampedPage = computed(() => Math.min(this.currentPage(), this.totalPages()));

  readonly pagedVolumes = computed(() => {
    const start = (this.clampedPage() - 1) * this.pageSize;
    return this.filteredVolumes().slice(start, start + this.pageSize);
  });

  // Fenêtre glissante de 7 pages — copie du helper de jobs.component.
  readonly visiblePages = computed(() => {
    const total   = this.totalPages();
    const current = this.clampedPage();
    if (total <= 7) return Array.from({ length: total }, (_, i) => i + 1);
    const start = Math.max(1, Math.min(current - 3, total - 6));
    const end   = Math.min(total, start + 6);
    return Array.from({ length: end - start + 1 }, (_, i) => start + i);
  });

  // Facettes — calculées sur la liste complète pour que la barre de filtres reste stable.
  readonly availableInitials = computed(() => {
    const set = new Set<string>();
    for (const v of this.volumes()) set.add(LibraryComponent.initial(v.title));
    return set;
  });

  readonly availableSources = computed(() => {
    const counts = new Map<string, number>();
    for (const v of this.volumes()) {
      const k = LibraryComponent.sourceKey(v);
      counts.set(k, (counts.get(k) ?? 0) + 1);
    }
    return [...counts.entries()]
      .map(([key, count]) => ({ key, label: LibraryComponent.SOURCE_LABELS[key], count }))
      .sort((a, b) => a.label.localeCompare(b.label));
  });

  readonly availableYears = computed(() => {
    const set = new Set<number>();
    for (const v of this.volumes()) if (v.year != null) set.add(v.year);
    return [...set].sort((a, b) => b - a);
  });

  readonly availableAgeRatings = computed(() => {
    const set = new Set<AgeRating>();
    for (const v of this.volumes()) if (v.ageRating) set.add(v.ageRating);
    return AGE_RATINGS.filter(r => set.has(r.value));
  });

  readonly hasActiveFilter = computed(() =>
    this.search().trim() !== '' ||
    this.letter() !== null ||
    this.completeness() !== 'all' ||
    this.sourceFilter() !== null ||
    this.yearFilter() !== null ||
    this.ageRatingFilter() !== null
  );

  // Disponibilité des étapes de la popup Refresh — à partir de volumes()/library() déjà chargés,
  // aucun appel réseau supplémentaire (mêmes règles que sur volume.component.ts).
  hasAnyDownloadedIssues = computed(() => this.volumes().some(v => v.countOfDownloadedIssues > 0));
  hasAnySourcedVolume = computed(() => this.volumes().some(v => v.sourceType.toLowerCase() !== 'manual'));
  manualVolumeCount = computed(() => this.volumes().filter(v => v.sourceType.toLowerCase() === 'manual').length);
  hasKavitaLibrary = computed(() => {
    const lib = this.library();
    return !!lib && (lib.kavitaLibraryId > 0 || !!lib.kavitaPath);
  });

  refreshModalVisible = signal(false);
  refreshOptions = signal<RefreshVolumeOptions>({
    syncFromSource: true, syncNewIssuesOnly: true,
    recalculateStatistics: true, regenerateComicInfo: true, scanKavita: true
  });
  refreshCanRun = computed(() => {
    const o = this.refreshOptions();
    return o.syncFromSource || o.recalculateStatistics || o.regenerateComicInfo || o.scanKavita;
  });

  // Lot de jobs "Refresh" en cours — un par volume, chacun indépendant (pas de job parent, le
  // modèle Job actuel ne supporte pas l'imbrication). PAS géré par PageJobService (conçu pour 1
  // seul jobId par page) : signal local, non persisté entre reloads — les jobs individuels
  // continuent côté serveur même si la page est rechargée, seul le suivi live est perdu.
  activeRefreshJobIds = signal<string[]>([]);
  refreshDetailsVisible = signal(false);
  private handledRefreshJobIds = new Set<string>();

  private readonly refreshJobs = computed(() =>
    this.activeRefreshJobIds()
      .map(id => this.hub.jobs().find(j => j.jobId === id))
      .filter((j): j is JobContext => !!j)
  );
  refreshCompletedCount = computed(() =>
    this.refreshJobs().filter(j => j.state === 'SUCCESS' || j.state === 'ERROR').length
  );
  refreshBatchPercent = computed(() => {
    const total = this.activeRefreshJobIds().length;
    return total ? Math.round((this.refreshCompletedCount() / total) * 100) : 0;
  });

  // Clé de page dérivée (pas figée à la construction) : ce composant peut être réutilisé d'une
  // library à l'autre sans être détruit — d'où le switchMap sur route.params ci-dessous.
  private readonly pageKey = computed(() => {
    const lib = this.library();
    return lib ? libraryPageKey(lib.id) : null;
  });

  // Job actif associé à cette page (ex: peuplement des issues d'un Volume ajouté depuis
  // VolumeAddComponent, cf. PageJobService) — un seul à la fois, tant qu'il tourne le bouton
  // "+ Add" reste désactivé.
  readonly activeJobId = computed(() => {
    const key = this.pageKey();
    return key ? this.pageJobs.activeJobId(key)() : null;
  });

  private readonly currentJob = computed(() => {
    const jobId = this.activeJobId();
    return jobId ? this.hub.jobs().find(j => j.jobId === jobId) ?? null : null;
  });

  private handledJobIds = new Set<string>();

  constructor() {
    this.kavitaService.loadLibraries();

    // ─── Persistance de la vue liste (filtres + page + scroll), par id de library ──────────────

    // Sauvegarde réactive des filtres + page : couvre le changement de :id sans destruction du
    // composant (Angular réutilise l'instance). `viewStateReady` bloque l'écriture tant que la
    // library cible n'est pas chargée, sinon la valeur restaurée serait écrasée par les défauts.
    effect(() => {
      const snapshot = {
        search:       this.search(),
        letter:       this.letter(),
        completeness: this.completeness(),
        source:       this.sourceFilter(),
        year:         this.yearFilter(),
        ageRating:    this.ageRatingFilter(),
        page:         this.currentPage(),
      };
      const lib = this.library();
      if (!lib || !this.viewStateReady || this.volumesLoading()) return;
      this.viewState.patch(lib.id, snapshot);
    });

    // Piste la position de scroll fenêtre en continu et la persiste (hors chargement).
    fromEvent(window, 'scroll')
      .pipe(auditTime(150), takeUntilDestroyed(this.#destroyRef))
      .subscribe(() => {
        this.lastScrollY = window.scrollY;
        const lib = this.library();
        if (lib && this.viewStateReady && !this.volumesLoading()) {
          this.viewState.patch(lib.id, { scrollY: window.scrollY });
        }
      });

    // Restaure la position de scroll une fois la liste des volumes rendue (pendingScrollY posé par
    // restoreViewState quand on « revient » sur la page). Les signaux sont lus inconditionnellement
    // pour que l'effect se ré-exécute à la fin du chargement. Double rAF (cas normal) + relance à
    // 300 ms : passe après le scroll-to-top asynchrone du RouterScroller, le reflow d'insertion des
    // cartes et l'animation de view transition (~250 ms).
    effect(() => {
      const loading = this.volumesLoading();
      void this.pagedVolumes();   // dépendance : re-tenter quand la liste (re)rend
      const y = this.pendingScrollY;
      if (y === null || loading) return;
      this.pendingScrollY = null;
      const apply = () => { window.scrollTo(0, y); this.lastScrollY = y; };
      requestAnimationFrame(() => requestAnimationFrame(apply));
      setTimeout(apply, 300);
    });

    // Filet de sécurité : commit final de la position de scroll à la destruction (ouverture d'un
    // volume, add-volume…) — au moment du onDestroy `window.scrollY` peut déjà valoir 0 (retrait
    // du DOM), d'où le repli sur `lastScrollY`.
    this.#destroyRef.onDestroy(() => {
      const lib = this.library();
      if (lib) this.viewState.patch(lib.id, { scrollY: window.scrollY || this.lastScrollY });
    });

    effect(() => {
      const job = this.currentJob();
      if (!job || this.handledJobIds.has(job.jobId)) return;
      if (job.state !== 'SUCCESS' && job.state !== 'ERROR') return;

      this.handledJobIds.add(job.jobId);
      const key = this.pageKey();
      if (key) this.pageJobs.clear(key);
      if (job.state === 'ERROR') this.error.set('Failed to add volume — see the console for details.');
      // La liste des volumes est déjà rafraîchie par l'abonnement lastDataUpdated ci-dessous.
    });

    effect(() => {
      const jobs = this.refreshJobs();
      if (jobs.length === 0) return;
      const allDone = jobs.every(j => j.state === 'SUCCESS' || j.state === 'ERROR');
      if (!allDone) return;
      if (jobs.some(j => !this.handledRefreshJobIds.has(j.jobId))) {
        jobs.forEach(j => this.handledRefreshJobIds.add(j.jobId));
        if (jobs.some(j => j.state === 'ERROR'))
          this.error.set('Some volumes failed to refresh — see individual job consoles for details.');
      }
      // Les données Volume/Library à jour arrivent via lastDataUpdated, déjà écouté ci-dessous.
    });

    this.route.params
      .pipe(
        tap(() => { this.viewStateReady = false; }),
        switchMap(params => this.libraryService.getById(params['id'])),
        takeUntilDestroyed(this.#destroyRef)
      )
      .subscribe({
        next: lib => {
          this.library.set(lib);
          this.loading.set(false);
          this.restoreViewState(lib.id);
          this.loadVolumes(lib.id);
          this.viewStateReady = true;
        },
        error: err => {
          this.error.set(err?.error?.message ?? 'Library not found.');
          this.loading.set(false);
        }
      });

    const dataUpdated$ = toObservable(this.hub.lastDataUpdated).pipe(
      filter((d): d is UpdatedData => d !== null),
      takeUntilDestroyed(this.#destroyRef)
    );

    dataUpdated$.pipe(
      filter(d => d.dataType.endsWith('Library') && d.id === this.library()?.id)
    ).subscribe(() =>
      this.libraryService.getById(this.library()!.id)
        .pipe(takeUntilDestroyed(this.#destroyRef))
        .subscribe(lib => this.library.set(lib))
    );

    dataUpdated$.pipe(
      filter(d => d.dataType.endsWith('Volume'))
    ).subscribe(() => {
      const lib = this.library();
      if (lib) this.loadVolumes(lib.id);
    });
  }

  private loadVolumes(libraryId: string): void {
    this.volumesLoading.set(true);
    this.volumeService.getByLibrary(libraryId)
      .pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        next:  volumes => { this.volumes.set(volumes); this.volumesLoading.set(false); },
        error: ()      => { this.volumesLoading.set(false); }
      });
  }

  // Réapplique les filtres + la page mémorisés pour cette library (ou les valeurs par défaut si
  // aucun état : indispensable puisque le composant est réutilisé d'une library à l'autre — sans
  // reset, les filtres de la précédente resteraient collés). Écriture directe sur les signaux (pas
  // via les setters) → aucun scroll parasite. La position de scroll n'est ré-armée que si on
  // « revient » sur la page (retour depuis un volume / add-volume, ou back/forward navigateur) —
  // pas lors d'une arrivée depuis la sidebar ou un autre écran (cf. NavigationTrackerService).
  private restoreViewState(libraryId: string): void {
    const saved = this.viewState.get(libraryId);
    const s = saved ?? EMPTY_LIBRARY_VIEW_STATE;
    this.search.set(s.search);
    this.letter.set(s.letter);
    this.completeness.set(s.completeness);
    this.sourceFilter.set(s.source);
    this.yearFilter.set(s.year);
    this.ageRatingFilter.set(s.ageRating);
    this.currentPage.set(s.page);
    const isReturn = this.navTracker.isReturnInto(libraryPageKey(libraryId));
    this.pendingScrollY = (saved && isReturn) ? saved.scrollY : null;
  }

  // Remonte le viewport au début de la liste (en-tête « Volumes (n) »), en tenant compte du
  // header sticky via scroll-margin-top (cf. library.component.scss).
  private scrollToVolumesTop(): void {
    this.volumesTop()?.nativeElement.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }

  // Retour page 1 + remontée en tête de liste — commun aux changements de filtre discrets et au
  // bouton Clear (pas à la saisie du champ recherche, qui ne doit pas faire sauter le scroll).
  private resetPaging(): void {
    this.currentPage.set(1);
    this.scrollToVolumesTop();
  }

  getKavitaLibraryName(id: number): string {
    if (id === 0) return 'None';
    return this.kavitaService.libraries().find(l => l.id === id)?.name ?? `#${id}`;
  }

  statusBadgeColor(status: VolumeStatus): string {
    const map: Record<VolumeStatus, string> = {
      MONITORED: 'primary',
      COMPLETED: 'success',
      PAUSED:    'secondary'
    };
    return map[status];
  }

  progressPercent(volume: Volume): number {
    if (!volume.countOfIssues) return 0;
    return Math.round((volume.countOfDownloadedIssues / volume.countOfIssues) * 100);
  }

  volumeSubtitle(volume: Volume): string {
    const parts = [
      volume.year?.toString(),
      volume.publisher,
      LibraryComponent.SOURCE_LABELS[LibraryComponent.sourceKey(volume)]
    ].filter((p): p is string => !!p);
    return parts.length ? parts.join(' · ') : '—';
  }

  // ─── Filtres — chaque changement discret ramène à la page 1 + remonte en tête de liste ──────
  setLetter(l: string | null): void {
    this.letter.set(this.letter() === l ? null : l);
    this.resetPaging();
  }

  setCompleteness(mode: 'all' | 'complete' | 'incomplete'): void {
    this.completeness.set(mode);
    this.resetPaging();
  }

  setSourceFilter(source: string): void {
    this.sourceFilter.set(source || null);
    this.resetPaging();
  }

  setYearFilter(year: string): void {
    this.yearFilter.set(year ? Number(year) : null);
    this.resetPaging();
  }

  setAgeRatingFilter(rating: string): void {
    this.ageRatingFilter.set((rating || null) as AgeRating | null);
    this.resetPaging();
  }

  // Saisie au fil de l'eau : retour page 1 sans remonter le scroll (sinon saut à chaque frappe).
  onSearch(value: string): void {
    this.search.set(value);
    this.currentPage.set(1);
  }

  clearFilters(): void {
    this.search.set('');
    this.letter.set(null);
    this.completeness.set('all');
    this.sourceFilter.set(null);
    this.yearFilter.set(null);
    this.ageRatingFilter.set(null);
    this.resetPaging();
  }

  goToPage(page: number): void {
    if (page < 1 || page > this.totalPages()) return;
    this.currentPage.set(page);
    this.scrollToVolumesTop();
  }

  ageRatingLabel(rating: AgeRating): string {
    return AGE_RATINGS.find(r => r.value === rating)?.label ?? rating;
  }

  synchronize(): void {
    const lib = this.library();
    if (!lib || this.syncing()) return;

    this.syncing.set(true);
    this.syncDone.set(null);
    this.error.set(null);

    this.libraryService.sync(lib.id)
      .pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        next: res => {
          this.syncDone.set(res.message);
          this.syncing.set(false);
        },
        error: err => {
          this.error.set(err?.error?.message ?? 'Failed to start synchronization.');
          this.syncing.set(false);
        }
      });
  }

  openRefreshModal(): void {
    if (this.activeRefreshJobIds().length > 0) return;
    this.refreshOptions.set({
      syncFromSource: this.hasAnySourcedVolume(),
      syncNewIssuesOnly: true,
      recalculateStatistics: true,
      regenerateComicInfo: this.hasAnyDownloadedIssues(),
      scanKavita: this.hasKavitaLibrary()
    });
    this.refreshModalVisible.set(true);
  }

  toggleRefreshOption(key: keyof RefreshVolumeOptions): void {
    this.refreshOptions.update(o => ({ ...o, [key]: !o[key] }));
  }

  // Radio "NEW issues only" / "ALL issues" sous la case "Sync with source" (ne se toggle pas).
  setSyncScope(newIssuesOnly: boolean): void {
    this.refreshOptions.update(o => ({ ...o, syncNewIssuesOnly: newIssuesOnly }));
  }

  confirmRefresh(): void {
    const lib = this.library();
    if (!lib) return;
    this.refreshModalVisible.set(false);
    this.libraryService.refresh(lib.id, this.refreshOptions())
      .pipe(takeUntilDestroyed(this.#destroyRef))
      .subscribe({
        next:  res => { this.activeRefreshJobIds.set(res.jobIds); this.refreshDetailsVisible.set(false); },
        error: err => this.error.set(err?.error?.message ?? 'Refresh failed.')
      });
  }
}
