import { Component, input, model } from '@angular/core';
import { ButtonDirective, TableDirective } from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';

export type TierRow = Record<string, string | number | null>;

export interface TierColumn {
  key: string;
  label: string;
  type: 'text' | 'number';
  /** When true, an empty input is stored as null (used for the optional "format" fallback column). */
  nullable?: boolean;
}

@Component({
  selector: 'app-tier-list-editor',
  standalone: true,
  imports: [TableDirective, ButtonDirective, IconDirective],
  templateUrl: './tier-list-editor.component.html'
})
export class TierListEditorComponent {
  columns = input.required<TierColumn[]>();
  rows = model<TierRow[]>([]);

  addRow(): void {
    const blank: TierRow = {};
    for (const col of this.columns()) {
      blank[col.key] = col.type === 'number' ? 0 : (col.nullable ? null : '');
    }
    this.rows.update(r => [...r, blank]);
  }

  removeRow(index: number): void {
    this.rows.update(r => r.filter((_, i) => i !== index));
  }

  updateCell(index: number, column: TierColumn, rawValue: string): void {
    const value: string | number | null = column.type === 'number'
      ? (rawValue === '' ? 0 : Number(rawValue))
      : (column.nullable && rawValue === '' ? null : rawValue);

    this.rows.update(r => r.map((row, i) => i === index ? { ...row, [column.key]: value } : row));
  }
}
