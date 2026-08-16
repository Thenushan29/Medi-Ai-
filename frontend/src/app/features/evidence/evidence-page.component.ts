import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';

import { ApiService } from '../../core/api.service';
import { ConfidenceBadgeComponent } from '../../shared/confidence-badge.component';
import type { Alert, Allergy, DocumentDetail, Medication } from '../../core/models';

/**
 * Evidence viewer (§10.9). Split view: the source document on one side, what was read from it on
 * the other, with the original printed text shown beside every normalized value.
 *
 * This is the screen that answers "how do you know the AI didn't make this up?" (§4.4).
 *
 * Arriving from an alert, the `alert` query parameter names the finding, and the rows that finding
 * rests on are marked in the extracted-data pane. **Nothing is drawn on the image.** Extraction
 * records the printed text an item was read from, not its position on the page — there are no
 * bounding boxes anywhere in the pipeline, and §26 puts word-level coordinates in the post-Round-1
 * OCR work. A box positioned by guesswork would be the one thing this screen exists to rule out.
 */
@Component({
  selector: 'mt-evidence-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, RouterLink, ConfidenceBadgeComponent],
  template: `
    <section class="mx-auto max-w-7xl px-6 py-8">
      @if (document(); as d) {
        <a [routerLink]="['/patients', d.patientId]" class="text-xs text-brand-700 hover:underline">
          ← Back to timeline
        </a>

        @if (citedAlert(); as alert) {
          <div
            class="mt-4 rounded-xl border px-5 py-4"
            [class]="
              alert.severity === 'Red'
                ? 'border-red-200 bg-red-50'
                : alert.severity === 'Amber'
                  ? 'border-amber-200 bg-amber-50'
                  : 'border-slate-200 bg-slate-50'
            "
          >
            <p class="text-xs font-medium uppercase tracking-wide text-slate-500">
              Showing evidence for
            </p>
            <p class="mt-1 text-sm font-semibold text-slate-900">{{ alert.title }}</p>

            @if (alert.explanationEn) {
              <p class="mt-1 text-xs text-slate-700">{{ alert.explanationEn }}</p>
            }

            <p class="mt-3 text-xs text-slate-500">
              @if (citedCount() > 0) {
                {{ citedCount() }} item{{ citedCount() === 1 ? '' : 's' }} read from this document
                {{ citedCount() === 1 ? 'is' : 'are' }} marked below.
              } @else {
                This finding cites the document as a whole rather than a single line on it.
              }
              The image itself is not marked up — MediTrail records the text an item was read from,
              not where it sits on the page.
            </p>
          </div>
        }

        <div class="mt-4 grid gap-6 lg:grid-cols-2">
          <!-- Source -->
          <div class="lg:sticky lg:top-6 lg:self-start">
            <div class="overflow-hidden rounded-xl border border-slate-200 bg-white">
              @if (d.contentType === 'application/pdf') {
                <iframe [src]="d.sourceUrl" class="h-[70vh] w-full" [title]="d.fileName"></iframe>
              } @else {
                <img [src]="d.sourceUrl" [alt]="'Source document: ' + d.fileName" class="w-full" />
              }
            </div>
            <p class="mt-2 truncate text-xs text-slate-500">{{ d.fileName }}</p>
          </div>

          <!-- What was read from it -->
          <div class="space-y-6">
            <div class="rounded-xl border border-slate-200 bg-white p-5">
              <div class="flex items-start justify-between gap-4">
                <div>
                  <h1 class="text-lg font-semibold text-slate-900">
                    {{ d.documentType || 'Document' }}
                  </h1>
                  <p class="mt-0.5 text-xs text-slate-500">
                    {{ d.documentDate ? (d.documentDate | date: 'mediumDate') : 'Date unreadable' }}
                    @if (d.providerName) {
                      · {{ d.providerName }}
                    }
                  </p>
                </div>
                <mt-confidence [score]="d.overallConfidence" />
              </div>

              @if (d.legibilityNotes) {
                <p class="mt-3 rounded-lg bg-slate-50 px-3 py-2 text-xs text-slate-600">
                  {{ d.legibilityNotes }}
                </p>
              }

              @if (d.status === 'Failed') {
                <p class="mt-3 rounded-lg bg-red-50 px-3 py-2 text-xs text-red-800">
                  {{ d.failureReason || 'This document could not be read.' }}
                </p>
              }

              @if (d.extractionModel) {
                <p class="mt-3 text-xs text-slate-400">Read by {{ d.extractionModel }}</p>
              }
            </div>

            @if (d.medications.length > 0) {
              <div class="rounded-xl border border-slate-200 bg-white">
                <h2 class="border-b border-slate-100 px-5 py-3 text-xs font-medium text-slate-600">
                  Medications
                </h2>
                <ul class="divide-y divide-slate-100">
                  @for (m of d.medications; track m.id) {
                    <li class="px-5 py-4" [class]="citesMedication(m) ? 'bg-brand-50/70 border-l-4 border-brand-500' : ''">
                      @if (citesMedication(m)) {
                        <p class="mb-1 text-xs font-medium text-brand-700">Cited by this finding</p>
                      }
                      <div class="flex items-start justify-between gap-4">
                        <p class="text-sm font-medium text-slate-900">
                          {{ m.brandName || m.genericName || 'Unnamed medication' }}
                          @if (m.brandName && m.genericName) {
                            <span class="font-normal text-slate-500">({{ m.genericName }})</span>
                          }
                        </p>
                        <mt-confidence [score]="m.confidence" />
                      </div>

                      <p class="mt-1 text-xs text-slate-600">
                        @if (m.strengthValue) {
                          {{ m.strengthValue }}{{ m.strengthUnit }}
                        }
                        @if (m.frequency) {
                          · {{ m.frequency }}
                        }
                        @if (m.durationDays) {
                          · {{ m.durationDays }} days
                        }
                      </p>

                      @if (m.sourceText) {
                        <p class="mt-2 border-l-2 border-slate-200 pl-3 font-mono text-xs text-slate-500">
                          {{ m.sourceText }}
                        </p>
                      }
                    </li>
                  }
                </ul>
              </div>
            }

            @if (d.labResults.length > 0) {
              <div class="rounded-xl border border-slate-200 bg-white">
                <h2 class="border-b border-slate-100 px-5 py-3 text-xs font-medium text-slate-600">
                  Lab results
                </h2>
                <ul class="divide-y divide-slate-100">
                  @for (l of d.labResults; track l.id) {
                    <li class="px-5 py-4" [class]="l.isOutOfRange ? 'bg-amber-50/50' : ''">
                      <div class="flex items-start justify-between gap-4">
                        <p class="text-sm font-medium text-slate-900">{{ l.testName || 'Unnamed test' }}</p>
                        <mt-confidence [score]="l.confidence" />
                      </div>

                      <p class="mt-1 text-xs" [class]="l.isOutOfRange ? 'text-amber-800' : 'text-slate-600'">
                        {{ l.valueNumeric ?? l.valueText ?? '—' }} {{ l.unit }}
                        @if (l.normalRangeText) {
                          <span class="text-slate-400">· normal {{ l.normalRangeText }}</span>
                        }
                        @if (l.isOutOfRange) {
                          <span class="ml-1 font-medium">⚠ outside normal range</span>
                        }
                      </p>

                      @if (l.sourceText) {
                        <p class="mt-2 border-l-2 border-slate-200 pl-3 font-mono text-xs text-slate-500">
                          {{ l.sourceText }}
                        </p>
                      }
                    </li>
                  }
                </ul>
              </div>
            }

            @if (d.allergies.length > 0) {
              <div class="rounded-xl border border-slate-200 bg-white">
                <h2 class="border-b border-slate-100 px-5 py-3 text-xs font-medium text-slate-600">
                  Allergies and warnings on this document
                </h2>
                <ul class="divide-y divide-slate-100">
                  @for (a of d.allergies; track a.id) {
                    <li class="px-5 py-4" [class]="citesAllergy(a) ? 'bg-brand-50/70 border-l-4 border-brand-500' : ''">
                      @if (citesAllergy(a)) {
                        <p class="mb-1 text-xs font-medium text-brand-700">Cited by this finding</p>
                      }
                      <div class="flex items-start justify-between gap-4">
                        <p class="text-sm text-slate-900">
                          <!-- A warning reads as the sentence printed on the document; its
                               substance column names only the drug, which "Refers to" below
                               already gives. -->
                          {{ (a.isDocumentWarning ? a.sourceText : null) || a.substance }}
                          @if (a.isDocumentWarning) {
                            <span class="ml-2 rounded bg-slate-100 px-1.5 py-0.5 text-xs text-slate-600">
                              printed warning
                            </span>
                          }
                        </p>
                        <mt-confidence [score]="a.confidence" />
                      </div>

                      @if (a.relatesTo.length > 0) {
                        <p class="mt-1 text-xs text-slate-500">Refers to: {{ a.relatesTo.join(', ') }}</p>
                      }
                      <!-- Not repeated for a warning, where it is already the line above. -->
                      @if (a.sourceText && !a.isDocumentWarning) {
                        <p class="mt-2 border-l-2 border-slate-200 pl-3 font-mono text-xs text-slate-500">
                          {{ a.sourceText }}
                        </p>
                      }
                    </li>
                  }
                </ul>
              </div>
            }

            @if (d.medications.length === 0 && d.labResults.length === 0 && d.allergies.length === 0) {
              <p class="rounded-xl border border-dashed border-slate-300 px-6 py-10 text-center text-sm text-slate-500">
                Nothing has been extracted from this document yet.
              </p>
            }
          </div>
        </div>
      } @else if (error(); as message) {
        <p class="rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-800">{{ message }}</p>
      } @else {
        <div class="h-96 animate-pulse rounded-xl bg-slate-100" aria-busy="true"></div>
      }
    </section>
  `
})
export class EvidencePageComponent {
  private readonly api = inject(ApiService);

