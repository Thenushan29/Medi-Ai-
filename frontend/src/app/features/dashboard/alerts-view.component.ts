import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';

import { LanguageService } from '../../core/language.service';
import { ConfidenceBadgeComponent } from '../../shared/confidence-badge.component';
import type { Alert, AlertSeverity, VerificationStatus } from '../../core/models';

/**
 * Alerts view (§10.8). Severity-sorted cards, each carrying its explanation, confidence,
 * verification badge and a link to every source document.
 *
 * Severity is never signalled by colour alone — an icon and a word accompany it (§15 accessibility).
 */
@Component({
  selector: 'mt-alerts-view',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, ConfidenceBadgeComponent],
  template: `
    @if (alerts().length === 0) {
      <p class="rounded-xl border border-dashed border-slate-300 px-6 py-12 text-center text-sm text-slate-500">
        No risks were found across these documents. That is a good result — but it only covers what
        could actually be read.
      </p>
    } @else {
      <ul class="space-y-4">
        @for (alert of alerts(); track alert.id) {
          <li class="overflow-hidden rounded-xl border bg-white" [class]="borderFor(alert.severity)">
            <div class="flex items-start justify-between gap-4 px-5 py-4">
              <div class="min-w-0">
                <div class="flex items-center gap-2">
                  <span
                    class="inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-semibold"
                    [class]="chipFor(alert.severity)"
                  >
                    <span aria-hidden="true">{{ glyphFor(alert.severity) }}</span>
                    {{ labelFor(alert.severity) }}
                  </span>

                  @if (alert.detectedBy === 'llm') {
                    <span class="rounded bg-slate-100 px-1.5 py-0.5 text-xs text-slate-600">AI-flagged</span>
                  }
                </div>

                <h3 class="mt-2 font-medium text-slate-900">{{ alert.title }}</h3>

                @if (alert.involvedGenerics.length > 0) {
                  <p class="mt-1 text-xs text-slate-500">{{ alert.involvedGenerics.join(' · ') }}</p>
                }
              </div>

              <mt-confidence [score]="alert.confidence" />
            </div>

            <div class="border-t border-slate-100 px-5 py-4">
              <p class="text-sm leading-relaxed text-slate-700">
                {{ language.pick(alert.explanationEn, alert.explanationTa) }}
              </p>

              @if (language.current() === 'ta' && !alert.explanationTa) {
                <p class="mt-1 text-xs italic text-slate-400">
                  தமிழ் விளக்கம் இன்னும் கிடைக்கவில்லை — ஆங்கிலம் காட்டப்படுகிறது.
                </p>
              }

              @if (action(alert); as suggested) {
                <p class="mt-3 text-sm font-medium text-slate-900">{{ suggested }}</p>
              }
            </div>

            <!-- Verification: what an independent source said, including when it said nothing (FR-5.7). -->
            <div class="border-t border-slate-100 bg-slate-50/60 px-5 py-3">
              <p class="text-xs" [class]="verificationClass(alert.verificationStatus)">
                {{ verificationLabel(alert.verificationStatus) }}
              </p>

              @if (alert.verificationExcerpt) {
                <blockquote class="mt-2 border-l-2 border-emerald-300 pl-3 text-xs italic text-slate-600">
                  {{ alert.verificationExcerpt }}
                  @if (alert.verificationSource) {
                    <footer class="mt-1 not-italic text-slate-400">— {{ alert.verificationSource }}</footer>
                  }
                </blockquote>
              }
            </div>

            @if (alert.evidence.length > 0) {
              <div class="flex flex-wrap gap-2 border-t border-slate-100 px-5 py-3">
                <span class="text-xs text-slate-500">Evidence:</span>
                @for (evidence of alert.evidence; track evidence.documentId) {
                  <a
                    [routerLink]="['/documents', evidence.documentId]"
                    class="rounded border border-slate-200 px-2 py-0.5 text-xs text-brand-700 hover:border-brand-500"
                  >
                    {{ evidence.fileName }}
                  </a>
                }
              </div>
            }

            @if (alert.requiresProfessionalConsult) {
              <p class="border-t border-red-100 bg-red-50 px-5 py-3 text-xs font-medium text-red-800">
                @if (language.current() === 'ta') {
                  ⚠ இதை நீங்களே மாற்ற வேண்டாம். அடுத்த மருந்து எடுப்பதற்கு முன் மருத்துவர் அல்லது
                  மருந்தாளுநரிடம் காட்டுங்கள்.
                } @else {
                  ⚠ Do not change anything yourself. Show this to a doctor or pharmacist before your next dose.
                }
              </p>
            }
          </li>
        }
      </ul>
    }
  `
})
export class AlertsViewComponent {
  protected readonly language = inject(LanguageService);

  readonly alerts = input.required<Alert[]>();

  protected action(alert: Alert): string {
    return this.language.pick(alert.suggestedActionEn, alert.suggestedActionTa);
  }

  protected labelFor(severity: AlertSeverity): string {
    switch (severity) {
      case 'Red':
        return 'Risk';
      case 'Amber':
        return 'Check';
      default:
        return 'Info';
    }
  }

  protected glyphFor(severity: AlertSeverity): string {
    switch (severity) {
      case 'Red':
        return '⚠';
      case 'Amber':
        return '!';
      default:
        return 'i';
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

  protected borderFor(severity: AlertSeverity): string {
    switch (severity) {
      case 'Red':
        return 'border-red-200';
      case 'Amber':
        return 'border-amber-200';
      default:
        return 'border-slate-200';
    }
  }

  /**
   * "Not found" and "unverified" are stated plainly rather than hidden. A finding is never removed
   * because openFDA could not confirm it — absence of confirmation is not evidence of safety.
   */
  protected verificationLabel(status: VerificationStatus): string {
    switch (status) {
      case 'Confirmed':
        return '✓ Verified against FDA drug label data';
      case 'NotFound':
        return 'AI-flagged — the FDA label does not mention this. Verify with a pharmacist.';
      case 'Unverified':
        return 'AI-flagged — could not be checked against FDA data. Verify with a pharmacist.';
      case 'NotApplicable':
        return 'Found by comparing your own documents — no external check applies.';
      default:
        return 'Verification pending.';
    }
  }

  protected verificationClass(status: VerificationStatus): string {
    return status === 'Confirmed' ? 'text-emerald-700 font-medium' : 'text-slate-500';
  }
}
