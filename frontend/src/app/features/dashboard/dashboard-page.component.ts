import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';

import { ApiService } from '../../core/api.service';
import { ConfidenceBadgeComponent } from '../../shared/confidence-badge.component';
import { AlertsViewComponent } from './alerts-view.component';
import { ChatDrawerComponent } from './chat-drawer.component';
import { DoctorSearchDrawerComponent } from '../doctor-search/doctor-search-drawer.component';
import { LabTrendsViewComponent } from './lab-trends-view.component';
import { MedicationsViewComponent } from './medications-view.component';
import type { Alert, LabTrend, MedicationGroup, PatientDetail, TimelineEntry } from '../../core/models';

type Tab = 'timeline' | 'medications' | 'labs' | 'alerts';

/** Dashboard shell (§10.4) with all four views and the chat drawer. */
@Component({
  selector: 'mt-dashboard-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe,
    RouterLink,
    ConfidenceBadgeComponent,
    AlertsViewComponent,
    MedicationsViewComponent,
    LabTrendsViewComponent,
    ChatDrawerComponent,
    DoctorSearchDrawerComponent
  ],
  template: `
    <section class="mx-auto max-w-5xl px-6 py-10">
      @if (patient(); as p) {
        <header class="border-b border-slate-200 pb-6">
          <!-- Name left, add-documents right (FR-2.9). items-start keeps the two aligned on a
               narrow screen where the name wraps, and leaves the chips below flush left. -->
          <div class="flex items-start justify-between gap-4">
            <h1 class="text-2xl font-semibold tracking-tight text-slate-900">{{ p.displayName }}</h1>

            <!-- Secondary, not primary: the point of this screen is reading the findings. -->
            <a
              [routerLink]="['/patients', patientId(), 'upload']"
              aria-label="Add more documents for this patient"
              class="shrink-0 rounded-full border border-slate-200 bg-white px-3 py-1 text-xs text-slate-600 hover:border-brand-500 hover:text-brand-700"
            >
              + Add documents
            </a>
          </div>

          <div class="mt-4 flex flex-wrap gap-2">
            <span class="rounded-full border border-slate-200 bg-white px-3 py-1 text-xs text-slate-600">
              {{ p.documentCount }} document{{ p.documentCount === 1 ? '' : 's' }}
            </span>

            @if (timeSpan(); as span) {
              <span class="rounded-full border border-slate-200 bg-white px-3 py-1 text-xs text-slate-600">
                {{ span }}
              </span>
            }

            @if (p.redAlertCount > 0) {
              <span class="rounded-full border border-red-200 bg-red-50 px-3 py-1 text-xs font-medium text-red-800">
                ⚠ {{ p.redAlertCount }} risk{{ p.redAlertCount === 1 ? '' : 's' }}
              </span>
            }
            @if (p.amberAlertCount > 0) {
              <span class="rounded-full border border-amber-200 bg-amber-50 px-3 py-1 text-xs font-medium text-amber-800">
                ! {{ p.amberAlertCount }} to check
              </span>
            }
            <!-- The info tier gets its own chip rather than being folded into "to check" or left
                 out. Without it the header under-counts the Alerts tab, which lists every
                 severity — a header that disagrees with the tab beside it reads as a miscount. -->
            @if (p.infoAlertCount > 0) {
              <span class="rounded-full border border-blue-200 bg-blue-50 px-3 py-1 text-xs font-medium text-blue-800">
                i {{ p.infoAlertCount }} for information
              </span>
            }
            @if (p.failedDocumentCount > 0) {
              <span class="rounded-full border border-slate-200 bg-slate-50 px-3 py-1 text-xs text-slate-600">
                {{ p.failedDocumentCount }} unreadable
              </span>
            }
          </div>

          @if (p.statusMessage) {
            <p class="mt-4 rounded-lg border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-900">
              {{ p.statusMessage }}
            </p>
          }
        </header>

        <nav class="mt-6 flex gap-1 border-b border-slate-200" role="tablist">
          @for (t of tabs; track t.key) {
            <button
              type="button"
              role="tab"
              [attr.aria-selected]="tab() === t.key"
              class="-mb-px border-b-2 px-4 py-2 text-sm"
              [class]="
                tab() === t.key
                  ? 'border-brand-600 font-medium text-brand-700'
                  : 'border-transparent text-slate-500 hover:text-slate-800'
              "
              (click)="tab.set(t.key)"
            >
              {{ t.label }}
              @if (countFor(t.key); as count) {
                <span
                  class="ml-1.5 rounded-full px-1.5 py-0.5 text-xs"
                  [class]="t.key === 'alerts' && p.redAlertCount > 0
                    ? 'bg-red-100 text-red-800'
                    : 'bg-slate-100 text-slate-600'"
                >
                  {{ count }}
                </span>
              }
            </button>
          }
        </nav>

        <div class="py-8">
          @switch (tab()) {
            @case ('timeline') {
              @if (timeline().length === 0) {
                <p class="rounded-xl border border-dashed border-slate-300 px-6 py-12 text-center text-sm text-slate-500">
                  No documents yet.
                  <a [routerLink]="['/patients', p.id, 'upload']" class="text-brand-700 underline">Upload some</a>
                  to get started.
                </p>
              } @else {
                <ol class="space-y-3">
                  @for (entry of timeline(); track entry.documentId) {
                    <li class="relative">
                      <!-- Outside the card link, so opening the evidence and removing the
                           document are not the same tap. -->
                      <button
                        type="button"
                        class="absolute right-3 top-3 z-10 rounded p-1 text-xs text-slate-400 hover:text-red-700"
                        [attr.aria-label]="'Remove ' + entry.fileName"
                        (click)="confirmRemoveDocument(entry)"
                      >
                        Remove
                      </button>

                      <a
                        [routerLink]="['/documents', entry.documentId]"
                        class="block rounded-xl border border-slate-200 bg-white px-5 py-4 pr-20 hover:border-brand-500"
                      >
                        <div class="flex items-start justify-between gap-4">
                          <div class="min-w-0">
                            <p class="text-sm font-medium text-slate-900">
                              {{ entry.documentDate ? (entry.documentDate | date: 'mediumDate') : 'Date unreadable' }}
                              @if (entry.visitLabel) {
                                <span class="ml-2 font-normal text-slate-400">{{ entry.visitLabel }}</span>
                              }
                            </p>
                            <p class="mt-0.5 truncate text-xs text-slate-500">
                              {{ entry.documentType || 'Document' }}
                              @if (entry.providerName) {
                                · {{ entry.providerName }}
                              }
                              @if (entry.providerFacility) {
                                · {{ entry.providerFacility }}
                              }
                            </p>
                          </div>
                          <div class="flex shrink-0 items-center gap-2">
                            <!-- Slate, not a severity colour: a reused extraction is information
                                 about how the file was processed, not a risk (FR-2.6). Without it
                                 two identical cards look like the system missed the duplicate. -->
                            @if (entry.status === 'Cached') {
                              <span
                                class="rounded-full border border-slate-200 bg-slate-50 px-3 py-1 text-xs text-slate-600"
                                title="This file is byte-identical to one already uploaded, so the earlier extraction was reused."
                              >
                                Same file — reused, no AI call
                              </span>
                            }
                            <mt-confidence [score]="entry.overallConfidence" />
                          </div>
                        </div>

                        @if (entry.status === 'Failed') {
                          <p class="mt-3 rounded-lg bg-red-50 px-3 py-2 text-xs text-red-800">
                            {{ entry.failureReason || 'This document could not be read.' }}
                          </p>
                        } @else {
                          <!-- What the visit was for, as written on the page. Worded "recorded on
                               this document" so it reads as transcription, which is all it is —
                               MediTrail never states a diagnosis of its own (§17.1). -->
                          @if (entry.diagnoses.length > 0) {
                            <p class="mt-3 text-xs text-slate-600">
                              <span class="text-slate-400">Recorded on this document:</span>
                              @for (diagnosis of entry.diagnoses; track diagnosis) {
                                <span class="ml-1 rounded bg-slate-100 px-1.5 py-0.5 font-medium">{{ diagnosis }}</span>
                              }
                            </p>
                          }

                          <div class="mt-3 flex flex-wrap gap-3 text-xs text-slate-500">
                            <span>{{ entry.medicationCount }} medication{{ entry.medicationCount === 1 ? '' : 's' }}</span>
                            <span>{{ entry.labResultCount }} lab result{{ entry.labResultCount === 1 ? '' : 's' }}</span>
                            @if (entry.outOfRangeCount > 0) {
                              <span class="text-amber-700">{{ entry.outOfRangeCount }} out of range</span>
                            }
                            @if (entry.warningCount > 0) {
                              <span class="text-amber-700">{{ entry.warningCount }} warning on document</span>
                            }
                          </div>
                        }

                        @if (entry.legibilityNotes) {
                          <p class="mt-2 text-xs italic text-slate-400">{{ entry.legibilityNotes }}</p>
                        }
                      </a>
                    </li>
                  }
                </ol>
              }
            }
            @case ('alerts') {
              <mt-alerts-view [alerts]="alerts()" (findDoctor)="openDoctorSearch($event)" />
            }
            @case ('medications') {
              <mt-medications-view [groups]="medications()" />
            }
            @case ('labs') {
              <mt-lab-trends-view [trends]="labTrends()" />
            }
          }
        </div>

        <!-- Chat drawer trigger. Kept out of the tab strip: it is available from every view. -->
        <button
          type="button"
          class="fixed bottom-6 right-6 z-30 rounded-full bg-brand-600 px-5 py-3 text-sm font-medium text-white shadow-lg"
          (click)="chatOpen.set(true)"
        >
          Ask about your records
        </button>

        @if (chatOpen()) {
          <mt-chat-drawer
            [patientId]="p.id"
            [ready]="p.status === 'Ready' || p.status === 'Failed'"
            [documents]="timeline()"
            (closed)="chatOpen.set(false)"
          />
        }

        @if (doctorAlert(); as alert) {
          <mt-doctor-search-drawer
            [patientId]="p.id"
            [alert]="alert"
            (closed)="doctorAlert.set(null)"
          />
        }
      } @else if (error(); as message) {
        <p class="rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-800">{{ message }}</p>
      } @else {
        <div class="h-40 animate-pulse rounded-xl bg-slate-100" aria-busy="true"></div>
      }
    </section>
  `
})
export class DashboardPageComponent {
  private readonly api = inject(ApiService);

