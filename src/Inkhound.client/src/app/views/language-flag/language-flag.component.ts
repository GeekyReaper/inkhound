import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { IconDirective } from '@coreui/icons-angular';

// Libellés produits côté backend par BedethequeSourceService.ExtractLangueFromFlag → icône
// drapeau CoreUI (flagSet). Les icônes référencées doivent être déclarées dans icons/icon-subset.ts.
const FLAG_BY_LANGUAGE: Record<string, string> = {
  'français':    'cif-fr',
  'anglais':     'cif-us',   // Bedetheque affiche le drapeau USA pour l'anglais
  'japonais':    'cif-jp',
  'italien':     'cif-it',
  'allemand':    'cif-de',
  'espagnol':    'cif-es',
  'néerlandais': 'cif-nl',
  'portugais':   'cif-pt',
};

// Affiche la langue d'un résultat de recherche sous forme de drapeau quand elle est connue et
// mappée ; repli sur le libellé texte sinon ; rien si la langue est absente (ex. ComicVine).
@Component({
  selector: 'app-language-flag',
  templateUrl: './language-flag.component.html',
  imports: [IconDirective],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class LanguageFlagComponent {
  language = input.required<string | null>();

  icon = computed(() => {
    const lang = this.language()?.trim().toLowerCase();
    return lang ? FLAG_BY_LANGUAGE[lang] ?? null : null;
  });
}
