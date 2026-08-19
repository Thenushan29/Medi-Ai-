import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';

import { ApiService } from '../../core/api.service';
import { LanguageService } from '../../core/language.service';
import { ConfidenceBadgeComponent } from '../../shared/confidence-badge.component';
import type {
  Alert,
  AlertSeverity,
  LabTrend,
  MedicationGroup,
  PatientDetail,
  VerificationStatus
} from '../../core/models';

/**
 * Doctor / pharmacist one-pager (Round 2 R2-1.1). Reuses persisted alerts — no extra model call.
 * Printable. Never diagnoses; never recommends starting, stopping, or changing a drug (§5.3).
 */
@Component({
  selector: 'mt-summary-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, RouterLink, ConfidenceBadgeComponent],
  template: `
    <section class="mx-auto max-w-3xl px-6 py-10">
      @if (patient(); as p) {
        <div class="print:hidden mb-6 flex flex-wrap items-center justify-between gap-3">
          <a [routerLink]="['/patients', p.id]" class="text-xs text-brand-700 hover:underline">
            {{ language.pick('← Back to dashboard', '← டாஷ்போர்டுக்குத் திரும்பு') }}
          </a>
          <button
            type="button"
            class="rounded-lg bg-brand-600 px-4 py-2 text-sm font-medium text-white"
            (click)="print()"
          >
            {{ language.pick('Print / save as PDF', 'அச்சிடு / PDF ஆகச் சேமி') }}
          </button>
        </div>

        <header class="border-b border-slate-200 pb-6">
          <p class="text-xs font-medium uppercase tracking-wide text-slate-500">
            {{ language.pick('For a doctor or pharmacist', 'மருத்துவர் அல்லது மருந்தாளுநருக்கு') }}
          </p>
          <h1 class="mt-1 text-2xl font-semibold tracking-tight text-slate-900">{{ p.displayName }}</h1>
          <p class="mt-2 text-sm text-slate-600">
            {{ p.documentCount }}
            {{ language.pick(
              p.documentCount === 1 ? 'document' : 'documents',
              'ஆவணங்கள்'
            ) }}
            @if (p.earliestDocumentDate && p.latestDocumentDate) {
              · {{ p.earliestDocumentDate | date: 'mediumDate' }}
              – {{ p.latestDocumentDate | date: 'mediumDate' }}
            }
          </p>
          <p class="mt-3 rounded-lg border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-900">
            {{ language.pick(
              'MediTrail is an information tool, not a diagnosis. It never recommends starting, stopping, or changing a medication. Confirm every finding against the source images.',
              'MediTrail ஒரு தகவல் கருவி மட்டுமே. இது நோயறிதல் அல்ல. மருந்தைத் தொடங்கவோ நிறுத்தவோ மாற்றவோ இது பரிந்துரைக்காது. ஒவ்வொரு கண்டுபிடிப்பையும் மூலப் படத்துடன் உறுதிப்படுத்துங்கள்.'
            ) }}
          </p>
        </header>

        <section class="mt-8">
          <h2 class="text-sm font-semibold uppercase tracking-wide text-slate-500">
            {{ language.pick('Findings', 'கண்டுபிடிப்புகள்') }}
          </h2>

          @if (alerts().length === 0) {
            <p class="mt-4 rounded-xl border border-dashed border-slate-300 px-5 py-8 text-sm text-slate-500">
              {{ language.pick(
                'No risks were flagged in the documents that could be read. That only covers what was actually extracted.',
                'படிக்கப்பட்ட ஆவணங்களில் ஆபத்துகள் காட்டப்படவில்லை. இது பிரித்தெடுக்கப்பட்டவற்றை மட்டுமே உள்ளடக்கியது.'
              ) }}
            </p>
          } @else {
            <ol class="mt-4 space-y-4">
              @for (alert of alerts(); track alert.id) {
                <li class="rounded-xl border border-slate-200 bg-white p-5">
                  <div class="flex flex-wrap items-start justify-between gap-3">
                    <div>
                      <span
                        class="inline-flex rounded-full px-2 py-0.5 text-xs font-semibold"
                        [class]="chipFor(alert.severity)"
                      >
                        {{ labelFor(alert.severity) }}
                      </span>
                      <h3 class="mt-2 font-medium text-slate-900">{{ alert.title }}</h3>
                      @if (alert.involvedGenerics.length > 0) {
                        <p class="mt-1 text-xs text-slate-500">{{ alert.involvedGenerics.join(' · ') }}</p>
                      }
                    </div>
                    <mt-confidence [score]="alert.confidence" />
                  </div>

                  <p class="mt-3 text-sm leading-relaxed text-slate-700">
                    {{ language.pick(alert.explanationEn, alert.explanationTa) }}
                  </p>

                  @if (action(alert); as suggested) {
                    <p class="mt-2 text-sm font-medium text-slate-900">{{ suggested }}</p>
                  }

                  <p class="mt-3 text-xs text-slate-500">{{ verificationLabel(alert.verificationStatus) }}</p>

                  @if (alert.evidence.length > 0) {
                    <p class="mt-2 text-xs text-slate-500">
                      {{ language.pick('Source:', 'மூலம்:') }}
                      {{ alert.evidence.map(e => e.fileName).join(', ') }}
                    </p>
                  }

                  @if (alert.requiresProfessionalConsult) {
                    <p class="mt-3 text-xs font-medium text-red-800">
                      {{ language.pick(
                        'Show this to a doctor or pharmacist before the next dose. Do not change anything from this page.',
                        'அடுத்த மருந்துக்கு முன் மருத்துவர் அல்லது மருந்தாளுநரிடம் காட்டுங்கள். இந்தப் பக்கத்திலிருந்து எதையும் மாற்ற வேண்டாம்.'
                      ) }}
                    </p>
                  }
                </li>
              }
            </ol>
          }
        </section>

        <section class="mt-10">
          <h2 class="text-sm font-semibold uppercase tracking-wide text-slate-500">
            {{ language.pick('Medications', 'மருந்துகள்') }}
          </h2>
          @if (medications().length === 0) {
            <p class="mt-4 text-sm text-slate-500">
              {{ language.pick('No medications were extracted.', 'மருந்துகள் பிரித்தெடுக்கப்படவில்லை.') }}
            </p>
          } @else {
            <ul class="mt-4 divide-y divide-slate-100 rounded-xl border border-slate-200 bg-white">
              @for (group of medications(); track group.displayName) {
                <li class="px-5 py-3">
                  <p class="text-sm font-medium text-slate-900">
                    {{ group.displayName }}
                    @if (group.therapeuticClass) {
                      <span class="font-normal text-slate-400">· {{ group.therapeuticClass }}</span>
                    }
                    @if (group.hasConflict) {
                      <span class="ml-2 rounded bg-amber-100 px-1.5 py-0.5 text-xs font-medium text-amber-900">
                        {{ language.pick('flagged', 'குறிக்கப்பட்டது') }}
                      </span>
                    }
                  </p>
                  <p class="mt-1 text-xs text-slate-500">
                    {{ group.rows.length }} prescription{{ group.rows.length === 1 ? '' : 's' }}
                  </p>
                </li>
              }
            </ul>
          }
        </section>

        <section class="mt-10">
          <h2 class="text-sm font-semibold uppercase tracking-wide text-slate-500">
            {{ language.pick('Lab trends', 'ஆய்வகப் போக்குகள்') }}
          </h2>
          @if (labTrends().length === 0) {
            <p class="mt-4 text-sm text-slate-500">
              {{ language.pick(
                'No numeric lab series in these documents. MediTrail will not invent a trend.',
                'எண்வரிசை ஆய்வக முடிவுகள் இல்லை. MediTrail ஒரு போக்கை உருவாக்காது.'
              ) }}
            </p>
          } @else {
            <ul class="mt-4 space-y-3">
              @for (trend of labTrends(); track trend.testKey) {
                <li class="rounded-xl border border-slate-200 bg-white px-5 py-4">
                  <p class="text-sm font-medium text-slate-900">
                    {{ trend.displayName }}
                    @if (trend.unit) {
                      <span class="font-normal text-slate-400">({{ trend.unit }})</span>
                    }
                  </p>
                  <p class="mt-1 text-xs text-slate-500">
                    {{ trend.points.length }} reading{{ trend.points.length === 1 ? '' : 's' }}
                    · {{ trend.direction }}
                    @if (trend.latestOutOfRange) {
                      · latest outside printed range
                    }
                  </p>
                  <p class="mt-2 text-sm text-slate-700">
                    {{ language.pick(trend.explanationEn, trend.explanationTa) }}
                  </p>
                </li>
              }
            </ul>
          }
        </section>
      } @else if (error(); as message) {
        <p class="rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-800">{{ message }}</p>
      } @else {
        <div class="h-40 animate-pulse rounded-xl bg-slate-100" aria-busy="true"></div>
      }
    </section>
  `
})
export class SummaryPageComponent {
  private readonly api = inject(ApiService);
  protected readonly language = inject(LanguageService);