  readonly patientId = input.required<string>();

  protected openDoctorSearch(alert: Alert): void {
    this.chatOpen.set(false);
    this.doctorAlert.set(alert);
  }

  /**
   * Removes one document — a page uploaded by mistake, or one that turned out to belong to
   * someone else. The backend re-runs the analysis, so the whole dashboard is reloaded rather
   * than just the timeline row: alerts and medications derived from this document change too.
   */
  protected confirmRemoveDocument(entry: TimelineEntry): void {
    const confirmed = confirm(
      `Remove ${entry.fileName}?\n\n` +
        'The file and everything read from it are deleted, and any finding based on it is ' +
        'recalculated. This cannot be undone.'
    );

    if (!confirmed) return;

    this.api.deleteDocument(entry.documentId).subscribe({
      next: () => this.load(),
      error: (err: Error) => this.error.set(err.message)
    });
  }

  /** Count badge per tab. Returns 0 for Timeline, which needs no count. */
  protected countFor(tab: Tab): number {
    switch (tab) {
      case 'alerts':
        return this.alerts().length;
      case 'medications':
        return this.medications().length;
      case 'labs':
        return this.labTrends().length;
      default:
        return 0;
    }
  }

  protected readonly tabs: { key: Tab; label: string }[] = [
    { key: 'timeline', label: 'Timeline' },
    { key: 'medications', label: 'Medications' },
    { key: 'labs', label: 'Lab Trends' },
    { key: 'alerts', label: 'Alerts' }
  ];