  readonly documentId = input.required<string>();

  /** Query parameter, bound by withComponentInputBinding. Absent when opened from the timeline. */
  readonly alert = input<string>();

  protected readonly document = signal<DocumentDetail | null>(null);
  protected readonly citedAlert = signal<Alert | null>(null);
  protected readonly error = signal<string | null>(null);

  constructor() {
    queueMicrotask(() =>
      this.api.getDocument(this.documentId()).subscribe({
        next: doc => {
          this.document.set(doc);
          this.loadAlert(doc.patientId);
        },
        error: (err: Error) => this.error.set(err.message)
      })
    );
  }

  /**
   * A missing alert is not an error worth showing. The document is the point of this screen; the
   * finding is context on top of it, and losing the context must not lose the evidence.
   */
  private loadAlert(patientId: string): void {
    const id = this.alert();
    if (!id) return;

    this.api.getAlerts(patientId).subscribe({
      next: alerts => this.citedAlert.set(alerts.find(a => a.id === id) ?? null),
      error: () => this.citedAlert.set(null)
    });
  }

  protected citesMedication(medication: Medication): boolean {
    return this.cites(medication.genericName);
  }

  protected citesAllergy(allergy: Allergy): boolean {
    return allergy.relatesTo.some(substance => this.cites(substance));
  }

  protected citedCount(): number {
    const d = this.document();
    if (!d || !this.citedAlert()) return 0;

    return (
      d.medications.filter(m => this.citesMedication(m)).length +
      d.allergies.filter(a => this.citesAllergy(a)).length
    );
  }

  /**
   * Matched component-wise, not by whole string: a combination product is stored as one
   * slash-separated generic ("aspirin/codeine"), and an alert about aspirin names only the
   * component. Whole-string equality would leave the row that caused the finding unmarked.
   */
  private cites(generic: string | undefined): boolean {
    const alert = this.citedAlert();
    if (!alert || !generic) return false;

    const parts = components(generic);
    return alert.involvedGenerics.some(involved =>
      components(involved).some(part => parts.includes(part))
    );
  }
}

function components(generic: string): string[] {
  return generic
    .toLowerCase()
    .split('/')
    .map(part => part.trim())
    .filter(part => part.length > 0);
}
