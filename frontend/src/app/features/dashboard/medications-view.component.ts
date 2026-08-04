import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';

import { ConfidenceBadgeComponent } from '../../shared/confidence-badge.component';
import type { MedicationGroup } from '../../core/models';

/**
 * Medications view (§10.6). Grouped by generic; conflicted groups are highlighted with the reason
 * inline, and every row links to the document it was read from.
 */
@Component({
  selector: 'mt-medications-view',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, RouterLink, ConfidenceBadgeComponent],
  template: `
    @if (groups().length === 0) {
      <p class="rounded-xl border border-dashed border-slate-300 px-6 py-12 text-center text-sm text-slate-500">
        No medications were found in these documents.
      </p>
    } @else {
      <div class="space-y-4">
        @for (group of groups(); track group.displayName) {
          <section class="overflow-hidden rounded-xl border bg-white"
                   [class]="group.hasConflict ? 'border-amber-300' : 'border-slate-200'">
            <header class="flex flex-wrap items-center justify-between gap-2 px-5 py-3"
                    [class]="group.hasConflict ? 'bg-amber-50' : 'bg-slate-50/60'">
              <div>
                <h3 class="font-medium text-slate-900">
                  {{ group.displayName }}
                  @if (group.hasConflict) {
                    <span class="ml-2 rounded-full bg-amber-200 px-2 py-0.5 text-xs font-semibold text-amber-900">
                      ⚠ see Alerts
                    </span>
                  }
                </h3>

                <p class="mt-0.5 text-xs text-slate-500">
                  @if (group.therapeuticClass) {
                    {{ group.therapeuticClass }} ·
                  }
                  {{ group.rows.length }} prescription{{ group.rows.length === 1 ? '' : 's' }}
                  @if (group.firstPrescribed) {
                    · {{ group.firstPrescribed | date: 'MMM yyyy' }}
                    @if (group.lastPrescribed && group.lastPrescribed !== group.firstPrescribed) {
                      – {{ group.lastPrescribed | date: 'MMM yyyy' }}
                    }
                  }
                </p>
              </div>

              @if (!group.genericName) {
                <span
                  class="rounded border border-slate-200 px-2 py-0.5 text-xs text-slate-500"
                  title="No active ingredient could be identified, so this medicine could not be included in the cross-checks."
                >
                  generic not identified
                </span>
              }
            </header>

            <div class="overflow-x-auto">
              <table class="w-full text-sm">
                <thead>
                  <tr class="border-t border-slate-100 text-left text-xs text-slate-500">
                    <th class="px-5 py-2 font-medium">Brand</th>
                    <th class="px-5 py-2 font-medium">Strength</th>
                    <th class="px-5 py-2 font-medium">Frequency</th>
                    <th class="px-5 py-2 font-medium">Duration</th>
                    <th class="px-5 py-2 font-medium">Prescriber</th>
                    <th class="px-5 py-2 font-medium">Read as</th>
                    <th class="px-5 py-2"></th>
                  </tr>
                </thead>
                <tbody>
                  @for (row of group.rows; track row.id) {
                    <tr class="border-t border-slate-100 align-top">
                      <td class="px-5 py-3 text-slate-900">{{ row.brandName || '—' }}</td>
                      <td class="px-5 py-3 text-slate-700">
                        @if (row.strengthValue) {
                          {{ row.strengthValue }}{{ row.strengthUnit }}
                        } @else {
                          —
                        }
                      </td>
                      <td class="px-5 py-3 text-slate-700">
                        {{ row.frequency || '—' }}
                        @if (row.frequencyPerDay) {
                          <span class="block text-xs text-slate-400">{{ row.frequencyPerDay }}× a day</span>
                        }
                      </td>
                      <td class="px-5 py-3 text-slate-700">
                        {{ row.durationDays ? row.durationDays + ' days' : '—' }}
                      </td>
                      <td class="px-5 py-3 text-slate-700">{{ row.providerName || '—' }}</td>
                      <td class="px-5 py-3">
                        <!-- The printed text beside the normalized value, so the reading can be checked (US-7). -->
                        @if (row.sourceText) {
                          <span class="font-mono text-xs text-slate-500">{{ row.sourceText }}</span>
                        } @else {
                          <span class="text-slate-400">—</span>
                        }
                      </td>
                      <td class="whitespace-nowrap px-5 py-3 text-right">
                        <mt-confidence [score]="row.confidence" />
                        <a
                          [routerLink]="['/documents', row.documentId]"
                          class="mt-1 block text-xs text-brand-700 hover:underline"
                        >
                          View source
                        </a>
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          </section>
        }
      </div>
    }
  `
})
export class MedicationsViewComponent {
  readonly groups = input.required<MedicationGroup[]>();
}