  protected readonly tab = signal<Tab>('timeline');
  protected readonly patient = signal<PatientDetail | null>(null);
  protected readonly timeline = signal<TimelineEntry[]>([]);
  protected readonly alerts = signal<Alert[]>([]);
  protected readonly medications = signal<MedicationGroup[]>([]);
  protected readonly labTrends = signal<LabTrend[]>([]);
  protected readonly chatOpen = signal(false);
  protected readonly doctorAlert = signal<Alert | null>(null);
  protected readonly error = signal<string | null>(null);

  /** "2021 – 2024", or a single year when everything falls in one (§10.4 header chips). */
  protected readonly timeSpan = computed(() => {
    const p = this.patient();
    if (!p?.earliestDocumentDate || !p.latestDocumentDate) return null;

    const from = p.earliestDocumentDate.slice(0, 4);
    const to = p.latestDocumentDate.slice(0, 4);
    return from === to ? from : `${from} – ${to}`;
  });

  constructor() {
    queueMicrotask(() => this.load());
  }

  /**
   * Each view loads independently. One failing endpoint leaves the others usable — a broken
   * lab-trend call should not blank out the alerts someone came here to read.
   */
  private load(): void {
    const id = this.patientId();

    this.api.getPatient(id).subscribe({
      next: patient => this.patient.set(patient),
      error: (err: Error) => this.error.set(err.message)
    });

    this.api.getTimeline(id).subscribe({
      next: entries => this.timeline.set(entries),
      error: (err: Error) => this.error.set(err.message)
    });

    this.api.getAlerts(id).subscribe({ next: alerts => this.alerts.set(alerts) });
    this.api.getMedications(id).subscribe({ next: groups => this.medications.set(groups) });
    this.api.getLabTrends(id).subscribe({ next: trends => this.labTrends.set(trends) });
  }
}
