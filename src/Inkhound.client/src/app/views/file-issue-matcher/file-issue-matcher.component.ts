import { ChangeDetectionStrategy, Component, computed, effect, input, signal, untracked } from '@angular/core';
import { IconDirective } from '@coreui/icons-angular';
import { ISSUE_CATEGORY_ORDER, Issue } from '../../core/services/issue.service';
import { formatSize } from '../../core/util/format-size';

// Fichier apparaissable — commun aux fichiers d'un torrent et aux fichiers d'un dossier.
export interface MatchableFile {
  name: string;
  size: number;
  detectedIssueNumber: number | null;
}

// Un appariement retenu : index du fichier dans le tableau `files()` → issueId.
export interface FileIssueAssignment {
  fileIndex: number;
  issueId: string;
}

/**
 * Tableau générique d'appariement fichiers ↔ issues d'un volume.
 * - auto-appariement par numéro de tome détecté (issues MISSING uniquement)
 * - `<select>` manuel par ligne (toutes les issues, DOWNLOADING désactivées, une issue prise
 *   ailleurs disparaît des autres listes)
 * - « coché » ⟺ « une issue lui est assignée »
 * Le parent lit la sélection courante via `viewChild(FileIssueMatcherComponent).selection()`.
 */
@Component({
  selector: 'app-file-issue-matcher',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [IconDirective],
  templateUrl: './file-issue-matcher.component.html',
  styleUrl: './file-issue-matcher.component.scss'
})
export class FileIssueMatcherComponent {
  files  = input.required<MatchableFile[]>();
  issues = input.required<Issue[]>();

  // fileIndex (position dans files()) -> issueId
  private readonly fileAssignments = signal<Map<number, string>>(new Map());

  readonly selectedFileIndices = computed(() => new Set(this.fileAssignments().keys()));
  readonly allFilesSelected    = computed(() => this.fileAssignments().size > 0);

  // Sélection courante — lue par le parent.
  readonly selection = computed<FileIssueAssignment[]>(() =>
    [...this.fileAssignments().entries()].map(([fileIndex, issueId]) => ({ fileIndex, issueId })));

  // Issues triées par catégorie (Standard en premier, cf. ISSUE_CATEGORY_ORDER) puis par numéro —
  // remplace l'ordre brut de issues(), qui varie selon l'appelant (trié par numéro seul côté
  // VolumeComponent, pas trié du tout côté ProwlarrSearchComponent). Centralisé ici pour que les
  // deux consommateurs bénéficient du même tri sans dupliquer la logique.
  private readonly sortedIssues = computed(() =>
    [...this.issues()].sort((a, b) =>
      ISSUE_CATEGORY_ORDER.indexOf(a.category) - ISSUE_CATEGORY_ORDER.indexOf(b.category)
      || a.issueNumber - b.issueNumber));

  // Seule cible de l'auto-appariement.
  private readonly missingIssues = computed(() => this.sortedIssues().filter(i => i.status === 'MISSING'));

  protected readonly formatSize = formatSize;

  constructor() {
    // Nouveau jeu de fichiers (ou liste d'issues arrivée après coup) → ardoise vierge + auto-appariement.
    // En pratique les deux entrées sont prêtes avant l'affichage du composant ; ce reset ne se
    // reproduit pas pendant la revue (les issues ne changent pas tant que la modale est ouverte).
    effect(() => {
      const files = this.files();
      const issues = this.issues();
      untracked(() => {
        this.fileAssignments.set(new Map());
        if (issues.length > 0) this.applyAutoAssignments(files);
      });
    });
  }

  // Auto-appariement des fichiers non encore assignés aux issues MISSING, par numéro détecté.
  // Additif — n'écrase jamais une assignation existante ni une issue déjà prise.
  private applyAutoAssignments(files: MatchableFile[]): void {
    this.fileAssignments.update(current => {
      const next = new Map(current);
      const takenIssueIds = new Set(next.values());
      files.forEach((file, i) => {
        if (next.has(i) || file.detectedIssueNumber === null) return;
        const issue = this.missingIssues().find(
          x => x.issueNumber === file.detectedIssueNumber && !takenIssueIds.has(x.id));
        if (issue) {
          next.set(i, issue.id);
          takenIssueIds.add(issue.id);
        }
      });
      return next;
    });
  }

  // Décocher une ligne libère l'issue qui lui était assignée (sélecteur remis à « — assign issue — »,
  // issue de nouveau disponible pour un autre fichier).
  onToggleFile(index: number): void {
    if (this.fileAssignments().has(index)) this.onManualAssign(index, '');
  }

  onToggleAllFiles(): void {
    if (this.allFilesSelected()) {
      this.fileAssignments.set(new Map());
    } else {
      this.applyAutoAssignments(this.files());
    }
  }

  isFileSelected(index: number): boolean {
    return this.selectedFileIndices().has(index);
  }

  isFileAssignable(index: number): boolean {
    return this.fileAssignments().has(index);
  }

  // Toutes les issues du volume sauf celles assignées à un AUTRE fichier ; les DOWNLOADING restent
  // listées mais non sélectionnables (voir isIssueSelectable).
  availableIssuesFor(index: number): Issue[] {
    const takenElsewhere = new Set(
      [...this.fileAssignments().entries()].filter(([i]) => i !== index).map(([, id]) => id));
    return this.sortedIssues().filter(i => !takenElsewhere.has(i.id));
  }

  issueOptionLabel(issue: Issue): string {
    const title = issue.title ? ` — ${issue.title}` : '';
    const state = { MISSING: 'Missing', DOWNLOADING: 'Downloading', DOWNLOADED: 'Downloaded' }[issue.status];
    return `#${issue.issueNumber}${title} · ${state}`;
  }

  isIssueSelectable(issue: Issue): boolean {
    return issue.status !== 'DOWNLOADING';
  }

  currentAssignment(index: number): string {
    return this.fileAssignments().get(index) ?? '';
  }

  onManualAssign(index: number, issueId: string): void {
    this.fileAssignments.update(m => {
      const next = new Map(m);
      if (issueId) next.set(index, issueId);
      else next.delete(index);
      return next;
    });
  }
}
