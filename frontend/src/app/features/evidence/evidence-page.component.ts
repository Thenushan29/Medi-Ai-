import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';

import { ApiService } from '../../core/api.service';
import { ConfidenceBadgeComponent } from '../../shared/confidence-badge.component';
import type { DocumentDetail } from '../../core/models';

/**
 * Evidence viewer (§10.9). Split view: the source document on one side, what was read from it on
 * the other, with the original printed text shown beside every normalized value.
 *
 * This is the screen that answers "how do you know the AI didn't make this up?" (§4.4).
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
                    <li class="px-5 py-4">
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
                    <li class="px-5 py-4">
                      <div class="flex items-start justify-between gap-4">
                        <p class="text-sm text-slate-900">
                          {{ a.substance }}
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
                      @if (a.sourceText) {
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

  protected readonly document = signal<DocumentDetail | null>(null);
  protected readonly error = signal<string | null>(null);

  constructor() {
    queueMicrotask(() =>
      this.api.getDocument(this.documentId()).subscribe({
        next: doc => this.document.set(doc),
        error: (err: Error) => this.error.set(err.message)
      })
    );
  }
}
