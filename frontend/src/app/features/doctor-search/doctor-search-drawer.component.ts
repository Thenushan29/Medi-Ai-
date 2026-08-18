import { ChangeDetectionStrategy, Component, inject, input, output, signal } from '@angular/core';

import { ApiService } from '../../core/api.service';
import type { Alert, SpecialtyOption, SpecialtyResolution } from '../../core/models';

/**
 * Three-step nearby-clinic drawer. Step 1 confirms specialty with traceable RxClass evidence.
 * Disease-class names are drug-class information, never a statement about the patient.
 */
@Component({
  selector: 'mt-doctor-search-drawer',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="fixed inset-0 z-40 flex justify-end" role="dialog" aria-labelledby="doctor-search-title">
      <div class="flex-1 bg-slate-900/20" (click)="closed.emit()" aria-hidden="true"></div>

      <aside class="flex h-full w-full max-w-lg flex-col border-l border-slate-200 bg-white shadow-xl">
        <header class="flex items-center justify-between border-b border-slate-200 px-5 py-4">
          <div>
            <h2 id="doctor-search-title" class="font-medium text-slate-900">Find a nearby clinic</h2>
            <p class="mt-0.5 text-xs text-slate-500">
              Nearby facilities from public map data — not a referral.
            </p>
          </div>
          <button type="button" class="text-slate-400 hover:text-slate-700" (click)="closed.emit()" aria-label="Close">
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" class="h-5 w-5" aria-hidden="true">
              <path stroke-linecap="round" d="M6 6l12 12M18 6L6 18" />
            </svg>
          </button>
        </header>

        <ol class="flex border-b border-slate-100 px-5 py-3 text-xs text-slate-500">
          <li [class]="step() === 1 ? 'font-medium text-brand-700' : ''">1 Specialty</li>
          <li class="px-2 text-slate-300">·</li>
          <li [class]="step() === 2 ? 'font-medium text-brand-700' : ''">2 Location</li>
          <li class="px-2 text-slate-300">·</li>
          <li [class]="step() === 3 ? 'font-medium text-brand-700' : ''">3 Results</li>
        </ol>

        <div class="flex-1 overflow-y-auto px-5 py-5">
          @if (error(); as message) {
            <p class="rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-800">{{ message }}</p>
          } @else if (step() === 1) {
            @if (suggestion(); as specialty) {
              <p class="text-sm text-slate-600">For this alert we suggest:</p>
              <p class="mt-1 text-2xl font-semibold tracking-tight text-slate-900">
                {{ specialty.label.toUpperCase() }}
              </p>
              <p class="mt-2 text-sm leading-relaxed text-slate-600">{{ specialty.reason }}</p>

              <button
                type="button"
                class="mt-4 inline-flex items-center gap-1 text-sm font-medium text-brand-700"
                (click)="whyOpen.set(!whyOpen())"
                [attr.aria-expanded]="whyOpen()"
              >
                Why this specialty?
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" class="h-4 w-4" [class.rotate-180]="whyOpen()" aria-hidden="true">
                  <path stroke-linecap="round" stroke-linejoin="round" d="M6 9l6 6 6-6" />
                </svg>
              </button>

              @if (whyOpen()) {
                <div class="mt-3 rounded-xl border border-slate-200 bg-slate-50 px-4 py-3">
                  <p class="text-xs leading-relaxed text-slate-500">
                    Drug-class information from NLM RxClass (MED-RT, may_treat). This is not a
                    statement about the patient.
                  </p>
                  @if (specialty.evidence.length === 0) {
                    <p class="mt-2 text-xs text-slate-500">No linked class pages for this suggestion.</p>
                  } @else {
                    <div class="mt-3 flex flex-wrap gap-2">
                      @for (chip of specialty.evidence; track chip.sourceId ?? chip.label) {
                        @if (chip.sourceUrl) {
                          <a
                            [href]="chip.sourceUrl"
                            target="_blank"
                            rel="noopener noreferrer"
                            class="inline-flex items-center gap-1 rounded-full border border-slate-200 bg-white px-3 py-1 text-xs text-brand-700 hover:border-brand-500"
                          >
                            {{ chip.label }}
                            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" class="h-3 w-3" aria-hidden="true">
                              <path stroke-linecap="round" stroke-linejoin="round" d="M14 5h5v5M19 5l-7 7M10 5H6a1 1 0 0 0-1 1v12a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1v-4" />
                            </svg>
                          </a>
                        } @else {
                          <span class="rounded-full border border-slate-200 bg-white px-3 py-1 text-xs text-slate-600">
                            {{ chip.label }}
                          </span>
                        }
                      }
                    </div>
                  }
                </div>
              }

              <label class="mt-6 block text-sm font-medium text-slate-700">
                Change specialty ▾
                <select
                  class="mt-1 w-full rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-800"
                  [value]="specialty.code"
                  (change)="onSpecialtyChange($event)"
                >
                  @for (option of specialties(); track option.code) {
                    <option [value]="option.code">{{ option.label }}</option>
                  }
                </select>
              </label>
            } @else {
              <div class="space-y-3" aria-busy="true">
                <div class="h-4 w-40 animate-pulse rounded bg-slate-100"></div>
                <div class="h-8 w-56 animate-pulse rounded bg-slate-100"></div>
                <div class="h-16 w-full animate-pulse rounded bg-slate-100"></div>
              </div>
            }
          } @else {
            <p class="text-sm text-slate-600">
              Next we will ask for a town and when you can go, then show nearby facilities from
              public map data.
            </p>
          }
        </div>

        <footer class="border-t border-slate-200 px-5 py-4">
          @if (step() === 1) {
            <button
              type="button"
              class="w-full rounded-lg bg-brand-600 px-4 py-2 text-sm font-medium text-white disabled:opacity-40"
              [disabled]="!suggestion()"
              (click)="step.set(2)"
            >
              Continue
            </button>
          } @else {
            <button type="button" class="text-sm text-brand-700" (click)="step.set(1)">Back to specialty</button>
          }
        </footer>
      </aside>
    </div>
  `
})
export class DoctorSearchDrawerComponent {
  private readonly api = inject(ApiService);

  readonly patientId = input.required<string>();
  readonly alert = input<Alert | null>(null);
  readonly closed = output<void>();

  protected readonly step = signal(1);
  protected readonly whyOpen = signal(false);
  protected readonly specialties = signal<SpecialtyOption[]>([]);
  protected readonly suggestion = signal<SpecialtyResolution | null>(null);
  protected readonly error = signal<string | null>(null);

  constructor() {
    queueMicrotask(() => this.load());
  }

  protected onSpecialtyChange(event: Event): void {
    this.loadSuggestion((event.target as HTMLSelectElement).value);
  }

  private load(): void {
    this.api.getSpecialties().subscribe({
      next: list => this.specialties.set(list),
      error: (err: Error) => this.error.set(err.message)
    });
    this.loadSuggestion();
  }

  private loadSuggestion(override?: string): void {
    this.api.suggestSpecialty(this.patientId(), this.alert()?.id, override).subscribe({
      next: value => {
        this.suggestion.set(value);
        this.error.set(null);
      },
      error: (err: Error) => this.error.set(err.message)
    });
  }
}
