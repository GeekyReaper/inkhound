import { FormControl } from '@angular/forms';
import { cronError, cronValidator, describeCron, fieldAtCursor, nextOccurrences, splitFields } from './cron.utils';

describe('cron.utils', () => {
  describe('splitFields', () => {
    it('découpe sur les blancs multiples', () => {
      expect(splitFields('  */15   * *  * * ')).toEqual(['*/15', '*', '*', '*', '*']);
    });
  });

  describe('cronError / describeCron', () => {
    it('accepte une expression 5 champs valide', () => {
      expect(cronError('*/15 * * * *')).toBeNull();
      expect(describeCron('*/15 * * * *')).toEqual({ valid: true, description: 'Every 15 minutes' });
    });

    it('décrit en 24h', () => {
      const r = describeCron('0 3 * * *');
      expect(r.valid).toBe(true);
      if (r.valid) expect(r.description).toContain('03:00');
    });

    it('refuse une expression vide, 6 champs, ou une valeur hors plage', () => {
      expect(cronError('')).toContain('empty');
      expect(cronError('0 0 3 * * *')).toContain('Expected 5 fields');
      expect(describeCron('0 3 * * 8').valid).toBe(false);
      expect(describeCron('60 * * * *').valid).toBe(false);
    });
  });

  describe('nextOccurrences', () => {
    it('calcule les occurrences à partir de la date fournie', () => {
      const from = new Date(2026, 8, 19, 10, 2, 0);   // 19/09/2026 10:02 local
      const dates = nextOccurrences('*/15 * * * *', 3, from);
      expect(dates.map(d => d.getMinutes())).toEqual([15, 30, 45]);
      expect(dates.every(d => d.getHours() === 10)).toBe(true);
    });

    it('renvoie vide si invalide', () => {
      expect(nextOccurrences('nope', 3)).toEqual([]);
    });
  });

  describe('fieldAtCursor', () => {
    const expr = '*/15 0-6 * * 1-5';   // positions : 0-4 | 5-8 | 9-10 | 11-12 | 13-16

    it('retourne le champ sous le curseur, fin de token incluse', () => {
      expect(fieldAtCursor(expr, 0)).toBe(0);
      expect(fieldAtCursor(expr, 4)).toBe(0);
      expect(fieldAtCursor(expr, 5)).toBe(1);
      expect(fieldAtCursor(expr, 10)).toBe(2);
      expect(fieldAtCursor(expr, 16)).toBe(4);
    });

    it('retourne null entre deux tokens séparés par plusieurs blancs ou hors bornes', () => {
      expect(fieldAtCursor('*  *', 1)).toBe(0);   // collé à la fin du 1er token
      expect(fieldAtCursor('*  *', 2)).toBeNull();
      expect(fieldAtCursor(expr, 99)).toBeNull();
    });
  });

  describe('cronValidator', () => {
    it('vide = valide, invalide = { cron }', () => {
      expect(cronValidator(new FormControl(''))).toBeNull();
      expect(cronValidator(new FormControl('0 3 * * *'))).toBeNull();
      expect(cronValidator(new FormControl('0 3 * *'))).toHaveProperty('cron');
    });
  });
});
