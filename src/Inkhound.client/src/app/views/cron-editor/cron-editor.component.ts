import { Component, computed, forwardRef, input, signal } from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';
import {
  ButtonCloseDirective, ButtonDirective,
  DropdownComponent, DropdownItemDirective, DropdownMenuDirective, DropdownToggleDirective,
  FormControlDirective, InputGroupComponent,
  ModalBodyComponent, ModalComponent, ModalFooterComponent, ModalHeaderComponent, ModalTitleDirective
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { SmartDatePipe } from '../../core/pipes/smart-date.pipe';
import {
  CRON_FIELDS, CRON_OPERATORS, CRON_PRESETS, CronPreset,
  describeCron, fieldAtCursor, nextOccurrences, splitFields
} from './cron.utils';

// Éditeur d'expression cron (5 champs) façon crontab.guru. Sur la page : résumé compact
// (expression en lecture seule + traduction en langage naturel + bouton Edit). L'édition se fait
// dans une modale sur un BROUILLON : saisie libre + presets, traduction mise à jour à chaque frappe,
// puces des 5 champs (celle sous le curseur est mise en avant et pilote le panneau d'aide : plage,
// alias, opérateurs), prochaines exécutions. « Apply » pousse le brouillon dans le FormControl.
// ControlValueAccessor : se branche directement sur un formControlName — la validation reste au
// FormControl (cf. cronValidator dans cron.utils.ts).
@Component({
  selector: 'app-cron-editor',
  standalone: true,
  imports: [
    InputGroupComponent, FormControlDirective, ButtonDirective, ButtonCloseDirective,
    DropdownComponent, DropdownToggleDirective, DropdownMenuDirective, DropdownItemDirective,
    ModalComponent, ModalHeaderComponent, ModalBodyComponent, ModalFooterComponent, ModalTitleDirective,
    IconDirective, SmartDatePipe
  ],
  templateUrl: './cron-editor.component.html',
  styleUrl: './cron-editor.component.scss',
  providers: [{ provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => CronEditorComponent), multi: true }]
})
export class CronEditorComponent implements ControlValueAccessor {
  // id de l'<input> en lecture seule, pour le <label cLabel for="…"> du parent.
  readonly inputId     = input.required<string>();
  readonly placeholder = input('0 3 * * *');
  readonly nextCount   = input(5);
  // Titre de la modale (ex. nom de la tâche).
  readonly title       = input('Edit schedule');

  readonly fields    = CRON_FIELDS;
  readonly operators = CRON_OPERATORS;
  readonly presets   = CRON_PRESETS;

  // ── Valeur du FormControl (résumé en page) ──────────────────────────────
  value    = signal('');
  disabled = signal(false);
  touched  = signal(false);

  readonly parsed = computed(() => describeCron(this.value()));
  readonly showError = computed(() => !this.parsed().valid && (this.touched() || this.value().trim().length > 0));

  // ── Brouillon de la modale ──────────────────────────────────────────────
  modalVisible = signal(false);
  draft        = signal('');
  cursorField  = signal<number | null>(null);

  readonly draftParsed = computed(() => describeCron(this.draft()));
  // Toujours 5 tokens (« · » pour un champ absent) pour les puces sous l'input.
  readonly draftTokens = computed(() => {
    const tokens = splitFields(this.draft());
    return CRON_FIELDS.map((_, i) => tokens[i] ?? '·');
  });
  readonly nextRuns    = computed(() => this.draftParsed().valid ? nextOccurrences(this.draft(), this.nextCount()) : []);

  // Champ mis en avant : celui sous le curseur, sinon le premier — le panneau d'aide a toujours
  // quelque chose à montrer.
  readonly activeField     = computed(() => this.cursorField() ?? 0);
  readonly activeFieldInfo = computed(() => CRON_FIELDS[this.activeField()]);

  private onChange: (value: string) => void = () => {};
  private onTouched: () => void = () => {};

  // ── ControlValueAccessor ─────────────────────────────────────────────────
  writeValue(value: string | null): void { this.value.set(value ?? ''); }
  registerOnChange(fn: (value: string) => void): void { this.onChange = fn; }
  registerOnTouched(fn: () => void): void { this.onTouched = fn; }
  setDisabledState(isDisabled: boolean): void { this.disabled.set(isDisabled); }

  // ── Modale ───────────────────────────────────────────────────────────────
  open(): void {
    if (this.disabled()) return;
    this.draft.set(this.value());
    this.cursorField.set(null);
    this.modalVisible.set(true);
  }

  cancel(): void {
    this.modalVisible.set(false);
  }

  // Le bouton Apply n'est actif que sur un brouillon valide ; le FormControl (cronValidator)
  // reste néanmoins l'autorité côté formulaire.
  apply(): void {
    const v = this.draft().trim();
    this.value.set(v);
    this.onChange(v);
    this.touched.set(true);
    this.onTouched();
    this.modalVisible.set(false);
  }

  onVisibleChange(visible: boolean): void {
    if (!visible) this.modalVisible.set(false);
  }

  // ── Événements de l'input du brouillon ───────────────────────────────────
  onDraftInput(event: Event): void {
    const el = event.target as HTMLInputElement;
    this.draft.set(el.value);
    this.updateCursor(el);
  }

  onCursorMove(event: Event): void {
    this.updateCursor(event.target as HTMLInputElement);
  }

  applyPreset(preset: CronPreset): void {
    this.draft.set(preset.expression);
    this.cursorField.set(null);
  }

  private updateCursor(el: HTMLInputElement): void {
    this.cursorField.set(fieldAtCursor(el.value, el.selectionStart ?? el.value.length));
  }
}
