import { ChangeDetectionStrategy, Component, OnDestroy, computed, inject, input, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Subscription, switchMap, timer } from 'rxjs';

import { ApiService } from '../../core/api.service';
import { LanguageService } from '../../core/language.service';
import type { PatientStatus, ProcessingStatus } from '../../core/models';

/**
 * Named pipeline stages the user sees (§10.3). Must match what PatientAnalyzer actually writes.
 * Lab trends are computed on GET /labs, not in this run — they are not a background stage
 * (Round 2 R2-0.4). AnalyzingTrends remains on the wire enum so stored ordinals stay stable.
 */
const STAGES: { key: PatientStatus; en: string; ta: string }[] = [
  { key: 'Extracting', en: 'Reading documents', ta: 'ஆவணங்களைப் படிக்கிறது' },
  { key: 'Merging', en: 'Building timeline', ta: 'காலவரிசையை உருவாக்குகிறது' },
  { key: 'CrossChecking', en: 'Cross-checking medications', ta: 'மருந்துகளை ஒப்பிடுகிறது' },
  { key: 'Verifying', en: 'Verifying against drug data', ta: 'மருந்துத் தரவுடன் உறுதிப்படுத்துகிறது' }
];

@Component({
  selector: 'mt-processing-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-2xl px-6 py-12">
      <h1 class="text-2xl font-semibold tracking-tight text-slate-900">
        {{ language.pick('Reading your records', 'உங்கள் பதிவுகளைப் படிக்கிறோம்') }}
      </h1>
      <p class="mt-1 text-sm text-slate-500">
        {{ language.pick(
          'This usually takes a minute or two. You can leave this page open — nothing is lost if you refresh.',
          'இது ஒரு அல்லது இரு நிமிடங்கள் ஆகலாம். இந்தப் பக்கத்தைத் திறந்து வைக்கலாம் — புதுப்பித்தாலும் எதுவும் தொலைவதில்லை.'
        ) }}
      </p>
      <p class="mt-2 text-xs text-slate-400">
        {{ language.pick(
          'Lab trends are calculated when you open that tab, not in this background run. A cached file is byte-identical to one already read — no second AI call.',
          'ஆய்வகப் போக்குகள் அந்தத் தாவலைத் திறக்கும்போது கணக்கிடப்படுகின்றன, இந்தப் பின்னணி ஓட்டத்தில் அல்ல. ஒரே கோப்பின் நகல் மீண்டும் AI அழைப்பை ஏற்படுத்தாது.'
        ) }}
      </p>

      <ol class="mt-10 space-y-1">
        @for (stage of stages; track stage.key; let i = $index) {
          <li class="flex items-start gap-3 py-2">
            <span
              class="mt-0.5 flex size-6 shrink-0 items-center justify-center rounded-full border text-xs"
              [class]="stageClasses(i)"
              aria-hidden="true"
            >
              {{ stageIndex() > i ? '✓' : i + 1 }}
            </span>
            <span class="text-sm" [class]="stageIndex() >= i ? 'text-slate-900' : 'text-slate-400'">
              {{ language.pick(stage.en, stage.ta) }}
            </span>
          </li>
        }
      </ol>

      @if (status(); as s) {
        <div class="mt-10 rounded-xl border border-slate-200 bg-white">
          <div class="flex items-center justify-between border-b border-slate-100 px-5 py-3">
            <span class="text-xs font-medium text-slate-600">Documents</span>
            <span class="text-xs text-slate-500">{{ s.completed }} of {{ s.total }} read</span>
          </div>

          <ul class="divide-y divide-slate-100">
            @for (doc of s.documents; track doc.documentId) {
              <li class="flex items-center justify-between px-5 py-3">
                <span class="truncate pr-4 text-sm text-slate-700">{{ doc.fileName }}</span>
                <span class="shrink-0 text-xs" [class]="documentClasses(doc.status)">
                  {{ documentLabel(doc.status) }}
                </span>
              </li>
              @if (doc.failureReason) {
                <li class="bg-red-50 px-5 py-2 text-xs text-red-800">{{ doc.failureReason }}</li>
              }
            }
          </ul>
        </div>

        @if (s.failed > 0) {
          <p class="mt-4 text-xs text-slate-500">
            {{ s.failed }} document{{ s.failed === 1 ? '' : 's' }} could not be read. The rest were
            processed — nothing else was lost.
          </p>
        }
      }

      @if (error(); as message) {
        <p class="mt-6 rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-800">
          {{ message }}
        </p>
      }
    </section>
  `
})
export class ProcessingPageComponent implements OnDestroy {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  protected readonly language = inject(LanguageService);
  private poll?: Subscription;

  readonly patientId = input.required<string>();

  protected readonly stages = STAGES;
  protected readonly status = signal<ProcessingStatus | null>(null);
  protected readonly error = signal<string | null>(null);

  protected readonly stageIndex = computed(() => {
    const current = this.status()?.status;
    if (!current) return 0;
    // AnalyzingTrends is a leftover enum value the analyzer never writes. Treat it as finished
    // so a stale row cannot rewind the stepper to "Reading documents".
    if (current === 'Ready' || current === 'Failed' || current === 'AnalyzingTrends') {
      return STAGES.length;
    }
    const index = STAGES.findIndex(s => s.key === current);
    return index < 0 ? 0 : index;
  });

  constructor() {
    // Polled every 2 seconds (§10.3). A long-lived socket would be more elegant and buys
    // nothing at this cadence.
    this.poll = timer(0, 2000)
      .pipe(switchMap(() => this.api.getStatus(this.patientId())))
      .subscribe({
        next: status => {
          this.status.set(status);
          this.error.set(null);

          if (status.isComplete) {
            this.poll?.unsubscribe();
            this.router.navigate(['/patients', this.patientId()]);
          }
        },
        error: (err: Error) => this.error.set(err.message)
      });
  }

  ngOnDestroy(): void {
    this.poll?.unsubscribe();
  }

  protected stageClasses(index: number): string {
    const current = this.stageIndex();
    if (current > index) return 'border-brand-600 bg-brand-600 text-white';
    if (current === index) return 'border-brand-600 text-brand-700';
    return 'border-slate-200 text-slate-400';
  }

  protected documentLabel(status: string): string {
    switch (status) {
      case 'Extracted':
        return 'Read';
      case 'Cached':
        return 'Read (cached)';
      case 'Failed':
        return 'Could not read';
      case 'Extracting':
        return 'Reading…';
      default:
        return 'Waiting';
    }
  }

  protected documentClasses(status: string): string {
    switch (status) {
      case 'Extracted':
      case 'Cached':
        return 'text-emerald-700';
      case 'Failed':
        return 'text-red-700';
      default:
        return 'text-slate-400';
    }
  }
}
