import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';
import cronstrue from 'cronstrue';
import { CronExpressionParser } from 'cron-parser';

// Logique pure du CronEditorComponent (testée dans cron.utils.spec.ts) : description en langage
// naturel (cronstrue), prochaines occurrences (cron-parser, syntaxe 5 champs compatible Cronos côté
// backend), découpage en champs, champ sous le curseur, presets et validateur Reactive Forms.

export interface CronFieldInfo {
  name: string;        // libellé court affiché sous le token
  range: string;       // plage autorisée
  aliases?: string;    // noms acceptés (JAN-DEC, SUN-SAT)
  note?: string;       // précision (ex. 7 = dimanche)
}

// Les 5 champs d'une expression, dans l'ordre.
export const CRON_FIELDS: readonly CronFieldInfo[] = [
  { name: 'minute',       range: '0-59' },
  { name: 'hour',         range: '0-23' },
  { name: 'day (month)',  range: '1-31' },
  { name: 'month',        range: '1-12', aliases: 'JAN-DEC' },
  { name: 'day (week)',   range: '0-6',  aliases: 'SUN-SAT', note: '0 and 7 are both Sunday' }
];

// Opérateurs acceptés dans chaque champ (légende du panneau d'aide).
export const CRON_OPERATORS: readonly { symbol: string; meaning: string; example: string }[] = [
  { symbol: '*', meaning: 'any value',        example: '* = every' },
  { symbol: ',', meaning: 'value list',       example: '1,15 = 1st and 15th' },
  { symbol: '-', meaning: 'range of values',  example: '1-5 = 1 through 5' },
  { symbol: '/', meaning: 'step values',      example: '*/15 = every 15' }
];

export interface CronPreset { label: string; expression: string; }

export const CRON_PRESETS: readonly CronPreset[] = [
  { label: 'Every 15 minutes',            expression: '*/15 * * * *' },
  { label: 'Every hour',                  expression: '0 * * * *' },
  { label: 'Every 6 hours',               expression: '0 */6 * * *' },
  { label: 'Every day at 03:00',          expression: '0 3 * * *' },
  { label: 'Every Sunday at 04:00',       expression: '0 4 * * 0' },
  { label: 'Weekdays at 02:00',           expression: '0 2 * * 1-5' },
  { label: '1st of the month at 02:00',   expression: '0 2 1 * *' }
];

export type CronDescription =
  | { valid: true; description: string }
  | { valid: false; error: string };

// Découpe sur les blancs (tokens non vides). Ne garantit pas 5 éléments : c'est au parseur de
// rejeter, l'éditeur affiche les puces pour les tokens réellement présents.
export function splitFields(expression: string): string[] {
  return expression.trim().split(/\s+/).filter(t => t.length > 0);
}

// Validation stricte "5 champs" via cron-parser : renvoie le message d'erreur, ou null si valide.
// Cronos côté backend n'accepte que 5 champs — les secondes (6 champs) sont donc refusées ici aussi.
export function cronError(expression: string): string | null {
  const trimmed = expression.trim();
  if (!trimmed) return 'Expression is empty.';
  const fields = splitFields(trimmed);
  if (fields.length !== 5) return `Expected 5 fields (minute hour day month weekday), got ${fields.length}.`;
  try {
    CronExpressionParser.parse(trimmed);
    return null;
  } catch (e) {
    return e instanceof Error ? e.message : String(e);
  }
}

export function describeCron(expression: string): CronDescription {
  const error = cronError(expression);
  if (error) return { valid: false, error };
  try {
    // verbose: false → « Every 15 minutes » plutôt que « Every 15 minutes, every hour, every day ».
    const description = cronstrue.toString(expression.trim(), {
      use24HourTimeFormat: true,
      verbose: false,
      throwExceptionOnParseError: true
    });
    return { valid: true, description };
  } catch (e) {
    return { valid: false, error: e instanceof Error ? e.message : String(e) };
  }
}

// Prochaines occurrences dans le fuseau du navigateur (le scheduler backend tourne en heure
// serveur — identiques en self-hosted classique). Tableau vide si l'expression est invalide.
export function nextOccurrences(expression: string, count: number, from: Date = new Date()): Date[] {
  if (cronError(expression)) return [];
  try {
    const it = CronExpressionParser.parse(expression.trim(), { currentDate: from });
    const dates: Date[] = [];
    for (let i = 0; i < count; i++) dates.push(it.next().toDate());
    return dates;
  } catch {
    return [];
  }
}

// Index (0-4) du champ où se trouve le curseur, null si hors des tokens (blanc, ou au-delà du 5e).
// Un curseur collé à la fin d'un token ("*/15|") lui appartient ; entre deux tokens il appartient
// au token précédent tant qu'il n'a pas franchi un blanc.
export function fieldAtCursor(expression: string, cursor: number): number | null {
  if (cursor < 0 || cursor > expression.length) return null;
  const re = /\S+/g;
  let index = 0;
  let m: RegExpExecArray | null;
  while ((m = re.exec(expression)) !== null) {
    const start = m.index;
    const end = start + m[0].length;
    if (cursor >= start && cursor <= end) return index < 5 ? index : null;
    index++;
  }
  return null;
}

// Validateur Reactive Forms : vide = valide (c'est le `Enabled` de la tâche qui rend le cron
// obligatoire côté backend), sinon la règle de cronError.
export const cronValidator: ValidatorFn = (control: AbstractControl): ValidationErrors | null => {
  const value = String(control.value ?? '');
  if (!value.trim()) return null;
  const error = cronError(value);
  return error ? { cron: error } : null;
};
