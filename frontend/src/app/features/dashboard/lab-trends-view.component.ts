import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { NgApexchartsModule, type ApexOptions } from 'ng-apexcharts';

import { LanguageService } from '../../core/language.service';
import { ConfidenceBadgeComponent } from '../../shared/confidence-badge.component';
import type { LabTrend, TrendDirection } from '../../core/models';

/**
 * Lab Trends view (§10.7): one chart per standardized test, with the normal range shown as a
 * shaded band, out-of-range points emphasised, and a one-sentence explanation below.
 */
@Component({
  selector: 'mt-lab-trends-view',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgApexchartsModule, ConfidenceBadgeComponent],
  template: `
    @if (trends().length === 0) {
      <div class="rounded-xl border border-dashed border-slate-300 px-6 py-12 text-center">
        <p class="text-sm text-slate-500">No numeric lab results were found in these documents.</p>
        <p class="mx-auto mt-2 max-w-md text-xs text-slate-400">
          Trends need test values with dates. Prescriptions that only list suggested investigations
          have nothing to chart.
        </p>
      </div>
    } @else {
      <div class="space-y-6">
        @for (trend of trends(); track trend.testKey) {
          <section class="rounded-xl border border-slate-200 bg-white p-5">
            <header class="flex flex-wrap items-start justify-between gap-3">
              <div>
                <h3 class="font-medium text-slate-900">
                  {{ trend.displayName }}
                  @if (trend.unit) {
                    <span class="font-normal text-slate-400">({{ trend.unit }})</span>
                  }
                </h3>
                <p class="mt-0.5 text-xs" [class]="directionClass(trend.direction)">
                  {{ directionLabel(trend) }}
                </p>
              </div>
              <mt-confidence [score]="trend.confidence" />
            </header>

            @if (trend.points.length >= 2) {
              <apx-chart
                [series]="optionsFor(trend).series!"
                [chart]="optionsFor(trend).chart!"
                [xaxis]="optionsFor(trend).xaxis!"
                [yaxis]="optionsFor(trend).yaxis!"
                [annotations]="optionsFor(trend).annotations!"
                [stroke]="optionsFor(trend).stroke!"
                [markers]="optionsFor(trend).markers!"
                [tooltip]="optionsFor(trend).tooltip!"
                [grid]="optionsFor(trend).grid!"
                [colors]="optionsFor(trend).colors!"
              />
            } @else {
              <!-- One point is not a chart. Show the value rather than a misleading single dot. -->
              <p class="mt-4 rounded-lg bg-slate-50 px-4 py-3 text-sm text-slate-700">
                One reading only:
                <strong>{{ trend.points[0].value }}{{ trend.unit ? ' ' + trend.unit : '' }}</strong>
                on {{ trend.points[0].date }}
                @if (trend.points[0].isOutOfRange) {
                  <span class="ml-1 text-amber-800">⚠ outside the printed range</span>
                }
              </p>
            }

            <p class="mt-4 text-sm leading-relaxed text-slate-700">
              {{ language.pick(trend.explanationEn, trend.explanationTa) }}
            </p>

            @if (trend.normalRangeText) {
              <p class="mt-2 text-xs text-slate-400">
                Reference range printed on the report: {{ trend.normalRangeText }}
              </p>
            }
          </section>
        }
      </div>
    }
  `
})
export class LabTrendsViewComponent {
  protected readonly language = inject(LanguageService);

  readonly trends = input.required<LabTrend[]>();

  private readonly cache = new Map<string, ApexOptions>();

  protected directionLabel(trend: LabTrend): string {
    const change = trend.percentChange != null ? ` (${trend.percentChange > 0 ? '+' : ''}${trend.percentChange}%)` : '';

    switch (trend.direction) {
      case 'Rising':
        return `Rising across ${trend.points.length} readings${change}`;
      case 'Falling':
        return `Falling across ${trend.points.length} readings${change}`;
      case 'Stable':
        return `Broadly level across ${trend.points.length} readings`;
      default:
        return 'Not enough readings to show a trend yet';
    }
  }

  protected directionClass(direction: TrendDirection): string {
    return direction === 'Rising' || direction === 'Falling' ? 'text-amber-700' : 'text-slate-500';
  }

  /** Memoised — ApexCharts re-renders whenever the options object identity changes. */
  protected optionsFor(trend: LabTrend): ApexOptions {
    const cached = this.cache.get(trend.testKey);
    if (cached) return cached;

    const options: ApexOptions = {
      series: [{ name: trend.displayName, data: trend.points.map(p => ({ x: p.date, y: p.value })) }],
      chart: { type: 'line', height: 240, toolbar: { show: false }, fontFamily: 'inherit' },
      colors: ['#0f766e'],
      stroke: { curve: 'straight', width: 2 },
      grid: { borderColor: '#e2e8f0', strokeDashArray: 4 },
      xaxis: { type: 'category', labels: { style: { colors: '#64748b', fontSize: '11px' } } },
      yaxis: {
        labels: { style: { colors: '#64748b', fontSize: '11px' } },
        title: { text: trend.unit ?? undefined, style: { color: '#94a3b8', fontWeight: 400 } }
      },
      // Out-of-range points are emphasised individually; colour alone never carries the meaning,
      // since the explanation below says the same thing in words.
      markers: {
        size: 5,
        colors: trend.points.map(p => (p.isOutOfRange ? '#dc2626' : '#0f766e')),
        strokeColors: '#fff',
        strokeWidth: 2
      },
      tooltip: { theme: 'light' },
      annotations: this.rangeBand(trend)
    };

    this.cache.set(trend.testKey, options);
    return options;
  }

  /** The normal range as a shaded band — the clinical context that makes a value readable. */
  private rangeBand(trend: LabTrend): ApexOptions['annotations'] {
    if (trend.normalMin == null && trend.normalMax == null) return { yaxis: [] };

    return {
      yaxis: [
        {
          y: trend.normalMin ?? 0,
          y2: trend.normalMax ?? undefined,
          fillColor: '#10b981',
          opacity: 0.08,
          borderColor: '#10b981',
          label: {
            text: 'normal range',
            position: 'left',
            textAnchor: 'start',
            style: { color: '#059669', background: 'transparent', fontSize: '10px' }
          }
        }
      ]
    };
  }
}
