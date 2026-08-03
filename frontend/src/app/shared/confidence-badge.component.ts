import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

import { confidenceBand } from '../core/models';

/**
 * Confidence indicator (FR-8.6), shown on every extracted item, alert and answer.
 * Never colour alone — the band always carries a word (§15 accessibility).
 */
@Component({
  selector: 'mt-confidence',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <span
      class="inline-flex items-center gap-1 rounded-full border px-2 py-0.5 text-xs font-medium"
      [class]="classes()"
      [title]="tooltip()"
    >
      <span aria-hidden="true">{{ glyph() }}</span>
      {{ label() }}
    </span>
  `
})
export class ConfidenceBadgeComponent {
  readonly score = input<number | null | undefined>(null);

  protected readonly band = computed(() => confidenceBand(this.score()));

  protected readonly label = computed(() => {
    switch (this.band()) {
      case 'high':
        return `High · ${this.score()}%`;
      case 'medium':
        return `Verify · ${this.score()}%`;
      case 'low':
        return `Low · ${this.score()}%`;
      default:
        return 'Not scored';
    }
  });

  protected readonly glyph = computed(() => {
    switch (this.band()) {
      case 'high':
        return '✓';
      case 'medium':
        return '!';
      case 'low':
        return '⚠';
      default:
        return '–';
    }
  });

  protected readonly tooltip = computed(() => {
    switch (this.band()) {
      case 'high':
        return 'The model read this clearly and it is consistent with the other documents.';
      case 'medium':
        return 'Readable but not certain — worth confirming with a pharmacist.';
      case 'low':
        return 'The model was unsure. Check this against the source document before relying on it.';
      default:
        return 'No confidence score was recorded for this item.';
    }
  });

  protected readonly classes = computed(() => {
    switch (this.band()) {
      case 'high':
        return 'border-emerald-200 bg-emerald-50 text-emerald-800';
      case 'medium':
        return 'border-amber-200 bg-amber-50 text-amber-800';
      case 'low':
        return 'border-red-200 bg-red-50 text-red-800';
      default:
        return 'border-slate-200 bg-slate-50 text-slate-600';
    }
  });
}