  readonly patientId = input.required<string>();

  protected readonly patient = signal<PatientDetail | null>(null);
  protected readonly alerts = signal<Alert[]>([]);
  protected readonly medications = signal<MedicationGroup[]>([]);
  protected readonly labTrends = signal<LabTrend[]>([]);
  protected readonly error = signal<string | null>(null);

  constructor() {
    queueMicrotask(() => this.load());
  }

  protected print(): void {
    window.print();
  }

  protected action(alert: Alert): string {
    return this.language.pick(alert.suggestedActionEn, alert.suggestedActionTa);
  }

  protected labelFor(severity: AlertSeverity): string {
    const ta = this.language.current() === 'ta';
    switch (severity) {
      case 'Red':
        return ta ? 'ஆபத்து' : 'Risk';
      case 'Amber':
        return ta ? 'சரிபார்க்க' : 'Check';
      default:
        return ta ? 'தகவல்' : 'Info';
    }
  }

  protected chipFor(severity: AlertSeverity): string {
    switch (severity) {
      case 'Red':
        return 'bg-red-100 text-red-800';
      case 'Amber':
        return 'bg-amber-100 text-amber-900';
      default:
        return 'bg-blue-100 text-blue-800';
    }
  }

  protected verificationLabel(status: VerificationStatus): string {
    const ta = this.language.current() === 'ta';
    switch (status) {
      case 'Confirmed':
        return ta
          ? 'FDA மருந்து அட்டையுடன் உறுதிசெய்யப்பட்டது.'
          : 'Verified against FDA drug label data.';
      case 'NotFound':
        return ta
          ? 'FDA அட்டையில் இது இல்லை. மருந்தாளுநரிடம் உறுதிப்படுத்துங்கள்.'
          : 'FDA label does not mention this. Verify with a pharmacist.';
      case 'Unverified':
        return ta
          ? 'FDA தரவுடன் சரிபார்க்க முடியவில்லை. மருந்தாளுநரிடம் உறுதிப்படுத்துங்கள்.'
          : 'Could not be checked against FDA data. Verify with a pharmacist.';
      case 'NotApplicable':
        return ta
          ? 'உங்கள் ஆவணங்களை ஒப்பிட்டுக் கண்டுபிடிக்கப்பட்டது — வெளிச் சரிபார்ப்பு பொருந்தாது.'
          : 'Found by comparing these documents — no external check applies.';
      default:
        return ta ? 'சரிபார்ப்பு நிலுவையில்.' : 'Verification pending.';
    }
  }

  private load(): void {
    const id = this.patientId();

    this.api.getPatient(id).subscribe({
      next: patient => this.patient.set(patient),
      error: (err: Error) => this.error.set(err.message)
    });
    this.api.getAlerts(id).subscribe({ next: alerts => this.alerts.set(alerts) });
    this.api.getMedications(id).subscribe({ next: groups => this.medications.set(groups) });
    this.api.getLabTrends(id).subscribe({ next: trends => this.labTrends.set(trends) });
  }
}
