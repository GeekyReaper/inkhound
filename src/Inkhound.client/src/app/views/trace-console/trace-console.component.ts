import { Component, effect, ElementRef, input, viewChild } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ETraceLevel, TraceDefinition } from '../../core/models/hub.models';

/**
 * Console de traces purement présentationnelle : rendu monospace, coloration par niveau et
 * autoscroll. Partagée par la modale de console d'un job et par la page du module Chatbot, qui
 * affichent la même chose à partir de deux sources de traces différentes.
 */
@Component({
  selector: 'app-trace-console',
  standalone: true,
  imports: [DatePipe],
  templateUrl: './trace-console.component.html',
  styleUrl: './trace-console.component.scss',
})
export class TraceConsoleComponent {
  readonly traces      = input.required<TraceDefinition[]>();
  readonly emptyText   = input('No trace captured.');
  readonly autoScroll  = input(true);
  /** Hauteur CSS de la zone de console (toute unité valide). */
  readonly height      = input('50vh');

  private consoleEl = viewChild<ElementRef<HTMLDivElement>>('consoleEl');

  constructor() {
    effect(() => {
      this.traces();
      if (!this.autoScroll()) return;

      const el = this.consoleEl();
      if (el) {
        el.nativeElement.scrollTop = el.nativeElement.scrollHeight;
      }
    });
  }

  traceLevelClass(level: ETraceLevel): string {
    switch (level) {
      case 'ERROR':    return 'trace-error';
      case 'CRITICAL': return 'trace-critical';
      case 'WARNING':  return 'trace-warning';
      case 'DEBUG':    return 'trace-debug';
      default:         return 'trace-info';
    }
  }
}
