import { Component, computed, DestroyRef, inject, input, OnInit, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs/operators';
import {
  AlertComponent, ButtonDirective, CardBodyComponent, CardComponent, CardHeaderComponent,
  FormControlDirective, FormDirective, FormFeedbackComponent, FormLabelDirective, SpinnerComponent
} from '@coreui/angular';
import { OptionDefinition } from '../../core/models/hub.models';
import { OptionsService } from '../../core/services/options.service';
import { TierColumn, TierListEditorComponent, TierRow } from '../tier-list-editor/tier-list-editor.component';

const LIST_FIELD_NAMES = ['FormatScoreByFormatJson', 'PageWeightTiersJson', 'ZipCompressionTiersJson', 'ScoreBandsJson'];

@Component({
  selector: 'app-scoring-settings',
  standalone: true,
  imports: [
    ReactiveFormsModule, FormDirective, FormControlDirective, FormLabelDirective, FormFeedbackComponent,
    CardComponent, CardHeaderComponent, CardBodyComponent, ButtonDirective, SpinnerComponent, AlertComponent,
    TierListEditorComponent
  ],
  templateUrl: './scoring-settings.component.html'
})
export class ScoringSettingsComponent implements OnInit {
  private optionsService = inject(OptionsService);
  readonly #destroyRef = inject(DestroyRef);

  serviceName = input<string>('Kavita');

  readonly formatScoreColumns: TierColumn[] = [
    { key: 'format', label: 'Format', type: 'text' },
    { key: 'score', label: 'Bonus / pénalité', type: 'number' }
  ];
  readonly pageWeightColumns: TierColumn[] = [
    { key: 'format', label: 'Format', type: 'text', nullable: true },
    { key: 'label', label: 'Libellé', type: 'text' },
    { key: 'minMb', label: 'Min (Mo)', type: 'number' },
    { key: 'maxMb', label: 'Max (Mo)', type: 'number' },
    { key: 'score', label: 'Bonus / pénalité', type: 'number' }
  ];
  readonly zipCompressionColumns: TierColumn[] = [
    { key: 'label', label: 'Libellé', type: 'text' },
    { key: 'minPct', label: 'Min (%)', type: 'number' },
    { key: 'maxPct', label: 'Max (%)', type: 'number' },
    { key: 'score', label: 'Bonus / pénalité', type: 'number' }
  ];
  readonly scoreBandColumns: TierColumn[] = [
    { key: 'min', label: 'Score min', type: 'number' },
    { key: 'band', label: 'Bande', type: 'text' }
  ];

  options = signal<OptionDefinition[]>([]);
  form = signal<FormGroup>(new FormGroup({}));
  loading = signal(true);
  saving = signal(false);
  saveStatus = signal<'idle' | 'success' | 'error'>('idle');

  formatScoreRows = signal<TierRow[]>([]);
  pageWeightRows = signal<TierRow[]>([]);
  zipCompressionRows = signal<TierRow[]>([]);
  scoreBandRows = signal<TierRow[]>([]);

  readonly scalarDefs = computed(() =>
    this.options().filter(o => o.section.startsWith('Scoring') && !LIST_FIELD_NAMES.includes(o.name)));

  readonly scalarGroups = computed(() => {
    const groups: { section: string; options: OptionDefinition[] }[] = [];
    for (const def of this.scalarDefs()) {
      const last = groups.at(-1);
      if (last && last.section === def.section) last.options.push(def);
      else groups.push({ section: def.section, options: [def] });
    }
    return groups;
  });

  ngOnInit(): void {
    this.load();
  }

  private load(): void {
    this.loading.set(true);
    this.optionsService.getOptions(this.serviceName())
      .pipe(takeUntilDestroyed(this.#destroyRef), finalize(() => this.loading.set(false)))
      .subscribe({
        next: defs => {
          this.options.set(defs);
          this.form.set(this.buildForm(defs.filter(d => d.section.startsWith('Scoring') && !LIST_FIELD_NAMES.includes(d.name))));
          this.formatScoreRows.set(this.parseRows(defs, 'FormatScoreByFormatJson'));
          this.pageWeightRows.set(this.parseRows(defs, 'PageWeightTiersJson'));
          this.zipCompressionRows.set(this.parseRows(defs, 'ZipCompressionTiersJson'));
          this.scoreBandRows.set(this.parseRows(defs, 'ScoreBandsJson'));
        }
      });
  }

  private parseRows(defs: OptionDefinition[], name: string): TierRow[] {
    const def = defs.find(d => d.name === name);
    if (!def) return [];
    try {
      return JSON.parse(def.value) as TierRow[];
    } catch {
      return [];
    }
  }

  save(): void {
    const fg = this.form();
    if (fg.invalid) return;

    const payload: Record<string, string> = {};
    for (const def of this.scalarDefs()) {
      payload[def.name] = String(fg.get(def.name)?.value ?? '');
    }
    payload['FormatScoreByFormatJson'] = JSON.stringify(this.formatScoreRows());
    payload['PageWeightTiersJson'] = JSON.stringify(this.pageWeightRows());
    payload['ZipCompressionTiersJson'] = JSON.stringify(this.zipCompressionRows());
    payload['ScoreBandsJson'] = JSON.stringify(this.scoreBandRows());

    this.saving.set(true);
    this.saveStatus.set('idle');
    this.optionsService.updateOptions(this.serviceName(), payload)
      .pipe(takeUntilDestroyed(this.#destroyRef), finalize(() => this.saving.set(false)))
      .subscribe({
        next: () => this.saveStatus.set('success'),
        error: () => this.saveStatus.set('error')
      });
  }

  private buildForm(defs: OptionDefinition[]): FormGroup {
    const controls: Record<string, FormControl> = {};
    for (const def of defs) {
      const validators = [];
      if (def.mandatory) validators.push(Validators.required);
      if (def.regexValidator) validators.push(Validators.pattern(def.regexValidator));
      controls[def.name] = new FormControl(def.value ?? def.defaultValue ?? '', validators);
    }
    return new FormGroup(controls);
  }
}
