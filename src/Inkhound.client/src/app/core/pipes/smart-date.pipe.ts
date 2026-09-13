import { formatDate } from '@angular/common';
import { inject, LOCALE_ID, Pipe, PipeTransform } from '@angular/core';

// Date + heure compacte : format 24 h sans secondes, et l'année n'est affichée que si elle diffère
// de l'année courante — "13 Sep, 14:32" cette année, "13 Sep 2025, 14:32" sinon. Remplace
// `date:'medium'` / `date:'short'` sur les horodatages métier (créé le, dernier refresh, …).
// null/undefined/invalide → chaîne vide (les templates gardent leur repli "never").
@Pipe({ name: 'smartDate' })
export class SmartDatePipe implements PipeTransform {
  private locale = inject(LOCALE_ID);

  transform(value: string | number | Date | null | undefined): string {
    if (value == null || value === '') return '';
    const date = value instanceof Date ? value : new Date(value);
    if (isNaN(date.getTime())) return '';

    const sameYear = date.getFullYear() === new Date().getFullYear();
    return formatDate(date, sameYear ? 'd MMM, HH:mm' : 'd MMM y, HH:mm', this.locale);
  }
}
